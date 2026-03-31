# Feature: Android APK/AAB Signing Support

## Problem

DotnetDeployer currently delegates Android builds to `dotnet publish -c Release -f net9.0-android` without passing any signing parameters. This means every APK/AAB is signed with the .NET SDK's auto-generated **debug keystore**, which is unique per machine.

**Consequence:** When a CI agent builds release v0.0.64 and then a different agent (or the same agent after reprovisioning) builds v0.0.65, the two APKs have different signing certificates. Android refuses to update:

```
INSTALL_FAILED_UPDATE_INCOMPATIBLE: Existing package com.example.app
signatures do not match newer version; ignoring!
```

Users must uninstall the old version before installing the new one, losing all app data.

## Requirements

1. **Self-contained configuration** — All signing information must be expressible in `deployer.yaml` without requiring external keystore files to be checked into the repo or pre-staged on the build machine. The keystore binary must be provided as a base64-encoded environment variable.

2. **Secure by design** — No secrets (passwords, keystore contents) in plaintext in the YAML. All sensitive values are referenced via environment variable names, following the existing pattern (`tokenEnvVar`, `apiKeyEnvVar`).

3. **Backward-compatible** — If no `signing` block is present, behavior is unchanged (debug-signed).

4. **Applies to both APK and AAB** — The same signing config works for both `ApkGenerator` and `AabGenerator`.

## Proposed YAML Schema

```yaml
packages:
  - project: src/MyApp.Android/MyApp.Android.csproj
    formats:
      - type: Apk
        arch:
          - arm64
        signing:
          keystoreBase64EnvVar: ANDROID_KEYSTORE_BASE64
          storePasswordEnvVar: ANDROID_KEYSTORE_PASSWORD
          keyAlias: my-release-key
          keyPasswordEnvVar: ANDROID_KEY_PASSWORD
```

### Field Descriptions

| Field | Type | Required | Description |
|---|---|---|---|
| `keystoreBase64EnvVar` | `string` | Yes | Name of an environment variable containing the keystore file encoded as base64. |
| `storePasswordEnvVar` | `string` | Yes | Name of an environment variable containing the keystore password. |
| `keyAlias` | `string` | Yes | Alias of the key within the keystore. Not a secret — safe to put in YAML. |
| `keyPasswordEnvVar` | `string` | Yes | Name of an environment variable containing the key password. |

### How Users Prepare the Keystore

```bash
# 1. Generate keystore (one-time)
keytool -genkey -v -keystore release.keystore -alias my-release-key \
  -keyalg RSA -keysize 2048 -validity 10000

# 2. Encode as base64 for CI secrets
base64 -w 0 release.keystore
# → Store the output as a CI secret variable (e.g., Azure DevOps variable group, GitHub secret)
```

## Implementation Plan

### 1. New Config Class: `AndroidSigningConfig`

**File:** `src/DotnetDeployer/Configuration/AndroidSigningConfig.cs`

```csharp
using YamlDotNet.Serialization;

namespace DotnetDeployer.Configuration;

public class AndroidSigningConfig
{
    [YamlMember(Alias = "keystoreBase64EnvVar")]
    public string KeystoreBase64EnvVar { get; set; } = "";

    [YamlMember(Alias = "storePasswordEnvVar")]
    public string StorePasswordEnvVar { get; set; } = "";

    [YamlMember(Alias = "keyAlias")]
    public string KeyAlias { get; set; } = "";

    [YamlMember(Alias = "keyPasswordEnvVar")]
    public string KeyPasswordEnvVar { get; set; } = "";
}
```

### 2. Extend `PackageFormatConfig`

**File:** `src/DotnetDeployer/Configuration/PackageFormatConfig.cs`

Add to `PackageFormatConfig`:

```csharp
[YamlMember(Alias = "signing")]
public AndroidSigningConfig? Signing { get; set; }
```

### 3. New Helper: `AndroidSigningHelper`

**File:** `src/DotnetDeployer/Packaging/Android/AndroidSigningHelper.cs`

Responsibilities:
- Read the base64-encoded keystore from the environment variable.
- Decode and write it to a temp file.
- Build the MSBuild arguments for signing.
- Provide a cleanup mechanism (implement `IDisposable`).

Outline:

```csharp
namespace DotnetDeployer.Packaging.Android;

public sealed class AndroidSigningHelper : IDisposable
{
    private readonly string? keystorePath;

    private AndroidSigningHelper(string? keystorePath)
    {
        this.keystorePath = keystorePath;
    }

    /// <summary>
    /// Creates a signing context from the config. Decodes the keystore to a temp file.
    /// Returns a failure Result if any required env var is missing or empty.
    /// </summary>
    public static Result<AndroidSigningHelper> Create(AndroidSigningConfig? config)
    {
        if (config is null)
            return Result.Success(new AndroidSigningHelper(null));

        // 1. Read env vars, fail if any is missing
        // 2. Base64-decode keystore → temp file
        // 3. Return helper with temp file path
    }

    /// <summary>
    /// Returns the MSBuild arguments for signing, or empty string if no signing is configured.
    /// </summary>
    public string GetSigningArgs()
    {
        if (keystorePath is null) return "";

        // Return: -p:AndroidKeyStore=true
        //         -p:AndroidSigningKeyStore={keystorePath}
        //         -p:AndroidSigningKeyAlias={alias}
        //         -p:AndroidSigningStorePass={password}
        //         -p:AndroidSigningKeyPass={password}
    }

    public void Dispose()
    {
        // Delete temp keystore file if it exists
    }
}
```

### 4. Update `ApkGenerator` and `AabGenerator`

Both generators need to:
1. Accept `AndroidSigningConfig?` (from the `PackageFormatConfig.Signing` property).
2. Create an `AndroidSigningHelper` before running `dotnet publish`.
3. Append `signingHelper.GetSigningArgs()` to the publish command.
4. Dispose the helper after the build (cleanup temp keystore).

The `Generate` method signature in `IPackageGenerator` currently is:

```csharp
Task<Result<GeneratedPackage>> Generate(
    string projectPath, Architecture arch, ProjectMetadata metadata,
    string outputPath, ILogger logger);
```

The signing config needs to flow from `PackageFormatConfig` to the generator. Two approaches:

**Option A (recommended):** Pass `PackageFormatConfig` (or just `AndroidSigningConfig?`) as an additional parameter to `Generate`. This requires updating `IPackageGenerator`. Since only Android generators use it, add it with a default `null`.

**Option B:** Inject signing config into the generator constructor via the factory, which already has access to the config.

Both are viable. Option B keeps the interface clean.

### 5. Update `PackageGeneratorFactory`

The factory currently creates generators without config context. It needs to pass `AndroidSigningConfig?` to Android generators. This means `PackageGeneratorFactory` (or its callers) must receive the `PackageFormatConfig` when creating generators.

### 6. Validation

In `AndroidSigningHelper.Create()`, validate:
- All four fields in `AndroidSigningConfig` are non-empty.
- All referenced environment variables exist and are non-empty.
- The base64 string decodes successfully to a valid byte sequence.

Return descriptive `Result.Failure` messages for each case.

## Example: Consumer Project Configuration

### Azure DevOps

```yaml
# azure-pipelines.yml
variables:
  - group: android-signing   # Contains ANDROID_KEYSTORE_BASE64, ANDROID_KEYSTORE_PASSWORD, ANDROID_KEY_PASSWORD

steps:
  - script: dotnetdeployer
    env:
      ANDROID_KEYSTORE_BASE64: $(ANDROID_KEYSTORE_BASE64)
      ANDROID_KEYSTORE_PASSWORD: $(ANDROID_KEYSTORE_PASSWORD)
      ANDROID_KEY_PASSWORD: $(ANDROID_KEY_PASSWORD)
      GITHUB_TOKEN: $(GitHubApiKey)
```

### GitHub Actions

```yaml
steps:
  - run: dotnetdeployer
    env:
      ANDROID_KEYSTORE_BASE64: ${{ secrets.ANDROID_KEYSTORE_BASE64 }}
      ANDROID_KEYSTORE_PASSWORD: ${{ secrets.ANDROID_KEYSTORE_PASSWORD }}
      ANDROID_KEY_PASSWORD: ${{ secrets.ANDROID_KEY_PASSWORD }}
      GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

### `deployer.yaml`

```yaml
version: 1
github:
  enabled: true
  owner: MyOrg
  repo: MyApp
  packages:
    - project: src/MyApp.Android/MyApp.Android.csproj
      formats:
        - type: Apk
          arch:
            - arm64
          signing:
            keystoreBase64EnvVar: ANDROID_KEYSTORE_BASE64
            storePasswordEnvVar: ANDROID_KEYSTORE_PASSWORD
            keyAlias: my-release-key
            keyPasswordEnvVar: ANDROID_KEY_PASSWORD
```

## Testing

### Unit Tests

1. **`AndroidSigningHelper` tests:**
   - Returns empty args when config is null (backward compat).
   - Returns correct MSBuild args when all env vars are set.
   - Returns failure when an env var is missing.
   - Returns failure when base64 is invalid.
   - Temp keystore file is deleted on Dispose.

2. **`AndroidSigningConfig` YAML deserialization:**
   - Round-trip serialize/deserialize with all fields.
   - Deserialize with no `signing` block → null.

3. **`ApkGenerator`/`AabGenerator` integration:**
   - Verify the signing args are appended to the `dotnet publish` command (mock `ICommand`).

### Manual Validation

```bash
# Generate a test keystore
keytool -genkey -v -keystore test.keystore -alias test-key \
  -keyalg RSA -keysize 2048 -validity 1 \
  -dname "CN=Test, OU=Test, O=Test, L=Test, ST=Test, C=US" \
  -storepass testpass -keypass testpass

# Set env vars
export ANDROID_KEYSTORE_BASE64=$(base64 -w 0 test.keystore)
export ANDROID_KEYSTORE_PASSWORD=testpass
export ANDROID_KEY_PASSWORD=testpass

# Run deployer with dry-run on a sample Android project
dotnetdeployer --dry-run
```

## Files to Modify

| File | Change |
|---|---|
| `src/DotnetDeployer/Configuration/AndroidSigningConfig.cs` | **New** — Config class |
| `src/DotnetDeployer/Configuration/PackageFormatConfig.cs` | Add `Signing` property |
| `src/DotnetDeployer/Packaging/Android/AndroidSigningHelper.cs` | **New** — Keystore decode + MSBuild args |
| `src/DotnetDeployer/Packaging/Android/ApkGenerator.cs` | Use signing helper in publish command |
| `src/DotnetDeployer/Packaging/Android/AabGenerator.cs` | Use signing helper in publish command |
| `src/DotnetDeployer/Packaging/PackageGeneratorFactory.cs` | Pass signing config to Android generators |
| `test/DotnetDeployer.Tests/...` | New tests for signing |
| `README.md` | Document signing configuration |

# Secret Management

DotnetDeployer can read sensitive values from environment variables, files,
`deployer.secrets.yaml`, or the system keyring. For local interactive use, the
recommended path is the system keyring.

## Quick Start

Reference a named secret in `deployer.yaml`:

```yaml
version: 1

nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKey:
    from: secret
    key: nuget_api_key
```

Store the value once:

```bash
dotnetdeployer secrets set nuget_api_key
```

Then deploy normally:

```bash
dotnetdeployer
```

## CLI Commands

```bash
dotnetdeployer secrets set <key>
dotnetdeployer secrets check <key>
dotnetdeployer secrets delete <key>
```

`secrets set` prompts without echoing the value. It also accepts stdin, which is
useful for scripted setup:

```bash
printf "%s" "$NUGET_API_KEY" | dotnetdeployer secrets set nuget_api_key
```

There is also a `--value` option:

```bash
dotnetdeployer secrets set nuget_api_key --value "..."
```

Prefer the prompt or stdin for real secrets, because command-line values can be
captured in shell history or process listings.

## Resolution Order

When a value source uses `from: secret`, DotnetDeployer resolves it in this
order:

1. `deployer.secrets.yaml` in the current working directory.
2. The system keyring.

This keeps existing `deployer.secrets.yaml` workflows working while allowing a
repo to use keyring-backed secrets without changing its YAML.

## System Keyring Backends

| OS | Backend |
|----|---------|
| Windows | Windows Credential Manager |
| macOS | Keychain through the `security` command |
| Linux | Secret Service through `secret-tool` |

On Linux, install `secret-tool` if it is not available:

```bash
# Debian/Ubuntu
sudo apt install libsecret-tools

# Fedora
sudo dnf install libsecret

# Arch
sudo pacman -S libsecret
```

Secrets are stored for the current operating-system user. Key names are global
within DotnetDeployer, so use distinct names if you need different credentials
for different accounts or feeds.

## Common Secrets

### NuGet

```yaml
nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKey:
    from: secret
    key: nuget_api_key
```

```bash
dotnetdeployer secrets set nuget_api_key
```

### GitHub Releases

```yaml
github:
  enabled: true
  owner: MyOrg
  repo: MyApp
  token:
    from: secret
    key: github_token
```

```bash
dotnetdeployer secrets set github_token
```

### GitHub Pages

```yaml
githubPages:
  enabled: true
  owner: MyOrg
  repo: myapp-pages
  token:
    from: secret
    key: github_token
  project: src/MyApp.Browser/MyApp.Browser.csproj
```

## Android Signing

For Android signing, store the keystore as base64 and store passwords as named
secrets:

```yaml
android:
  signing:
    keystore:
      from: secret
      key: android_keystore_base64
      encoding: base64
    storePassword:
      from: secret
      key: android_store_pass
    keyAlias: release-key
    keyPassword:
      from: secret
      key: android_key_pass
```

Encode the keystore:

```bash
base64 -w 0 < release.keystore | dotnetdeployer secrets set android_keystore_base64
```

Store the remaining values:

```bash
dotnetdeployer secrets set android_store_pass
dotnetdeployer secrets set android_key_pass
```

`keyAlias` may be a literal if it is not sensitive. If you prefer to keep it out
of YAML too, use the same model:

```yaml
keyAlias:
  from: secret
  key: android_key_alias
```

## CI Usage

The system keyring is intended for local interactive deployments. In CI, use the
CI provider's secret store and expose values as environment variables:

```yaml
nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKey:
    from: env
    name: NUGET_API_KEY
```

For Azure Pipelines, map the variable group value into the step:

```yaml
env:
  NUGET_API_KEY: $(NugetApiKey)
```

## Portable File Fallback

For environments without a usable keyring, keep using `deployer.secrets.yaml`:

```yaml
nuget_api_key: ...
github_token: ...
android_keystore_base64: ...
android_store_pass: ...
android_key_pass: ...
```

Add it to `.gitignore`:

```gitignore
deployer.secrets.yaml
```

## Troubleshooting

| Problem | Fix |
|---------|-----|
| `Secret key '<key>' was not found` | Run `dotnetdeployer secrets set <key>` or add the key to `deployer.secrets.yaml`. |
| Linux says `Could not find 'secret-tool'` | Install `libsecret-tools` or use `deployer.secrets.yaml`. |
| The wrong account is used | Store the value under a different key and update `deployer.yaml`. |
| CI cannot find a keyring secret | Use `from: env` and the CI provider's secret store instead. |

# DotnetDeployer

**Deploy your .NET projects with a single YAML file. No scripts. No ceremony. Just ship it.**

---

## Why?

Let's be honest: CI/CD pipelines are powerful, but they're also *way* too complicated for what most projects need. You want to push a NuGet package or create a GitHub Release with some binaries. That's it. But instead, you end up wrestling with YAML indentation, provider-specific syntax, environment variables, and a dozen moving parts.

Tools like Nuke are fantastic for complex scenarios, but they come with their own learning curve and boilerplate. Sometimes you just want to declare *what* you want to deploy and let something else figure out the *how*.

That's DotnetDeployer. Write a simple `deployer.yaml`, run the tool, and you're done.

---

## What does it do?

DotnetDeployer handles the boring parts of deployment:

- **NuGet packages**: Pack and push all packable projects in your solution
- **GitHub Releases**: Build platform-specific packages (AppImage, DEB, RPM, EXE, DMG, MSIX, APK, AAB) and upload them as release assets
- **GitHub Pages**: Deploy WebAssembly apps directly to GitHub Pages
- **Automatic versioning**: Integrates with GitVersion out of the box
- **CI-friendly**: Outputs `##vso[build.updatebuildnumber]` for Azure Pipelines (and similar patterns for other CI systems)

---

## Quick Start

### 1. Create `deployer.yaml`

```yaml
version: 1

nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKeyEnvVar: NUGET_API_KEY
```

### 2. Run the tool

```bash
# From source (self-deployment style)
dotnet run --project path/to/DotnetDeployer.Tool.csproj

# Or install as a global tool
dotnet tool install -g DotnetDeployer.Tool
dotnetdeployer
```

### 3. That's it

The tool will:
- Detect your solution
- Use GitVersion to determine the version
- Pack all packable projects
- Push to NuGet (or simulate with `--dry-run`)

---

## Full Configuration Example

```yaml
version: 1

nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKeyEnvVar: NUGET_API_KEY

github:
  enabled: true
  owner: YourGitHubUsername
  repo: YourRepo
  tokenEnvVar: GITHUB_TOKEN
  outputDir: artifacts
  
  packages:
    - project: src/MyApp.Desktop/MyApp.Desktop.csproj
      formats:
        - type: appimage
          arch: [x64, arm64]
        - type: deb
          arch: [x64]
        - type: exe-setup
          arch: [x64]
        - type: dmg
          arch: [x64, arm64]

githubPages:
  enabled: true
  owner: YourGitHubUsername
  repo: MyApp-Pages
  tokenEnvVar: GITHUB_TOKEN
  projects:
    - project: src/MyApp.Browser/MyApp.Browser.csproj
```

---

## CLI Options

| Option | Description |
|--------|-------------|
| `--config`, `-c` | Path to deployer.yaml (default: `deployer.yaml`) |
| `--dry-run`, `-n` | Simulate deployment without making changes |
| `--release-version`, `-v` | Override version for the release |

---

## Azure Pipelines Integration

DotnetDeployer can deploy itself. Here's how:

```yaml
trigger:
  - master
  - main

variables:
  - group: api-keys
  - name: Agent.Source.Git.ShallowFetchDepth
    value: 0

pool:
  vmImage: 'ubuntu-latest'

steps:
  - checkout: self
    fetchDepth: 0

  - pwsh: dotnet run --project src/DotnetDeployer.Tool/DotnetDeployer.Tool.csproj -- --dry-run
    displayName: Dry run (PRs)
    condition: and(succeeded(), ne(variables['Build.SourceBranch'], 'refs/heads/master'))
    env:
      NUGET_API_KEY: $(NugetApiKey)

  - pwsh: dotnet run --project src/DotnetDeployer.Tool/DotnetDeployer.Tool.csproj
    displayName: Deploy
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/master'))
    env:
      NUGET_API_KEY: $(NugetApiKey)
```

The tool automatically outputs `##vso[build.updatebuildnumber]` to set the build name to the detected version.

---

## Supported Package Types

| Platform | Types |
|----------|-------|
| Linux | AppImage, DEB, RPM, Flatpak |
| Windows | EXE (self-extracting), EXE (setup wizard), MSIX |
| macOS | DMG |
| Android | APK, AAB |
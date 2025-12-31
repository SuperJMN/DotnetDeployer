# AGENTS.md - DotnetDeployer Integration Guide

This guide explains how to set up DotnetDeployer for any .NET project. Follow these steps to create the necessary configuration files.

---

## Quick Reference

To deploy a .NET project using DotnetDeployer, you need:

1. **`deployer.yaml`** - Configuration file in the repository root
2. **`azure-pipelines.yml`** (or equivalent CI config) - Pipeline that invokes the tool
3. **`GitVersion.yml`** (optional) - For automatic semantic versioning

---

## Step 1: Create `deployer.yaml`

Place this file in the **repository root** (same directory as the `.sln` or `.slnx` file).

### Minimal Example (NuGet only)

```yaml
version: 1

nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKeyEnvVar: NUGET_API_KEY
```

### Full Example (NuGet + GitHub Releases + GitHub Pages)

```yaml
version: 1

# NuGet deployment - packs and pushes all packable projects
nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKeyEnvVar: NUGET_API_KEY

# GitHub Releases - builds platform-specific packages and uploads as assets
github:
  enabled: true
  owner: YourGitHubUsername      # GitHub username or org
  repo: YourRepoName             # Repository name
  tokenEnvVar: GITHUB_TOKEN      # Environment variable with PAT
  outputDir: artifacts           # Optional: where to save packages locally
  
  packages:
    - project: src/MyApp.Desktop/MyApp.Desktop.csproj
      formats:
        - type: appimage
          arch: [x64, arm64]
        - type: deb
          arch: [x64]
        - type: rpm
          arch: [x64]
        - type: exe-setup
          arch: [x64]
        - type: dmg
          arch: [x64, arm64]

# GitHub Pages - deploys WebAssembly apps
githubPages:
  enabled: true
  owner: YourGitHubUsername
  repo: MyApp-Pages              # Dedicated repo for GitHub Pages
  tokenEnvVar: GITHUB_TOKEN
  branch: main                   # Branch to push to (default: main)
  customDomain: myapp.example.com  # Optional: custom domain
  projects:
    - project: src/MyApp.Browser/MyApp.Browser.csproj
```

### Configuration Reference

| Section | Property | Description |
|---------|----------|-------------|
| `nuget` | `enabled` | Enable/disable NuGet deployment |
| `nuget` | `source` | NuGet feed URL |
| `nuget` | `apiKeyEnvVar` | Environment variable containing API key |
| `github` | `enabled` | Enable/disable GitHub Releases |
| `github` | `owner` | GitHub username or organization |
| `github` | `repo` | Repository name |
| `github` | `tokenEnvVar` | Environment variable with GitHub token |
| `github` | `outputDir` | Local directory for generated packages |
| `github.packages[].formats[].type` | Package type: `appimage`, `deb`, `rpm`, `flatpak`, `exe-sfx`, `exe-setup`, `msix`, `dmg`, `apk`, `aab` |
| `github.packages[].formats[].arch` | Architectures: `x64`, `arm64`, `x86` |
| `githubPages` | `enabled` | Enable/disable GitHub Pages deployment |
| `githubPages` | `customDomain` | Optional custom domain for the site |

---

## Step 2: Create `azure-pipelines.yml`

Place this file in the **repository root**.

### Standard Template

```yaml
trigger:
  - master
  - main

variables:
  - group: api-keys                              # Variable group with secrets
  - name: Agent.Source.Git.ShallowFetchDepth
    value: 0                                     # Required for GitVersion

pool:
  vmImage: 'ubuntu-latest'

steps:
  - checkout: self
    fetchDepth: 0                                # Required for GitVersion

  # Dry run for PRs (no actual deployment)
  - pwsh: |
      dotnet tool install -g DotnetDeployer.Tool
      dotnetdeployer --dry-run
    displayName: Dry run (PRs)
    condition: and(succeeded(), ne(variables['Build.SourceBranch'], 'refs/heads/master'))
    env:
      NUGET_API_KEY: $(NugetApiKey)
      GITHUB_TOKEN: $(GitHubToken)

  # Actual deployment for master/main branch
  - pwsh: |
      dotnet tool install -g DotnetDeployer.Tool
      dotnetdeployer
    displayName: Deploy
    condition: and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/master'))
    env:
      NUGET_API_KEY: $(NugetApiKey)
      GITHUB_TOKEN: $(GitHubToken)
```

### Key Points

- **`fetchDepth: 0`**: Required for GitVersion to calculate the version correctly
- **`Agent.Source.Git.ShallowFetchDepth: 0`**: Ensures full history is fetched
- **Variable group `api-keys`**: Must contain secrets like `NugetApiKey`, `GitHubToken`
- **Condition**: Only deploys on `master` or `main` branch; PRs do a dry run

### Alternative: Run from Source (for DotnetDeployer itself)

If the project IS DotnetDeployer or you want to run from source:

```yaml
  - pwsh: dotnet run --project src/DotnetDeployer.Tool/DotnetDeployer.Tool.csproj
    displayName: Deploy
    env:
      NUGET_API_KEY: $(NugetApiKey)
```

---

## Step 3: Create `GitVersion.yml` (Optional)

Place this file in the **repository root** for semantic versioning configuration.

### Recommended Configuration

```yaml
mode: ContinuousDelivery
branches:
  master:
    regex: ^master$|^main$
    tag: ''
    increment: Patch
  feature:
    regex: ^feature[/-]
    tag: alpha
    increment: Minor
  hotfix:
    regex: ^hotfix[/-]
    tag: beta
    increment: Patch
  pull-request:
    regex: ^(pull|pr)[/-]
    tag: pr
    increment: Inherit
```

If not present, GitVersion uses sensible defaults.

---

## Step 4: Configure Azure DevOps Variable Group

In Azure DevOps, create a variable group named `api-keys` with:

| Variable | Description | Secret? |
|----------|-------------|---------|
| `NugetApiKey` | NuGet.org API key | Yes |
| `GitHubToken` | GitHub Personal Access Token with `repo` scope | Yes |

---

## How It Works

1. **DotnetDeployer** reads `deployer.yaml` from the repository root
2. **GitVersion** calculates the semantic version from git history
3. The tool packs/builds with the calculated version
4. Packages are pushed to configured destinations
5. Azure Pipelines build number is updated via `##vso[build.updatebuildnumber]`

---

## Common Scenarios

### NuGet Library Only

```yaml
version: 1

nuget:
  enabled: true
  source: https://api.nuget.org/v3/index.json
  apiKeyEnvVar: NUGET_API_KEY
```

### Desktop App with Multi-Platform Releases

```yaml
version: 1

github:
  enabled: true
  owner: MyOrg
  repo: MyDesktopApp
  tokenEnvVar: GITHUB_TOKEN
  packages:
    - project: src/MyApp/MyApp.csproj
      formats:
        - type: exe-setup
          arch: [x64]
        - type: appimage
          arch: [x64, arm64]
        - type: dmg
          arch: [x64, arm64]
```

### WebAssembly App to GitHub Pages

```yaml
version: 1

githubPages:
  enabled: true
  owner: MyOrg
  repo: myapp-demo
  tokenEnvVar: GITHUB_TOKEN
  projects:
    - project: src/MyApp.Browser/MyApp.Browser.csproj
```

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Version is always `1.0.0` | Ensure `fetchDepth: 0` in checkout step |
| GitVersion not found | Tool auto-installs, but ensure .NET SDK 8.0+ is available |
| NuGet push fails | Check `NUGET_API_KEY` is set correctly in variable group |
| GitHub release fails | Ensure `GITHUB_TOKEN` has `repo` scope |

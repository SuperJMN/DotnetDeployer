# DotnetDeployer

Deployment and packaging helper for .NET solutions. It provides:
- A CLI to publish NuGet packages and create GitHub releases with multi-platform artifacts (Windows, Linux, Android, WebAssembly).
- High-level automation that discovers projects in your solution, builds artifacts, and optionally publishes a GitHub release.
- Optional WebAssembly site generation and deployment to GitHub Pages.

Notes:
- Conversations and issue discussions may be in Spanish, but code and commit messages are in English.
- Target frameworks: library/tool target net8.0; tests target net9.0.

## Prerequisites
- .NET 8 SDK
- Git (for release and Pages workflows)
- GitHub token with repo permissions for creating releases and pushing to Pages
- For Android packaging: a signing keystore (base64) and credentials

## Build and Test
- Restore and build the full solution:
  - `dotnet build DotnetDeployer.sln -c Release`
- Run all tests:
  - `dotnet test test/DotnetDeployer.Tests -c Release`
- Run only unit tests (exclude Integration by namespace):
  - `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName!~Integration"`
- Run a specific test:
  - `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName=DotnetDeployer.Tests.ApkNamingTests.Returns_only_signed_apk_without_suffix"`
  - `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName~ApkNamingTests"`
- Formatting (if you have dotnet-format):
  - `dotnet format --verify-no-changes`
  - `dotnet format`

## CLI Overview
Run from source:
- `dotnet run --project src/DotnetDeployer.Tool -- --help`

Commands:
- `nuget`: discover and pack projects, optionally push to NuGet.
- `release`: discover platform projects, package artifacts, and optionally publish a GitHub release. Can also deploy a WASM site to GitHub Pages.

Secrets:
- Prefer environment variables. Do not print secrets.
  - `NUGET_API_KEY` for NuGet pushes
  - `GITHUB_TOKEN` for GitHub release creation and Pages deployment

---

## Command: nuget
Publish NuGet packages from discovered projects in a solution (or explicit projects).

Key options:
- `--solution` Path to the solution (.sln). If omitted, the tool searches upward in parent directories.
- `--project` One or more explicit csproj files to pack (bypasses auto discovery).
- `--version` Semver to use. If omitted, version is inferred using GitVersion (falls back to `git describe`).
- `--api-key` NuGet API key (can be provided via `NUGET_API_KEY`).
- `--name-pattern` Wildcard to narrow auto-discovery (e.g., `YourApp*`).
- `--no-push` Only pack; do not push to NuGet.

Examples:
- Using env var for the API key (recommended):
  - `export NUGET_API_KEY={{NUGET_API_KEY}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- nuget --solution DotnetDeployer.sln --version 1.2.3`
- Explicit projects and only pack (no push):
  - `dotnet run --project src/DotnetDeployer.Tool -- nuget --project src/DotnetDeployer/DotnetDeployer.csproj --version 1.2.3 --no-push`
- Discovery with name pattern (excludes tests/demos/samples/desktop by convention):
  - `dotnet run --project src/DotnetDeployer.Tool -- nuget --solution path/to/YourApp.sln --name-pattern "YourApp*" --version 1.2.3`

---

## Command group: github
Use the `github` command group to publish packaged artifacts or WebAssembly sites through GitHub. Binary releases and GitHub Pages deployments now live in separate subcommands.

### Subcommand: release
Create artifacts per platform and optionally publish a GitHub release with uploaded assets.

Platform discovery rules (by project suffix):
- `.Desktop` => Windows (self-contained `.exe` plus `.msix`) and/or Linux (`.AppImage`, `.flatpak`, `.rpm`)
- `.Android` => Signed Android packages (`.apk` or `.aab`)

> Need WebAssembly? Run `dotnet run --project src/DotnetDeployer.Tool -- github pages ...` instead of adding `wasm` here.

General options:
- `--solution` Solution file. If omitted, the tool searches parent directories.
- `--prefix` Prefix to narrow project discovery inside the solution. Defaults to the solution name.
- `--version` Release version. If omitted, GitVersion is used (fallback to `git describe`).
- `--package-name`, `--app-id`, `--app-name` App metadata. If omitted, reasonable defaults are inferred from the solution name. For Android, `--app-id` may be read from the Android csproj `<ApplicationId>`.
- `--owner`, `--repository` GitHub owner and repository. If omitted, inferred from the current git remote (origin).
- `--github-token` GitHub token (or `--token` deprecated alias). Can be provided via `GITHUB_TOKEN` env var.
- `--release-name`, `--tag`, `--body`, `--draft`, `--prerelease` Standard GitHub release metadata.
- `--no-publish` Build/package artifacts but do not publish a GitHub release (preferred over deprecated `--dry-run`).
- `--platform` One or more of: `windows`, `linux`, `android`. Passing other values fails with guidance to use the appropriate command.

Android options:
- `--android-keystore-base64`, `--android-key-alias`, `--android-key-pass`, `--android-store-pass` Signing credentials.
- `--android-app-version` Integer ApplicationVersion. If omitted, generated from semantic version.
- `--android-app-display-version` Display string for version name (defaults to `--version`).

Examples:
- Multi-platform (Windows + Linux + Android) without publishing yet:
  - `export GITHUB_TOKEN={{GITHUB_TOKEN}}`
  - `export ANDROID_KEYSTORE_BASE64={{ANDROID_KEYSTORE_B64}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- github release \`
    `--solution /abs/path/YourApp.sln \`
    `--version 1.2.3 \`
    `--package-name YourApp \`
    `--app-id com.example.yourapp \`
    `--app-name "Your App" \`
    `--platform windows linux android \`
    `--android-keystore-base64 "$ANDROID_KEYSTORE_BASE64" \`
    `--android-key-alias {{ANDROID_KEY_ALIAS}} \`
    `--android-key-pass {{ANDROID_KEY_PASS}} \`
    `--android-store-pass {{ANDROID_STORE_PASS}} \`
    `--no-publish`

- Publish for Windows and Linux only (auto-discovery with default prefix):
  - `export GITHUB_TOKEN={{GITHUB_TOKEN}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- github release --solution /abs/path/YourApp.sln --version 1.2.3 --platform windows linux`

- Create artifacts but skip release publication:
  - `export GITHUB_TOKEN={{GITHUB_TOKEN}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- github release --solution /abs/path/YourApp.sln --version 1.2.3 --no-publish`

### Subcommand: pages
Publish the WebAssembly site produced by the `.Browser` project to GitHub Pages. The command builds the WASM site and, unless `--no-publish` is specified, pushes it to the target repository.

Project discovery rules (by project suffix):
- `.Browser` => WebAssembly site (wwwroot contents)

General options:
- `--solution` Solution file. If omitted, the tool searches parent directories.
- `--prefix` Prefix to narrow project discovery inside the solution. Defaults to the solution name.
- `--version` Deployment version. If omitted, GitVersion is used (fallback to `git describe`).
- `--owner`, `--repository` GitHub owner and repository. If omitted, inferred from the current git remote (origin).
- `--github-token` GitHub token (or `--token` deprecated alias). Can be provided via `GITHUB_TOKEN` env var.
- `--no-publish` Build the WebAssembly site but do not push to GitHub Pages (preferred over deprecated `--dry-run`).

Examples:
- Publish the Browser project to GitHub Pages:
  - `export GITHUB_TOKEN={{GITHUB_TOKEN}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- github pages --solution /abs/path/YourApp.sln --version 1.2.3`

- Build the site without publishing (useful for validation in CI):
  - `dotnet run --project src/DotnetDeployer.Tool -- github pages --solution /abs/path/YourApp.sln --version 1.2.3 --no-publish`

---

## Command: export
Build artifacts for selected platforms and write them to a target directory without publishing anything.

Behavior and discovery mirror the release command:
- Project discovery by suffix: `.Desktop` (Windows/Linux), `.Browser` (WASM), `.Android` (Android).
- Use `--prefix` to guide discovery when your solution contains multiple app roots.

Key options:
- `--solution` Solution file. If omitted, the tool searches parent directories.
- `--prefix` Prefix for discovery. Defaults to the solution name.
- `--version` Artifacts version. If omitted, GitVersion is used (fallback to `git describe`).
- `--package-name`, `--app-id`, `--app-name` App metadata. If omitted, inferred from the solution name.
- `--platform` One or more of: `windows`, `linux`, `android`, `wasm`.
- `--output` Output directory where artifacts will be written. Required.
- `--include-wasm` If set and `wasm` is included in `--platform`, writes the WASM site contents into a `wasm` subfolder.

Android options:
- `--android-keystore-base64`, `--android-key-alias`, `--android-key-pass`, `--android-store-pass` Signing credentials.
- `--android-app-version` Integer ApplicationVersion (auto-generated from semver if omitted).
- `--android-app-display-version` Display string for version name (defaults to `--version`).

Notes:
- No GitHub token is needed for export.
- For Linux packaging, the tool builds for the current machine architecture by default (e.g., linux-x64 on x64 hosts).

Examples:
- Export Windows + Linux:
  - `dotnet run --project src/DotnetDeployer.Tool -- export --solution /abs/path/YourApp.sln --version 1.2.3 --platform windows linux --output /abs/path/out`
- Export Android (requires signing):
  - `export ANDROID_KEYSTORE_BASE64={{ANDROID_KEYSTORE_B64}}`
  - `dotnet run --project src/DotnetDeployer.Tool -- export \`
    `--solution /abs/path/YourApp.sln \`
    `--version 1.2.3 \`
    `--package-name YourApp \`
    `--app-id com.example.yourapp \`
    `--platform android \`
    `--android-keystore-base64 "$ANDROID_KEYSTORE_BASE64" \`
    `--android-key-alias {{ANDROID_KEY_ALIAS}} \`
    `--android-key-pass {{ANDROID_KEY_PASS}} \`
    `--android-store-pass {{ANDROID_STORE_PASS}} \`
    `--output /abs/path/out`
- Export Linux + WASM, including site:
  - `dotnet run --project src/DotnetDeployer.Tool -- export --solution /abs/path/YourApp.sln --version 1.2.3 --platform linux wasm --include-wasm --output /abs/path/out`

---

## WebAssembly to GitHub Pages: separate repository (Planned)
Some teams prefer hosting the WASM site in a different repository than the one used for releases. The planned enhancement will allow specifying a separate Pages repository for WebAssembly.

Proposed API (subject to change):
```csharp
var result = await deployer.CreateRelease()
    .ForSolution("/abs/path/YourApp.sln")
    .ForRepository(owner: "acme", repository: "yourapp", apiKey: githubToken)
    .WithVersion("1.2.3")
    .WithPackageName("YourApp")
    .TargetPlatforms(TargetPlatform.Windows | TargetPlatform.Linux | TargetPlatform.WebAssembly)
    .ConfigureWebAssembly(cfg => cfg
        .Project("src/YourApp.Browser/YourApp.Browser.csproj")
        .PagesRepository(owner: "acme", repository: "yourapp-pages", branch: "gh-pages")) // branch optional
    .Run();
```

Proposed CLI flags (subject to change):
- `--pages-owner OWNER`
- `--pages-repository REPO`
- `--pages-branch BRANCH` (optional)

Example (planned):
- `export GITHUB_TOKEN={{GITHUB_TOKEN}}`
- `dotnet run --project src/DotnetDeployer.Tool -- github pages \`
  `--solution /abs/path/YourApp.sln \`
  `--version 1.2.3 \`
  `--pages-owner acme \`
  `--pages-repository yourapp-pages \`
  `--pages-branch gh-pages`

Branch resolution when omitted (planned):
- Attempt to detect the repository default branch via the GitHub API.
- Fallback to a common branch name such as `gh-pages` or `main`.

---

## Security notes
- Prefer environment variables for secrets. Do not echo them to the console.
- Avoid printing tokens in logs or command output. The tool consumes `GITHUB_TOKEN` and `NUGET_API_KEY` via environment variables or explicit flags.
- For Android, pass the keystore as Base64 (e.g., `ANDROID_KEYSTORE_BASE64`) and provide passwords via environment variables.

## Troubleshooting
- Version inference:
  - If `--version` is omitted, the tool tries GitVersion; if that fails, it falls back to `git describe`.
- Owner/repo inference:
  - If `--owner`/`--repository` are omitted, they are inferred from `git remote origin`.
- Project discovery:
  - If discovery fails for your prefix, the tool logs available prefixes based on `.Desktop`, `.Browser`, `.Android` projects found in the solution.
- Pages deployment:
  - Pages deployment is best-effort. Failures do not cancel the GitHub release; check logs for the warning and run the Pages step separately if needed.

---

© DotnetDeployer contributors.

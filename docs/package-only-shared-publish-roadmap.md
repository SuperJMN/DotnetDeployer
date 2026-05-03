# Roadmap: Package-Only Jobs and Shared Publish

## Goal

Make DotnetDeployer the build/release orchestrator that can generate selected binary packages without publishing them, while preserving DotnetPackaging project metadata behavior and avoiding redundant `dotnet publish` runs.

DotnetDeployer remains responsible for:

- reading `deployer.yaml`
- resolving versions
- selecting projects and package targets
- coordinating publish/package operations
- publishing to NuGet, GitHub Releases, and GitHub Pages when requested

DotnetDeployer must not take responsibility for Fleet workers, artifact downloads, or remote storage.

## Current State

- Normal deployment reads `deployer.yaml` and publishes configured outputs.
- Package generation currently calls DotnetPackaging `PackProject(...)` per target for Windows/Linux/macOS formats.
- `PackProject(...)` preserves important `.csproj` metadata, but also publishes internally, so multiple formats for the same RID can trigger multiple `dotnet publish` runs.
- A local `--package-only` contract has been introduced for selected package generation:
  - `--package-only`
  - `--package-project`
  - `--package-target <type>:<arch>`
  - `--output-dir`

## Target Design

DotnetDeployer should group requested targets by publish plan:

```text
project + rid + configuration + self-contained + single-file + trim + msbuild properties
```

Then it should:

1. Resolve version once.
2. Resolve selected package project and targets.
3. Build one publish output per publish plan.
4. Package every target that can share that publish output.
5. Write final artifacts to the requested `outputDir`.

For example:

```text
exe-setup:x64
msix:x64
```

should publish `win-x64` once, then create both artifacts from that publish directory.

## Implementation Phases

### Phase 1: Stabilize Package-Only CLI

- Keep the new CLI contract small and explicit:
  - `--package-only`
  - `--package-project <github.packages[].project>`
  - `--package-target <type>:<arch>` repeated
  - `--output-dir <path>`
- Document supported target names and aliases.
- Ensure `--package-only` never publishes NuGet, GitHub Releases, or GitHub Pages.
- Ensure output is written only under `--output-dir` or `github.outputDir`.

### Phase 2: Introduce Publish Plans

- Add a `PackagePublishPlan` model.
- Map each package target to a publish plan:
  - Linux formats use `linux-<arch>`.
  - Windows formats use `win-<arch>`.
  - macOS formats use `osx-<arch>`.
  - Android remains special for now because MSBuild directly produces APK/AAB artifacts.
- Add tests proving that compatible targets are grouped and incompatible targets are separated.

### Phase 3: Consume DotnetPackaging Project Context

- After DotnetPackaging exposes `ProjectPackagingContext`, use it instead of calling `PackProject(...)` directly for grouped formats.
- Preserve current metadata defaults:
  - executable name
  - product/name
  - company/vendor
  - description
  - license
  - URL
  - terminal mode
- Keep the current `PackProject(...)` path until the shared-publish path reaches parity.

### Phase 4: Shared Publish for Windows/Linux/macOS

- Publish once per plan using DotnetPackaging `DotnetPublisher` or an equivalent adapter.
- Pass the published container plus context-derived options to each format packager.
- Keep generated artifact naming centralized in `PackageNaming`.
- Keep phase markers:
  - `package.publish.<rid>`
  - `package.generate.<type>.<arch>`

### Phase 5: Android Follow-Up

- Keep APK/AAB direct publish behavior in the first implementation.
- Later, consider a `DotnetPackaging.Android` layer if Android packaging grows beyond "copy MSBuild artifact and sign".

## Acceptance Criteria

- `dotnetdeployer --package-only --package-target exe-setup:x64 --output-dir ...` generates only the requested package.
- Multiple targets sharing a RID publish once.
- Package metadata remains equivalent to the old `PackProject(...)` path.
- Normal deploy behavior remains unchanged.
- DotnetDeployer still works as a CLI process for Fleet workers.

## Manual NuGet Publishing When GitHub Is Unavailable

Use this path when the packages need to be available from NuGet before GitHub Releases are back.

```bash
cd /mnt/fast/Repos/DotnetDeployer

VERSION=1.2.3
OUT=./artifacts/nuget-manual

dotnet restore
dotnet build -c Release -p:ContinuousIntegrationBuild=true -p:Version=$VERSION --no-restore
dotnet pack -c Release --no-build -p:IncludeSymbols=false -p:SymbolPackageFormat=snupkg -p:Version=$VERSION -o "$OUT"

find "$OUT" -name '*.nupkg' ! -name '*.symbols.nupkg' -print0 |
  xargs -0 -I{} dotnet nuget push "{}" \
    --api-key "$NUGET_API_KEY" \
    --source https://api.nuget.org/v3/index.json \
    --skip-duplicate
```

After publishing:

```bash
dotnet dnx dotnetdeployer.tool --help
dotnet dnx dotnetdeployer.tool --package-only --help
```

Verify that the newly published tool exposes the package-only options before wiring Fleet to it.


# Shared Application Info Audit

## Status

DotnetPackaging `from-project` and DotnetDeployer now share project-derived
application information through `DotnetProjectKit`.

`DotnetProjectKit` owns metadata extraction, desktop identity normalization,
project asset discovery and common runtime target mapping. DotnetPackaging and
DotnetDeployer keep only product-specific adapters and orchestration.

## Removed Duplication

The extraction removed the previous overlap in these areas:

- MSBuild metadata extraction:
  - `DotnetPackaging.ProjectMetadataReader`
  - `DotnetDeployer.Msbuild.MsbuildMetadataExtractor`
- Project-aware defaults:
  - `DotnetPackaging.ProjectMetadataDefaults`
  - `DotnetPackaging.ProjectPackagingContext`
  - `DotnetDeployer.Msbuild.DesktopProjectIdentity`
  - `DotnetDeployer.Packaging.Linux.LinuxPackageOptions`
- Application assets:
  - `DotnetPackaging.IconDiscovery`
  - `DotnetDeployer.Msbuild.ProjectIconResolver`
  - `DotnetDeployer.Packaging.Linux.AppImageIconContainer`
- Publish and runtime targeting:
  - `DotnetPackaging.Publish.DotnetPublisher`
  - `DotnetPackaging.Publish.ProjectPublishRequest`
  - `DotnetDeployer.Domain.Architecture`
  - `DotnetDeployer.Versioning.PublishVersionProperties`

## Shared Model

`ApplicationInfo` is the shared, project-facing model. Packaging-specific types
such as `FromDirectoryOptions` and deployer-specific package generation inputs
are adapters around it.

Important first-class fields:

- Identity: `Id`, `PackageName`, `DisplayName`, `ExecutableName`,
  `StartupWmClass`, `IsTerminal`.
- Versioning: `Version`, `RepositoryUrl`, `VcsGit`, `VcsBrowser`.
- Descriptions: `Summary`, `Comment`, `Description`.
- Ownership: `Authors`, `Creator`, `Company`, `Vendor`, `Maintainer`,
  `Copyright`.
- Legal and links: `License`, `ProjectUrl`, `Homepage`, `SupportUrl`.
- Taxonomy: `MainCategory`, `AdditionalCategories`, `Keywords`, `Tags`.
- Assets: `Icon`, `Logo`, `Screenshots`, plus typed variants for png, svg,
  ico, icns, Android adaptive icon parts, and platform-specific logos.
- Platform hints: service definition, macOS bundle identifier, Windows publisher
  data, Linux desktop category data, Android target framework.

Each field should keep source information for diagnostics:

```text
value + source
```

Initial sources:

- `override`: explicit CLI/library/deployer config value.
- `msbuild`: evaluated project property.
- `convention`: discovered from file layout.
- `default`: deterministic fallback.

## Resolution Contract

`ApplicationInfoResolver` accepts:

- project path
- optional `ApplicationInfoOverrides`
- optional resolver settings for convention roots and asset names
- logger

Precedence:

```text
explicit overrides
> deployer/package command config
> evaluated MSBuild project properties
> project/file conventions
> deterministic defaults
```

Important MSBuild inputs:

- `AssemblyName`
- `AssemblyTitle`
- `Product`
- `PackageId`
- `Version`
- `Authors`
- `Company`
- `Description`
- `Copyright`
- `PackageLicenseExpression`
- `PackageProjectUrl`
- `RepositoryUrl`
- `ApplicationIcon`
- `PackageIcon`
- `OutputType`
- `TargetFramework`
- `TargetFrameworks`
- `IsPackable`

Initial asset conventions:

- current project directory
- referenced project directories
- repository root up to `.git`
- `Assets/`
- `Resources/`
- `wwwroot/`
- common names: `icon.svg`, `icon.png`, `icon-256.png`, `icon-512.png`,
  `logo.svg`, `logo.png`, `app.png`, `app.ico`

Desktop identity rules should live here too. For Avalonia desktop hosts,
`*.Desktop` is usually a host artifact, so the default display identity and
`StartupWMClass` should prefer the host-free application name unless explicitly
overridden.

## Adapters

DotnetPackaging adapters:

- `ApplicationInfo -> FromDirectoryOptions`
- `ApplicationInfo -> PackageMetadata`
- `ApplicationInfo -> AppImageMetadata`
- format-specific helpers for DMG and EXE metadata where generic options are
  not enough

DotnetDeployer adapters:

- `ApplicationInfo -> package artifact naming`
- `ApplicationInfo -> package generator inputs`
- `ApplicationInfo + deployment version -> ProjectPublishRequest`
- `ApplicationInfo assets -> AppImage icon container enrichment`

## Extraction Boundary

Responsibilities owned by `DotnetProjectKit`:

- project metadata extraction through `dotnet msbuild`
- XML fallback for non-evaluable projects
- project reference discovery for asset lookup
- application identity normalization
- icon/logo/screenshot discovery
- RID and architecture mapping
- common runtime target construction

Responsibilities intentionally kept out:

- actual package writers such as AppImage, Deb, Rpm, Dmg, Exe, Msix
- GitHub/NuGet deployment
- Fleet worker behavior
- release orchestration
- Android SDK provisioning and signing

## Acceptance Scenarios

- MSBuild-only project: description, authors, company, version, license and URL
  are resolved without file conventions.
- Convention-only assets: a project with `Assets/icon.png` and no icon property
  produces an icon asset.
- Referenced Avalonia app assets: a desktop host finds an icon in a referenced
  app project.
- Explicit overrides: CLI/config values win over MSBuild and conventions.
- Avalonia `.Desktop` host: display name and `StartupWMClass` normalize to the
  host-free application identity unless explicitly overridden.
- Shared publish: multiple package formats can consume one published container
  without losing application metadata.

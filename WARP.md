# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Contexto del proyecto (alto nivel)
- Estructura: `src/DotnetDeployer` (librería), `src/DotnetDeployer.Tool` (CLI con System.CommandLine), `test/DotnetDeployer.Tests` (xUnit). Artefactos temporales en `out/`, paquetes en `nupkg/`. Paquetes centralizados en `src/Directory.Packages.props`.
- Frameworks: librería, CLI y tests apuntan a `net10.0`.

## Comandos habituales
- Build solución completa: `dotnet build DotnetDeployer.sln -c Release`
- Tests (todo): `dotnet test test/DotnetDeployer.Tests -c Release`
- Tests unitarios (excluye Integration): `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName!~Integration"`
- Un test concreto (ejemplo):
  - `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName=DotnetDeployer.Tests.ApkNamingTests.Returns_only_signed_apk_without_suffix"`
  - Por clase: `--filter "FullyQualifiedName~ApkNamingTests"`
- Formato: `dotnet format --verify-no-changes` (o `dotnet format` para aplicar cambios)
- Ayuda de la CLI: `dotnet run --project src/DotnetDeployer.Tool -- --help`

### CLI (resumen)
- NuGet (pack/push): `dotnet run --project src/DotnetDeployer.Tool -- nuget --solution <.sln> [--version <semver>] [--no-push]`
- Releases GitHub: `dotnet run --project src/DotnetDeployer.Tool -- github release --solution <.sln> --version <semver> [--platform windows linux android] [--no-publish]`
- GitHub Pages (WASM): `dotnet run --project src/DotnetDeployer.Tool -- github pages --solution <.sln> --version <semver> [--no-publish]`
- Exportar artefactos sin publicar: `dotnet run --project src/DotnetDeployer.Tool -- export --solution <.sln> --version <semver> --platform <...> --output <dir>`

## Arquitectura (big picture)
- Core de empaquetado y publicación (`src/DotnetDeployer`):
  - Dotnet (wrapper de invocaciones `dotnet`) y Command (ejecución de procesos) abstraen el shell.
  - Packager crea artefactos por plataforma apoyándose en DotnetPackaging.*:
    - Windows: `.exe` y/o `.msix` (via `Platforms.Windows`)
    - Linux: `.AppImage`, `.flatpak`, `.rpm`, `.deb`
    - Android: `.apk`/`.aab` firmado (via `Platforms.Android`)
    - WebAssembly: sitio (contenido de `wwwroot`)
  - Publisher usa Octokit para crear releases y subir assets a GitHub.
  - Deployer orquesta Packager y Publisher y ofrece una API fluida (builder) para configurar versión, metadatos y proyectos por plataforma.
- CLI (`src/DotnetDeployer.Tool`):
  - System.CommandLine define comandos `nuget`, `github release`, `github pages` y `export`, que delegan en la librería.
  - Logging con Serilog (consola). Los tests integran `Serilog.Sinks.XUnit`.
- Versionado y descubrimiento:
  - Si no se pasa `--version`, se intenta GitVersion y después `git describe`.
  - Owner/repo se infiere de `git remote origin` si no se especifican `--owner`/`--repository`.

## Convenciones operativas para Warp
- Conversación/asistencia en español; código, comentarios y commits en inglés.
- PR: usar texto sin escapar en asunto y cuerpo.
- No cerrar la terminal. Evitar comandos interactivos salvo necesidad. Cuidado con comillas simples/dobles en comandos.
- Variables de entorno usadas por la herramienta (no se imprimen): `NUGET_API_KEY`, `GITHUB_TOKEN`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS`, `ANDROID_STORE_PASS`.

## Pruebas
- xUnit en `test/DotnetDeployer.Tests` (namespace `Integration` para integraciones filtrables).
- Ejecutar la suite completa antes de cambiar lógica sensible de empaquetado/publicación.

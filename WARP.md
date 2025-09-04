# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Contexto del repositorio

Solución .NET con tres proyectos:
- src/DotnetDeployer: biblioteca principal con la lógica de empaquetado, publicación y orquestación.
- src/DotnetDeployer.Tool: CLI (System.CommandLine) que expone comandos de publicación de NuGet y creación de releases de GitHub.
- test/DotnetDeployer.Tests: pruebas (xUnit). Algunas pruebas de integración requieren rutas externas y credenciales; filtra si quieres ejecutar solo unitarias.

Gestión centralizada de versiones de paquetes en src/Directory.Packages.props. TargetFramework: net8.0 (librería y tool); tests usan net9.0.

## Comandos habituales

- Restaurar y compilar toda la solución
  - dotnet build DotnetDeployer.sln -c Release

- Ejecutar todas las pruebas
  - dotnet test test/DotnetDeployer.Tests -c Release

- Ejecutar solo pruebas unitarias (excluye integración por nombre de espacio)
  - dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName!~Integration"

- Ejecutar un test específico (ejemplos)
  - dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName=DotnetDeployer.Tests.ApkNamingTests.Returns_only_signed_apk_without_suffix"
  - dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName~ApkNamingTests"

- Formateo (si tienes dotnet-format disponible)
  - dotnet format --verify-no-changes
  - dotnet format

- Ejecutar el CLI desde el código fuente
  - dotnet run --project src/DotnetDeployer.Tool -- --help

### Uso del comando "nuget" del CLI
Publica paquetes NuGet a partir de proyectos descubiertos en una solución (o explícitos). Variables y flags relevantes:
- Versión: --version 1.2.3 o inferida automáticamente (GitVersion/gid describe) si omites --version.
- API key: --api-key o variable de entorno NUGET_API_KEY.

Ejemplos:
- Usando env var para la API key (recomendado):
  - export NUGET_API_KEY={{NUGET_API_KEY}}
  - dotnet run --project src/DotnetDeployer.Tool -- nuget --solution DotnetDeployer.sln --version 1.2.3

- Proyectos explícitos y solo empaquetar (sin push):
  - dotnet run --project src/DotnetDeployer.Tool -- nuget --project src/DotnetDeployer/DotnetDeployer.csproj --version 1.2.3 --no-push

- Descubrimiento con patrón de nombre (excluye tests/demos/samples/desktop):
  - dotnet run --project src/DotnetDeployer.Tool -- nuget --solution path/to/YourApp.sln --name-pattern "YourApp*" --version 1.2.3

### Uso del comando "release" del CLI
Crea artefactos por plataforma (Windows, Linux, Android, WebAssembly) y, opcionalmente, una release en GitHub con subida de assets.

- Autodescubre proyectos en la solución según sufijos: .Desktop (Windows/Linux), .Browser (WASM), .Android (Android). Puedes guiar con --prefix.
- Token GitHub: --github-token o env var GITHUB_TOKEN. Owner/repo inferidos de "git remote origin" si no se pasan --owner y --repository.
- --no-publish genera artefactos sin crear la release (alias deprecado: --dry-run).
- Android: requiere firma. Pasa el keystore en Base64 y credenciales; si no das --android-app-version, se deriva de la semver.

Ejemplo completo (Windows+Linux+Android+WASM) sin publicar aún:
- export GITHUB_TOKEN={{GITHUB_TOKEN}}
- export ANDROID_KEYSTORE_BASE64={{ANDROID_KEYSTORE_B64}}
- dotnet run --project src/DotnetDeployer.Tool -- release \
    --solution /abs/path/YourApp.sln \
    --version 1.2.3 \
    --package-name YourApp \
    --app-id com.example.yourapp \
    --app-name "Your App" \
    --platform windows linux android wasm \
    --android-keystore-base64 "$ANDROID_KEYSTORE_BASE64" \
    --android-key-alias {{ANDROID_KEY_ALIAS}} \
    --android-key-pass {{ANDROID_KEY_PASS}} \
    --android-store-pass {{ANDROID_STORE_PASS}} \
    --no-publish

Para publicar de verdad, elimina --no-publish y opcionalmente define --tag y --release-name. Si omites --version, se intenta inferir con GitVersion (fallback a git describe).

## Arquitectura (alto nivel)

- Deployer (src/DotnetDeployer/Deployer.cs)
  - Punto de entrada de alto nivel para: empaquetar para plataformas y crear releases de GitHub. Usa:
    - Context: agrupa IDotnet (wrapper de dotnet publish/pack/push), ICommand (ejecución de procesos), ILogger y IHttpClientFactory.
    - Packager: delega la creación de artefactos por plataforma.
    - Publisher: publica paquetes NuGet y despliega sitios WASM a GitHub Pages.
  - Expone CreateRelease() que devuelve un ReleaseBuilder y métodos conveniencia (CreateGitHubRelease, CreateGitHubReleaseForAvalonia).

- Packager (src/DotnetDeployer/Core/Packager.cs)
  - Windows: WindowsDeployment -> genera .exe self-contained por arquitectura (x64, arm64). Nombres: {PackageName}-{Version}-windows-{arch}.exe
  - Linux: LinuxDeployment -> publica y convierte a AppImage ({PackageName}-{Version}-linux-{arch}.appimage)
  - Android: AndroidDeployment -> publica APKs, filtra los firmados que contienen ApplicationId y los renombra a {PackageName}-{DisplayVersion}-android[-sufijo].apk (evita duplicados).
  - WASM: publica proyecto y extrae wwwroot como sitio (WasmApp) para despliegue (no se adjunta por defecto como asset de release).

- Publisher (src/DotnetDeployer/Core/Publisher.cs)
  - NuGet: escribe temporalmente el .nupkg y hace dotnet nuget push con --skip-duplicate.
  - GitHub Pages: clona rama, copia contenidos de WasmApp (añade .nojekyll), commit y push con autor/committer configurados.

- Servicios GitHub (src/DotnetDeployer/Services/GitHub)
  - GitHubReleaseUsingGitHubApi: usa Octokit para crear la release y subir assets. El cuerpo de la release incluye commit y mensaje (via GitInfo) para trazabilidad.

- CLI (src/DotnetDeployer.Tool/Program.cs)
  - Comandos:
    - nuget: descubre proyectos packeables en una .sln (excluye tests/demos/samples/desktop), calcula versión si falta, empaqueta y opcionalmente hace push.
    - release: descubre proyectos por sufijo (.Desktop/.Browser/.Android), resuelve owner/repo desde git, deriva ApplicationVersion Android desde semver si no se pasa explícito y permite saltar publicación.
  - Resolución de solución: si no se pasa --solution, busca *.sln hacia arriba en el árbol.
  - Reglas de Android: si --app-id no se pasó, intenta leer <ApplicationId> del .csproj Android; si falta, construye uno de fallback (io.{owner}.{packageName}) saneado.

- Utilidades y contratos
  - IDotnet/Dotnet: abstracción de operaciones dotnet (publish/pack/push), añade metadatos (commit y release notes) en pack.
  - ReleaseBuilder/ReleaseConfiguration/ReleasePackagingStrategy: builder fluido para preparar qué plataformas empaquetar y con qué opciones, y estrategia para ejecutarlas y recolectar INamedByteSource.
  - ArgumentsParser: construye cadenas de argumentos y propiedades MSBuild (-p:Prop=Val) de forma segura.

## Notas de uso (credenciales y seguridad)

- Prefiere variables de entorno para secretos (no las imprimas):
  - NUGET_API_KEY para publicación a NuGet.
  - GITHUB_TOKEN para crear releases en GitHub.
  - Para Android: pasa el keystore como Base64 (ANDROID_KEYSTORE_BASE64 en los ejemplos), y las contraseñas/alias como variables.
- Si ves secretos redactados en prompts, sustituye por placeholders {{NOMBRE_SECRETO}} en los comandos y no intentes leer su valor.

## Reglas del proyecto para agentes

- Conversaciones e instrucciones: en Español.
- Código, comentarios de código, mensajes de commit y resúmenes de PR: en Inglés.
- Preferencias de estilo (aplicables al evolucionar el código aquí):
  - Usar CSharpFunctionalExtensions; mantener un enfoque funcional cuando sea práctico.
  - Preferir programación reactiva si no complica en exceso.
  - No usar el sufijo Async en métodos que devuelven Task.
- Contexto externo: este repo proporciona el tool "dotnetdeployer". En repos como Zafiro.Avalonia, prioriza este tool frente a Nuke Build para empaquetado/publicación.


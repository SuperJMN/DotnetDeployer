# Repository Guidelines

## Project Structure & Module Organization
La solución `DotnetDeployer.sln` contiene tres bloques principales: `src/DotnetDeployer` aloja la biblioteca con la lógica de empaquetado y publicación; `src/DotnetDeployer.Tool` implementa la CLI basada en System.CommandLine; `test/DotnetDeployer.Tests` agrupa las pruebas xUnit (unitarias y algunas integraciones). Artefactos temporales viven bajo `out/` y paquetes generados se depositan en `nupkg/`. Las dependencias compartidas de los proyectos se gestionan en `src/Directory.Packages.props`.

## Build, Test, and Development Commands
- `dotnet build DotnetDeployer.sln -c Release`: restaura, compila y valida dependencias para toda la solución.
- `dotnet test test/DotnetDeployer.Tests -c Release`: ejecuta la batería completa de pruebas.
- `dotnet test test/DotnetDeployer.Tests -c Release --filter "FullyQualifiedName!~Integration"`: limita la ejecución a pruebas unitarias cuando las integraciones requieren credenciales.
- `dotnet format` / `dotnet format --verify-no-changes`: aplica y verifica el formato estándar de C#.
- `dotnet run --project src/DotnetDeployer.Tool -- --help`: explora los comandos disponibles de la CLI.

## Coding Style & Naming Conventions
Usa C# 12 con indentación de 4 espacios, expresiones y patrones cuando mantengan la legibilidad. Prioriza el enfoque funcional soportado por `CSharpFunctionalExtensions` (`Result`, `Maybe`) y evita sufijos `Async` en métodos que devuelven `Task`. Los nombres de proyectos siguen el patrón `<Producto>.<Plataforma>` (Desktop/Browser/Android). Los mensajes de log usan Serilog y deben ser descriptivos. Código, comentarios y commits permanecen en inglés aun cuando la discusión se haga en español.

## Testing Guidelines
Las pruebas residen en `DotnetDeployer.Tests`, dirigidas por xUnit targeting `net9.0`. Crea clases con sufijo `Tests` y métodos descriptivos en PascalCase. Distingue integraciones mediante el espacio de nombres `Integration` para habilitar filtrado. Añade pruebas unitarias cuando introduzcas comportamiento nuevo y reutiliza fixtures existentes para escenarios de publicación y empaquetado. Ejecuta la suite completa antes de abrir un PR si tocas lógica de `Packager`, `Publisher` o `ReleaseBuilder`.

## Commit & Pull Request Guidelines
Sigue mensajes cortos, en imperativo y en inglés (por ejemplo, `Add selectable Android package formats`). Referencia issues con `#` cuando aplique. En los PR explica el objetivo, detalla impactos en empaquetado o despliegue, enlaza builds relevantes y adjunta capturas para cambios visibles en CLI. Indica cómo probaste la funcionalidad (`dotnet test`, ejecuciones de la herramienta) y señala cualquier deuda técnica pendiente.

## Security & Configuration Tips
Nunca expongas secretos en texto plano. Usa variables como `NUGET_API_KEY`, `GITHUB_TOKEN`, `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`, `ANDROID_KEY_PASS` y `ANDROID_STORE_PASS`. Prefiere rutas absolutas al invocar `--solution` y evita subir archivos generados en `out/` o `nupkg/`.

# AGENTS

Guía operativa y de estilo para trabajar con este repositorio usando agentes.

Precedencia de reglas
- Las reglas se aplican en orden de precedencia creciente: las que aparecen más tarde prevalecen sobre las anteriores.
- Las reglas de proyecto (asociadas a rutas concretas) tienen prioridad sobre reglas personales.
- Entre reglas de proyecto, las de subdirectorios prevalecen sobre las del directorio padre.

## Comunicación y formato
- Conversaciones y asistencia: en español.
- Código, mensajes de commit, comentarios de código y resúmenes de PR: en inglés.
- PR: usar texto sin escapar en asunto y cuerpo.

## Terminal y ejecución
- No cerrar la terminal ni ejecutar comandos que finalicen la sesión.
- Evitar comandos interactivos salvo que sea estrictamente necesario.
- Extremar cuidado con comillas simples y dobles en los comandos.

## Despliegue y CI

- Logs de despliegue: incluir sistema operativo, tipo de paquete y arquitectura. Ejemplos:
  - “[INFO Linux AppImage X64] …”
  - “[INFO macOS DMG X64] …”
  - “[INFO Android ARM64] …” 
  - ◦  “[INFO Windows Installer X64] …” 
  - “[INFO Windows SFX X64] …”
- Proyectos de librería y aplicaciones: preparar azure-pipelines.yml cuando proceda. Ver referencia en \mnt\fast\Repos\Zafiro\azure-pipelines.yml.
- La build debe pasar correctamente antes de fusionar una PR.

## Lineamientos de diseño y estilo (C# / Reactive)

- Preferir programación funcional y reactiva cuando no complique en exceso.
- Validación: preferir ReactiveUI.Validations.
- Result handling: usar CSharpFunctionalExtensions cuando sea posible.
- Convenciones:
  - No usar sufijo “Async” en métodos que devuelven Task. 
  - No usar guiones bajos para campos privados. 
  - Evitar eventos (salvo indicación explícita). 
  - Favorecer inmutabilidad; mutar solo lo estrictamente necesario.
  - Evitar poner lógica en Observable.Subscribe; preferir encadenar operadores y proyecciones.

# Errores y notificaciones

- Para flujos de Result<T> usar el operador Successes.
- Para fallos, HandleErrorsWith() empleando INotificationService para notificar al usuario.

# Toolkit Zafiro

Es mi propio toolkit. Disponible en https://github.com/SuperJMN/Zafiro. Muchos de los métodos que no conozcas pueden formar parte de este toolkit. Tenlo en consideración.

# Manejo de bytes (sin Streams imperativos)

- Usar Zafiro.DivineBytes para flujos de bytes evitables con Stream.
- ByteSource es la abstracción observable y componible equivalente a un stream de lectura.

# Refactorización guiada por responsabilidades

1. Leer el código y describir primero sus responsabilidades.
2. Enumerar cada responsabilidad como una frase nominal clara.
3. Para cada responsabilidad, crear una clase o método con nombre específico y semántico.
4. Extraer campos y dependencias según cada responsabilidad.
5. Evitar variables compartidas entre responsabilidades; si aparecen, replantear los límites.
6. No introducir patrones arbitrarios; mantener la interfaz pública estable.
7. No eliminar logs ni validaciones existentes.

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
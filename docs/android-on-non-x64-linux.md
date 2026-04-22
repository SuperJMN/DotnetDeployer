# Android publishing on non-x64 Linux hosts

## Background

`Microsoft.Android.Sdk.Linux` (the workload pack `dotnet publish` consumes
when the target framework is `net*-android`) ships several MSBuild-side
native binaries that are **x86_64-only**:

- `tools/Linux/aapt2`             — invoked as a separate process.
- `tools/libMono.Unix.so`         — P/Invoked from MSBuild tasks.
- `tools/libZipSharpNative-*.so`  — P/Invoked from MSBuild tasks.

The package is still named `Microsoft.Android.Sdk.Linux` (no host arch
suffix) and the latest .NET 11 preview at the time of writing
(`36.99.0-preview.3.10`, 2026-04-14) keeps these binaries x86_64-only.
There is no `linux-arm64` host variant published.

Consequence: a Linux arm64 worker (Raspberry Pi, Apple Silicon Linux VM,
Ampere CI runner, …) **cannot** drive `dotnet publish` against an Android
target framework natively. `qemu-user-static` only solves the standalone
process case (`aapt2`); it cannot inject an x86_64 `.so` into a native
arm64 `dotnet` process.

## Design goal

DotnetFleet's worker pool is intentionally **fungible** ("any worker can
take any deployment"). Discriminating workers by capability (e.g. Android
only on x86_64 nodes) breaks that contract and shifts the routing burden
onto the coordinator and the user.

DotnetDeployer must therefore make Android publishing work on **every**
supported host architecture, even when the cost is higher (emulation).

## Approach: containerized x86_64 publish

When DotnetDeployer detects:

```
RuntimeInformation.IsOSPlatform(Linux)
  && RuntimeInformation.OSArchitecture != X64
  && project targets net*-android
```

it routes the Android publish through a `linux/amd64` Docker container
backed by `mcr.microsoft.com/dotnet/sdk:10.0`. On x86_64 hosts the native
path is unchanged — zero overhead for the common case. On non-x86_64
hosts the build runs under qemu-user emulation transparently.

### Required host capabilities (non-x64 path)

| Capability                | Why                                  | How DotnetDeployer reacts if missing |
| ------------------------- | ------------------------------------ | ------------------------------------ |
| `docker` (any recent)     | Run the amd64 SDK container          | Fail with explicit install hint      |
| `qemu-user-static` + binfmt registered for amd64 | Emulate x86_64 syscalls inside the container | Auto-register via `tonistiigi/binfmt --install amd64` (idempotent) |

We deliberately do **not** auto-install Docker via apt: the user must be
added to the `docker` group, which only takes effect on a new login
session — too brittle to silently chain.

### Mount layout inside the container

- `/work`            ← bind of the repo / project root (rw).
- `/root/.nuget`     ← bind of a host cache dir to avoid re-downloading
                       NuGet packages on every build.
- `/keystore`        ← bind of the temp keystore used for signing (ro).

### Commands run inside the container

```
dotnet workload restore <project>
dotnet publish <project> -c Release -f net*-android \
  -p:Version=… -p:ApplicationVersion=… -p:ApplicationDisplayVersion=… \
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=/keystore/<file> …
```

The resulting `.apk`/`.aab` lands under `/work/.../bin/Release/...`,
visible to the host immediately via the bind mount.

## Performance expectations

Benchmarks observed on a Raspberry Pi 4 (Cortex-A72, 4GB) with
`qemu-x86_64-static`:

| Operation                                      | Measured  |
| ---------------------------------------------- | --------- |
| `docker pull mcr.microsoft.com/dotnet/sdk:10.0`| ~3 min (one-off, ~1 GB) |
| `dotnet --version` inside emulated container   | ~17 s (cold) |
| `dotnet publish` of a small Avalonia Android app | TBD — expected 30–60 min vs ~2 min on x64 |

The trade-off is acceptable as long as worker fungibility is preserved.

## Out of scope (today)

- Auto-installing Docker on the worker.
- Detecting and offloading to a different worker on the coordinator.
- Caching the SDK image bytes on the coordinator and shipping them to
  workers (a future optimization to avoid every worker pulling 1 GB).

## Status / TODO

- [x] Diagnosed root cause (workload pack is host-x86_64-only).
- [x] Verified `linux/amd64` containers run on RPi4 arm64 with qemu binfmt.
- [x] Verified `mcr.microsoft.com/dotnet/sdk:10.0` boots emulated.
- [ ] Implement `IAndroidPublisher` abstraction with two strategies:
      `NativeAndroidPublisher` and `ContainerizedAndroidPublisher`.
- [ ] Strategy selection based on host arch + target framework.
- [ ] `EnsureDocker()` precondition check with actionable error.
- [ ] `EnsureBinfmtAmd64()` idempotent helper.
- [ ] Wire publish through container with bind mounts and NuGet cache.
- [ ] End-to-end validation: Pokemon APK built on rpi4 worker.
- [ ] Unit tests for the strategy selector.
- [ ] Document in main README.

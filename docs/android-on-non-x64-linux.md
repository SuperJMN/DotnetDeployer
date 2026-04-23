# Android publish on non-x64 Linux

**Status: works via shim overlay on Linux/arm64.** Other non-x64 Linux architectures (armhf, riscv64…) remain unsupported.

## What does and doesn't work today

| Host | Android publish |
|---|---|
| Linux x86_64 | Works natively |
| Windows x86_64 | Works natively |
| macOS (Intel + Apple Silicon) | Works natively |
| **Linux arm64** (Raspberry Pi, Ampere/Graviton, Linux VMs on Apple Silicon) | **Works via shim overlay** (see below) |
| Linux on any other non-x64 architecture | Fails — no shim available |

When a Fleet worker on Linux/arm64 picks up an Android-targeted job, `AndroidPrerequisitesInstaller` invokes the bootstrap script from [`SuperJMN/DotnetAndroidArm64Shims`](https://github.com/SuperJMN/DotnetAndroidArm64Shims) once per process, before doing anything else. The script is idempotent: it scans `~/.dotnet/packs/Microsoft.Android.Sdk.Linux/`, downloads the release tarball matching each installed pack version, verifies SHA256 sums, backs up the originals, and overlays the arm64 builds. Re-runs are no-ops.

After the overlay, the rest of the publish pipeline (JDK/SDK provisioning, `dotnet publish`, signing) runs unmodified. Validated on a Raspberry Pi 4 with a vanilla `dotnet new android` template against shim release `36.1.53`: signed APK in ~3m40s, installed and launched on a real Pixel device.

If the installed `Microsoft.Android.Sdk.Linux` pack version has no matching shim release, `EnsureAsync()` returns an actionable failure pointing at <https://github.com/SuperJMN/DotnetAndroidArm64Shims/releases>. Other targets in the same project (NuGet, deb, etc.) keep deploying.

## Why we don't route through a container (post-mortem)

The first instinct is "spin up a `linux/amd64` container with `qemu-user-static`, run `dotnet publish` inside, mount the result back". Tried it; doesn't work.

`qemu-user` emulating amd64 on aarch64 cannot run the .NET runtime reliably. Even a trivial `dotnet new console` segfaults inside `mcr.microsoft.com/dotnet/sdk:10.0` under `linux/amd64`. The PLINQ ETW provider initialization throws on startup; threading-heavy paths abort with SIGSEGV. Confirmed empirically with both Debian's qemu-user-static 5.2 and `tonistiigi/binfmt`'s qemu 9.x, on a Raspberry Pi 4 running Raspberry Pi OS 64-bit (kernel 6.x).

This isn't something a flag fixes — the .NET runtime makes assumptions about signal handling and futex semantics that qemu-user doesn't honour. It's a known class of bugs in the dotnet/runtime tracker. Kept here as historical context for "why we don't containerize".

## Why we don't reimplement the overlay in C#

We deliberately invoke `install-shims.sh` over `curl | bash` instead of downloading the tarball directly from C#:

- The bootstrap script is the **stable contract**. Its internals (release discovery, SHA256 verification, pack-version → tarball mapping, backup of originals) will evolve.
- Keeping the overlay logic in the shim repo lets it ship its own release cadence, CI and tests, separate from DotnetDeployer.
- `aapt2` has strict version-match against the pack (`error XA0111: Unsupported version of AAPT2`), and Microsoft bumps it on a roughly monthly cadence. Centralising the version-matrix logic in one place avoids drift.

## How this gets fully resolved upstream

If Microsoft ships a linux-arm64 host build of `Microsoft.Android.Sdk.Linux`, the shim overlay becomes redundant: the bootstrap script's release set goes empty and the call becomes a true no-op, with no changes needed in DotnetDeployer. Tracked upstream in <https://github.com/dotnet/android/issues/11184>.

# Android publish on non-x64 Linux

**Status: blocked / TODO.** Tracking a clean resolution rather than shipping a brittle workaround.

## What does and doesn't work today

| Host | Android publish |
|---|---|
| Linux x86_64 | Works natively |
| Windows x86_64 | Works natively |
| macOS (Intel + Apple Silicon) | Works natively |
| **Linux arm64** (Raspberry Pi, Ampere/Graviton, Linux VMs on Apple Silicon) | **Fails fast** with a clear error from `AndroidPublishExecutor` |

If a Fleet worker on Linux/arm64 picks up an Android-targeted job, DotnetDeployer aborts that target with a message pointing at the two tracking issues (see below). Other targets (desktop deb, Windows setup exe, NuGet packages) for the same project are unaffected and continue to publish.

## Why we can't just route through a container

The first instinct is "spin up a `linux/amd64` container with `qemu-user-static`, run `dotnet publish` inside, mount the result back". Tried it; doesn't work.

`qemu-user` emulating amd64 on aarch64 cannot run the .NET runtime reliably. Even a trivial `dotnet new console` segfaults inside `mcr.microsoft.com/dotnet/sdk:10.0` under `linux/amd64`. The PLINQ ETW provider initialization throws on startup; threading-heavy paths abort with SIGSEGV. Confirmed empirically with both Debian's qemu-user-static 5.2 and `tonistiigi/binfmt`'s qemu 9.x, on a Raspberry Pi 4 running Raspberry Pi OS 64-bit (kernel 6.x).

This isn't something a flag fixes — the .NET runtime makes assumptions about signal handling and futex semantics that qemu-user doesn't honour. It's a known class of bugs in the dotnet/runtime tracker.

## Why we can't just patch the SDK pack on disk

The `Microsoft.Android.Sdk.Linux` workload pack ships these binaries as **x86_64 ELFs only** — they're invoked from MSBuild tasks running inside the native arm64 dotnet process, so they have to be replaceable with arm64 builds:

- `tools/Linux/aapt2`
- `tools/libMono.Unix.so`
- `tools/libZipSharpNative-3-3.so`
- `tools/Linux/binutils/bin/{as,ld,llc,llvm-mc,llvm-objcopy,llvm-strip}` and friends (only needed for AOT)

We *could* build arm64 replacements ourselves and overlay them onto the pack. That's a real project — see [`SuperJMN/DotnetAndroidArm64Shims`](https://github.com/SuperJMN/DotnetAndroidArm64Shims). It's deliberately kept out of DotnetDeployer because:

- The maintenance burden is non-trivial (`aapt2` has strict version-match against the pack, and Microsoft bumps it monthly — `error XA0111: Unsupported version of AAPT2`).
- It deserves its own release cadence, CI, and test surface separate from the deployer.

## How this gets unblocked

Either of these closes the gap:

1. **Upstream provides linux-arm64 builds.** Asked here: <https://github.com/dotnet/android/issues/11184>. If accepted, the pack just works on arm64 and we delete the short-circuit in `AndroidPublishExecutor`.
2. **`DotnetAndroidArm64Shims` ships v1.** Then DotnetDeployer's `AndroidPrerequisitesInstaller` invokes its bootstrap to overlay the shim binaries before publish, and `AndroidPublishExecutor` lets the publish proceed natively.

Whichever lands first wins. Until then: clear failure with actionable links, no silent fallbacks, no half-working containerized path.

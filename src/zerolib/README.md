# ZeroLib minimal runtime library

There are two guiding principles:

1. Public API surface that doesn't exist in .NET cannot be added (i.e. source code compilable against zerolib needs to be compilable against .NET).
2. APIs that do hidden allocations cannot be added. If an API returns a fresh object instance, it's all good. If an API allocates as part of it's operation and doesn't return that as result, it's not good. We don't have a GC. This is all memory leaks.

To work in this codebase, simply add a file with a `Main` to this directory and `bflat build --stdlib:none`. You'll get a tight inner dev loop. But you can do things differently; more power to you.

## WASI / WebAssembly

`-d WASI -d WASM` builds zerolib for WebAssembly (`lib/wasi/wasm/zerolib.dll` in the layouts). bflat itself can't target WebAssembly: this build is meant for [NativeAOT-LLVM](https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM) (`--systemmodule:zerolib --targetos:wasi --targetarch:wasm`).

- **Platform calls** go to WASI's C library directly (`write`, `abort`, `calloc`, `nanosleep`, `__wasi_clock_time_get`) instead of `libSystem.Native`; compile with `--directpinvoke:libc`.
- **32-bit:** `WASM` takes the same paths as `X86`/`ARM` where pointer size matters.
- **Extra types** in `System/Runtime/Wasm.cs`: NativeAOT-LLVM resolves its whole helper table up front and substitutes a few members by name, so they must exist even when unused.
- **Static fields of reference types** are initialized at startup as on Linux, but WASM has no linker-defined section bounds and its relocations are absolute, so the module header comes from the native side (`ZeroGetModuleHeader`).

NativeAOT-LLVM doesn't emit zerolib's `[RuntimeExport]` methods on this target, so the native side has to provide the entry points the compiled code calls: `main` calling `__managed__Main`, `ZeroGetModuleHeader`, the shadow-stack and P/Invoke transitions (`RhpReversePInvokeAndPushSparseVirtualUnwindFrame`, `RhpPInvoke`, `RhpGcPoll`, ...), allocation (`RhpNewFast`, `RhpNewArray`, `RhpNewPtrArrayFast`, `RhpAssignRef`) and the exception hooks. [bflat_test's `wasm/runtime.c`](https://github.com/OnurGumus/bflat_test/blob/csharp15/wasm/runtime.c) is a complete implementation in under 80 lines of C.

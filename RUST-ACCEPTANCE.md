# PiP ClickThrough Rust 2.0 — Acceptance report

Validated on Windows x64 (KPC/KIRAA), 7 November 2026.

## Build and packaging

- Rust 1.99.1, target `x86_64-pc-windows-gnu`, LLVM `llvm-dlltool`, `rust-lld`.
- `build-rust.ps1`: Cargo unit tests and optimized Release build succeeded.
- Single executable in `Release-Portable-Rust\PiPClickThrough.exe`, 5,821,952 bytes.
- Release SHA-256:
  `CA2B285D3A03A8A0A50F8FF89706F57D8B0A315F71CEF5CB99EE4832D90D059A`.
- Verified the isolated executable launches and performs `--scan-to` without
  companion DLLs or a .NET runtime.
- GUI switched from stock Win32 controls to custom dark/cyan egui dashboard.

## Verified runtime behavior

- `cargo test --all-targets`: **3/3 PASS**.
- A real Chrome Picture-in-Picture window was detected using Win32 APIs.
- `test-pipctl.ps1`: **RESULT: PASS**. Correct browser/real HWND; all three
  style flags; interactive mode; reenable; opacity 75/100; restore.
- Legacy `pipctl.ps1` commands work against the Rust named-pipe server.
- Hotkey dispatch IDs 2/3/4/5 changed click-through, opacity and restore as expected.
- Concurrent `--recover-only` was rejected while the GUI held the mutex.
- **Crash-recovery test PASS**: forced termination after capture, then
  `--recover-only` restored original style `0x00200108`; journal became `[]`.
- **Tray exit test PASS**: app exited, background bridge disappeared, recovery
  journal became `[]` after managed PiP had been captured.
- GUI was captured and inspected on the user's actual Windows desktop.

## Observed resource usage (single sample, machine dependent)

- Working set: approximately 87.6 MiB.
- Idle CPU time: approximately 3.91 ms per second over a four-second interval.

## Boundaries and regression risk

- Verified integration with **Chrome**; Edge/Brave/Vivaldi/Opera/Chromium use
  the same scanner policy but were not individually tested.
- Browser video/DRM restrictions, exclusive fullscreen games and privileged
  browser windows may behave differently.
- Global hotkey dispatch was tested via Win32 messages, but every physical key
  combination was not exercised by a human user.
- The real UI should be used for normal operation; `--recover-only` must not
  be used while it is running. The mutex prevents concurrent mutation.
- Automatic capture only accepts strict PiP titles; unknown Chromium topmost
  windows require explicit manual confirmation.

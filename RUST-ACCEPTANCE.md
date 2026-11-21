# PiP ClickThrough Rust 2.0 — Acceptance report

## v2.1.1 — layered-window recovery order regression (21 November 2026)

- Found a real Win32 recovery edge case: when a previously layered
  window has lost `WS_EX_LAYERED`, the old `restore()` order called
  `SetLayeredWindowAttributes` **before** restoring the required style
  bit. A new test reproduced Windows error **87** on an isolated,
  hidden `STATIC` test HWND owned by the test process.
- Fixed by writing the original controlled extended style first,
  then restoring its original alpha/color key and topmost state.
  The existing identity check and failure-retaining recovery journal
  remain unchanged.
- Verified locally on KIRAA, Rust Windows GNU: prior test FAIL,
  corrected test PASS; `cargo test --locked --offline --all-targets`
  **6/6 PASS**, `cargo fmt -- --check` PASS and
  `cargo build --locked --offline --release` PASS.
- The test neither discovers nor modifies any real browser or Discord
  window, does not display a GUI and destroys its own hidden HWND.
  This is a focused native API regression, **not** a live Discord
  pop-out acceptance run or an independently validated security feature.
- Win32 API contract: [SetLayeredWindowAttributes](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setlayeredwindowattributes)
  requires a layered window.

## v2.1.1 — Discord channel-named pop-out detection (10 November 2026)

- Created and verified a per-user desktop shortcut at
  `C:\Users\esket\Desktop\PiP ClickThrough.lnk` pointing at the Rust Release EXE.
- Root cause: the real Discord Canary live pop-out was titled
  `Ma Nữ Ponyo` (HWND `0x2400B2`), not `Stream của ...`.
  When minimized, `GetWindowRect` only reported 160×28, so the original
  scanner discarded the window.
- Added a dedicated `discordPopout` candidate path: require the real
  Discord process, Chromium top-level window, a normal Discord main window
  in the **same PID**, and reject main chat, hidden overlay and fullscreen.
- Added minimized-window discovery using `GetWindowPlacement` restored
  bounds; minimized candidates are shown but not auto-captured.
- Live QA: `pipctl list` detected a single verified `discordPopout`,
  title `Ma Nữ Ponyo`, without adding the main Discord window.
- With Auto-detect, Auto-enable and AutoDiscordStreams enabled, the real
  pop-out was automatically captured with `WS_EX_LAYERED`,
  `WS_EX_TRANSPARENT` and `WS_EX_NOACTIVATE` all set.
- Turning click-through off restored original style `0x00200108`; turning
  it back on produced `0x08280128`.
- Minimize/restore QA: while minimized, the pop-out remained in the candidate
  list and was not auto-captured; after restoring it was recaptured.
- Forced-crash recovery and restart: original style restored, journal
  became `[]`, restart reconnected the same live pop-out.
- GUI screenshot inspected and confirmed `Discord Canary • Discord pop-out`,
  active PASS-THROUGH, and all three relevant preferences ON.
- `cargo check` PASS; `cargo test --all-targets`: **5/5 PASS**.
- Discord Stable and PTB share the code path but were not individually
  verified with a live pop-out in this acceptance run.

## v2.1.0 — Discord Stream pop-out integration (8 November 2026)

- Observed a real pinned Discord Canary pop-out on KIRAA:
  `Stream của thwhnim`, HWND `0x51123C`, process `DiscordCanary.exe`,
  window class `Chrome_WidgetWin_1`, initial extended style `0x00200108`.
- Scanner: classified the window as `discordStream`, verified and topmost;
  correctly omitted main Discord chat and hidden Discord overlay windows.
- The **existing app**, with Auto-detect and Auto-enable turned on,
  auto-captured the stream as if it were a browser PiP.
- Runtime: `pipctl list`, `status`, `verify`, `on`, `off` passed.
  Click-through extended style `0x08280128`; Windows reported
  `transparent=true`, `layered=true`, `nonActivating=true`.
- Turning off click-through returned extended style to `0x00200108`
  without losing the Discord native topmost flag. Reenabling succeeded.
- Window capture confirmed the stream video still rendered after activation.
- **Crash recovery PASS:** force-killed the Rust app with Discord stream
  captured, executed `--recover-only`, verified exact original style
  `0x00200108` and empty journal `[]`.
- **Restart PASS:** the app automatically recaptured the same stream and
  reapplied all three click-through flags.
- Unit tests: **4/4 PASS** including classifier checks against normal chat,
  Discord overlay, unpinned stream and non-Discord false positives.
- Discord Stable / PTB code paths share the classifier but no live popped-out
  stream was available in those versions for integration tests.
- No Discord injection, plugin, modification, video recording or API access.
  The change only targets the native Windows pop-out HWND.

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

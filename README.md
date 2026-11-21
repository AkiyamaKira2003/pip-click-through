# PiP ClickThrough — Windows

## Rust 2.1.2 (native graphical edition)

**v2.1.2 safety maintenance:** prevents auto-capture of Discord Overlay
utility-window titles with extra game or diagnostic suffixes. The existing
public v2.1.1 release remains unchanged; use the `rust-v2.1.2` portable ZIP
once the new build is verified and published. Do not replace files while the
portable app is running, and do not mistake CI test evidence for a live
Discord interaction test.

**v2.1.1 detection fix:** Discord live pop-out windows do not always have
titles starting with "Stream". Some live pop-outs use a channel title
instead. PiPClickThrough now detects
these as separate Discord pop-out windows only when a normal Discord main
window is also present **in the same process**, and excludes main chat,
hidden overlay and fullscreen windows. A minimized pop-out is still listed
using its restored bounds, but auto-capture waits until you restore it from
the taskbar. When restored, it can be auto-captured and made always-on-top
even if Discord's own Stay On Top is not enabled. Enable **Auto-detect**,
**Auto-enable**, and **Discord stream pop-outs** inside the app.

**Additional false-positive guard:** Internal windows whose titles begin with
`Discord Overlay` are excluded even if a diagnostic/game suffix is appended.
This conservative rule avoids accidentally treating an overlay utility window
as an auto-capturable video pop-out when a normal Discord window shares its
process. A voice channel deliberately named with that reserved prefix may need
manual handling. The regression uses synthetic titles; it does not assert that
those exact suffixes were observed in a live Discord client.

**Discord stream support:** The native scanner supports Discord Stable,
PTB and Canary pop-out video windows in addition to browser PiP. Discord's
main chat window and hidden game overlay are excluded. Auto-capture works
for verified, non-minimized pop-out windows. Fullscreen-like or otherwise
unverified windows require manual confirmation. Toggle interactive mode
with **Ctrl+Shift+F10** to use Discord video controls, and **Ctrl+Shift+F12**
to restore windows. The
`autoDiscordStreams` setting defaults to enabled and can be switched off
independently of browser PiP.

The primary Windows desktop implementation is now in **Rust**, with a custom
dark/cyan **egui** dashboard (not stock Win32 buttons), native Win32
window-management, a notification-area controller, and the original CLI pipe
protocol. The prior C#/.NET WinForms source remains preserved as a legacy
reference in this repository.

**Launch the new build:** `Release-Portable-Rust\PiPClickThrough.exe` (after
running `build-rust.ps1`). Do not launch the old `Release-Portable` build by
mistake. See the build recipe below for dependencies.

New UI features: overview dashboard, browser window picker, manual-capture
confirmation, quick action controls, opacity slider, settings, separate
window details, activity and native hotkeys. It modifies existing Chromium PiP
windows; it does not create video players, capture another program's screen or
inject DLLs.

### Build native Rust edition

On Windows x64, install **Rust GNU** (stable) and **LLVM** (for `llvm-dlltool`).
The build script uses `rust-lld` and creates a local temporary `dlltool.exe`
copy without altering the machine-wide PATH.

```powershell
winget install LLVM.LLVM --exact
rustup toolchain install stable-x86_64-pc-windows-gnu --profile minimal
.\build-rust.ps1
.\Release-Portable-Rust\PiPClickThrough.exe
.\pipctl.ps1 status
```

If the existing portable executable is still running, use
`.\build-rust.ps1 -ArchiveOnly` instead. It runs the same locked tests and
Rust release build, creates `dist\PiPClickThrough-Rust-2.1.2-win-x64.zip`
from isolated temporary staging, then cleans that staging. It **does not**
replace or terminate the running `Release-Portable-Rust\PiPClickThrough.exe`.
Close the app normally before manually updating the installed portable files.

For reviewers with access to this repository, successful **Native Rust Windows
checks** GitHub Actions runs also provide a seven-day downloadable portable ZIP
under the run's **Artifacts** section. The ZIP contains only the executable,
README and two CLI scripts; local QA acceptance notes are excluded. The workflow
verifies an exact allowlist and records its SHA-256 before upload. This
is an ephemeral CI build, **not** a signed installer, a published release, or
proof that live browser/Discord integrations passed interactive acceptance.

The app saves `settings.json` and a crash-recovery `recovery.json` inside
`%LOCALAPPDATA%\PiPClickThrough`. When its last window closes normally, it
restores all managed PiP style flags and the original always-on-top state.
Settings updates write and flush a new same-folder temporary file before
replacing `settings.json`; a failed write or replacement leaves the previous
complete settings file intact. This reduces interrupted-write corruption,
but is **not** a guarantee against power loss or every filesystem failure.
The live portable EXE is never replaced by running source tests.
The Rust edition uses the same Win32 pipe name and existing `pipctl.ps1`.
The background bridge restricts the pipe ACL to the current Windows user.
Only **verified PiP windows** can be captured via CLI or auto-detection;
unverified topmost Chromium popups require explicit manual confirmation.

The Rust edition depends on `eframe/egui` (MIT/Apache-2.0); the old source
also retains its existing fonts/assets and associated license notices.

### Legacy C# implementation

Ứng dụng mới viết bằng WinForms .NET 8, điều khiển trực tiếp cửa sổ PiP gốc của Chrome / Edge / Chromium bằng Win32. Không dùng Chrome Extension, không phát lại video, không dùng mã nguồn Layer.

## CLI chẩn đoán và điều khiển (PowerShell/CMD)

Chạy GUI PiPClickThrough.exe trước. CLI kết nối với app đang chạy qua named pipe chỉ cho người dùng Windows hiện tại. Không tạo trình phát video hoặc trình duyệt khác.

```powershell
.\pipctl.ps1 list                  # Liệt kê cửa sổ THẬT, không có dữ liệu giả
.\pipctl.ps1 status                # Trạng thái đang chạy
.\pipctl.ps1 capture auto          # Chọn PiP thật được xác minh
.\pipctl.ps1 capture 0x125A0166    # Chọn chính xác một HWND
.\pipctl.ps1 verify                # Đọc lại 3 Win32 style flags
.\pipctl.ps1 off                   # Cho phép tương tác chuột
.\pipctl.ps1 on                    # Xuyên chuột
.\pipctl.ps1 toggle                # Bật/tắt
.\pipctl.ps1 opacity 80            # Đặt opacity 80%
.\pipctl.ps1 restore               # Khôi phục window style gốc
.\test-pipctl.ps1                  # Kiểm thử tích hợp trên PiP thật, tự restore
```

Tại Command Prompt cũng có thể chạy `pipctl.cmd list`, `pipctl.cmd capture auto`, `pipctl.cmd verify`, `pipctl.cmd restore`.

CLI không thể điều khiển một PiP không đạt tiêu chí xác thực của scanner. Cửa sổ không chắc chắn chỉ được chọn thủ công từ GUI sau xác nhận rõ ràng. `--preview-ui` đã được loại bỏ khỏi bản phát hành để không hiển thị sai trình duyệt Edge khi máy chỉ có Chrome.

## Giao diện / Typography

- Giao diện borderless tự thiết kế và icon Lucide ISC, giấy phép đi kèm tại `LUCIDE-LICENSE.txt`.
- Font chữ **Inter** bản tĩnh Regular / Medium / SemiBold do Rasmus Andersson phát triển, nhúng trực tiếp trong EXE theo **SIL Open Font License 1.1** (`INTER-LICENSE.txt`). Không cần cài font lên Windows, không truy cập mạng khi chạy.
- Dropdown là popup riêng, hiển thị tên trình duyệt dạng tên riêng (ví dụ `Chrome`), tự căn bố cục tên video và kích thước. Tránh viết hoa toàn bộ hoặc chèn khoảng trắng lặp.
- Không có chế độ dữ liệu giả trong bản phát hành: danh sách cửa sổ phải đến từ Win32 scanner trên máy thực.

## Bắt đầu

Các bước dưới đây dành cho **bản Rust 2.1.2 hiện tại**. Nếu chưa có
`Release-Portable-Rust\PiPClickThrough.exe`, hãy build bằng
`./build-rust.ps1` theo hướng dẫn ở trên.

1. Mở video Chrome hoặc Edge và chọn Picture-in-Picture.
2. Chạy file **Release-Portable-Rust/PiPClickThrough.exe** (không dùng thư mục `Release-Portable` của bản C# cũ).
3. PiP có tiêu đề được nhận diện sẽ tự được bật Click-through.
4. Nếu không tự nhận diện, chọn cửa sổ trong danh sách và bấm **Capture window**.
5. Dùng Ctrl+Shift+F10 để mở khả năng tương tác trở lại, kéo, đổi kích thước, đóng video.
6. Bấm **Restore all** để trả window style về gốc.
7. Nút X sẽ ẩn app xuống System Tray; chọn **Exit (restore windows)** từ menu tray để tắt hẳn.

## Hotkeys

| Phím tắt | Chức năng |
| --- | --- |
| Ctrl+Shift+F9 | Mở giao diện |
| Ctrl+Shift+F10 | Toggle Click-through / Interactive |
| Ctrl+Shift+PageUp | Tăng độ trong suốt 10% |
| Ctrl+Shift+PageDown | Giảm độ trong suốt 10% |
| Ctrl+Shift+F12 | Restore all và tắt Auto-enable |

## Bảo vệ cửa sổ

- Auto capture chỉ nhận các cửa sổ Chromium có đủ: visible, topmost, window class và tiêu đề PiP tin cậy.
- Các popup không xác định chỉ có thể capture thủ công sau khi xác nhận; từ chối cửa sổ gần fullscreen.
- Ghi recovery journal trước khi sửa style tại đường dẫn %LOCALAPPDATA%/PiPClickThrough/recovery.json.
- Khi app được mở lại sau khi crash, chỉ phục hồi HWND có cùng PID, window class và process start time.
- Không tiêm DLL vào Chrome, không hook bàn phím/chuột và không yêu cầu administrator cho cửa sổ cùng mức quyền.
- Video DRM, trình duyệt tự cập nhật window style và game exclusive fullscreen có thể gây hạn chế. Hãy dùng Borderless Windowed cho game.

## Build bản C#/.NET cũ (legacy, không phải bản Rust hiện tại)

Phần này chỉ dành cho mã **WinForms .NET 8** được giữ lại để tham khảo.
Để build bản Rust đang được khuyến nghị, dùng `./build-rust.ps1`
trong mục **Build native Rust edition** phía trên, đầu ra là
`Release-Portable-Rust\PiPClickThrough.exe`.
Project C# cũ dùng `PiPClickThrough.csproj` và .NET 8 SDK.

PowerShell:

    dotnet publish ./PiPClickThrough.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o ./Release-Portable

Lệnh kiểm tra dành riêng cho binary C# cũ:

    ./PiPClickThrough.exe --scan-to C:/Temp/pip-scan.json
    ./PiPClickThrough.exe --selftest-to C:/Temp/pip-selftest.txt

Giới hạn nhận diện tiêu đề trong `PiPDetector.cs` là của bản
C# cũ; bản Rust có cơ chế nhận diện Discord pop-out được mô tả ở đầu tài liệu.

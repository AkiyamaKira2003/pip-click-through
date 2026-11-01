# PiP ClickThrough — Windows

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

1. Mở video Chrome hoặc Edge và chọn Picture-in-Picture.
2. Chạy file **Release-Portable/PiPClickThrough.exe**.
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

## Build

Ứng dụng dùng .NET 8 SDK, project file PiPClickThrough.csproj. Tại KPC có thể build bằng SDK portable đang có ở D:/Projects/FarmTimerOverlay/.dotnet-sdk/dotnet.exe.

PowerShell:

    dotnet publish ./PiPClickThrough.csproj -c Release -r win-x64 -p:PublishSingleFile=true -p:SelfContained=true -o ./Release-Portable

Lệnh kiểm tra:

    ./PiPClickThrough.exe --scan-to C:/Temp/pip-scan.json
    ./PiPClickThrough.exe --selftest-to C:/Temp/pip-selftest.txt

Ứng dụng hiện chỉ nhận diện tự động chính xác với tiêu đề PiP được liệt kê trong PiPDetector.cs; nếu trình duyệt dùng tiêu đề khác thì chọn thủ công.

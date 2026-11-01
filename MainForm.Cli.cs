using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace PiPClickThrough;

internal sealed partial class MainForm
{
    private const string CliPipeName = "PiPClickThrough.Control.v1";
    private sealed class CliRequest
    {
        public string Command { get; set; } = "";
        public string Arg { get; set; } = "";
    }

    private void StartCliServer()
    {
        _ = Task.Run(async () =>
        {
            while (!cliCancellation.IsCancellationRequested)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(CliPipeName,
                        PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync(cliCancellation.Token);
                    using var input = new StreamReader(pipe, new UTF8Encoding(false),
                        leaveOpen: true);
                    using var output = new StreamWriter(pipe, new UTF8Encoding(false),
                        leaveOpen: true) { AutoFlush = true };

                    var line = await input.ReadLineAsync(cliCancellation.Token);
                    var reply = new TaskCompletionSource<string>(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    try
                    {
                        var request = JsonSerializer.Deserialize<CliRequest>(line ?? "",
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                        BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                reply.TrySetResult(JsonSerializer.Serialize(
                                    ExecuteCli(request ?? new CliRequest())));
                            }
                            catch (Exception ex)
                            {
                                reply.TrySetResult(JsonSerializer.Serialize(
                                    new { ok = false, error = ex.Message }));
                            }
                        }));
                        var result = await reply.Task.WaitAsync(
                            TimeSpan.FromSeconds(6), cliCancellation.Token);
                        await output.WriteLineAsync(result);
                    }
                    catch (Exception ex)
                    {
                        await output.WriteLineAsync(JsonSerializer.Serialize(
                            new { ok = false, error = ex.Message }));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    WriteLog("CLI server: " + ex.Message);
                    try { await Task.Delay(500, cliCancellation.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        });
    }

    private object ExecuteCli(CliRequest request)
    {
        string cmd = request.Command.Trim().ToLowerInvariant();
        string arg = request.Arg.Trim();
        if (cmd is "list" or "scan")
        {
            var found = PiPDetector.Scan();
            return new
            {
                ok = true, count = found.Count,
                windows = found.Select(w => new
                {
                    handle = "0x" + w.Handle.ToString("X"),
                    pid = w.Pid, browser = WindowSelect.BrowserName(w),
                    title = w.Title, width = w.Width, height = w.Height,
                    confirmedPiP = w.StrictMatch, topmost = w.Topmost,
                    style = "0x" + Native.ExStyle(w.Hwnd).ToString("X8")
                }).ToArray()
            };
        }
        if (cmd == "status") return CliStatus();
        if (cmd is "capture" or "select")
        {
            var found = PiPDetector.Scan();
            PiPWindow? target;
            if (string.IsNullOrWhiteSpace(arg) || arg.Equals("auto", StringComparison.OrdinalIgnoreCase))
                target = found.FirstOrDefault(w => w.StrictMatch);
            else
            {
                bool prefixedHex = arg.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                string raw = prefixedHex ? arg[2..] : arg;
                long handle = -1;
                if (prefixedHex && long.TryParse(raw, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long asHex))
                    handle = asHex;
                else if (!prefixedHex && long.TryParse(arg, out long asDecimal))
                    handle = asDecimal;
                else if (!prefixedHex && long.TryParse(arg, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long bareHex))
                    handle = bareHex;
                target = found.FirstOrDefault(w => w.Handle == handle);
            }
            if (target == null)
                return new { ok = false, error = "No matching REAL Chrome/Edge PiP found. Run: pipctl.ps1 list" };
            if (!target.StrictMatch)
                return new { ok = false, error = "This window is not a verified PiP. CLI refuses unsafe captures." };
            SetClickThrough(true);
            manager.Capture(target);
            windows.UpdateOptions(PiPDetector.Scan());
            UpdateStatus();
            return CliStatus();
        }
        if (cmd is "on" or "off" or "toggle")
        {
            SetClickThrough(cmd == "toggle" ? !manager.ClickThrough : cmd == "on");
            return CliStatus();
        }
        if (cmd is "restore" or "release")
        {
            RestoreAll();
            return CliStatus();
        }
        if (cmd == "opacity")
        {
            if (!int.TryParse(arg, out int opacity) || opacity < 20 || opacity > 100)
                return new { ok = false, error = "Expected opacity 20..100" };
            alpha.Value = opacity;
            return CliStatus();
        }
        if (cmd == "refresh")
        {
            ScanAndUpdate();
            return CliStatus();
        }
        if (cmd == "show")
        {
            ShowWindow();
            return CliStatus();
        }
        if (cmd == "verify")
        {
            var matches = PiPDetector.Scan();
            var status = matches.Where(w => manager.Handles.Contains(w.Handle))
                .Select(w => new
                {
                    handle = "0x" + w.Handle.ToString("X"),
                    validWindow = PiPDetector.SameWindow(w),
                    title = w.Title,
                    style = "0x" + Native.ExStyle(w.Hwnd).ToString("X8"),
                    transparent = (Native.ExStyle(w.Hwnd) & Native.WS_EX_TRANSPARENT) != 0,
                    layered = (Native.ExStyle(w.Hwnd) & Native.WS_EX_LAYERED) != 0,
                    nonActivating = (Native.ExStyle(w.Hwnd) & Native.WS_EX_NOACTIVATE) != 0
                }).ToArray();
            return new { ok = true, clickThrough = manager.ClickThrough,
                capturedCount = manager.Handles.Count, windows = status };
        }
        return new
        {
            ok = false,
            error = "Unknown command. Use: list, status, capture [0xHWND], on, off, toggle, opacity N, verify, refresh, restore, show"
        };
    }

    private object CliStatus()
    {
        var found = PiPDetector.Scan();
        return new
        {
            ok = true,
            app = "PiP ClickThrough",
            clickThrough = manager.ClickThrough,
            autoDetect = settings.AutoDetect,
            autoEnable = settings.AutoEnable,
            opacity = manager.Opacity,
            capturedCount = manager.Handles.Count,
            capturedHandles = manager.Handles.Select(h => "0x" + h.ToString("X")).ToArray(),
            realPiPCount = found.Count(w => w.StrictMatch),
            selected = windows.SelectedWindow?.Handle is long h
                ? "0x" + h.ToString("X") : null
        };
    }
}

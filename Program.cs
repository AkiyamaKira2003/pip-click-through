using System.Text.Json;

namespace PiPClickThrough;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--scan-to")
        {
            File.WriteAllText(args[1], JsonSerializer.Serialize(PiPDetector.Scan(),
                new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        if (args.Length >= 2 && args[0] == "--recover-only")
        {
            using var recovered = new WindowController(AppSettings.DataDirectory);
            File.WriteAllText(args[1], "Recovery checked at " + DateTime.Now.ToString("O"));
            return;
        }
        if (args.Length >= 2 && args[0] == "--font-info-to")
        {
            using var regular = UiFonts.Create(10);
            using var medium = UiFonts.CreateMedium(10);
            using var semibold = UiFonts.Create(10, true);
            File.WriteAllLines(args[1], new[]
            {
                "Regular: " + regular.FontFamily.Name + " / " + regular.Style,
                "Medium: " + medium.FontFamily.Name + " / " + medium.Style,
                "SemiBold: " + semibold.FontFamily.Name + " / " + semibold.Style
            });
            return;
        }
        if (args.Length >= 2 && args[0] == "--selftest-to")
        {
            using var testMutex = new Mutex(false,
                @"Local\PiPClickThrough_SingleInstance", out bool standalone);
            if (!standalone)
            {
                File.WriteAllText(args[1],
                    "SKIP: PiP ClickThrough is already running. Use test-pipctl.ps1 instead.");
                return;
            }
            var messages = new List<string>();
            try
            {
                var found = PiPDetector.Scan();
                messages.Add("Browser topmost candidates: " + found.Count);
                var target = found.FirstOrDefault(w => w.StrictMatch);
                if (target == null)
                {
                    messages.Add("SKIP: no strict PiP window currently open.");
                }
                else
                {
                    messages.Add("TARGET: " + target);
                    var original = Native.ExStyle(target.Hwnd);
                    bool originalTopmost = Native.IsTopmost(target.Hwnd);
                    messages.Add("Original style: 0x" + original.ToString("X8"));
                    using var manager = new WindowController(AppSettings.DataDirectory);
                    try
                    {
                        manager.Capture(target);
                        var ghost = Native.ExStyle(target.Hwnd);
                        bool hasAll = (ghost & (Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT |
                            Native.WS_EX_NOACTIVATE)) == (Native.WS_EX_LAYERED |
                            Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE);
                        messages.Add("Capture flags: " + (hasAll ? "PASS" : "FAIL") +
                            " style=0x" + ghost.ToString("X8"));
                        if (!hasAll) throw new Exception("Click-through flags did not take effect.");
                        Native.GetWindowRect(target.Hwnd, out var rect);
                        var center = new System.Drawing.Point(rect.Left + rect.Width / 2,
                            rect.Top + rect.Height / 2);
                        IntPtr hit = Native.GetAncestor(Native.WindowFromPoint(center), 2);
                        messages.Add("Hit-test center resolves to PiP: " +
                            (hit == target.Hwnd ? "YES (not transparent)" : "NO (pass-through candidate)"));
                        manager.SetClickThrough(false);
                        var unlocked = Native.ExStyle(target.Hwnd);
                        bool interactive = (unlocked & Native.WS_EX_TRANSPARENT) ==
                            (original & Native.WS_EX_TRANSPARENT);
                        messages.Add("Interactive restores transparent bit: " +
                            (interactive ? "PASS" : "FAIL"));
                        if (!interactive) throw new Exception("Interactive did not restore original bit.");
                        manager.SetOpacity(75);
                        bool opacityLayer = (Native.ExStyle(target.Hwnd) & Native.WS_EX_LAYERED) != 0;
                        messages.Add("Opacity has layered flag: " + (opacityLayer ? "PASS" : "FAIL"));
                        if (!opacityLayer) throw new Exception("Opacity layering failed.");
                        manager.SetOpacity(100);
                    }
                    finally { manager.RestoreAll(); }
                    var restored = Native.ExStyle(target.Hwnd);
                    long mask = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
                    bool pass = ((restored ^ original) & mask) == 0 &&
                        Native.IsTopmost(target.Hwnd) == originalTopmost;
                    messages.Add("Restore original controlled bits & topmost: " +
                        (pass ? "PASS" : "FAIL") + " restored=0x" + restored.ToString("X8"));
                    if (!pass) throw new Exception("Restore validation failed.");
                }
                messages.Add("RESULT: COMPLETE");
            }
            catch (Exception ex)
            {
                messages.Add("RESULT: FAILED " + ex);
            }
            File.WriteAllLines(args[1], messages);
            return;
        }

        // A global named mutex prevents competing instances from fighting over the same PiP.
        using var mutex = new Mutex(true, @"Local\PiPClickThrough_SingleInstance", out bool first);
        if (!first)
        {
            MessageBox.Show("PiP ClickThrough is already running in the system tray.",
                "PiP ClickThrough", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        ApplicationConfiguration.Initialize();
        try { Application.Run(new MainForm()); }
        catch (Exception ex)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDirectory);
                File.AppendAllText(Path.Combine(AppSettings.DataDirectory, "errors.log"),
                    DateTime.Now.ToString("O") + " " + ex + Environment.NewLine);
            }
            catch { }
            MessageBox.Show(ex.Message, "PiP ClickThrough error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

using System.Diagnostics;
using System.Text.Json.Serialization;

namespace PiPClickThrough;

public sealed class PiPWindow
{
    public required long Handle { get; init; }
    public required uint Pid { get; init; }
    public required string ProcessName { get; init; }
    public required string Title { get; init; }
    public required string WindowClass { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required bool Topmost { get; init; }
    public required bool StrictMatch { get; init; }
    [JsonIgnore]
    public IntPtr Hwnd => new(Handle);
    public override string ToString() =>
        (StrictMatch ? "[PiP] " : "[Manual] ") +
        ProcessName + " | " + (string.IsNullOrWhiteSpace(Title) ? "(no title)" : Title) +
        " | " + Width + "x" + Height + " | 0x" + Handle.ToString("X");
}

public static class PiPDetector
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    { "chrome", "msedge", "brave", "chromium", "vivaldi", "opera" };

    private static bool LooksLikePiPTitle(string title)
    {
        var s = title.Trim().ToLowerInvariant();
        return s == "picture in picture" || s == "picture-in-picture" ||
               s == "picture in picture - google chrome" ||
               s == "hình trong hình" || s == "ảnh trong ảnh" ||
               s == "ピクチャー イン ピクチャー" || s == "ピクチャー イン ピクチャ" ||
               s == "화면 속 화면";
    }

    public static List<PiPWindow> Scan()
    {
        var found = new List<PiPWindow>();
        var cache = new Dictionary<uint, string?>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == Environment.ProcessId) return true;
            if (!cache.TryGetValue(pid, out string? name))
            {
                try { name = Process.GetProcessById((int)pid).ProcessName; }
                catch { name = null; }
                cache[pid] = name;
            }
            if (name == null || !Browsers.Contains(name)) return true;

            var cls = Native.ClassName(hwnd);
            if (!cls.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal)) return true;
            if (!Native.GetWindowRect(hwnd, out var rect) || rect.Width < 120 || rect.Height < 80) return true;
            if (Native.DwmGetWindowAttribute(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0)
                return true;

            bool topmost = Native.IsTopmost(hwnd);
            string title = Native.WindowText(hwnd);
            bool strict = LooksLikePiPTitle(title) && topmost;
            // Manual candidates are constrained to Chromium's topmost popups; ordinary
            // browser windows never qualify for automatic capture.
            if (!topmost) return true;
            found.Add(new PiPWindow
            {
                Handle = hwnd.ToInt64(), Pid = pid, ProcessName = name,
                Title = title, WindowClass = cls, Width = rect.Width,
                Height = rect.Height, Topmost = topmost, StrictMatch = strict
            });
            return true;
        }, IntPtr.Zero);
        return found.OrderByDescending(w => w.StrictMatch).ThenBy(w => w.ProcessName).ToList();
    }

    public static bool SameWindow(PiPWindow expected)
    {
        var hwnd = expected.Hwnd;
        if (!Native.IsWindow(hwnd)) return false;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == expected.Pid && Native.ClassName(hwnd) == expected.WindowClass;
    }
}

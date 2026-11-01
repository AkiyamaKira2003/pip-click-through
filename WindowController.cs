using System.Diagnostics;
using System.Text.Json;

namespace PiPClickThrough;

public sealed class RecoveryItem
{
    public long Handle { get; set; }
    public uint Pid { get; set; }
    public long ProcessStartTicks { get; set; }
    public string WindowClass { get; set; } = "";
    public long OriginalExStyle { get; set; }
    public bool OriginalTopmost { get; set; }
    public bool HadLayerAttributes { get; set; }
    public uint OriginalColorKey { get; set; }
    public byte OriginalAlpha { get; set; } = 255;
    public uint OriginalLayerFlags { get; set; }
}

public sealed class WindowController : IDisposable
{
    private const long ControlledBits = Native.WS_EX_LAYERED |
                                        Native.WS_EX_TRANSPARENT |
                                        Native.WS_EX_NOACTIVATE;
    private readonly Dictionary<long, RecoveryItem> tracked = new();
    private readonly string journalPath;
    private bool disposed;

    public bool ClickThrough { get; private set; } = true;
    public bool AlwaysOnTop { get; private set; } = true;
    public int Opacity { get; private set; } = 100;
    public IReadOnlyCollection<long> Handles => tracked.Keys.ToArray();
    public event Action<string>? Status;

    public WindowController(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        journalPath = Path.Combine(dataDirectory, "recovery.json");
        RecoverAfterCrash();
    }

    private void Note(string info) => Status?.Invoke(info);

    private static long ProcessStart(uint pid)
    {
        try { using var process = Process.GetProcessById((int)pid); return process.StartTime.Ticks; }
        catch { return 0; }
    }

    private static bool StillSame(RecoveryItem state)
    {
        IntPtr h = new(state.Handle);
        if (!Native.IsWindow(h) || Native.ClassName(h) != state.WindowClass) return false;
        Native.GetWindowThreadProcessId(h, out uint pid);
        if (pid != state.Pid) return false;
        long now = ProcessStart(pid);
        return now != 0 && state.ProcessStartTicks == now;
    }

    private static RecoveryItem Snapshot(PiPWindow window)
    {
        var hwnd = window.Hwnd;
        long original = Native.ExStyle(hwnd);
        bool hasOriginalAttributes = (original & Native.WS_EX_LAYERED) != 0 &&
            Native.GetLayeredWindowAttributes(hwnd, out _, out _, out _);
        uint key = 0, flags = 0;
        byte alpha = 255;
        if (hasOriginalAttributes)
            Native.GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags);

        return new RecoveryItem
        {
            Handle = window.Handle, Pid = window.Pid,
            ProcessStartTicks = ProcessStart(window.Pid),
            WindowClass = window.WindowClass,
            OriginalExStyle = original, OriginalTopmost = Native.IsTopmost(hwnd),
            HadLayerAttributes = hasOriginalAttributes,
            OriginalColorKey = key, OriginalAlpha = alpha,
            OriginalLayerFlags = flags
        };
    }

    private void SaveJournal()
    {
        var temp = journalPath + ".tmp";
        string json = JsonSerializer.Serialize(tracked.Values.ToArray(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(temp, json);
        File.Move(temp, journalPath, true);
    }

    public void Capture(PiPWindow window)
    {
        if (disposed) throw new ObjectDisposedException(nameof(WindowController));
        if (tracked.ContainsKey(window.Handle)) return;
        if (!PiPDetector.SameWindow(window))
            throw new InvalidOperationException("Window disappeared or changed identity.");

        var state = Snapshot(window);
        if (state.ProcessStartTicks == 0) throw new InvalidOperationException("Cannot verify browser process.");
        tracked.Add(window.Handle, state);
        try
        {
            // Durable journal BEFORE any window modification. A crash can be recovered.
            SaveJournal();
            Apply(state);
            Note("Captured: " + window.Title + " | 0x" + window.Handle.ToString("X"));
        }
        catch
        {
            try { RestoreOne(state); } catch { /* best effort */ }
            tracked.Remove(window.Handle);
            try { SaveJournal(); } catch { /* capture already failed */ }
            throw;
        }
    }

    private void Apply(RecoveryItem state)
    {
        if (!StillSame(state)) return;
        var hwnd = new IntPtr(state.Handle);
        long style = Native.ExStyle(hwnd);
        long target = style;
        if (ClickThrough)
            target |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED;
        else
        {
            // Restore only our three bits. Never wipe unrelated Chromium style updates.
            target = (target & ~ControlledBits) | (state.OriginalExStyle & ControlledBits);
            if (Opacity < 100) target |= Native.WS_EX_LAYERED;
        }
        if (style != target) Native.WriteExStyle(hwnd, target);
        if ((target & Native.WS_EX_LAYERED) != 0)
        {
            byte alpha = (byte)(Math.Clamp(Opacity, 20, 100) * 255 / 100);
            if (!Native.SetLayeredWindowAttributes(hwnd, 0, alpha, Native.LWA_ALPHA))
                throw new System.ComponentModel.Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
        }

        bool desiredTopmost = AlwaysOnTop;
        if (Native.IsTopmost(hwnd) != desiredTopmost && !Native.SetTopmost(hwnd, desiredTopmost))
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
    }

    private static void RestoreOne(RecoveryItem state)
    {
        if (!StillSame(state)) return;
        var hwnd = new IntPtr(state.Handle);
        long current = Native.ExStyle(hwnd);
        long target = (current & ~ControlledBits) | (state.OriginalExStyle & ControlledBits);
        // Restore layered alpha before removing layered style.
        if ((state.OriginalExStyle & Native.WS_EX_LAYERED) != 0 && state.HadLayerAttributes)
            Native.SetLayeredWindowAttributes(hwnd, state.OriginalColorKey,
                state.OriginalAlpha, state.OriginalLayerFlags);
        if (current != target) Native.WriteExStyle(hwnd, target);
        if (Native.IsTopmost(hwnd) != state.OriginalTopmost)
            Native.SetTopmost(hwnd, state.OriginalTopmost);
    }

    public void SetClickThrough(bool enable)
    {
        ClickThrough = enable;
        Enforce();
        Note(enable ? "Click-through ON" : "Interactive mode: mouse clicks enabled");
    }

    public void SetAlwaysOnTop(bool enable)
    {
        AlwaysOnTop = enable;
        Enforce();
    }

    public void SetOpacity(int percent)
    {
        Opacity = Math.Clamp(percent, 20, 100);
        Enforce();
    }

    public void Enforce()
    {
        bool journalDirty = false;
        foreach (var state in tracked.Values.ToArray())
        {
            if (!StillSame(state))
            {
                tracked.Remove(state.Handle);
                journalDirty = true;
                Note("PiP closed: 0x" + state.Handle.ToString("X"));
                continue;
            }
            try { Apply(state); }
            catch (Exception e) { Note("Win32 apply failed: " + e.Message); }
        }
        if (journalDirty) SaveJournal();
    }

    public void RestoreAll()
    {
        var errors = new List<string>();
        foreach (var state in tracked.Values.ToArray())
        {
            try { RestoreOne(state); tracked.Remove(state.Handle); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }
        SaveJournal(); // Retains failed entries so next launch can retry.
        if (errors.Count > 0) Note("Restore errors: " + string.Join("; ", errors));
        else Note("All captured windows restored");
    }

    private void RecoverAfterCrash()
    {
        if (!File.Exists(journalPath)) return;
        try
        {
            var items = JsonSerializer.Deserialize<List<RecoveryItem>>(File.ReadAllText(journalPath))
                        ?? new List<RecoveryItem>();
            foreach (var entry in items)
            {
                if (!StillSame(entry)) continue;
                try { RestoreOne(entry); }
                catch { tracked[entry.Handle] = entry; }
            }
            SaveJournal();
        }
        catch (Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(journalPath)!, "recovery-errors.log"),
                DateTime.Now + " " + ex + Environment.NewLine); } catch { }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RestoreAll();
    }
}

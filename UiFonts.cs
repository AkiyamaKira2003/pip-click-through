using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace PiPClickThrough;

/// <summary>
/// Embedded Inter desktop typeface (SIL OFL 1.1, see INTER-LICENSE.txt).
/// Uses REAL Regular, Medium and SemiBold faces, not a synthesized GDI Bold.
/// The private font collection doesn't install fonts or change Windows settings.
/// </summary>
internal static class UiFonts
{
    private static readonly PrivateFontCollection Collection = new();
    private static readonly List<GCHandle> Pinned = new();
    private static readonly FontFamily? Regular;
    private static readonly FontFamily? Medium;
    private static readonly FontFamily? SemiBold;

    static UiFonts()
    {
        try
        {
            foreach (string face in new[] {"Inter-Regular", "Inter-Medium", "Inter-SemiBold"})
            {
                using var stream = typeof(UiFonts).Assembly.GetManifestResourceStream(
                    "PiPClickThrough.Fonts." + face + ".ttf");
                if (stream == null) continue;
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                byte[] bytes = copy.ToArray();
                var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                Pinned.Add(handle);
                Collection.AddMemoryFont(handle.AddrOfPinnedObject(), bytes.Length);
            }
            Regular = Collection.Families.FirstOrDefault(f => f.Name == "Inter");
            Medium = Collection.Families.FirstOrDefault(f => f.Name == "Inter Medium");
            SemiBold = Collection.Families.FirstOrDefault(f => f.Name == "Inter SemiBold");
        }
        catch { /* Fall back to Windows' regular UI fonts if an asset is missing. */ }
    }

    internal static Font Create(float points, bool semibold = false) =>
        Make(points, semibold ? SemiBold : Regular, semibold);

    internal static Font CreateMedium(float points) => Make(points, Medium ?? Regular, false);

    private static Font Make(float points, FontFamily? family, bool emphasized)
    {
        if (family != null)
        {
            try { return new Font(family, points, FontStyle.Regular, GraphicsUnit.Point); }
            catch { }
        }
        try
        {
            return new Font(emphasized ? "Segoe UI Variable Text Semibold" :
                "Segoe UI Variable Text", points, FontStyle.Regular, GraphicsUnit.Point);
        }
        catch { return new Font("Segoe UI", points, emphasized ?
            FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point); }
    }
}

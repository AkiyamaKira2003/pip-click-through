using System.Drawing.Imaging;
using System.Reflection;

namespace PiPClickThrough;

/// <summary>
/// Genuine Lucide SVG icons rasterized at build-time into embedded PNG resources.
/// Runtime never accesses the internet and no icons are manually drawn.
/// Source and third-party license: Assets/lucide, LUCIDE-LICENSE.txt.
/// </summary>
internal static class IconAssets
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    private static Bitmap? Get(string name)
    {
        lock (Sync)
        {
            if (Cache.TryGetValue(name, out var bmp)) return bmp;
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("PiPClickThrough.Icons." + name + ".png");
            if (stream is null) return null;
            using var original = new Bitmap(stream);
            bmp = new Bitmap(original);
            Cache.Add(name, bmp);
            return bmp;
        }
    }

    internal static void Draw(Graphics g, string name, RectangleF bounds, Color color)
    {
        var png = Get(name);
        if (png == null || bounds.Width <= 0 || bounds.Height <= 0) return;
        var matrix = new ColorMatrix(new[]
        {
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, 0, 0 },
            new float[] { 0, 0, 0, color.A/255f, 0 },
            new float[] { color.R/255f, color.G/255f, color.B/255f, 0, 1 }
        });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        var dest = Rectangle.Round(bounds);
        var mode = g.InterpolationMode;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.DrawImage(png, dest, 0, 0, png.Width, png.Height,
                    GraphicsUnit.Pixel, attributes);
        g.InterpolationMode = mode;
    }
}

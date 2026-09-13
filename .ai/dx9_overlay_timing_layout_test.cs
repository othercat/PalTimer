using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Pal98Timer;

internal sealed class OverlayTimingProvider<T>
{
    public T Get() { return default(T); }
}

internal static class OverlayTimingLayoutTest
{
    const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static readonly Assembly Product = typeof(TimerCore).Assembly;
    static readonly Type Snapshot = Product.GetType("Pal98Timer.Dx9OverlaySnapshot", true);
    static readonly Type Overlay = Product.GetType("Pal98Timer.Dx9OverlayForm", true);
    static readonly Type Entry = Product.GetType("Pal98Timer.Dx9OverlayTimelineEntry", true);
    static readonly Type Layout = Product.GetType("Pal98Timer.Dx9OverlayLayoutSettings", true);

    static object Point(string name, string best, string current, bool active)
    { return Activator.CreateInstance(Entry, new object[] { name, best, current, 0L, active, false }); }

    static object Data(string font, string label)
    {
        return Activator.CreateInstance(Snapshot, new object[] {
            IntPtr.Zero, font, "00:11:28.34", "123.45s", "00:12:34", "蜂0 蜜0 火0 血0 观0 剑0 钱0", 3,
            Point("见石碑", "00:06:05", "00:05:59", true),
            Point("李大娘", "00:11:13", "", false), Point("上船", "00:18:37", "", false),
            "已暂停", false, true, label });
    }

    static Bitmap Render(Form form, object data)
    {
        Overlay.GetField("CurrentSnapshot", Any).SetValue(form, data);
        var bitmap = new Bitmap(form.Width, form.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(35, 31, 27));
            Overlay.GetMethod("OnPaint", Any).Invoke(form, new object[] { new PaintEventArgs(graphics, form.ClientRectangle) });
        }
        return bitmap;
    }

    [STAThread]
    static int Main(string[] args)
    {
        Directory.CreateDirectory(args[0]);
        var providerType = typeof(OverlayTimingProvider<>).MakeGenericType(Snapshot);
        object provider = Activator.CreateInstance(providerType);
        var callback = Delegate.CreateDelegate(typeof(Func<>).MakeGenericType(Snapshot), provider, providerType.GetMethod("Get"));
        int checks = 0;
        foreach (string fontName in new[] { "SimSun", "MingLiU" })
        foreach (float scale in new[] { 0.8F, 1.0F, 1.5F })
        foreach (string label in new[] { "1.2秒", "0.8秒", "0.8秒&快走速" })
        {
            object layout = Layout.GetMethod("CreateDefault", Any).Invoke(null, null);
            using (var form = (Form)Activator.CreateInstance(Overlay, Any, null, new object[] { callback, layout }, null))
            {
                Overlay.GetField("CurrentScale", Any).SetValue(form, scale);
                float height = (float)Overlay.GetMethod("GetOverlayHeightLogicalPixels", Any).Invoke(form, null);
                form.ClientSize = new Size((int)Math.Ceiling(340 * scale), (int)Math.Ceiling(height * scale));
                using (var empty = Render(form, Data(fontName, "")))
                using (var painted = Render(form, Data(fontName, label)))
                using (var graphics = Graphics.FromImage(painted))
                using (var font = new Font(fontName, 9 * scale))
                {
                    if (graphics.MeasureString(label, font).Width > (340 - 14) * scale)
                        throw new Exception("Mode text does not fit: " + label);
                    int minY = painted.Height, maxX = -1, pixels = 0;
                    for (int y = 0; y < painted.Height; ++y)
                    for (int x = 0; x < painted.Width; ++x)
                        if (painted.GetPixel(x, y) != empty.GetPixel(x, y))
                        { minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); ++pixels; }
                    if (pixels == 0 || minY < (height - 27) * scale - 1 || maxX < painted.Width - 16 * scale)
                        throw new Exception("Mode text is missing, overlaps older rows, or is not right-aligned: " + label);
                    string file = fontName + "-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "-" + (++checks) + ".png";
                    painted.Save(Path.Combine(args[0], file), ImageFormat.Png);
                }
            }
        }
        Console.WriteLine("PASS: " + checks + " production OnPaint renders; three labels, Simplified/Traditional fonts, 80/100/150% scales, bottom row and right alignment; long idle time retained above");
        return 0;
    }
}

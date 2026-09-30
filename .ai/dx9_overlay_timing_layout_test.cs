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

    static object Data(string font, string label, int cloudId = -1)
    {
        return Activator.CreateInstance(Snapshot, new object[] {
            IntPtr.Zero, font, "00:11:28.34", "123.45s", "00:12:34", "蜂0 蜜0 火0 血0 观0 剑0 钱0", 3,
            Point("见石碑", "00:06:05", "00:05:59", true),
            Point("李大娘", "00:11:13", "", false), Point("上船", "00:18:37", "", false),
            "已暂停", false, true, label, "", "", "", cloudId });
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
        foreach (int cloudId in new[] { int.MinValue, -1, 0, 123, int.MaxValue })
        {
            string actual = (string)Snapshot.GetField("FooterText", Any).GetValue(Data("SimSun", "0.8秒", cloudId));
            string expected = cloudId < 0 ? "0.8秒" : "云ID:" + cloudId.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 0.8秒";
            if (actual != expected) throw new Exception("Unexpected cloud ID label: " + actual);
        }
        foreach (string fontName in new[] { "SimSun", "MingLiU" })
        foreach (float scale in new[] { 0.8F, 1.0F, 1.5F, 2.0F })
        foreach (float fontSize in new[] { 9.0F, 18.0F })
        foreach (string label in new[] { "1.2秒", "0.8秒", "0.8秒&快走速" })
        {
            object layout = Layout.GetMethod("CreateDefault", Any).Invoke(null, null);
            Layout.GetField("FontSize", Any).SetValue(layout, fontSize);
            using (var form = (Form)Activator.CreateInstance(Overlay, Any, null, new object[] { callback, layout }, null))
            {
                Overlay.GetField("CurrentScale", Any).SetValue(form, scale);
                float height = (float)Overlay.GetMethod("GetOverlayHeightLogicalPixels", Any).Invoke(form, null);
                form.ClientSize = new Size((int)Math.Ceiling(340 * scale), (int)Math.Ceiling(height * scale));
                using (var empty = Render(form, Data(fontName, "")))
                using (var painted = Render(form, Data(fontName, label)))
                using (var graphics = Graphics.FromImage(painted))
                using (var font = new Font(fontName, fontSize * scale))
                {
                    if (graphics.MeasureString(label, font).Width > (340 - 14) * scale)
                        throw new Exception("Mode text does not fit: " + label);
                    int minY = painted.Height, maxX = -1, pixels = 0;
                    for (int y = 0; y < painted.Height; ++y)
                    for (int x = 0; x < painted.Width; ++x)
                        if (painted.GetPixel(x, y) != empty.GetPixel(x, y))
                        { minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); ++pixels; }
                    float footerTop = height - Math.Max(18.0F, fontSize * 2.0F) - 9.0F;
                    if (pixels == 0 || minY < footerTop * scale - 1 || maxX < painted.Width - 16 * scale)
                        throw new Exception("Mode text is missing, overlaps older rows, or is not right-aligned: " + label);
                    foreach (int cloudId in new[] { 0, 123, int.MaxValue })
                    using (var cloud = Render(form, Data(fontName, label, cloudId)))
                    {
                        int minCloudY = cloud.Height, minCloudX = cloud.Width, maxCloudX = -1, cloudPixels = 0;
                        for (int y = 0; y < cloud.Height; ++y)
                        for (int x = 0; x < cloud.Width; ++x)
                            if (cloud.GetPixel(x, y) != empty.GetPixel(x, y))
                            { minCloudY = Math.Min(minCloudY, y); minCloudX = Math.Min(minCloudX, x); maxCloudX = Math.Max(maxCloudX, x); ++cloudPixels; }
                        string footer = (string)Snapshot.GetField("FooterText", Any).GetValue(Data(fontName, label, cloudId));
                        float footerWidth = graphics.MeasureString(footer, font).Width;
                        float expectedLeft = Math.Max(7 * scale, cloud.Width - 7 * scale - footerWidth);
                        if (cloudPixels == 0 || minCloudY < footerTop * scale - 1 ||
                            minCloudX < expectedLeft - 2 * scale || maxCloudX < cloud.Width - 16 * scale)
                            throw new Exception("Cloud footer is missing, not right-aligned as one unit, or overlaps older rows: " + label);
                        ++checks;
                        if (fontName == "SimSun" && fontSize == 9.0F && scale == 2.0F &&
                            cloudId == 123 && label == "0.8秒&快走速")
                            cloud.Save(Path.Combine(args[0], "obs-cloud-id-demo.png"), ImageFormat.Png);
                    }
                    string file = fontName + "-" + scale.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "-" + (++checks) + ".png";
                    painted.Save(Path.Combine(args[0], file), ImageFormat.Png);
                }
            }
        }
        Console.WriteLine("PASS: " + checks + " production OnPaint comparisons; three modes, Simplified/Traditional fonts, 80/100/150/200% scales and 9/18pt; cloud ID and mode right-aligned with one space, unknown IDs omitted, older rows unchanged");
        return 0;
    }
}

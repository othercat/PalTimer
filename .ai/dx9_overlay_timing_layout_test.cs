using System;
using System.Collections.Generic;
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
    const string ExamplePlugin = "钱12345 道具68　　";
    const string MaximumPlugin = "钱2147483647 道具251　　";

    static object Point(string name, string best, string current, bool active)
    { return Activator.CreateInstance(Entry, new object[] { name, best, current, 0L, active, false }); }

    static object Data(string font, string label, int cloudId = -1, string plugin = null)
    {
        var args = new List<object> {
            IntPtr.Zero, font, "00:11:28.34", "123.45s", "00:12:34", "蜂0 蜜0 火0 血0 观0 剑0 钱0", 3,
            Point("见石碑", "00:06:05", "00:05:59", true),
            Point("李大娘", "00:11:13", "", false), Point("上船", "00:18:37", "", false),
            "已暂停", false, true, label, "", "", "", cloudId };
        // null exercises the existing constructor used by reflection consumers.
        if (plugin != null) args.Add(plugin);
        return Activator.CreateInstance(Snapshot, args.ToArray());
    }

    static string Footer(object data)
    { return (string)Snapshot.GetField("FooterText", Any).GetValue(data); }

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
        foreach (string plugin in new[] { null, "", "　 \t", ExamplePlugin })
        foreach (string label in new[] { "", "0.8秒" })
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(plugin)) parts.Add(plugin.Trim());
            if (cloudId >= 0) parts.Add("云ID:" + cloudId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (label.Length != 0) parts.Add(label);
            string actual = Footer(Data("SimSun", label, cloudId, plugin));
            if (actual != string.Join(" ", parts)) throw new Exception("Footer order/spacing mismatch: " + actual);
            ++checks;
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
                var variants = new[] {
                    Data(fontName, label), Data(fontName, label, 123),
                    Data(fontName, label, -1, ExamplePlugin), Data(fontName, label, 123, ExamplePlugin),
                    Data(fontName, label, int.MaxValue, MaximumPlugin) };
                for (int variant = 0; variant < variants.Length; ++variant)
                {
                    object data = variants[variant];
                    Overlay.GetField("CurrentSnapshot", Any).SetValue(form, data);
                    float height = (float)Overlay.GetMethod("GetOverlayHeightLogicalPixels", Any).Invoke(form, null);
                    float footerHeight = (float)Overlay.GetMethod("GetFooterHeightLogicalPixels", Any).Invoke(form, null);
                    form.ClientSize = new Size((int)Math.Ceiling(340 * scale), (int)Math.Ceiling(height * scale));
                    using (var empty = Render(form, Data(fontName, "")))
                    using (var painted = Render(form, data))
                    {
                        int minY = painted.Height, maxY = -1, minX = painted.Width, maxX = -1, pixels = 0;
                        for (int y = 0; y < painted.Height; ++y)
                        for (int x = 0; x < painted.Width; ++x)
                            if (painted.GetPixel(x, y) != empty.GetPixel(x, y))
                            { minY = Math.Min(minY, y); maxY = Math.Max(maxY, y); minX = Math.Min(minX, x); maxX = Math.Max(maxX, x); ++pixels; }
                        float footerTop = height - footerHeight - 9.0F;
                        if (pixels == 0 || minY < footerTop * scale - 1 || minX < 6 * scale ||
                            maxX < painted.Width - (7 + fontSize) * scale || maxX >= painted.Width - 5 * scale || maxY >= painted.Height - 7 * scale)
                            throw new Exception("Footer missing, not right-aligned, clipped or overlaps older rows: " + Footer(data) +
                                "; font=" + fontName + "/" + fontSize + ", scale=" + scale + ", bounds=" + minX + "," + minY + "-" + maxX + "," + maxY);
                        if (fontSize == 18 && variant == 4 && footerHeight <= 36)
                            throw new Exception("Long footer must wrap at large font sizes without ellipsis.");
                        ++checks;
                        if (fontName == "SimSun" && scale == 2 && label == "0.8秒&快走速" &&
                            ((fontSize == 9 && variant == 3) || (fontSize == 18 && variant == 4)))
                            painted.Save(Path.Combine(args[0], fontSize == 9 ? "obs-fujia-demo.png" : "obs-fujia-long-demo.png"), ImageFormat.Png);
                    }
                }
            }
        }
        Console.WriteLine("PASS: " + checks + " footer content/render checks; plugin enabled/absent, legacy constructors, unknown/large cloud IDs, three modes, Simplified/Traditional fonts, 80/100/150/200% scales, 9/18pt; single spaces, right alignment, wrapping and unchanged upper rows.");
        return 0;
    }
}

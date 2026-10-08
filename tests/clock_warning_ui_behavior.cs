using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

// Isolated UI fixture: the client worker is stopped before injecting warning
// states. No real device credentials, server, game, or system clock is used.
internal static class ClockWarningUiBehavior
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    static int checks;
    static object Get(object value, string field) => value.GetType().GetField(field, Flags).GetValue(value);
    static void Set(object value, string field, object data) => value.GetType().GetField(field, Flags).SetValue(value, data);
    static object Call(object value, string method) => value.GetType().GetMethod(method, Flags).Invoke(value, null);
    static void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    [STAThread]
    static int Main(string[] args)
    {
        object module = null;
        try {
            Application.EnableVisualStyles();
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PalTimerOnline.dll"));
            Func<string, Type> type = name => assembly.GetType("Pal98Timer." + name, true);
            var storage = Activator.CreateInstance(type("CompetitionStorage"), Flags, null,
                new object[] { Path.Combine(args[0], "data"), new Func<string>(() => "isolated-clock-ui") }, null);
            var client = Activator.CreateInstance(type("CompetitionClient"), Flags, null,
                new object[] { storage, null, null, false, null, "3.37.8.0" }, null);
            ((Task<bool>)Call(client, "CloseAsync")).GetAwaiter().GetResult();
            Set(client, "enabled", true);
            var settings = Get(client, "settings");
            settings.GetType().GetProperty("Enabled").SetValue(settings, true);
            module = Activator.CreateInstance(type("OnlineModule"), Flags, null, new[] { client }, null);
            Func<long, object> issue = id => {
                var value = Activator.CreateInstance(type("CompetitionClockWarning"), true);
                Set(value, "Id", id);
                Set(value, "Message", "本机时间比服务器慢约 3分2秒，实时联机认证无法通过。\r\n请打开 Windows“日期和时间”，开启“自动设置时间”，点击“立即同步”。校时后会自动重连；本地计时继续。");
                return value;
            };
            Set(client, "clockWarning", issue(1)); Call(module, "UiTick");
            var first = (Form)Get(module, "clockWarningForm");
            Check(first.Visible && !first.Modal && first.Enabled, "clock warning is visible and modeless");
            Check(first.Controls.Find("btnClockSettings", true).Length == 1, "clock settings action is available");
            Call(module, "UiTick");
            Check(ReferenceEquals(first, Get(module, "clockWarningForm")), "repeated UI ticks retain the same warning window");
            using (var bitmap = new Bitmap(first.Width, first.Height)) {
                first.DrawToBitmap(bitmap, new Rectangle(0, 0, first.Width, first.Height));
                bitmap.Save(Path.Combine(args[0], "clock-warning.png"));
            }
            first.Close(); Call(module, "UiTick");
            Check(first.IsDisposed && ReferenceEquals(first, Get(module, "clockWarningForm")), "dismissal prevents repeated popups during the same failure");
            Set(client, "clockWarning", null); Call(module, "UiTick");
            Check(Get(module, "clockWarningForm") == null, "recovery clears the warning window");
            Set(client, "clockWarning", issue(2)); Call(module, "UiTick");
            var second = (Form)Get(module, "clockWarningForm");
            Check(second.Visible && !ReferenceEquals(second, first), "a new failure after recovery can notify again");
            Set(client, "clockWarning", null); Call(module, "UiTick");
            Check(second.IsDisposed, "automatic recovery closes an open warning");
            Console.WriteLine("PASS clock warning UI total=" + checks);
            return 0;
        } catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (module != null) ((IDisposable)module).Dispose(); }
    }
}

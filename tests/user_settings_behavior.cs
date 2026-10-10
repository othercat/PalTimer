using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Pal98Timer;

internal static class UserSettingsBehavior
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    static readonly Assembly Product = typeof(MConfig).Assembly;
    static readonly Type StoreType = Product.GetType("Pal98Timer.TimerSettingsStore", true);
    static readonly Type UserType = Product.GetType("Pal98Timer.TimerUserSettings", true);
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Console.WriteLine("PASS " + message); }
    static object Call(object target, string name, params object[] args)
    {
        try { return (target as Type ?? target.GetType()).GetMethod(name, Flags).Invoke(target is Type ? null : target, args); }
        catch (TargetInvocationException ex) { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }
    static object Store(string installation, string local, Action<string> error = null)
    { return Activator.CreateInstance(StoreType, Flags, null, new object[] { installation, local, error }, null); }
    static string DirectoryOf(object store) { return (string)StoreType.GetField("DirectoryPath", Flags).GetValue(store); }
    static string PathOf(object store, string name) { return (string)Call(store, "GetPath", name); }
    static void Save(object store, string name, string value) { Call(store, "WriteText", name, value, Encoding.UTF8); }
    static void Bind(object store) { UserType.GetField("store", Flags).SetValue(null, store); }
    static MConfig Config() { var result = (MConfig)FormatterServices.GetUninitializedObject(typeof(MConfig)); result.LoadConfig(); return result; }
    static bool Fails(Action action)
    {
        try { action(); return false; }
        catch (Exception ex) { if (ex.GetType().Name == "TimerSettingsException") return true; throw; }
    }
    sealed class DenyWrite : IDisposable
    {
        readonly string directory; readonly DirectorySecurity original;
        internal DenyWrite(string directory) {
            this.directory = directory; original = Directory.GetAccessControl(directory);
            var acl = Directory.GetAccessControl(directory);
            acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Deny));
            Directory.SetAccessControl(directory, acl);
        }
        public void Dispose() {
            var restore = new DirectorySecurity();
            restore.SetSecurityDescriptorSddlForm(original.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
            Directory.SetAccessControl(directory, restore);
        }
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [STAThread] static int Main(string[] args)
    {
        try { Run(Path.GetFullPath(args[0]), args[1]); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Run(string root, string keyboard)
    {
        string writable = Path.Combine(root, "writable"), writableUser = Path.Combine(root, "writable-user");
        Directory.CreateDirectory(writable);
        File.WriteAllText(Path.Combine(writable, "config.txt"), "local\r\n彩蛋\r\n一|二", Encoding.UTF8);
        var localStore = Store(writable, writableUser);
        Check(PathOf(localStore, "config.txt") == Path.Combine(writable, "config.txt"), "writable installation uses local settings directly");
        Save(localStore, "config.txt", "local edit\r\n彩蛋\r\n一|二");
        Check(File.ReadAllText(Path.Combine(writable, "config.txt")).StartsWith("local edit") && !Directory.Exists(writableUser), "local save does not create user configuration copies or receipts");
        Check(PathOf(Store(writable, writableUser), "config.txt") == Path.Combine(writable, "config.txt"), "writable restart stays local");
        string oldSettings = (string)StoreType.GetField("legacy", Flags).GetValue(localStore);
        Directory.CreateDirectory(oldSettings); File.WriteAllText(Path.Combine(oldSettings, "config.txt"), "old user preference");
        var withOldUser = Store(writable, writableUser);
        Check(PathOf(withOldUser, "config.txt") == Path.Combine(writable, "config.txt") &&
            !Directory.Exists(Path.Combine(writableUser, "PAL98")), "writable installation does not migrate old user settings to unified storage");
        File.WriteAllText(Path.Combine(oldSettings, "skip_node"), "old missing-local preference");
        Check(File.ReadAllText(PathOf(withOldUser, "skip_node")) == "old missing-local preference", "missing local setting remains readable from the legacy user directory without migration");
        Save(withOldUser, "skip_node", "local preference");
        Check(File.ReadAllText(PathOf(Store(writable, writableUser), "skip_node")) == "local preference", "saving a legacy fallback in local mode persists locally");
        string installation = Path.Combine(root, "中文只读安装包"), local = Path.Combine(root, "user");
        Directory.CreateDirectory(installation);
        string legacy = Path.Combine(installation, "config.txt");
        string original = "玩家旧标题\r\n彩蛋\r\n大吉|小吉";
        File.WriteAllText(legacy, original, Encoding.UTF8); File.SetAttributes(legacy, FileAttributes.ReadOnly);
        File.WriteAllText(Path.Combine(installation, "player.device"), "fixture credential must stay here");
        File.WriteAllText(Path.Combine(installation, "bestPAL98DX9.txt"), "fixture scores must stay here");
        File.WriteAllText(Path.Combine(installation, "sound_config.txt"), "GlobalEnabled=false\nToggleHotkey=0\n");
        File.WriteAllText(Path.Combine(installation, "keychange.txt"), "0\n65:66");
        File.WriteAllBytes(Path.Combine(installation, "bg.png"), new byte[] { 1, 2, 3 });
        int errors = 0; string errorText = "";
        var store = Store(installation, local, message => { errors++; errorText = message; }); Bind(store);
        using (new DenyWrite(installation)) {
            Check(Config().Title == "玩家旧标题", "read-only installation migrates actual preferences");
            Check(File.ReadAllText(PathOf(store, "config.txt")) == original && File.ReadAllText(legacy) == original, "migration preserves bytes and legacy source");
            Check((File.GetAttributes(PathOf(store, "config.txt")) & FileAttributes.ReadOnly) == 0, "migration does not inherit the package read-only attribute");
            Save(store, "config.txt", "用户新标题\r\n彩蛋\r\n一|二");
            Check(Config().Title == "用户新标题", "user settings take precedence after save");
            string previousDirectory = Path.Combine(Path.GetDirectoryName(DirectoryOf(store)), "state", "previous");
            Check(Array.Exists(Directory.GetFiles(previousDirectory, "config.txt.*"), p => File.ReadAllText(p) == original), "save retains previous preferences");
            Check(!File.Exists(Path.Combine(DirectoryOf(store), "player.device")) && !File.Exists(Path.Combine(DirectoryOf(store), "bestPAL98DX9.txt")), "credentials and scores are not migrated");
            Check(!SoundConfig.ins.GlobalEnabled, "sound preferences read from legacy read-only source");
            SoundConfig.ins.GlobalEnabled = true; SoundConfig.ins.SaveConfig();
            Check(File.ReadAllText(PathOf(store, "sound_config.txt")).Contains("GlobalEnabled=true"), "sound changes persist in user directory");
            new GBoard().Save(); var board = new GBoard(); board.Load();
            Check(File.Exists(PathOf(store, "GDisplay.cnf")) && !File.Exists(Path.Combine(installation, "GDisplay.cnf")), "layout round trip does not write the installation");
            var overlay = Product.GetType("Pal98Timer.Dx9OverlaySettings", true);
            Call(overlay, "SaveEnabled", true);
            Check((bool)Call(overlay, "LoadEnabled"), "overlay preference round trip uses user directory");
            var obs = Product.GetType("Pal98Timer.ObsWindowStyleStore", true);
            var style = Call(obs, "Load"); style.GetType().GetField("Enabled", Flags).SetValue(style, true); Call(obs, "Save", style);
            Check((bool)Call(obs, "Load").GetType().GetField("Enabled", Flags).GetValue(Call(obs, "Load")), "OBS preferences survive round trip");
            Save(store, "LastCore", "Pal98Dx9Automatic"); Save(store, "size", "300*280*1*2"); Save(store, "skip_node", "1");
            Bind(Store(installation, local)); Check(Config().Title == "用户新标题", "restart keeps the user version"); Bind(store);
        }
        File.SetAttributes(legacy, FileAttributes.Normal);
        File.WriteAllText(legacy, "本地更新标题\r\n新彩蛋\r\n三|四", Encoding.UTF8);
        DateTime future = DateTime.UtcNow.AddDays(2); File.SetLastWriteTimeUtc(legacy, future);
        Check(Config().Title == "本地更新标题", "newer local configuration synchronizes on the next read");
        Save(store, "config.txt", "再次保存标题\r\n彩蛋\r\n一|二");
        Bind(Store(installation, local)); Check(Config().Title == "再次保存标题", "a future local timestamp cannot repeatedly undo user saves"); Bind(store);
        File.Delete(PathOf(store, "config.txt"));
        Check(Config().Title == "本地更新标题", "missing user file falls back to the existing local file");
        PathOf(store, "bg.png"); Call(store, "Remove", "bg.png");
        Check(!File.Exists(PathOf(Store(installation, local), "bg.png")), "cleared background stays cleared after restart");
        File.WriteAllBytes(Path.Combine(installation, "bg.png"), new byte[] { 4, 5 });
        File.SetLastWriteTimeUtc(Path.Combine(installation, "bg.png"), future);
        Check(File.ReadAllBytes(PathOf(store, "bg.png"))[0] == 4, "a newly updated local background can still synchronize");
        var oldAcl = File.GetAccessControl(legacy);
        var noRead = File.GetAccessControl(legacy);
        noRead.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.ReadData, AccessControlType.Deny));
        File.SetLastWriteTimeUtc(legacy, future.AddDays(1)); File.SetAccessControl(legacy, noRead);
        int oldWarnings = errors;
        try {
            Check(Config().Title == "本地更新标题" && errors == oldWarnings + 1 && errorText.Contains("继续使用已保存的用户配置"), "unreadable newer local file cannot block a usable user copy");
            Config(); Check(errors == oldWarnings + 1, "repeated source denial does not repeat the same warning");
        } finally {
            var restored = new FileSecurity(); restored.SetSecurityDescriptorSddlForm(oldAcl.GetSecurityDescriptorSddlForm(AccessControlSections.Access), AccessControlSections.Access);
            File.SetAccessControl(legacy, restored);
        }
        Check(DirectoryOf(store) == DirectoryOf(Store(installation.ToUpperInvariant() + Path.DirectorySeparatorChar, local)), "installation identity ignores case and trailing separators");
        Check(DirectoryOf(store) != DirectoryOf(Store(installation + "-another", local)), "different installations keep separate settings");
        var keyAssembly = Assembly.LoadFrom(keyboard);
        var keyStoreType = keyAssembly.GetType("Pal98Timer.TimerSettingsStore", true);
        Check(DirectoryOf(store) == (string)keyStoreType.GetMethod("GetDirectory", Flags).Invoke(null, new object[] { installation, local }), "timer and KeyChanger resolve the same settings directory");
        try { PathOf(store, "../outside"); throw new Exception("path traversal accepted"); } catch (ArgumentException) { checks++; }
        string userFile = PathOf(store, "skip_node"); File.SetAttributes(userFile, FileAttributes.ReadOnly);
        int writeErrors = errors;
        try {
            Check(Fails(() => Save(store, "skip_node", "0")), "user file write denial is reported instead of silently discarded");
            Check(errors == writeErrors + 1 && errorText.Contains(DirectoryOf(store)) && errorText.Contains("读写权限"), "permission warning identifies user directory and recovery action");
            Check(File.ReadAllText(userFile) == "1", "failed save preserves original preferences");
        } finally { File.SetAttributes(userFile, FileAttributes.Normal); }
        int blockedErrors = 0;
        var blocked = Store(installation, Path.Combine(root, "blocked"), message => blockedErrors++);
        Directory.CreateDirectory(DirectoryOf(blocked));
        using (new DenyWrite(DirectoryOf(blocked))) {
            Check(Fails(() => PathOf(blocked, "config.txt")) && blockedErrors == 1, "unwritable user directory produces an explicit startup error");
        }
        Save(store, "skip_node", "0"); Check(File.ReadAllText(userFile) == "0", "saving works after the permission problem is corrected");
        File.SetAttributes(userFile, FileAttributes.ReadOnly);
        try { Check(Fails(() => Save(store, "skip_node", "1")) && errors == writeErrors + 2, "a new permission failure after recovery notifies again"); }
        finally { File.SetAttributes(userFile, FileAttributes.Normal); }
        string emptyInstallation = Path.Combine(root, "empty"); Directory.CreateDirectory(emptyInstallation);
        var defaults = Store(emptyInstallation, local, message => { }); Bind(defaults); Call(defaults, "Initialize");
        using (new DenyWrite(emptyInstallation)) {
            Check(Config().Title != null && PathOf(defaults, "config.txt") != Path.Combine(emptyInstallation, "config.txt"), "permission lost after startup activates user fallback");
        }
        File.Delete(PathOf(defaults, "config.txt"));
        using (new DenyWrite(DirectoryOf(defaults))) {
            Check(Fails(() => Config()), "default config save failure is not swallowed as successful startup");
        }
        Bind(store);
        // No constructor, core thread, game access or helper process is started.
        var incomplete = (GForm)FormatterServices.GetUninitializedObject(typeof(GForm));
        Call(incomplete, "tmMain_Tick", null, EventArgs.Empty);
        Check(true, "unfinished main form cannot run the helper-start timer callback");
        bool handled; incomplete.OnKeyPress(new KeyboardLib.HookStruct { vkCode = (int)Keys.F11, flags = 128 }, out handled);
        Check(!handled, "unfinished main form cannot launch KeyChanger through F11");
        CheckErrorDialogDoesNotPumpMainTimer();
        Console.WriteLine("PASS user settings total=" + checks + "; isolated directories only; no game or network used");
    }
    static void CheckErrorDialogDoesNotPumpMainTimer()
    {
        string title = "配置访问诊断回归-" + Guid.NewGuid().ToString("N");
        bool found = false;
        var closer = new Thread(() => {
            for (int i = 0; i < 150; i++) {
                IntPtr window = FindWindow("#32770", title);
                if (window != IntPtr.Zero) { found = true; Thread.Sleep(150); PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero); return; }
                Thread.Sleep(20);
            }
            Environment.Exit(77);
        });
        closer.IsBackground = true; closer.Start();
        int ticks = 0;
        using (var timer = new System.Windows.Forms.Timer { Interval = 10 }) {
            timer.Tick += delegate { ticks++; }; timer.Start();
            Call(UserType, "ShowError", "配置访问被拒绝。请检查用户目录读写权限。\n此测试窗口会自动关闭。", title);
            timer.Stop();
        }
        closer.Join(); Check(found && ticks == 0, "visible failure dialog does not pump main-window timers or launch KeyChanger");
    }
}

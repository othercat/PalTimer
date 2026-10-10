using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Pal98Timer;

internal static class StartupPermissionBehavior
{
    private static int checks;
    private static void Check(bool ok,string message) { if(!ok)throw new Exception(message);++checks; }
    private static MConfig Load(string path) {
        var settings=(MConfig)FormatterServices.GetUninitializedObject(typeof(MConfig));
        settings.LoadConfig(path);return settings;
    }
    static int Main(string[] args) {
        try { Run(Path.GetFullPath(args[0]),args.Length>1&&args[1]=="expect-old-failure");return 0; }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
    private static void Run(string root,bool old) {
        Directory.CreateDirectory(root);
        var bootstrap=typeof(MConfig).Assembly.GetType("Pal98Timer.StartupDependencies");
        var diagnostic=bootstrap == null ? null : bootstrap.GetField("DiagnosticDirectory",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
        if(diagnostic!=null)diagnostic.SetValue(null,Path.Combine(root,"diagnostics"));
        string path=Path.Combine(root,"config.txt"),text="玩家标题\r\n彩蛋\r\n大吉|小吉";
        File.WriteAllText(path,text,Encoding.UTF8);File.SetAttributes(path,FileAttributes.ReadOnly);
        try {
            if(old) {
                try{Load(path);}catch(UnauthorizedAccessException){Console.WriteLine("REPRODUCED old config reader requested write access and failed before window startup");return;}
                throw new Exception("Old reader unexpectedly accepted a read-only configuration");
            }
            var settings=Load(path);
            Check(settings.Title=="玩家标题"&&settings.Lucks.Length==2,"read-only config loads actual settings");
            Check(File.ReadAllText(path)==text,"read-only config bytes preserved");
        }finally{File.SetAttributes(path,FileAttributes.Normal);}
        string legacy="旧标题\nunused1\nunused2\nunused3\n旧彩蛋\n一|二";
        File.WriteAllText(path,legacy,Encoding.UTF8);File.SetAttributes(path,FileAttributes.ReadOnly);
        try {
            var settings=Load(path);
            Check(settings.Title=="旧标题"&&settings.ColorEgg=="旧彩蛋"&&settings.Lucks.Length==2,"legacy settings usable without write permission");
            Check(File.ReadAllText(path)==legacy,"failed migration preserves original settings");
        }finally{File.SetAttributes(path,FileAttributes.Normal);}
        string restricted=Path.Combine(root,"read-only-directory");Directory.CreateDirectory(restricted);
        var original=Directory.GetAccessControl(restricted);
        var acl=Directory.GetAccessControl(restricted);
        acl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
            FileSystemRights.Write,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
        Directory.SetAccessControl(restricted,acl);
        try {
            string missing=Path.Combine(restricted,"config.txt");
            var settings=Load(missing);
            Check(settings.Title.TrimEnd('\r')=="自动计时器"&&settings.Lucks.Length==2,"defaults work when directory rejects file creation");
            Check(!File.Exists(missing),"denied directory remains unchanged");
        }finally{
            var restore=new DirectorySecurity();
            restore.SetSecurityDescriptorSddlForm(original.GetSecurityDescriptorSddlForm(AccessControlSections.Access),AccessControlSections.Access);
            Directory.SetAccessControl(restricted,restore);
        }
        string writable=Path.Combine(root,"new-config.txt");
        var created=Load(writable);Check(File.Exists(writable)&&created.Title.TrimEnd('\r')=="自动计时器","writable directory still saves defaults");
        Console.WriteLine("PASS startup permission behavior: "+checks+" checks; no game or network used");
    }
}

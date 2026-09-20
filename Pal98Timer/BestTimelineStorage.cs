using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Pal98Timer
{
    internal static class BestTimelineStorage
    {
        // Same-volume replace keeps the active line intact until the complete
        // replacement is durable. No delete/move-old-first fallback is allowed.
        internal static void Write(string path, string contents, bool replaceExisting = true)
        {
            string target = Path.GetFullPath(path);
            string stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
            string pending = target + ".pending-" + stamp;
            string backup = Path.Combine(Path.GetDirectoryName(target),
                Path.GetFileNameWithoutExtension(target) + "-" + stamp + Path.GetExtension(target));
            byte[] bytes = new UTF8Encoding(false, true).GetBytes(contents);
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (!File.ReadAllBytes(pending).SequenceEqual(bytes))
                throw new IOException("最佳线临时文件校验失败，保留于：" + pending);
            // If replacement fails, retain the pending file for recovery and
            // propagate the error; callers must not reset or report success.
            if (replaceExisting && File.Exists(target)) File.Replace(pending, target, backup);
            else File.Move(pending, target);
        }
    }
}

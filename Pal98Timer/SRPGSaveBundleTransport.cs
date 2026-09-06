using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    // Disk saves already contain PALDLL's projection of roles 0..5. Never
    // rebuild that layout by truncating the expanded in-process role arrays.
    internal static class SRPGSaveBundleTransport
    {
        private const string Schema = "PalTimer.PAL98SaveBundle.v1";
        private const string CustomSuffix = ".pal98-custom-roles.json";
        private const int MaximumBytes = 4 * 1024 * 1024;

        private sealed class Context
        {
            internal string RuntimeHash, ProfileIdentity, LibraryHash;
            internal Dictionary<string, object> Library;
            internal int RoleCount, SaveLength;
        }

        internal static bool IsRequired(string gameFolder)
        {
            string dll = Path.Combine(gameFolder, "PAL.dll");
            if (!File.Exists(dll)) return false;
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(dll);
            return new Version(version.FileMajorPart, version.FileMinorPart,
                version.FileBuildPart, Math.Max(0, version.FilePrivatePart)) >= new Version(1, 6, 2, 0);
        }

        internal static void Capture(string gameFolder, string savedFile, SRPGobj package)
        {
            if (package == null) throw new ArgumentNullException("package");
            string name = Path.GetFileName(savedFile);
            ValidateSlot(name);
            string path = Within(gameFolder, name);
            if (!string.Equals(path, Path.GetFullPath(savedFile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("接力存档必须来自当前游戏目录。");
            Exception last = null;
            for (int attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Context context = ReadContext(gameFolder);
                    byte[] rpg = Read(path);
                    ValidateRpg(rpg, context);
                    byte[] custom = File.Exists(path + CustomSuffix) ? Read(path + CustomSuffix) : null;
                    ValidateCustom(custom, name, rpg, context);
                    // PALDLL writes the sidecar after the RPG. Retry only this
                    // explicit export, and require a stable complete snapshot.
                    if (Hash(Read(path)) != Hash(rpg) ||
                        ContextKey(ReadContext(gameFolder)) != ContextKey(context))
                        throw new IOException("游戏仍在保存，请稍后重试。");
                    var bundle = new Dictionary<string, object>
                    {
                        { "schema", Schema }, { "source_save_file", name },
                        { "rpg_sha256", Hash(rpg) }, { "rpg_size", rpg.Length },
                        { "runtime_sha256", context.RuntimeHash },
                        { "profile_identity", context.ProfileIdentity },
                        { "role_library_sha256", context.LibraryHash },
                        { "custom_roles", custom == null ? null : Convert.ToBase64String(custom) }
                    };
                    package.RPG = rpg;
                    package.Pal98SaveBundle = Encode(bundle);
                    return;
                }
                catch (Exception error) when (error is IOException || error is InvalidDataException) { last = error; }
                if (attempt != 19) Thread.Sleep(100);
            }
            throw new IOException("未取得完整的 RPG 与扩展角色存档；没有导出不完整进度。", last);
        }

        internal static void ValidateLegacyTarget(string gameFolder)
        {
            if (IsRequired(gameFolder))
                throw new InvalidDataException("旧接力包没有新版存档身份和扩展角色快照。请用新版计时器从原游戏进度重新导出。");
        }

        internal static void Import(string gameFolder, string saveName, SRPGobj package,
            Action<int> afterPublish = null)
        {
            ValidateSlot(saveName);
            Dictionary<string, object> bundle = Json(package.Pal98SaveBundle);
            if (Text(bundle, "schema") != Schema) throw new InvalidDataException("接力存档版本不受支持。");
            string originalName = Text(bundle, "source_save_file");
            ValidateSlot(originalName);
            Context context = ReadContext(gameFolder);
            if (Text(bundle, "runtime_sha256") != context.RuntimeHash ||
                Text(bundle, "profile_identity") != context.ProfileIdentity ||
                Text(bundle, "role_library_sha256") != context.LibraryHash)
                throw new InvalidDataException("接力包与目标 PALDLL、内容包或固定角色库不同；请先使用相同版本，计时器不会替换这些文件。");
            ValidateRpg(package.RPG, context);
            if (Number(bundle["rpg_size"]) != package.RPG.Length ||
                Text(bundle, "rpg_sha256") != Hash(package.RPG))
                throw new InvalidDataException("接力 RPG 的长度或 SHA-256 不符。");
            object rawCustom;
            if (!bundle.TryGetValue("custom_roles", out rawCustom))
                throw new InvalidDataException("接力包缺少扩展角色快照标记。");
            byte[] custom = rawCustom == null ? null : Convert.FromBase64String((string)rawCustom);
            ValidateCustom(custom, originalName, package.RPG, context);
            if (custom != null)
            {
                Dictionary<string, object> state = Json(custom);
                state["save_file"] = saveName;
                custom = Encode(state);
                ValidateCustom(custom, saveName, package.RPG, context);
            }
            SRPGFlyingFlagSidecarSnapshot flag = SRPGSidecarTransport.ReadFlyingFlagSnapshot(package);
            if (flag == null) throw new InvalidDataException("新版接力包缺少飞行旗快照标记。");
            var files = new Dictionary<string, byte[]>
            {
                { saveName, package.RPG }, { saveName + CustomSuffix, custom },
                // v1.62 does not activate the legacy 999-slot sidecar. Preserve
                // the previous file in the transaction backup, not beside a new RPG.
                { saveName + ".pal98-ext-magics.json", null },
                { SRPGSidecarTransport.FlyingFlagSidecarFileName, flag.Present ? flag.Payload : null }
            };
            Publish(gameFolder, files, afterPublish);
        }

        private static Context ReadContext(string gameFolder)
        {
            var result = new Context();
            result.RuntimeHash = Hash(Read(Within(gameFolder, "PAL.dll")));
            string pointerPath = Within(gameFolder, "palmod/Profiles/current.json");
            byte[] sss;
            if (File.Exists(pointerPath))
            {
                var pointer = Json(Read(pointerPath));
                if (Text(pointer, "schema") != "PAL98.EffectiveGameProfilePointer.v1")
                    throw new InvalidDataException("当前内容包指针无效。");
                string stage = Within(Within(gameFolder, "palmod/Profiles"), Text(pointer, "staging_relative_path"));
                byte[] descriptorBytes = Read(Within(stage, "manifest/game-profile.json"));
                if (!SameHash(Hash(descriptorBytes), Text(pointer, "descriptor_sha256")))
                    throw new InvalidDataException("当前内容包描述文件校验失败。");
                var descriptor = Json(descriptorBytes);
                if (Text(descriptor, "schema") != "PAL98.GameProfile.v1" ||
                    Text(descriptor, "profile_id") != Text(pointer, "profile_id") ||
                    Text(descriptor, "profile_version") != Text(pointer, "profile_version"))
                    throw new InvalidDataException("内容包身份不一致。");
                var runtime = Object(descriptor["required_runtime"]);
                if (Array(runtime["capabilities"]).Any(x => (string)x == "profile-save-sidecar.v1"))
                    throw new InvalidDataException("此内容包使用另一套剧情附属存档，当前接力功能尚不支持完整运输。");
                var resources = Array(descriptor["resource_set"]).Select(Object).ToArray();
                sss = Resource(stage, resources, "SSS.MKF");
                Resource(stage, resources, "WORD.DAT");
                if (resources.Any(x => Text(x, "kind") == "SKILL.GLOBAL")) Resource(stage, resources, "SKILL.GLOBAL");
                result.ProfileIdentity = Text(pointer, "profile_id") + "@" + Text(pointer, "profile_version") + "#" + Hash(descriptorBytes);
            }
            else
            {
                sss = Read(Within(gameFolder, "SSS.MKF"));
                result.ProfileIdentity = "root#" + Hash(sss) + "#" + Hash(Read(Within(gameFolder, "WORD.DAT")));
            }
            if (sss.Length < 12) throw new InvalidDataException("SSS 场景记录缺失。");
            long start = BitConverter.ToUInt32(sss, 0), end = BitConverter.ToUInt32(sss, 4);
            if (start < 12 || end <= start || end > sss.Length || (end - start) % 32 != 0)
                throw new InvalidDataException("SSS 事件区长度无效。");
            result.SaveLength = checked(14064 + (int)(end - start));
            string libraryPath = Within(gameFolder, "palmod/CustomRoles/roles.json");
            result.LibraryHash = "absent";
            if (File.Exists(libraryPath))
            {
                byte[] libraryBytes = Read(libraryPath);
                result.Library = Json(libraryBytes);
                if (Text(result.Library, "schema") != "PAL98.CustomRoleLibrary.v1" ||
                    Text(result.Library, "content_scope") != "all-effective-content-profiles")
                    throw new InvalidDataException("固定角色库格式无效。");
                Text(result.Library, "library_id"); Text(result.Library, "library_version");
                object[] roles = Array(result.Library["custom_roles"]);
                if (roles.Length > 10) throw new InvalidDataException("固定角色库超过 6–15 范围。");
                for (int i = 0; i < roles.Length; i++)
                    if (Number(Object(roles[i])["role_id"]) != i + 6)
                        throw new InvalidDataException("固定角色 ID 必须从 6 连续排列。");
                result.RoleCount = roles.Length;
                result.LibraryHash = Hash(libraryBytes);
            }
            return result;
        }

        private static byte[] Resource(string root, Dictionary<string, object>[] resources, string kind)
        {
            var matches = resources.Where(x => Text(x, "kind") == kind).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("内容包资源缺失或重复：" + kind);
            var row = matches[0];
            byte[] bytes = Read(Within(root, Text(row, "relative_path")));
            if (bytes.Length != Number(row["size_bytes"]) || !SameHash(Hash(bytes), Text(row, "sha256")))
                throw new InvalidDataException("内容包资源校验失败：" + kind);
            return bytes;
        }

        private static void ValidateRpg(byte[] bytes, Context context)
        {
            if (bytes == null || bytes.Length != context.SaveLength)
                throw new InvalidDataException("RPG 与当前内容包的 Win95 事件区长度不符。");
            int party = BitConverter.ToUInt16(bytes, 6) + 1;
            if (party < 1 || party > 3) throw new InvalidDataException("RPG 队伍人数无效。");
            for (int i = 0; i < party; i++)
                if (BitConverter.ToUInt16(bytes, 44 + 10 * i) >= 6 + context.RoleCount)
                    throw new InvalidDataException("RPG 队员超出固定角色库。");
        }

        private static void ValidateCustom(byte[] bytes, string name, byte[] rpg, Context context)
        {
            if (bytes == null)
            {
                if (context.RoleCount != 0) throw new InvalidDataException("扩展角色附属存档尚未写好或缺失。");
                return;
            }
            var state = Json(bytes);
            if (context.Library == null || Text(state, "schema") != "PAL98.CustomRoleSaveState.v1" ||
                Number(state["schema_version"]) != 1 || Text(state, "save_file") != name ||
                Number(state["rpg_size"]) != rpg.Length || !SameHash(Text(state, "rpg_sha256"), Hash(rpg)) ||
                Text(state, "library_id") != Text(context.Library, "library_id") ||
                Text(state, "library_version") != Text(context.Library, "library_version"))
                throw new InvalidDataException("扩展角色存档与 RPG 或固定库身份不一致。");
            object[] roles = Array(state["roles"]);
            if (roles.Length != context.RoleCount) throw new InvalidDataException("扩展角色存档数量与固定库不一致。");
            for (int i = 0; i < roles.Length; i++)
            {
                var role = Object(roles[i]);
                if (Number(role["role_id"]) != 6 + i) throw new InvalidDataException("扩展角色存档 ID 不连续。");
                ValidateNumbers(role["fields"], 75, short.MinValue, short.MaxValue);
                ValidateNumbers(role["experience_words"], 16, 0, uint.MaxValue);
                ValidateNumbers(role["modifiers"], 98, short.MinValue, short.MaxValue);
            }
        }

        private static void Publish(string root, Dictionary<string, byte[]> files, Action<int> afterPublish)
        {
            string stamp = ".paltimer-backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-" + Guid.NewGuid().ToString("N");
            var before = new Dictionary<string, byte[]>();
            var staged = new Dictionary<string, string>();
            var published = new List<string>();
            try
            {
                foreach (var item in files)
                {
                    string path = Within(root, item.Key);
                    if (Directory.Exists(path)) throw new IOException("存档目标被文件夹占用：" + item.Key);
                    before[path] = File.Exists(path) ? Read(path) : null;
                    if (before[path] != null) File.WriteAllBytes(path + stamp, before[path]);
                    if (item.Value != null)
                    {
                        string temporary = path + ".paltimer-" + Guid.NewGuid().ToString("N") + ".tmp";
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { stream.Write(item.Value, 0, item.Value.Length); stream.Flush(true); }
                        staged[path] = temporary;
                    }
                }
                foreach (var item in files)
                {
                    string path = Within(root, item.Key);
                    if (item.Value == null) { if (File.Exists(path)) File.Delete(path); }
                    else if (File.Exists(path)) File.Replace(staged[path], path, null, true);
                    else File.Move(staged[path], path);
                    published.Add(path);
                    if (afterPublish != null) afterPublish(published.Count);
                }
                foreach (var item in files)
                {
                    string path = Within(root, item.Key);
                    if (item.Value == null ? File.Exists(path) : Hash(Read(path)) != Hash(item.Value))
                        throw new IOException("接力存档落盘复核失败。");
                }
            }
            catch
            {
                foreach (string path in published.AsEnumerable().Reverse())
                {
                    byte[] bytes = before[path];
                    if (bytes == null) { if (File.Exists(path)) File.Delete(path); }
                    else
                    {
                        string rollback = path + ".rollback-" + Guid.NewGuid().ToString("N");
                        File.WriteAllBytes(rollback, bytes);
                        if (File.Exists(path)) File.Replace(rollback, path, null, true);
                        else File.Move(rollback, path);
                    }
                }
                throw;
            }
            finally { foreach (string path in staged.Values) if (File.Exists(path)) File.Delete(path); }
        }

        private static string ContextKey(Context c) { return c.RuntimeHash + c.ProfileIdentity + c.LibraryHash; }
        private static bool SameHash(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        private static void ValidateSlot(string name)
        {
            if (name == null || name.Length != 5 || name[0] < '1' || name[0] > '5' ||
                !name.EndsWith(".RPG", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("只支持进度 1–5 的 RPG。");
        }
        private static string Within(string root, string relative)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Path.IsPathRooted(relative)) throw new InvalidDataException("存档资源必须使用相对路径。");
            string path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("存档资源路径越界。");
            for (string current = path; current != null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("存档资源不支持重解析路径。");
            return path;
        }
        private static byte[] Read(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > MaximumBytes) throw new IOException("文件缺失或长度无效：" + path);
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length > MaximumBytes) throw new IOException("文件读取期间超过长度限制。");
            return bytes;
        }
        private static string Hash(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = MaximumBytes, RecursionLimit = 64 }; }
        private static Dictionary<string, object> Json(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("存档 JSON 长度无效。");
            try { return Object(Serializer().DeserializeObject(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\ufeff'))); }
            catch (Exception error) { throw new InvalidDataException("存档 JSON 格式无效。", error); }
        }
        private static byte[] Encode(Dictionary<string, object> value) { return Encoding.UTF8.GetBytes(Serializer().Serialize(value)); }
        private static Dictionary<string, object> Object(object value)
        { return value as Dictionary<string, object> ?? throw new InvalidDataException("存档字段必须是对象。"); }
        private static object[] Array(object value)
        {
            var items = value as IEnumerable;
            if (items == null || value is string || value is IDictionary) throw new InvalidDataException("存档字段必须是数组。");
            return items.Cast<object>().ToArray();
        }
        private static string Text(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw) || !(raw is string) || string.IsNullOrWhiteSpace((string)raw))
                throw new InvalidDataException("存档缺少有效字段：" + key);
            return (string)raw;
        }
        private static long Number(object value)
        {
            if (!(value is int) && !(value is long) && !(value is uint)) throw new InvalidDataException("存档数值必须为整数。");
            return Convert.ToInt64(value);
        }
        private static void ValidateNumbers(object value, int count, long min, long max)
        {
            object[] items = Array(value);
            if (items.Length != count || items.Any(x => Number(x) < min || Number(x) > max))
                throw new InvalidDataException("扩展角色字段数量或数值越界。");
        }
    }
}

// Local display-only contract shared with PalTimerOnline. No credentials or networking.
using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Collections.Generic;

namespace Pal98.LocalDisplay
{
    [DataContract]
    internal sealed class PlayerIdentity
    {
        [DataMember(IsRequired = true)] public int schema = 1;
        [DataMember(IsRequired = true)] public string source;
        [DataMember(IsRequired = true)] public string server_origin;
        [DataMember(IsRequired = true)] public string player_id;
        [DataMember(IsRequired = true)] public string display_name;
        [DataMember(IsRequired = true)] public bool bound;
        [DataMember(IsRequired = true)] public string updated_at_utc;
        [DataMember(IsRequired = true)] public string revision;
    }

    internal sealed class IdentitySnapshot
    {
        internal PlayerIdentity Value;
        internal string Token, Error;
    }

    internal static class LocalPlayerIdentity
    {
        internal const string RelativePath = "palmod/player-identity.v1.json";
        internal const int MaximumBytes = 8192;
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        internal static string Name(string value)
        {
            if (value == null) throw new InvalidDataException("身份名字缺失。");
            string name = value.Trim().Normalize(NormalizationForm.FormC);
            if (name.Length > 64 || name.Any(char.IsControl)) throw new InvalidDataException("身份名字不能超过64个字符或包含控制字符。");
            // The strict UTF-8 encoder also rejects unpaired UTF-16 surrogates.
            Utf8.GetByteCount(name);
            return name;
        }

        internal static string Origin(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidDataException("身份服务器来源无效。");
            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        internal static PlayerIdentity Manual(string name)
        {
            return New("manual", "", "", Name(name), false);
        }

        internal static PlayerIdentity Server(string server, string id, string name, bool bound)
        {
            string normalized = Name(name ?? "");
            // Explicit unbinding or an empty server name clears the old identity.
            if (!bound || normalized.Length == 0) return New("timer", Origin(server), "", "", false);
            Guid player;
            if (!Guid.TryParse(id, out player) || player == Guid.Empty) throw new InvalidDataException("绑定玩家ID无效。");
            return New("timer", Origin(server), player.ToString("D"), normalized, true);
        }

        private static PlayerIdentity New(string source, string origin, string id, string name, bool bound)
        {
            return new PlayerIdentity { source = source, server_origin = origin, player_id = id,
                display_name = name, bound = bound, revision = Guid.NewGuid().ToString("N"),
                updated_at_utc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture) };
        }

        private static void Validate(PlayerIdentity value)
        {
            if (value == null || value.schema != 1 || value.display_name == null || value.player_id == null || value.server_origin == null)
                throw new InvalidDataException("身份文件格式无效。");
            if (Name(value.display_name) != value.display_name) throw new InvalidDataException("身份名字格式无效。");
            Guid revision, player;
            DateTime timestamp;
            if (!Guid.TryParseExact(value.revision, "N", out revision) || revision == Guid.Empty ||
                !DateTime.TryParseExact(value.updated_at_utc, "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out timestamp))
                throw new InvalidDataException("身份文件版本标记无效。");
            if (value.source == "manual") {
                if (value.bound || value.player_id.Length != 0 || value.server_origin.Length != 0) throw new InvalidDataException("本地身份不能声明服务器绑定。");
            } else if (value.source == "timer") {
                if (Origin(value.server_origin) != value.server_origin || (value.bound
                    ? !Guid.TryParseExact(value.player_id, "D", out player) || player == Guid.Empty || value.display_name.Length == 0
                    : value.player_id.Length != 0 || value.display_name.Length != 0)) throw new InvalidDataException("服务器身份字段不一致。");
            } else throw new InvalidDataException("身份来源无效。");
        }

        internal static byte[] Serialize(PlayerIdentity value)
        {
            Validate(value);
            using (var stream = new MemoryStream()) {
                new DataContractJsonSerializer(typeof(PlayerIdentity)).WriteObject(stream, value);
                byte[] result = stream.ToArray();
                if (result.Length > MaximumBytes) throw new InvalidDataException("身份文件过大。");
                return result;
            }
        }

        internal static PlayerIdentity Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaximumBytes) throw new InvalidDataException("身份文件大小无效。");
            string json = Utf8.GetString(bytes);
            if (json.Length > 0 && json[0] == '\ufeff') json = json.Substring(1);
            // The framework reader coerces string booleans and ignores trailing
            // JSON. This flat, bounded contract must reject both explicitly.
            var fields = new FlatJson(json).Read();
            var value = new PlayerIdentity { schema = Field<int>(fields, "schema"), source = Field<string>(fields, "source"),
                server_origin = Field<string>(fields, "server_origin"), player_id = Field<string>(fields, "player_id"),
                display_name = Field<string>(fields, "display_name"), bound = Field<bool>(fields, "bound"),
                updated_at_utc = Field<string>(fields, "updated_at_utc"), revision = Field<string>(fields, "revision") };
            Validate(value); return value;
        }

        private static T Field<T>(Dictionary<string, object> fields, string key)
        {
            object value;
            if (!fields.TryGetValue(key, out value) || !(value is T)) throw new InvalidDataException("身份字段类型无效。");
            return (T)value;
        }

        private sealed class FlatJson
        {
            private readonly string text; private int at;
            internal FlatJson(string text) { this.text = text; }
            private void Space() { while (at < text.Length && " \t\r\n".IndexOf(text[at]) >= 0) ++at; }
            private char Take() { if (at == text.Length) throw new InvalidDataException("身份JSON不完整。"); return text[at++]; }
            private void Need(char c) { if (Take() != c) throw new InvalidDataException("身份JSON格式无效。"); }
            private string Text()
            {
                Need('"'); var result = new StringBuilder();
                while (true) {
                    char c = Take(); if (c == '"') break;
                    if (c < 32) throw new InvalidDataException("身份JSON包含控制字符。");
                    if (c == '\\') {
                        c = Take();
                        switch (c) {
                            case '"': case '\\': case '/': break;
                            case 'b': c = '\b'; break; case 'f': c = '\f'; break;
                            case 'n': c = '\n'; break; case 'r': c = '\r'; break; case 't': c = '\t'; break;
                            case 'u':
                                int code = 0;
                                for (int i = 0; i < 4; ++i) { char digit = Take(); int n = "0123456789abcdef".IndexOf(char.ToLowerInvariant(digit));
                                    if (n < 0) throw new InvalidDataException("身份Unicode转义无效。"); code = code * 16 + n; }
                                c = (char)code; break;
                            default: throw new InvalidDataException("身份JSON转义无效。");
                        }
                    }
                    result.Append(c);
                }
                string value = result.ToString(); Utf8.GetByteCount(value); return value;
            }
            private object Value()
            {
                Space(); if (at == text.Length) throw new InvalidDataException("身份JSON不完整。");
                if (text[at] == '"') return Text();
                foreach (string literal in new[] { "true", "false", "null" })
                    if (text.IndexOf(literal, at, StringComparison.Ordinal) == at) {
                        at += literal.Length; return literal == "null" ? null : (object)(literal == "true");
                    }
                int begin = at; if (text[at] == '-') ++at;
                int digits = at; while (at < text.Length && text[at] >= '0' && text[at] <= '9') ++at;
                int number;
                if (at == digits || at - digits > 1 && text[digits] == '0' || !int.TryParse(text.Substring(begin, at - begin),
                    System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out number))
                    throw new InvalidDataException("身份JSON值无效。");
                return number;
            }
            internal Dictionary<string, object> Read()
            {
                var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                Space(); Need('{'); Space();
                if (at < text.Length && text[at] == '}') ++at;
                else while (true) {
                    Space(); string key = Text(); Space(); Need(':');
                    if (fields.ContainsKey(key) || fields.Count >= 32) throw new InvalidDataException("身份JSON含重复或过多字段。");
                    fields.Add(key, Value()); Space(); char separator = Take();
                    if (separator == '}') break; if (separator != ',') throw new InvalidDataException("身份JSON分隔符无效。");
                }
                Space(); if (at != text.Length) throw new InvalidDataException("身份JSON含额外内容。");
                return fields;
            }
        }

        private static string Root(string root)
        {
            string full = Path.GetFullPath(root);
            string drive = Path.GetPathRoot(full);
            if (full.Length > drive.Length) full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(full)) throw new IOException("身份文件需要本地游戏目录。");
            for (var item = new DirectoryInfo(full); item != null; item = item.Parent)
                if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("身份文件不支持重解析目录。");
            return full;
        }

        private static void CheckPath(string path)
        {
            try {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("身份文件不支持重解析路径。");
            } catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }

        private static byte[] Read(string path)
        {
            CheckPath(Path.GetDirectoryName(path)); CheckPath(path);
            try {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) {
                    if (stream.Length > MaximumBytes) throw new InvalidDataException("身份文件超过8 KiB。");
                    byte[] bytes = new byte[(int)stream.Length]; int offset = 0, read;
                    while (offset < bytes.Length && (read = stream.Read(bytes, offset, bytes.Length - offset)) > 0) offset += read;
                    if (offset != bytes.Length || stream.ReadByte() != -1) throw new IOException("身份文件正在变化，请重试。");
                    return bytes;
                }
            } catch (FileNotFoundException) { return null; } catch (DirectoryNotFoundException) { return null; }
        }

        private static string Hash(byte[] bytes)
        {
            if (bytes == null) return "missing";
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }

        internal static IdentitySnapshot Load(string root)
        {
            var snapshot = new IdentitySnapshot();
            try {
                byte[] bytes = Read(Path.Combine(Root(root), RelativePath));
                snapshot.Token = Hash(bytes);
                if (bytes != null) snapshot.Value = Parse(bytes);
            } catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SerializationException || error is ArgumentException) {
                snapshot.Error = "身份未能读取，使用默认文案。";
            }
            return snapshot;
        }

        // expectedToken is required for manual edits. Timer updates deliberately
        // replace manual values on EVERY fresh success, even if the name is unchanged.
        // commit permits the timer to recheck its request generation immediately
        // before replacement, after staging/flush have completed.
        internal static bool Save(string root, PlayerIdentity value, string expectedToken, bool backup,
            Func<Action, bool> commit = null, byte[] behavior = null)
        {
            string full = Root(root), folder = Path.Combine(full, "palmod"), path = Path.Combine(full, RelativePath);
            byte[] bytes = Serialize(value);
            if (behavior != null && (behavior.Length == 0 || behavior.Length > 16384 || value.source != "timer"))
                throw new InvalidDataException("行为快照无效。");
            string mutexName = @"Local\PAL98.LocalPlayerIdentity.v1." + Hash(Utf8.GetBytes(full.ToUpperInvariant()));
            using (var gate = new Mutex(false, mutexName)) {
                bool locked;
                try { locked = gate.WaitOne(500); } catch (AbandonedMutexException) { locked = true; }
                if (!locked) throw new IOException("身份正在同步，请稍后重试。");
                string temporary = null, behaviorTemporary = null;
                string behaviorPath = Path.Combine(folder, "player-behavior.v1.json");
                try {
                    Root(full); CheckPath(folder); Directory.CreateDirectory(folder);
                    byte[] before = Read(path);
                    if (expectedToken != null && expectedToken != Hash(before)) throw new IOException("计时器或其它程序已更新身份，请刷新后重新修改。");
                    if (backup && expectedToken == null) throw new IOException("请先重新读取身份再保存。");
                    temporary = Path.Combine(folder, ".player-identity-" + Guid.NewGuid().ToString("N") + ".tmp");
                    using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
                    }
                    if (behavior != null) {
                        CheckPath(behaviorPath);
                        behaviorTemporary = Path.Combine(folder, ".player-behavior-" + Guid.NewGuid().ToString("N") + ".tmp");
                        using (var stream = new FileStream(behaviorTemporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                            stream.Write(behavior, 0, behavior.Length); stream.Flush(true);
                        }
                    }
                    Action replace = () => {
                        CheckPath(folder); CheckPath(path);
                        if (Hash(Read(path)) != Hash(before)) throw new IOException("身份在保存期间发生变化，请重试。");
                        string copy = null;
                        if (backup && before != null) {
                            string backups = Path.Combine(folder, "identity-backups"); CheckPath(backups); Directory.CreateDirectory(backups);
                            copy = Path.Combine(backups, "identity-" + Guid.NewGuid().ToString("N") + ".json");
                        }
                        // Match the exact identity revision. A crash between these
                        // replacements produces a mismatch/default, not a wrong player.
                        if (behaviorTemporary != null) {
                            CheckPath(behaviorPath);
                            if (File.Exists(behaviorPath)) File.Replace(behaviorTemporary, behaviorPath, null);
                            else File.Move(behaviorTemporary, behaviorPath);
                        }
                        if (before == null) File.Move(temporary, path); else File.Replace(temporary, path, copy);
                    };
                    if (commit != null) return commit(replace);
                    replace(); return true;
                } finally {
                    try {
                        if (temporary != null && File.Exists(temporary)) File.Delete(temporary);
                        if (behaviorTemporary != null && File.Exists(behaviorTemporary)) File.Delete(behaviorTemporary);
                    }
                    finally { gate.ReleaseMutex(); }
                }
            }
        }
    }
}

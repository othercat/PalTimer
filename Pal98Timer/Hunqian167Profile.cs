using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    public enum Hunqian167Edition
    {
        Unknown = 0,
        Easy = 1,
        Hard = 2,
        Nonhuman = 3
    }

    public sealed class Hunqian167ProfileIdentity
    {
        public Hunqian167ProfileIdentity(
            Hunqian167Edition edition,
            string profileId,
            string profileVersion,
            string displayName,
            string descriptorSha256,
            string sssSha256,
            int wordDatBytes)
        {
            Edition = edition;
            ProfileId = profileId;
            ProfileVersion = profileVersion;
            DisplayName = displayName;
            DescriptorSha256 = descriptorSha256;
            SssSha256 = sssSha256;
            WordDatBytes = wordDatBytes;
        }

        public Hunqian167Edition Edition { get; private set; }
        public string ProfileId { get; private set; }
        public string ProfileVersion { get; private set; }
        public string DisplayName { get; private set; }
        public string DescriptorSha256 { get; private set; }
        public string SssSha256 { get; private set; }
        public int WordDatBytes { get; private set; }
    }

    /// <summary>
    /// Public, read-only identities for the three v1.57 Hunqian 1.67 base profiles.
    /// Draw-card derivatives are deliberately not accepted by this three-edition core.
    /// </summary>
    public static class Hunqian167Profile
    {
        public const string PublicSchema = "PAL98.PublicToolProfile.v1";
        public const string CoreDisplayName = "仙剑98柔情DX9魂牵";
        public const string OrderedCredits = "女尸，孙小柔，othercat";
        public const int EventObjectRecordCount = 5332;
        public const int EventObjectRecordSize = 32;
        public const int ExpectedSaveLength = 184688;

        private const string PointerSchema = "PAL98.EffectiveGameProfilePointer.v1";
        private const string DescriptorSchema = "PAL98.GameProfile.v1";
        private const int MaximumPointerBytes = 4096;
        private const int MaximumDescriptorBytes = 1024 * 1024;

        private static readonly Hunqian167ProfileIdentity[] Supported = new Hunqian167ProfileIdentity[]
        {
            new Hunqian167ProfileIdentity(
                Hunqian167Edition.Easy,
                "pal98.hunqian167.easy",
                "1.0.2",
                "魂牵梦萦 1.67 简单 兼容配置档",
                "101fc674bfb54f4c094ee3c1eb74282a541c64bfc8043762491bf1b81155aea4",
                "8902882a9e6ef7c76604d462f1f86f40a483c05c9db41bbfd2981f2f7ee59163",
                5750),
            new Hunqian167ProfileIdentity(
                Hunqian167Edition.Hard,
                "pal98.hunqian167.hard",
                "1.0.2",
                "魂牵梦萦 1.67 困难 兼容配置档",
                "75f32ed288888f0b2358477fe783012380ec51b8bbb9db6ff677c5eed5d0c125",
                "43764f17a8de67a213e94f31fbb7809cabdb4f5f7060287fd196aaacec25e592",
                5750),
            new Hunqian167ProfileIdentity(
                Hunqian167Edition.Nonhuman,
                "pal98.hunqian167.nightmare",
                "1.0.3",
                "魂牵梦萦 1.67 非人 兼容配置档",
                "004705b7b9113107a0b6c2052f0f8579e04f651577e3691b2a9bf3b4262db8eb",
                "a496255f1b73cf989bdd0d2514c2ce488ff9bcd3fd597efff5a90e8406962bda",
                5650)
        };

        public static int EventObjectBytes
        {
            get { return checked(EventObjectRecordCount * EventObjectRecordSize); }
        }

        public static bool TryValidateGameDirectory(
            string gameDirectory,
            out Hunqian167ProfileIdentity selected,
            out string error)
        {
            selected = null;
            error = "";
            try
            {
                string root = Path.GetFullPath(gameDirectory);
                string profilesRoot = Path.Combine(root, "palmod", "Profiles");
                string pointerPath = Path.Combine(profilesRoot, "current.json");
                RejectReparsePoint(profilesRoot, "profile 根目录");
                RejectReparsePoint(pointerPath, "current.json");

                Dictionary<string, object> pointer = ReadJsonObject(pointerPath, MaximumPointerBytes);
                RequireExactKeys(pointer, "schema", "profile_id", "profile_version", "descriptor_sha256", "staging_relative_path");
                RequireEqual(pointer, "schema", PointerSchema);
                string profileId = RequireString(pointer, "profile_id");
                string profileVersion = RequireString(pointer, "profile_version");
                selected = Find(profileId, profileVersion);
                if (selected == null)
                {
                    throw new InvalidDataException("active profile 不是 v1.57 的魂牵 1.67 简单、困难或非人基础包。");
                }

                RequireEqual(pointer, "descriptor_sha256", selected.DescriptorSha256);
                string expectedRelative = selected.ProfileId + "/" + selected.ProfileVersion;
                RequireEqual(pointer, "staging_relative_path", expectedRelative);

                string staging = Path.GetFullPath(Path.Combine(profilesRoot, selected.ProfileId, selected.ProfileVersion));
                string profilesPrefix = Path.GetFullPath(profilesRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!staging.StartsWith(profilesPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("profile staging 越出游戏目录。");
                }

                string descriptorPath = Path.Combine(staging, "manifest", "game-profile.json");
                RejectReparsePoint(Path.Combine(profilesRoot, selected.ProfileId), "profile ID 目录");
                RejectReparsePoint(staging, "profile 版本目录");
                RejectReparsePoint(Path.Combine(staging, "manifest"), "profile manifest 目录");
                RejectReparsePoint(descriptorPath, "game-profile.json");

                byte[] descriptorBytes = ReadBoundedBytes(descriptorPath, MaximumDescriptorBytes);
                if (!string.Equals(Sha256Hex(descriptorBytes), selected.DescriptorSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("game-profile.json 的 SHA-256 与 v1.57 公开支持合同不一致。");
                }

                Dictionary<string, object> descriptor = DeserializeObject(descriptorBytes);
                RequireEqual(descriptor, "schema", DescriptorSchema);
                RequireEqual(descriptor, "profile_id", selected.ProfileId);
                RequireEqual(descriptor, "profile_version", selected.ProfileVersion);
                RequireEqual(descriptor, "display_name", selected.DisplayName);
                RequireEqual(descriptor, "save_namespace", selected.ProfileId);
                ValidateFeatures(RequireObject(descriptor, "features"));
                return true;
            }
            catch (Exception ex)
            {
                selected = null;
                error = ex.Message;
                return false;
            }
        }

        public static Hunqian167ProfileIdentity Find(string profileId, string profileVersion)
        {
            foreach (Hunqian167ProfileIdentity candidate in Supported)
            {
                if (string.Equals(candidate.ProfileId, profileId, StringComparison.Ordinal) &&
                    string.Equals(candidate.ProfileVersion, profileVersion, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void ValidateFeatures(Dictionary<string, object> features)
        {
            if (RequireInt64(features, "virtual_party_max") != 3 || RequireBoolean(features, "hd_sidecar"))
            {
                throw new InvalidDataException("profile party/save sidecar 边界不符合魂牵 1.67 公开合同。");
            }
        }

        private static Dictionary<string, object> ReadJsonObject(string path, int maximumBytes)
        {
            return DeserializeObject(ReadBoundedBytes(path, maximumBytes));
        }

        private static Dictionary<string, object> DeserializeObject(byte[] bytes)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = MaximumDescriptorBytes;
            Dictionary<string, object> result = serializer.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
            if (result == null)
            {
                throw new InvalidDataException("JSON 根对象无效。");
            }
            return result;
        }

        private static byte[] ReadBoundedBytes(string path, int maximumBytes)
        {
            FileInfo info = new FileInfo(path);
            if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes)
            {
                throw new InvalidDataException(path + " 缺失、为空或超过公开读取上限。");
            }
            return File.ReadAllBytes(path);
        }

        private static void RejectReparsePoint(string path, string label)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(label + " 不允许使用重解析点。");
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                StringBuilder builder = new StringBuilder();
                foreach (byte value in sha.ComputeHash(bytes))
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }
                return builder.ToString();
            }
        }

        private static void RequireExactKeys(Dictionary<string, object> value, params string[] keys)
        {
            if (value.Count != keys.Length)
            {
                throw new InvalidDataException("current.json 字段集合无效。");
            }
            foreach (string key in keys)
            {
                if (!value.ContainsKey(key))
                {
                    throw new InvalidDataException("current.json 缺少字段 " + key + "。");
                }
            }
        }

        private static void RequireEqual(Dictionary<string, object> value, string key, string expected)
        {
            if (!string.Equals(RequireString(value, key), expected, StringComparison.Ordinal))
            {
                throw new InvalidDataException(key + " 不符合魂牵 1.67 公开支持合同。");
            }
        }

        private static string RequireString(Dictionary<string, object> value, string key)
        {
            object raw;
            string result;
            if (!value.TryGetValue(key, out raw) || (result = raw as string) == null || result.Length == 0)
            {
                throw new InvalidDataException(key + " 必须是非空字符串。");
            }
            return result;
        }

        private static Dictionary<string, object> RequireObject(Dictionary<string, object> value, string key)
        {
            object raw;
            Dictionary<string, object> result;
            if (!value.TryGetValue(key, out raw) || (result = raw as Dictionary<string, object>) == null)
            {
                throw new InvalidDataException(key + " 必须是对象。");
            }
            return result;
        }

        private static long RequireInt64(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw) || raw == null || raw is bool)
            {
                throw new InvalidDataException(key + " 必须是整数。");
            }
            return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
        }

        private static bool RequireBoolean(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw) || !(raw is bool))
            {
                throw new InvalidDataException(key + " 必须是布尔值。");
            }
            return (bool)raw;
        }
    }
}

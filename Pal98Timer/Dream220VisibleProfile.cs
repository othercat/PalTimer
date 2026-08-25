using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace Pal98Timer
{
    public sealed class Dream220VisibleProfileIdentity
    {
        public Dream220VisibleProfileIdentity(
            string profileId,
            string profileVersion,
            string displayName,
            string descriptorSha256,
            string sssSha256,
            long sssSize,
            string wordSha256,
            long wordSize,
            int resourceCount)
        {
            ProfileId = profileId;
            ProfileVersion = profileVersion;
            DisplayName = displayName;
            DescriptorSha256 = descriptorSha256;
            SssSha256 = sssSha256;
            SssSize = sssSize;
            WordSha256 = wordSha256;
            WordSize = wordSize;
            ResourceCount = resourceCount;
        }

        public string ProfileId { get; private set; }
        public string ProfileVersion { get; private set; }
        public string DisplayName { get; private set; }
        public string DescriptorSha256 { get; private set; }
        public string SssSha256 { get; private set; }
        public long SssSize { get; private set; }
        public string WordSha256 { get; private set; }
        public long WordSize { get; private set; }
        public int ResourceCount { get; private set; }
    }

    /// <summary>
    /// Public, read-only identity facts for the PAL98 Dream 2.20 visible-blood profile.
    /// This type deliberately contains no PALDLL hook, fixed-address, package secret,
    /// local source path, or original resource byte.
    /// </summary>
    public static class Dream220VisibleProfile
    {
        public const string PublicSchema = "PAL98.PublicToolProfile.v1";
        public const string ProfileId = "pal98.dream220.compat";
        public const string ProfileVersion = "1.0.18";
        public const string DisplayName = "梦幻2.2显血版";
        public const string CoreDisplayName = "仙剑98柔情DX9梦幻22显血";
        public const string SaveNamespace = ProfileId;
        public const string OrderedCredits = "主播粉丝，孙小柔，othercat";
        public const int EventObjectRecordCount = 5369;
        public const int EventObjectRecordSize = 32;
        public const int ExpectedSaveLength = 185872;

        private const string PointerSchema = "PAL98.EffectiveGameProfilePointer.v1";
        private const string DescriptorSchema = "PAL98.GameProfile.v1";
        public const string DerivedProfileId = "pal98.dream220.compat.drawcard.16e143813df5";
        public const string DerivedDisplayName = "梦幻2.2显血版 + 抽卡";
        private const int MaximumPointerBytes = 4096;
        private const int MaximumDescriptorBytes = 1024 * 1024;

        public static readonly Dream220VisibleProfileIdentity Canonical =
            new Dream220VisibleProfileIdentity(
                ProfileId,
                ProfileVersion,
                DisplayName,
                "48d385ed161936064e007d9db6bfaae948772e951cc3463f4727b7ee7987ce38",
                "63028aea1fa375d46b080b112986df85a1fdb0ef2bf3fd899a8178abdc88b543",
                578054,
                "f5980ef5a7202cf0c306c1d458c8d7fb8a8544fe809c19ba0206cdec0ab62bea",
                5890,
                18);

        public static readonly Dream220VisibleProfileIdentity VisibleNeedleResistanceWuQiang =
            new Dream220VisibleProfileIdentity(
                DerivedProfileId,
                ProfileVersion,
                DerivedDisplayName,
                "8794d9f660ef3a1c5849c0b9dfb1a2c4d318d990b2e31f89e893f0e157e13e6a",
                "6bde8d9cfb83feda00b86ae6e2d77d798ac6290d57127fadb9f75cef9a27b9ab",
                578538,
                "23c1232fbfb4202524e0ceff95fe69f8a6e07b222831a7c64aa5f44209c9fce1",
                5890,
                19);

        public static int EventObjectBytes
        {
            get { return checked(EventObjectRecordCount * EventObjectRecordSize); }
        }

        public static bool TryValidateGameDirectory(string gameDirectory, out string error)
        {
            Dream220VisibleProfileIdentity selected;
            return TryValidateGameDirectory(gameDirectory, out selected, out error);
        }

        public static bool TryValidateGameDirectory(
            string gameDirectory,
            out Dream220VisibleProfileIdentity selected,
            out string error)
        {
            selected = null;
            error = "";
            try
            {
                if (string.IsNullOrWhiteSpace(gameDirectory))
                {
                    throw new InvalidDataException("游戏目录为空。");
                }

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
                    throw new InvalidDataException("current.json 不是受支持的梦幻2.2显血版基底或本次内容派生包。");
                }
                RequireEqual(pointer, "descriptor_sha256", selected.DescriptorSha256);

                string relativeStaging = RequireString(pointer, "staging_relative_path");
                string expectedRelativeStaging = selected.ProfileId + "/" + selected.ProfileVersion;
                if (!string.Equals(relativeStaging, expectedRelativeStaging, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("current.json 未指向受支持的精确 profile staging。");
                }

                string staging = Path.GetFullPath(Path.Combine(
                    profilesRoot, selected.ProfileId, selected.ProfileVersion));
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
                string actualDescriptorSha = Sha256Hex(descriptorBytes);
                if (!string.Equals(actualDescriptorSha, selected.DescriptorSha256, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("game-profile.json 的 SHA-256 与公开支持合同不一致。");
                }

                Dictionary<string, object> descriptor = DeserializeObject(descriptorBytes);
                RequireEqual(descriptor, "schema", DescriptorSchema);
                RequireEqual(descriptor, "profile_id", selected.ProfileId);
                RequireEqual(descriptor, "profile_version", selected.ProfileVersion);
                RequireEqual(descriptor, "display_name", selected.DisplayName);
                RequireEqual(descriptor, "save_namespace", selected.ProfileId);
                ValidateFeatures(RequireObject(descriptor, "features"));
                ValidateResourceSet(RequireArray(descriptor, "resource_set"), selected);
                return true;
            }
            catch (Exception ex)
            {
                selected = null;
                error = ex.Message;
                return false;
            }
        }

        public static Dream220VisibleProfileIdentity Find(string profileId, string profileVersion)
        {
            if (!string.Equals(profileVersion, ProfileVersion, StringComparison.Ordinal))
            {
                return null;
            }
            if (string.Equals(profileId, Canonical.ProfileId, StringComparison.Ordinal))
            {
                return Canonical;
            }
            return string.Equals(profileId, VisibleNeedleResistanceWuQiang.ProfileId, StringComparison.Ordinal)
                ? VisibleNeedleResistanceWuQiang
                : null;
        }

        private static void ValidateFeatures(Dictionary<string, object> features)
        {
            if (RequireInt64(features, "virtual_party_max") != 3 || RequireBoolean(features, "hd_sidecar"))
            {
                throw new InvalidDataException("profile party/save sidecar 边界不符合梦幻 2.20 公开合同。");
            }
        }

        private static void ValidateResourceSet(
            IList<object> resources,
            Dream220VisibleProfileIdentity selected)
        {
            if (resources.Count != selected.ResourceCount)
            {
                throw new InvalidDataException("profile resource_set 数量不符合公开支持合同。");
            }

            bool sawSss = false;
            bool sawWord = false;
            foreach (object item in resources)
            {
                Dictionary<string, object> resource = item as Dictionary<string, object>;
                if (resource == null)
                {
                    throw new InvalidDataException("profile resource_set 记录格式无效。");
                }

                string kind = RequireString(resource, "kind");
                if (string.Equals(kind, "SSS.MKF", StringComparison.Ordinal))
                {
                    ValidateResource(resource, selected.SssSha256, selected.SssSize);
                    sawSss = true;
                }
                else if (string.Equals(kind, "WORD.DAT", StringComparison.Ordinal))
                {
                    ValidateResource(resource, selected.WordSha256, selected.WordSize);
                    sawWord = true;
                }
            }

            if (!sawSss || !sawWord)
            {
                throw new InvalidDataException("profile 缺少受支持的 SSS.MKF 或 WORD.DAT 身份。");
            }
        }

        private static void ValidateResource(Dictionary<string, object> resource, string sha256, long size)
        {
            if (!string.Equals(RequireString(resource, "sha256"), sha256, StringComparison.Ordinal) ||
                RequireInt64(resource, "size_bytes") != size)
            {
                throw new InvalidDataException(RequireString(resource, "kind") + " 身份不符合公开支持合同。");
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
            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(label + " 不允许使用重解析点。");
            }
        }

        private static string Sha256Hex(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest)
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
                throw new InvalidDataException(key + " 不符合梦幻 2.2 显血版公开支持合同。");
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

        private static IList<object> RequireArray(Dictionary<string, object> value, string key)
        {
            object raw;
            if (!value.TryGetValue(key, out raw) || raw == null || raw is string)
            {
                throw new InvalidDataException(key + " 必须是数组。");
            }

            object[] array = raw as object[];
            if (array != null)
            {
                return array;
            }

            IEnumerable enumerable = raw as IEnumerable;
            if (enumerable == null)
            {
                throw new InvalidDataException(key + " 必须是数组。");
            }
            List<object> result = new List<object>();
            foreach (object item in enumerable)
            {
                result.Add(item);
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
            try
            {
                return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                throw new InvalidDataException(key + " 必须是整数。");
            }
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

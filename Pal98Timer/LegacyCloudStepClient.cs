using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using PalCloudLib;

namespace Pal98Timer
{
    // The bundled library owns authentication, hashing, endpoints and payload
    // encoding. Only its unjoinable Start loop is replaced. No protocol changes.
    internal sealed class LegacyCloudStepClient : ICloudStepClient
    {
        private const string ExpectedSha256 = "E1B03FDBB52BEC5C283D67DE49E7B4BDB65F6DAD3C78FACEF241ED33F0CE3468";
        private static readonly MethodInfo StepMethod = typeof(PCloud).GetMethod("doone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo Inited = typeof(PCloud).GetField("_inited", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo NextDo = typeof(PCloud).GetField("nextDo", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly PCloud cloud;
        internal static void ValidateContract()
        {
            string hash;
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(typeof(PCloud).Assembly.Location))
                hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            if (hash != ExpectedSha256 || StepMethod == null || StepMethod.ReturnType != typeof(int) ||
                StepMethod.GetParameters().Length != 0 || Inited == null || Inited.FieldType != typeof(bool) ||
                NextDo == null || NextDo.FieldType != typeof(int))
                throw new InvalidOperationException("云组件版本不匹配，请使用完整配套计时器；未启动云请求。");
        }
        internal LegacyCloudStepClient(string name)
        {
            ValidateContract();
            cloud = new PCloud(name, delegate(int id) { });
        }
        public bool Initialized { get { return (bool)Inited.GetValue(cloud); } }
        public int NextDataKind { get { return (int)NextDo.GetValue(cloud); } }
        public CloudStepResult Step(CloudPayload payload, bool finish)
        {
            if (payload != null)
            {
                if (payload.Lite != null) cloud.PutLiteData(payload.Lite);
                if (payload.Big != null) cloud.PutBigData(payload.Big);
                cloud.PutIsC(payload.IsC);
                foreach (var plugin in payload.Plugins) cloud.PutPluginData(plugin.Key, plugin.Value);
            }
            if (finish) cloud.FinishOne();
            int delay;
            try { delay = (int)StepMethod.Invoke(cloud, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            bool initialized = Initialized;
            return new CloudStepResult { Initialized = initialized, Id = cloud.CloudID, DelaySeconds = delay,
                Error = initialized && delay == 0 ? "" : cloud.LastError };
        }
        public void Stop()
        {
            cloud.PutLiteData(""); cloud.PutBigData(""); cloud.Stop();
        }
        public void Upload(string local, string remote) { cloud.OUpload(local, remote); }
        public void Download(string remote, string local) { cloud.ODownload(remote, local); }
    }
}

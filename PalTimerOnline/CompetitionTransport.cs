using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Pal98Timer
{
    internal sealed class CompetitionHttpResult
    {
        internal int Status;
        internal bool ReceivedResponse;
        internal string Body = "";
        internal string ErrorDetail = "";
        internal int RetryAfterSeconds;
        internal DateTimeOffset? ServerDate, ReceivedAt;
        internal bool Success { get { return Status >= 200 && Status < 300; } }
        internal bool Retryable { get { return Status == 0 || Status == 408 || Status == 429 || Status >= 500; } }
        internal string Description { get { return Status == 0 ? "比赛服务暂不可用，后台稍后重试" : "比赛服务返回 HTTP " + Status; } }
    }
    internal interface ICompetitionTransport : IDisposable
    {
        Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeoutMilliseconds, CancellationToken stop);
    }
    internal sealed class CompetitionHttpTransport : ICompetitionTransport
    {
        private readonly HttpClient http;
        internal CompetitionHttpTransport()
        {
            // No cookies, redirects (which could move credentials), custom TLS
            // validation or changes to the legacy cloud's global network policy.
            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PalTimer/3.37.7");
        }
        public async Task<CompetitionHttpResult> Send(string method, string url, string hwid, string secret, string body, int timeoutMilliseconds, CancellationToken stop)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
            using (var request = new HttpRequestMessage(new HttpMethod(method), url))
            {
                deadline.CancelAfter(timeoutMilliseconds);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
                if (method == "GET" || new Uri(url).AbsolutePath.StartsWith("/api/v1/timer/challenges/", StringComparison.Ordinal))
                    request.Headers.Add("X-PAL-HWID", hwid);
                if (body != null) request.Content = new StringContent(body, new UTF8Encoding(false), "application/json");
                bool receivedResponse = false;
                try
                {
                    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    {
                        receivedResponse = true;
                        var result = new CompetitionHttpResult { Status = (int)response.StatusCode,
                            ReceivedResponse = true,
                            ServerDate = response.Headers.Date, ReceivedAt = DateTimeOffset.UtcNow };
                        var retry = response.Headers.RetryAfter;
                        if (retry != null)
                        {
                            var seconds = retry.Delta.HasValue ? retry.Delta.Value.TotalSeconds : retry.Date.HasValue ? (retry.Date.Value - DateTimeOffset.UtcNow).TotalSeconds : 0;
                            result.RetryAfterSeconds = (int)Math.Max(0, Math.Min(86400, seconds));
                        }
                        // Error response bodies may echo sensitive input; never persist or display them.
                        if (!result.Success) {
                            // Challenge downloads need actionable server diagnostics.
                            // Keep ordinary API error bodies private. Extract only the
                            // bounded detail field and redact request credentials.
                            if (new Uri(url).AbsolutePath.StartsWith("/api/v1/timer/challenges/",StringComparison.Ordinal))
                                result.ErrorDetail = await ReadChallengeError(response,secret,hwid,deadline.Token).ConfigureAwait(false);
                            return result;
                        }
                        if (response.Content.Headers.ContentLength > 1048576) return new CompetitionHttpResult {ReceivedResponse = true};
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var buffer = new MemoryStream())
                        {
                            byte[] bytes = new byte[8192]; int count;
                            while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, deadline.Token).ConfigureAwait(false)) != 0)
                            {
                                if (buffer.Length + count > 1048576) return new CompetitionHttpResult {ReceivedResponse = true};
                                buffer.Write(bytes, 0, count);
                            }
                            result.Body = new UTF8Encoding(false, true).GetString(buffer.ToArray());
                        }
                        return result;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is OperationCanceledException || ex is DecoderFallbackException)
                { return new CompetitionHttpResult {ReceivedResponse = receivedResponse}; }
            }
        }
        private static async Task<string> ReadChallengeError(HttpResponseMessage response,string secret,string hwid,CancellationToken token)
        {
            if (response.Content.Headers.ContentLength > 8192) return "服务器错误信息超过容量。";
            try {
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var buffer = new MemoryStream()) {
                    byte[] bytes=new byte[1024];int count;
                    while ((count=await stream.ReadAsync(bytes,0,bytes.Length,token).ConfigureAwait(false)) != 0) {
                        if (buffer.Length+count>8192) return "服务器错误信息超过容量。";
                        buffer.Write(bytes,0,count);
                    }
                    var serializer=new System.Web.Script.Serialization.JavaScriptSerializer {MaxJsonLength=8192,RecursionLimit=8};
                    var value=serializer.DeserializeObject(new UTF8Encoding(false,true).GetString(buffer.ToArray())) as System.Collections.Generic.Dictionary<string,object>;
                    object detail;string text=value!=null && value.TryGetValue("detail",out detail) ? detail as string : null;
                    if (text==null) return "";
                    if (!string.IsNullOrEmpty(secret)) text=text.Replace(secret,"[凭据已隐藏]");
                    if (!string.IsNullOrEmpty(hwid)) text=text.Replace(hwid,"[设备标识已隐藏]");
                    text=new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(text,c=>!char.IsControl(c)||c=='\n')));
                    return text.Length<=1024 ? text : text.Substring(0,1024)+"…";
                }
            } catch (Exception error) when (error is IOException || error is DecoderFallbackException || error is ArgumentException || error is InvalidOperationException) {return "服务器未提供可读取的错误说明。";}
        }
        public void Dispose() { http.Dispose(); }
    }
}

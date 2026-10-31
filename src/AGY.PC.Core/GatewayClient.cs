using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace AGY.PC.Core;

public sealed class GatewayException(int status, string message) : Exception(message) { public int Status { get; } = status; }
public sealed class GatewayClient : IDisposable {
    private readonly HttpClient http;
    private readonly Credentials credentials;
    public GatewayClient(Credentials credentials, HttpMessageHandler? handler = null) {
        this.credentials = Credentials.Validate(credentials.Address, credentials.Token);
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, Credentials = null }) { Timeout = Timeout.InfiniteTimeSpan };
    }
    private HttpRequestMessage Request(string path, HttpMethod method, Dictionary<string,string>? query = null) {
        var uri = credentials.Address + "/v1/" + path;
        if (query?.Count > 0) uri += "?" + string.Join('&',query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        var request = new HttpRequestMessage(method,uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer",credentials.Token);
        // Retains the deployed gateway's accepted native-client identity; all network traffic is .NET HttpClient.
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/134.0.0.0 Safari/537.36 AGY-PC/1.0.0");
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }
    private static async Task Validate(HttpResponseMessage response, CancellationToken ct) {
        if (response.IsSuccessStatusCode) return;
        var message = response.StatusCode switch {
            HttpStatusCode.Unauthorized => "连接密钥无效或已失效，请重新连接。",
            HttpStatusCode.BadGateway => "服务器网关暂不可用（502）。请检查后台服务与隧道。",
            HttpStatusCode.Forbidden => "服务器拒绝了这次访问。",
            _ => $"服务器返回 HTTP {(int)response.StatusCode}。"
        };
        if (response.Content.Headers.ContentType?.MediaType == "application/json") {
            try {
                var data = await response.Content.ReadAsStringAsync(ct);
                using var json = JsonDocument.Parse(data);
                if (json.RootElement.TryGetProperty("error",out var error) && error.GetString() is { Length: > 0 } detail) message = detail;
            } catch (JsonException) { }
        }
        throw new GatewayException((int)response.StatusCode,message);
    }
    public async Task<T> JsonAsync<T>(string path, HttpMethod? method = null, object? body = null, Dictionary<string,string>? query = null, CancellationToken ct = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(65));
        using var request = Request(path,method ?? HttpMethod.Get,query);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body,Wire.Json),Encoding.UTF8,"application/json");
        using var response = await http.SendAsync(request,deadline.Token);
        await Validate(response,deadline.Token);
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(deadline.Token),Wire.Json) ?? throw new InvalidDataException("服务器响应为空。");
    }
    public async Task<Attachment> UploadAsync(string sid, string local, CancellationToken ct = default) {
        var info = new FileInfo(local);
        if (!info.Exists || (info.Attributes & FileAttributes.Directory) != 0) throw new InvalidOperationException("请选择存在的普通文件。");
        if (info.Length > FileRules.MaxFile) throw new InvalidOperationException("单个文件不能超过 25 MiB。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = Request($"sessions/{Id(sid)}/attachments",HttpMethod.Post,new() { ["name"] = info.Name });
        await using var stream = new FileStream(local,FileMode.Open,FileAccess.Read,FileShare.Read);
        request.Content = new StreamContent(stream); request.Content.Headers.ContentLength = stream.Length;
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(Mime(info.Extension));
        using var response = await http.SendAsync(request,deadline.Token); await Validate(response,deadline.Token);
        return JsonSerializer.Deserialize<Attachment>(await response.Content.ReadAsStringAsync(deadline.Token),Wire.Json) ?? throw new InvalidDataException("上传响应无效。");
    }
    public async Task DownloadAsync(string sid, string remote, string destination, CancellationToken ct = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var request = Request($"sessions/{Id(sid)}/files",HttpMethod.Get,new() { ["path"] = remote });
        request.Headers.Accept.Clear(); request.Headers.Accept.ParseAdd("*/*");
        using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token); await Validate(response,deadline.Token);
        if (response.Content.Headers.ContentLength > FileRules.MaxFile) throw new InvalidDataException("下载文件超过 25 MiB。");
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try {
            await using (var target = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)) {
                await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
                var buffer = new byte[65536]; long total = 0; int count;
                while ((count = await source.ReadAsync(buffer,deadline.Token)) != 0) {
                    total += count; if (total > FileRules.MaxFile) throw new InvalidDataException("下载文件超过 25 MiB。");
                    await target.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
                }
                if (response.Content.Headers.ContentLength is long expected && total != expected) throw new IOException("下载中断，文件尚未保存。");
                target.Flush(true);
            }
            File.Move(temporary,destination,true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async IAsyncEnumerable<Session> EventsAsync(string sid, [EnumeratorCancellation] CancellationToken ct = default) {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(40));
        using var request = Request($"sessions/{Id(sid)}/events",HttpMethod.Get);
        request.Headers.Accept.Clear(); request.Headers.Accept.ParseAdd("text/event-stream");
        using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token); await Validate(response,deadline.Token);
        if (response.Content.Headers.ContentType?.MediaType != "text/event-stream") throw new InvalidDataException("服务器没有返回事件流。");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        await foreach (var data in Sse.ReadAsync(stream,deadline.Token)) {
            yield return JsonSerializer.Deserialize<Session>(data,Wire.Json) ?? throw new InvalidDataException("会话快照无效。");
        }
    }
    public static string Id(string value) => Guid.TryParseExact(value,"D",out var id) ? id.ToString() : throw new InvalidOperationException("会话编号无效。");
    private static string Mime(string extension) => extension.ToLowerInvariant() switch {
        ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", ".gif" => "image/gif",
        ".pdf" => "application/pdf", ".txt" or ".md" or ".csv" or ".cs" or ".py" => "text/plain", ".json" => "application/json", ".zip" => "application/zip", _ => "application/octet-stream"
    };
    public void Dispose() => http.Dispose();
}
public static class Sse {
    public static async IAsyncEnumerable<string> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default) {
        using var reader = new StreamReader(stream,new UTF8Encoding(false,true),false,4096,true);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(ct) is { } line) {
            if (line.Length == 0) {
                if (data.Length > 0) { yield return data.ToString().TrimEnd('\n'); data.Clear(); }
            } else if (line.StartsWith("data:",StringComparison.Ordinal)) {
                var value = line[5..]; if (value.StartsWith(' ')) value = value[1..];
                if (data.Length + value.Length > 20 * 1024 * 1024) throw new InvalidDataException("会话快照过大。");
                data.Append(value).Append('\n');
            }
        }
    }
}

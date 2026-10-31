using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AGY.PC.Core;

public sealed record NetworkSettings(bool UseDoh = true, string Provider = "Cloudflare", bool SystemFallback = true, string? CustomUrl = null) {
    public void Validate() {
        if (!string.IsNullOrWhiteSpace(CustomUrl)) {
            if (!Uri.TryCreate(CustomUrl.Trim(), UriKind.Absolute, out var custom) || custom.Scheme != "https")
                throw new InvalidOperationException("自定义 DoH 地址必须是有效的 HTTPS URL。");
            return;
        }
        if (Provider is not ("Cloudflare" or "Google" or "AliDNS" or "DNSPod"))
            throw new InvalidOperationException("请选择有效的 DoH 服务或填写自定义 HTTPS DoH 地址。");
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri Endpoint => !string.IsNullOrWhiteSpace(CustomUrl) && Uri.TryCreate(CustomUrl.Trim(), UriKind.Absolute, out var custom)
        ? custom
        : new(Provider switch {
            "Google" => "https://dns.google/resolve",
            "AliDNS" => "https://dns.alidns.com/resolve",
            "DNSPod" => "https://doh.pub/dns-query",
            _ => "https://cloudflare-dns.com/dns-query"
        });
    [System.Text.Json.Serialization.JsonIgnore]
    public IPAddress Bootstrap => IPAddress.Parse(Provider switch {
        "Google" => "8.8.8.8",
        "AliDNS" => "223.5.5.5",
        "DNSPod" => "119.29.29.29",
        _ => "1.1.1.1"
    });
}

// The original HTTP URI is retained: HttpClient still handles TLS, SNI and certificate validation.
public static class NetworkTransport {
    private static readonly ConcurrentDictionary<string, DateTimeOffset> dohFastBypass = new(StringComparer.OrdinalIgnoreCase);

    public static bool UsesProxy(Uri destination, IWebProxy proxy) {
        if (proxy.IsBypassed(destination)) return false;
        var target = proxy.GetProxy(destination);
        return target is not null && target != destination;
    }
    private static bool LocalHost(string host) => host.Equals("localhost",StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost",StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local",StringComparison.OrdinalIgnoreCase)
        || !host.Contains('.');
    public static HttpMessageHandler Create(Uri origin, NetworkSettings settings, HttpMessageHandler? dnsHandler=null, IWebProxy? proxy=null) {
        settings.Validate();
        var transport = new SocketsHttpHandler {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(4),
            EnableMultipleHttp2Connections = true
        };
        if (!settings.UseDoh) return transport;
        proxy ??= HttpClient.DefaultProxy;
        transport.Proxy=proxy;
        var dnsTransport = new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(2.5) };
        // Bootstrap the known resolver without depending on broken system DNS. Proxy connections remain owned by the proxy.
        dnsTransport.ConnectCallback = async (context,ct) => {
            var destination = context.InitialRequestMessage.RequestUri!;
            if (!UsesProxy(destination,proxy) && string.IsNullOrWhiteSpace(settings.CustomUrl) && context.DnsEndPoint.Host.Equals(settings.Endpoint.Host,StringComparison.OrdinalIgnoreCase))
                return await OpenAsync([settings.Bootstrap],context.DnsEndPoint.Port,ct);
            return await OpenSystemAsync(context.DnsEndPoint,ct);
        };
        dnsTransport.Proxy=proxy;
        var resolver = new DohResolver(settings.Endpoint,dnsHandler ?? dnsTransport);
        if(dnsHandler is not null)dnsTransport.Dispose();
        transport.ConnectCallback = async (context,ct) => {
            var host = context.DnsEndPoint.Host;
            var destination = context.InitialRequestMessage.RequestUri!;
            if (!host.Equals(origin.IdnHost,StringComparison.OrdinalIgnoreCase) || UsesProxy(destination,proxy)
                || LocalHost(host) || IPAddress.TryParse(host,out _)) return await OpenSystemAsync(context.DnsEndPoint,ct);

            // When SystemFallback is enabled in real network mode, prefer fast OS/TUN resolution first
            // so blocked overseas DoH bootstrap IPs (like 1.1.1.1/8.8.8.8) never add 6s stalls per request.
            if (settings.SystemFallback && dnsHandler is null) {
                try {
                    return await OpenSystemAsync(context.DnsEndPoint, ct);
                } catch (Exception sysErr) when (!ct.IsCancellationRequested && sysErr is SocketException or IOException) {
                    // System DNS failed; fall through to DoH rescue below.
                }
            }

            if (settings.SystemFallback && dnsHandler is null && dohFastBypass.TryGetValue(settings.Endpoint.Host, out var until) && until > DateTimeOffset.UtcNow) {
                return await OpenSystemAsync(context.DnsEndPoint, ct);
            }

            IPAddress[] addresses;
            try {
                if (settings.SystemFallback && dnsHandler is null) {
                    using var quick = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    quick.CancelAfter(TimeSpan.FromMilliseconds(1500));
                    addresses = await resolver.ResolveAsync(host, quick.Token);
                } else {
                    addresses = await resolver.ResolveAsync(host, ct);
                }
            } catch (Exception e) when (settings.SystemFallback && !ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException) {
                dohFastBypass[settings.Endpoint.Host] = DateTimeOffset.UtcNow.AddMinutes(10);
                return await OpenSystemAsync(context.DnsEndPoint,ct);
            }
            // Fall back only on DNS failure, never replay an HTTP request or retry after TLS/auth failures.
            return await OpenAsync(addresses,context.DnsEndPoint.Port,ct);
        };
        return new OwnedTransport(transport,resolver);
    }
    private static async ValueTask<Stream> OpenSystemAsync(DnsEndPoint endpoint,CancellationToken ct) {
        var socket = new Socket(SocketType.Stream,ProtocolType.Tcp) { NoDelay=true };
        try { await socket.ConnectAsync(endpoint,ct); return new NetworkStream(socket,true); }
        catch { socket.Dispose(); throw; }
    }
    private static async ValueTask<Stream> OpenAsync(IPAddress[] addresses,int port,CancellationToken ct) {
        var ordered = addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        var socket = new Socket(SocketType.Stream,ProtocolType.Tcp) { NoDelay=true };
        try { await socket.ConnectAsync(ordered,port,ct); return new NetworkStream(socket,true); }
        catch { socket.Dispose(); throw; }
    }
    private sealed class OwnedTransport(HttpMessageHandler inner,DohResolver resolver) : DelegatingHandler(inner) {
        protected override void Dispose(bool disposing) { if(disposing)resolver.Dispose();base.Dispose(disposing); }
    }
}

public sealed class DohResolver(Uri endpoint, HttpMessageHandler handler) : IDisposable {
    private readonly HttpClient http = new(handler) { Timeout=TimeSpan.FromSeconds(6) };
    private readonly ConcurrentDictionary<string,(DateTimeOffset Until,IPAddress[] Addresses)> cache = new(StringComparer.OrdinalIgnoreCase);
    public async Task<IPAddress[]> ResolveAsync(string host,CancellationToken ct=default) {
        host = new System.Globalization.IdnMapping().GetAscii(host.TrimEnd('.'));
        if (Uri.CheckHostName(host) != UriHostNameType.Dns) throw new IOException("无效的 DNS 域名。");
        if (cache.TryGetValue(host,out var cached) && cached.Until>DateTimeOffset.UtcNow) return cached.Addresses.ToArray();
        var replies = await Task.WhenAll(QueryAsync(host,1,ct),QueryAsync(host,28,ct));
        var addresses = replies.SelectMany(x=>x.Addresses).Distinct().ToArray();
        if (addresses.Length==0) throw new IOException("DoH 未返回可用地址，请更换服务或启用系统 DNS 回退。");
        var ttl = replies.Where(x=>x.Addresses.Length>0).Min(x=>x.Ttl);
        if (ttl>0) { if(cache.Count>128)cache.Clear();cache[host]=(DateTimeOffset.UtcNow.AddSeconds(Math.Min(ttl,300)),addresses); }
        return addresses;
    }
    private async Task<(IPAddress[] Addresses,int Ttl)> QueryAsync(string host,int type,CancellationToken ct) {
        try {
            using var request = new HttpRequestMessage(HttpMethod.Get,endpoint+"?name="+Uri.EscapeDataString(host)+"&type="+type) { Version=HttpVersion.Version20, VersionPolicy=HttpVersionPolicy.RequestVersionOrLower };
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
            response.EnsureSuccessStatusCode();
            await response.Content.LoadIntoBufferAsync(65536,ct);
            using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root = json.RootElement;
            if (root.GetProperty("Status").GetInt32()!=0 || (root.TryGetProperty("TC",out var truncated)&&truncated.GetBoolean())) return ([],0);
            var addresses = new List<IPAddress>();var ttl=300;
            if(root.TryGetProperty("Answer",out var answers)) foreach(var answer in answers.EnumerateArray()) {
                ttl=Math.Min(ttl,Math.Max(0,answer.GetProperty("TTL").GetInt32()));
                if(answer.GetProperty("type").GetInt32()==type && IPAddress.TryParse(answer.GetProperty("data").GetString(),out var ip)
                    && (type==1 ? ip.AddressFamily==AddressFamily.InterNetwork : ip.AddressFamily==AddressFamily.InterNetworkV6)) addresses.Add(ip);
            }
            return (addresses.ToArray(),ttl);
        } catch(Exception e) when(!ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException or KeyNotFoundException or InvalidOperationException or FormatException) { return ([],0); }
    }
    public void Dispose()=>http.Dispose();
}

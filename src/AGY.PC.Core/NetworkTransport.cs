using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace AGY.PC.Core;

public sealed record NetworkSettings(bool UseDoh = true, string Provider = "Cloudflare", bool SystemFallback = true) {
    public void Validate() {
        if (Provider is not ("Cloudflare" or "Google")) throw new InvalidOperationException("请选择有效的 DoH 服务。");
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri Endpoint => new(Provider == "Google" ? "https://dns.google/resolve" : "https://cloudflare-dns.com/dns-query");
    [System.Text.Json.Serialization.JsonIgnore]
    public IPAddress Bootstrap => IPAddress.Parse(Provider == "Google" ? "8.8.8.8" : "1.1.1.1");
}

// The original HTTP URI is retained: HttpClient still handles TLS, SNI and certificate validation.
public static class NetworkTransport {
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
        var transport = new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(20), PooledConnectionLifetime=TimeSpan.FromMinutes(5) };
        if (!settings.UseDoh) return transport;
        proxy ??= HttpClient.DefaultProxy;
        transport.Proxy=proxy;
        var dnsTransport = new SocketsHttpHandler { AllowAutoRedirect=false, UseCookies=false, ConnectTimeout=TimeSpan.FromSeconds(6) };
        // Bootstrap the known resolver without depending on broken system DNS. Proxy connections remain owned by the proxy.
        dnsTransport.ConnectCallback = async (context,ct) => {
            var destination = context.InitialRequestMessage.RequestUri!;
            if (!UsesProxy(destination,proxy) && context.DnsEndPoint.Host.Equals(settings.Endpoint.Host,StringComparison.OrdinalIgnoreCase))
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
            IPAddress[] addresses;
            try { addresses = await resolver.ResolveAsync(host,ct); }
            catch (Exception e) when (settings.SystemFallback && !ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException) {
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
        var socket = new Socket(SocketType.Stream,ProtocolType.Tcp) { NoDelay=true };
        try { await socket.ConnectAsync(addresses,port,ct); return new NetworkStream(socket,true); }
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

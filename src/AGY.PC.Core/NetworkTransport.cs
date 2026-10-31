using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AGY.PC.Core;

public sealed record NetworkSettings(bool UseDoh = true, string Provider = "Cloudflare", bool SystemFallback = true, string? CustomUrl = null, string? ProxyUrl = null) {
    public void Validate() {
        if (!string.IsNullOrWhiteSpace(ProxyUrl) && !ProxyUrl.Trim().Equals("direct", StringComparison.OrdinalIgnoreCase)) {
            if (!Uri.TryCreate(ProxyUrl.Trim(), UriKind.Absolute, out var proxyUri) || (proxyUri.Scheme != "http" && proxyUri.Scheme != "https" && proxyUri.Scheme != "socks5"))
                throw new InvalidOperationException("代理地址必须是有效的 http://、https://、socks5:// 地址，或填 direct 表示直连。");
        }
        if (!string.IsNullOrWhiteSpace(CustomUrl)) {
            if (!Uri.TryCreate(CustomUrl.Trim(), UriKind.Absolute, out var custom) || custom.Scheme != "https")
                throw new InvalidOperationException("自定义 DoH 地址必须是有效的 HTTPS URL。");
            return;
        }
        if (Provider is not ("Cloudflare" or "Google" or "AliDNS" or "DNSPod" or "Custom"))
            throw new InvalidOperationException("请选择有效的 DoH 服务或填写自定义 HTTPS DoH 地址。");
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public Uri Endpoint => !string.IsNullOrWhiteSpace(CustomUrl) && Uri.TryCreate(CustomUrl.Trim(), UriKind.Absolute, out var custom)
        ? custom
        : new(Provider switch {
            "Google" => "https://dns.google/dns-query",
            "AliDNS" => "https://dns.alidns.com/dns-query",
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
    public static IPAddress[]? KnownBootstrapForHost(string host) => host.ToLowerInvariant() switch {
        "dns.alidns.com" => [IPAddress.Parse("223.5.5.5"), IPAddress.Parse("223.6.6.6")],
        "doh.pub" or "sm2.doh.pub" => [IPAddress.Parse("1.12.12.12"), IPAddress.Parse("120.53.53.53")],
        "cloudflare-dns.com" or "one.one.one.one" => [IPAddress.Parse("1.1.1.1"), IPAddress.Parse("1.0.0.1")],
        "dns.google" => [IPAddress.Parse("8.8.8.8"), IPAddress.Parse("8.8.4.4")],
        _ => null
    };
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
    private static IWebProxy ResolveProxy(NetworkSettings settings, IWebProxy? explicitProxy) {
        if (explicitProxy is not null) return explicitProxy;
        if (!string.IsNullOrWhiteSpace(settings.ProxyUrl)) {
            var raw = settings.ProxyUrl.Trim();
            if (raw.Equals("direct", StringComparison.OrdinalIgnoreCase)) return new WebProxy();
            return new WebProxy(new Uri(raw));
        }
        return HttpClient.DefaultProxy;
    }
    public static HttpMessageHandler Create(Uri origin, NetworkSettings settings, HttpMessageHandler? dnsHandler=null, IWebProxy? proxy=null) {
        settings.Validate();
        var effectiveProxy = ResolveProxy(settings, proxy);
        var hasCustomDoh = !string.IsNullOrWhiteSpace(settings.CustomUrl);
        var useProxy = !string.Equals(settings.ProxyUrl?.Trim(), "direct", StringComparison.OrdinalIgnoreCase)
            && !(hasCustomDoh && string.IsNullOrWhiteSpace(settings.ProxyUrl) && proxy is null);
        var transport = new SocketsHttpHandler {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,
            UseProxy = useProxy,
            Proxy = useProxy ? effectiveProxy : null
        };
        if (!settings.UseDoh) {
            transport.ConnectCallback = static (context, ct) => OpenSystemAsync(context.DnsEndPoint, ct);
            return transport;
        }
        var dnsTransport = new SocketsHttpHandler {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            UseProxy = useProxy,
            Proxy = useProxy ? effectiveProxy : null
        };
        // Bootstrap known resolvers directly without depending on system DNS; resolve custom DoH domains via IPv4-first system DNS.
        dnsTransport.ConnectCallback = async (context,ct) => {
            var destination = context.InitialRequestMessage.RequestUri!;
            if (useProxy && UsesProxy(destination,effectiveProxy))
                return await OpenSystemAsync(context.DnsEndPoint,ct);
            if (NetworkSettings.KnownBootstrapForHost(context.DnsEndPoint.Host) is { } knownIps)
                return await OpenAsync(knownIps, context.DnsEndPoint.Port, ct);
            if (string.IsNullOrWhiteSpace(settings.CustomUrl) && context.DnsEndPoint.Host.Equals(settings.Endpoint.Host,StringComparison.OrdinalIgnoreCase))
                return await OpenAsync([settings.Bootstrap],context.DnsEndPoint.Port,ct);
            return await OpenSystemAsync(context.DnsEndPoint,ct);
        };
        var resolver = new DohResolver(settings.Endpoint,dnsHandler ?? dnsTransport);
        if(dnsHandler is not null)dnsTransport.Dispose();
        transport.ConnectCallback = async (context,ct) => {
            var host = context.DnsEndPoint.Host;
            var destination = context.InitialRequestMessage.RequestUri!;
            if (!host.Equals(origin.IdnHost,StringComparison.OrdinalIgnoreCase) || (useProxy && UsesProxy(destination,effectiveProxy))
                || LocalHost(host) || IPAddress.TryParse(host,out _)) return await OpenSystemAsync(context.DnsEndPoint,ct);

            IPAddress[] addresses;
            try {
                addresses = await resolver.ResolveAsync(host, ct);
            } catch (Exception e) when (settings.SystemFallback && !ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException) {
                return await OpenSystemAsync(context.DnsEndPoint,ct);
            }
            // Fall back only on DNS failure, never replay an HTTP request or retry after TLS/auth failures.
            return await OpenAsync(addresses,context.DnsEndPoint.Port,ct);
        };
        return new OwnedTransport(transport,resolver);
    }
    private static async ValueTask<Stream> OpenSystemAsync(DnsEndPoint endpoint,CancellationToken ct) {
        if (IPAddress.TryParse(endpoint.Host, out var literalIp)) {
            var directSocket = new Socket(literalIp.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await directSocket.ConnectAsync(new IPEndPoint(literalIp, endpoint.Port), ct); return new NetworkStream(directSocket, true); }
            catch { directSocket.Dispose(); throw; }
        }
        if (endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) {
            return await OpenAsync([IPAddress.Loopback, IPAddress.IPv6Loopback], endpoint.Port, ct);
        }
        var resolved = await Dns.GetHostAddressesAsync(endpoint.Host, ct);
        if (resolved.Length > 0) {
            return await OpenAsync(resolved, endpoint.Port, ct);
        }
        var socket = new Socket(SocketType.Stream,ProtocolType.Tcp) { NoDelay=true };
        try { await socket.ConnectAsync(endpoint,ct); return new NetworkStream(socket,true); }
        catch { socket.Dispose(); throw; }
    }
    private static async ValueTask<Stream> OpenAsync(IPAddress[] addresses,int port,CancellationToken ct) {
        var ordered = addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).ToArray();
        if (ordered.Length == 1) {
            var single = new Socket(ordered[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await single.ConnectAsync(new IPEndPoint(ordered[0], port), ct); return new NetworkStream(single, true); }
            catch { single.Dispose(); throw; }
        }
        Exception? lastError = null;
        for (var i = 0; i < ordered.Length; i++) {
            var addr = ordered[i];
            var candidate = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (i < ordered.Length - 1) stepCts.CancelAfter(TimeSpan.FromMilliseconds(2200));
            try {
                await candidate.ConnectAsync(new IPEndPoint(addr, port), stepCts.Token);
                return new NetworkStream(candidate, true);
            } catch (Exception ex) {
                candidate.Dispose();
                if (ct.IsCancellationRequested) throw;
                lastError ??= ex;
            }
        }
        throw lastError ?? new SocketException((int)SocketError.HostUnreachable);
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
    private bool PreferJsonFirst => handler is not SocketsHttpHandler
        || endpoint.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
        || endpoint.AbsolutePath.EndsWith("/resolve", StringComparison.OrdinalIgnoreCase);

    private async Task<(IPAddress[] Addresses,int Ttl)> QueryAsync(string host,int type,CancellationToken ct) {
        if (PreferJsonFirst) {
            var jsonRes = await TryQueryJsonAsync(host, type, ct);
            if (jsonRes.Success || handler is not SocketsHttpHandler || endpoint.Host.EndsWith(".test", StringComparison.OrdinalIgnoreCase))
                return (jsonRes.Addresses, jsonRes.Ttl);
            var wireFallback = await TryQueryWireAsync(host, type, ct);
            return (wireFallback.Addresses, wireFallback.Ttl);
        } else {
            var wireRes = await TryQueryWireAsync(host, type, ct);
            if (wireRes.Success)
                return (wireRes.Addresses, wireRes.Ttl);
            var jsonFallback = await TryQueryJsonAsync(host, type, ct);
            return (jsonFallback.Addresses, jsonFallback.Ttl);
        }
    }

    private async Task<(bool Success, IPAddress[] Addresses, int Ttl)> TryQueryWireAsync(string host, int type, CancellationToken ct) {
        try {
            var queryPacket = BuildDnsWireQuery(host, (ushort)type);
            var b64Url = Convert.ToBase64String(queryPacket).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var sep = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint + sep + "dns=" + b64Url) {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            request.Headers.Accept.ParseAdd("application/dns-message");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (false, [], 0);
            await response.Content.LoadIntoBufferAsync(65536, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > 65536) return (false, [], 0);
            if (IsLikelyJson(bytes)) return ParseJsonBody(bytes, type);
            return ParseDnsWireResponse(bytes, type);
        } catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException) {
            return (false, [], 0);
        }
    }

    private async Task<(bool Success, IPAddress[] Addresses, int Ttl)> TryQueryJsonAsync(string host, int type, CancellationToken ct) {
        try {
            var sep = string.IsNullOrEmpty(endpoint.Query) ? "?" : "&";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint + sep + "name=" + Uri.EscapeDataString(host) + "&type=" + type) {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower
            };
            request.Headers.Accept.ParseAdd("application/dns-json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return (false, [], 0);
            await response.Content.LoadIntoBufferAsync(65536, ct);
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > 65536) return (false, [], 0);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "application/dns-message", StringComparison.OrdinalIgnoreCase) && !IsLikelyJson(bytes))
                return ParseDnsWireResponse(bytes, type);
            return ParseJsonBody(bytes, type);
        } catch (Exception e) when (!ct.IsCancellationRequested && e is HttpRequestException or IOException or JsonException or OperationCanceledException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException) {
            return (false, [], 0);
        }
    }

    private static bool IsLikelyJson(byte[] bytes) {
        foreach (var b in bytes) {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
            return b == (byte)'{';
        }
        return false;
    }

    private static (bool Success, IPAddress[] Addresses, int Ttl) ParseJsonBody(byte[] bytes, int type) {
        using var json = JsonDocument.Parse(bytes);
        var root = json.RootElement;
        if (root.GetProperty("Status").GetInt32() != 0 || (root.TryGetProperty("TC", out var truncated) && truncated.GetBoolean()))
            return (true, [], 0);
        var addresses = new List<IPAddress>();
        var ttl = 300;
        if (root.TryGetProperty("Answer", out var answers) && answers.ValueKind == JsonValueKind.Array) {
            foreach (var answer in answers.EnumerateArray()) {
                if (answer.TryGetProperty("TTL", out var ttlProp) && ttlProp.ValueKind == JsonValueKind.Number)
                    ttl = Math.Min(ttl, Math.Max(0, ttlProp.GetInt32()));
                if (answer.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.Number && typeProp.GetInt32() == type
                    && answer.TryGetProperty("data", out var dataProp) && dataProp.ValueKind == JsonValueKind.String
                    && IPAddress.TryParse(dataProp.GetString(), out var ip)
                    && (type == 1 ? ip.AddressFamily == AddressFamily.InterNetwork : ip.AddressFamily == AddressFamily.InterNetworkV6)) {
                    addresses.Add(ip);
                }
            }
        }
        return (true, addresses.ToArray(), ttl);
    }

    public static byte[] BuildDnsWireQuery(string asciiHost, ushort qtype) {
        var labels = asciiHost.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        using var ms = new MemoryStream(64);
        Span<byte> header = stackalloc byte[12];
        BinaryPrimitives.WriteUInt16BigEndian(header[0..2], 0);      // ID = 0 per RFC 8484
        BinaryPrimitives.WriteUInt16BigEndian(header[2..4], 0x0100); // Flags: RD = 1
        BinaryPrimitives.WriteUInt16BigEndian(header[4..6], 1);      // QDCOUNT = 1
        BinaryPrimitives.WriteUInt16BigEndian(header[6..8], 0);
        BinaryPrimitives.WriteUInt16BigEndian(header[8..10], 0);
        BinaryPrimitives.WriteUInt16BigEndian(header[10..12], 0);
        ms.Write(header);
        foreach (var label in labels) {
            var labelBytes = Encoding.ASCII.GetBytes(label);
            if (labelBytes.Length is 0 or > 63) throw new FormatException("Invalid DNS label length.");
            ms.WriteByte((byte)labelBytes.Length);
            ms.Write(labelBytes);
        }
        ms.WriteByte(0); // Root label
        Span<byte> footer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt16BigEndian(footer[0..2], qtype);
        BinaryPrimitives.WriteUInt16BigEndian(footer[2..4], 1); // QCLASS = IN
        ms.Write(footer);
        return ms.ToArray();
    }

    public static (bool Success, IPAddress[] Addresses, int Ttl) ParseDnsWireResponse(byte[] buf, int expectedType) {
        if (buf.Length < 12) return (false, [], 0);
        var flags = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(2, 2));
        var rcode = flags & 0x000F;
        var tc = (flags & 0x0200) != 0;
        if ((flags & 0x8000) == 0) return (false, [], 0); // Must be QR=1 response
        if (rcode != 0 || tc) return (true, [], 0);
        var qdcount = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(4, 2));
        var ancount = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(6, 2));
        var offset = 12;
        for (var q = 0; q < qdcount; q++) {
            offset = SkipWireName(buf, offset);
            if (offset + 4 > buf.Length) return (false, [], 0);
            offset += 4;
        }
        var addresses = new List<IPAddress>();
        var ttl = 300;
        for (var a = 0; a < ancount; a++) {
            offset = SkipWireName(buf, offset);
            if (offset + 10 > buf.Length) return (false, [], 0);
            var rtype = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset, 2));
            var rclass = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 2, 2));
            var rttl = BinaryPrimitives.ReadUInt32BigEndian(buf.AsSpan(offset + 4, 4));
            var rdlen = BinaryPrimitives.ReadUInt16BigEndian(buf.AsSpan(offset + 8, 2));
            offset += 10;
            if (offset + rdlen > buf.Length) return (false, [], 0);
            ttl = Math.Min(ttl, (int)Math.Min(rttl, int.MaxValue));
            if (rclass == 1 && rtype == expectedType) {
                if (expectedType == 1 && rdlen == 4) {
                    addresses.Add(new IPAddress(buf.AsSpan(offset, 4)));
                } else if (expectedType == 28 && rdlen == 16) {
                    addresses.Add(new IPAddress(buf.AsSpan(offset, 16)));
                }
            }
            offset += rdlen;
        }
        return (true, addresses.ToArray(), ttl);
    }

    private static int SkipWireName(byte[] buf, int offset) {
        var jumps = 0;
        while (offset < buf.Length) {
            var len = buf[offset];
            if (len == 0) return offset + 1;
            if ((len & 0xC0) == 0xC0) {
                if (offset + 1 >= buf.Length) throw new FormatException("Truncated DNS compression pointer.");
                return offset + 2;
            }
            if ((len & 0xC0) != 0) throw new FormatException("Invalid DNS label compression bits.");
            offset += 1 + len;
            if (++jumps > 128) throw new FormatException("DNS label count exceeded.");
        }
        throw new FormatException("Truncated DNS name.");
    }

    public void Dispose()=>http.Dispose();
}

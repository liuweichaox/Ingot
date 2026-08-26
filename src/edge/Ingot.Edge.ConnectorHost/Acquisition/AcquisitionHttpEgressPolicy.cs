using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>解析采集主机地址，使出站策略可以固定并验证实际连接目标。</summary>
public interface IAcquisitionDnsResolver
{
    Task<IPAddress[]> ResolveAsync(
        string host,
        AddressFamily addressFamily,
        CancellationToken ct);
}

public sealed class SystemAcquisitionDnsResolver : IAcquisitionDnsResolver
{
    public Task<IPAddress[]> ResolveAsync(
        string host,
        AddressFamily addressFamily,
        CancellationToken ct)
        => Dns.GetHostAddressesAsync(host, addressFamily, ct);
}

public sealed class AcquisitionHttpEgressPolicy(
    IOptions<AcquisitionSecurityOptions> options,
    IAcquisitionDnsResolver? dnsResolver = null)
{
    private readonly AcquisitionSecurityOptions _options = options.Value;
    private readonly IAcquisitionDnsResolver _dnsResolver = dnsResolver ?? new SystemAcquisitionDnsResolver();
    private readonly HashSet<string> _allowedHttpHosts = NormalizeHosts(
        options.Value.AllowedHttpHosts ?? Array.Empty<string>());
    private readonly HashSet<string> _allowedNetworkHosts = NormalizeHosts(
        options.Value.AllowedNetworkHosts ?? Array.Empty<string>());

    private static HashSet<string> NormalizeHosts(IEnumerable<string> values)
        => values
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value.Trim().TrimEnd('.'))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public async Task EnsureAllowedAsync(
        Uri endpoint,
        CancellationToken ct = default,
        bool requireExplicitAllowlist = false)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidOperationException("HTTP 采集目标必须是绝对 HTTP/HTTPS 地址。");
        _ = await ResolvePermittedAddressesAsync(endpoint.IdnHost, "HTTP", ct).ConfigureAwait(false);
        EnsureExplicitCredentialTarget(endpoint.IdnHost, "HTTP", requireExplicitAllowlist);
    }

    public async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken ct)
    {
        var addresses = await ResolvePermittedAddressesAsync(context.DnsEndPoint.Host, "HTTP", ct)
            .ConfigureAwait(false);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                socket.Dispose();
                lastError = exception;
            }
        }

        throw new HttpRequestException(
            $"无法连接允许的 HTTP 采集目标 {context.DnsEndPoint.Host}:{context.DnsEndPoint.Port}。",
            lastError);
    }

    public async Task<string> ResolvePinnedHostAsync(
        string host,
        string protocol,
        CancellationToken ct = default,
        bool requireExplicitAllowlist = false)
    {
        var addresses = await ResolvePermittedAddressesAsync(host, protocol, ct).ConfigureAwait(false);
        EnsureExplicitCredentialTarget(host, protocol, requireExplicitAllowlist);
        return addresses[0].ToString();
    }

    public async Task<Uri> ResolvePinnedEndpointAsync(
        Uri endpoint,
        string protocol,
        CancellationToken ct = default,
        bool requireExplicitAllowlist = false)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || string.IsNullOrWhiteSpace(endpoint.Host))
            throw new InvalidOperationException($"{protocol} 采集目标必须是带主机名的绝对地址。");
        var pinnedHost = await ResolvePinnedHostAsync(
            endpoint.IdnHost, protocol, ct, requireExplicitAllowlist).ConfigureAwait(false);
        // Never hand a validated DNS name back to a protocol library: doing so would permit a
        // second DNS lookup and reopen loopback/link-local/private-network SSRF through rebinding.
        // TLS endpoints therefore connect to the pinned address and fail closed if their
        // certificate cannot be validated for that address.
        var builder = new UriBuilder(endpoint) { Host = pinnedHost };
        return builder.Uri;
    }

    public async Task<TcpClient> ConnectTcpAsync(
        string host,
        int port,
        string protocol,
        int timeoutMs,
        CancellationToken ct = default)
    {
        var addresses = await ResolvePermittedAddressesAsync(host, protocol, ct).ConfigureAwait(false);
        var timeout = Math.Max(1000, timeoutMs);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(timeout);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var client = new TcpClient(address.AddressFamily)
            {
                ReceiveTimeout = timeout,
                SendTimeout = timeout
            };
            try
            {
                await client.ConnectAsync(address, port, attempt.Token).ConfigureAwait(false);
                return client;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                client.Dispose();
                throw new TimeoutException(
                    $"连接 {protocol} 采集目标 {host}:{port} 超过 {timeout}ms 未完成。");
            }
            catch (Exception exception) when (exception is SocketException or IOException)
            {
                client.Dispose();
                lastError = exception;
            }
        }

        throw new IOException($"无法连接允许的 {protocol} 采集目标 {host}:{port}。", lastError);
    }

    private async Task<IReadOnlyList<IPAddress>> ResolvePermittedAddressesAsync(
        string host,
        string protocol,
        CancellationToken ct)
    {
        var normalizedHost = host.Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalizedHost))
            throw new InvalidOperationException($"{protocol} 采集目标缺少主机名。");

        IPAddress[] addresses;
        if (IPAddress.TryParse(normalizedHost, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            try
            {
                addresses = await _dnsResolver.ResolveAsync(
                    normalizedHost,
                    AddressFamily.Unspecified,
                    ct).ConfigureAwait(false);
            }
            catch (SocketException exception)
            {
                throw new InvalidOperationException($"无法解析 {protocol} 采集目标 {normalizedHost}。", exception);
            }
        }

        addresses = addresses
            .OrderBy(static address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();
        if (addresses.Length == 0)
            throw new InvalidOperationException($"{protocol} 采集目标 {normalizedHost} 没有可用地址。");
        if (addresses.Any(static address => IsForbiddenTarget(address)))
        {
            throw new InvalidOperationException(
                $"{protocol} 采集目标 {normalizedHost} 解析到禁止的回环、链路本地、未指定或组播地址。");
        }
        if (IsExplicitlyAllowed(normalizedHost, protocol))
            return addresses;
        var allowPrivate = string.Equals(protocol, "HTTP", StringComparison.OrdinalIgnoreCase)
            ? _options.AllowPrivateNetworkHttpTargets
            : _options.AllowPrivateNetworkTargets;
        if (!allowPrivate || addresses.Any(static address => !IsPrivateNetwork(address)))
        {
            throw new InvalidOperationException(
                $"{protocol} 采集目标 {normalizedHost} 不在允许的私有网络或 " +
                "Acquisition:Security:AllowedHttpHosts/AllowedNetworkHosts 中。");
        }
        return addresses;
    }

    private bool IsExplicitlyAllowed(string host, string protocol)
        => _allowedNetworkHosts.Contains(host.Trim().TrimEnd('.')) ||
           string.Equals(protocol, "HTTP", StringComparison.OrdinalIgnoreCase) &&
           _allowedHttpHosts.Contains(host.Trim().TrimEnd('.'));

    private void EnsureExplicitCredentialTarget(
        string host,
        string protocol,
        bool requireExplicitAllowlist)
    {
        if (requireExplicitAllowlist && !IsExplicitlyAllowed(host, protocol))
        {
            throw new InvalidOperationException(
                $"{protocol} 采集目标 {host} 使用 Edge 凭据时必须显式加入 " +
                "Acquisition:Security:AllowedHttpHosts/AllowedNetworkHosts。");
        }
    }

    private static bool IsPrivateNetwork(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IsForbiddenTarget(address))
            return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                   bytes[0] == 192 && bytes[1] == 168;
        }
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xfe) == 0xfc;
    }

    private static bool IsForbiddenTarget(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) ||
            address.Equals(IPAddress.IPv6None) ||
            address.IsIPv6Multicast ||
            address.IsIPv6LinkLocal)
            return true;

        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254 ||
               bytes[0] is >= 224 and <= 239;
    }
}

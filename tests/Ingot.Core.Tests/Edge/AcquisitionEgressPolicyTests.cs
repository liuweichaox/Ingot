// 验证所有采集协议共享的出站目标私网、固定解析和显式白名单边界。

using Ingot.Edge.ConnectorHost.Acquisition;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionEgressPolicyTests
{
    [Theory]
    [InlineData("MQTT")]
    [InlineData("OPC UA")]
    [InlineData("Modbus TCP")]
    [InlineData("MELSEC 1E")]
    public async Task NonHttpProtocolsRejectPublicTargetsByDefault(string protocol)
    {
        var policy = Policy();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ResolvePinnedHostAsync("8.8.8.8", protocol));

        Assert.Contains("AllowedNetworkHosts", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MQTT")]
    [InlineData("OPC UA")]
    [InlineData("Modbus TCP")]
    [InlineData("MELSEC 1E")]
    public async Task NonHttpProtocolsRejectPrivateTargetsByDefault(string protocol)
    {
        var policy = Policy();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ResolvePinnedHostAsync("10.20.30.40", protocol));

        Assert.Contains("AllowedNetworkHosts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExplicitNetworkAllowlistPermitsAReviewedPublicTarget()
    {
        var policy = Policy("8.8.8.8");

        var pinnedHost = await policy.ResolvePinnedHostAsync("8.8.8.8", "MQTT");

        Assert.Equal("8.8.8.8", pinnedHost);
    }

    [Theory]
    [InlineData("HTTP")]
    [InlineData("MQTT")]
    [InlineData("OPC UA")]
    [InlineData("Modbus TCP")]
    [InlineData("MELSEC 1E")]
    public async Task ExplicitAllowlistPermitsAReviewedPrivateTarget(string protocol)
    {
        var policy = Policy("10.20.30.40");

        var pinnedHost = await policy.ResolvePinnedHostAsync("10.20.30.40", protocol);

        Assert.Equal("10.20.30.40", pinnedHost);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("fe80::1")]
    public async Task ExplicitAllowlistCannotOverrideForbiddenAddressClasses(string host)
    {
        var policy = Policy(host);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ResolvePinnedHostAsync(host, "MQTT"));

        Assert.Contains("禁止", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("HTTP")]
    [InlineData("MQTT")]
    [InlineData("OPC UA")]
    public async Task CredentialBearingTargetsRequireAnExplicitHostAllowlist(string protocol)
    {
        var policy = PrivateOptInPolicy();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            policy.ResolvePinnedHostAsync(
                "10.20.30.40",
                protocol,
                requireExplicitAllowlist: true));

        Assert.Contains("使用 Edge 凭据时必须显式加入", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CredentialBearingTargetCanUseAnExplicitNetworkAllowlist()
    {
        var policy = Policy("10.20.30.40");

        var pinnedHost = await policy.ResolvePinnedHostAsync(
            "10.20.30.40",
            "MQTT",
            requireExplicitAllowlist: true);

        Assert.Equal("10.20.30.40", pinnedHost);
    }

    [Fact]
    public async Task OpcUaEndpointIsRewrittenToTheValidatedAddress()
    {
        var policy = Policy("10.20.30.40");

        var pinned = await policy.ResolvePinnedEndpointAsync(
            new Uri("opc.tcp://10.20.30.40:4840"),
            "OPC UA");

        Assert.Equal("10.20.30.40", pinned.Host);
        Assert.Equal(4840, pinned.Port);
    }

    [Fact]
    public async Task AllowlistedHttpsHostnameIsRewrittenToTheSingleValidatedDnsResult()
    {
        var resolver = new RecordingDnsResolver(IPAddress.Parse("203.0.113.10"));
        var policy = new AcquisitionHttpEgressPolicy(
            Options.Create(new AcquisitionSecurityOptions
            {
                AllowedNetworkHosts = ["device.example"]
            }),
            resolver);

        var pinned = await policy.ResolvePinnedEndpointAsync(
            new Uri("https://device.example:4843/discovery"),
            "OPC UA");

        Assert.Equal("203.0.113.10", pinned.Host);
        Assert.Equal(4843, pinned.Port);
        Assert.Equal(1, resolver.CallCount);
    }

    private static AcquisitionHttpEgressPolicy Policy(params string[] allowedHosts)
        => new(Options.Create(new AcquisitionSecurityOptions
        {
            AllowedHttpHosts = allowedHosts,
            AllowedNetworkHosts = allowedHosts
        }));

    private static AcquisitionHttpEgressPolicy PrivateOptInPolicy()
        => new(Options.Create(new AcquisitionSecurityOptions
        {
            AllowPrivateNetworkHttpTargets = true,
            AllowPrivateNetworkTargets = true
        }));

    private sealed class RecordingDnsResolver(params IPAddress[] addresses) : IAcquisitionDnsResolver
    {
        public int CallCount { get; private set; }

        public Task<IPAddress[]> ResolveAsync(
            string host,
            AddressFamily addressFamily,
            CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(addresses);
        }
    }
}

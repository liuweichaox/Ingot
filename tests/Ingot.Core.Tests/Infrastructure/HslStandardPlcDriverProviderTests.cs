using System;
using System.Collections.Generic;
using System.Reflection;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Clients;
using HslCommunication.Core.Device;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class HslStandardPlcDriverProviderTests
{
    [Fact]
    public void Create_ShouldRejectUnsupportedProtocolOptions()
    {
        var provider = new HslStandardPlcDriverProvider();
        var config = new DeviceConfig
        {
            PlcCode = "PLC01",
            Driver = "siemens-s7",
            Host = "127.0.0.1",
            Port = 102,
            ProtocolOptions = new Dictionary<string, string>
            {
                ["unsupported-option"] = "1"
            }
        };

        var ex = Assert.Throws<NotSupportedException>(() => provider.Create(config));
        Assert.Contains("unsupported-option", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_ShouldReturnClientForKnownDriver()
    {
        var provider = new HslStandardPlcDriverProvider();
        var config = new DeviceConfig
        {
            PlcCode = "PLC01",
            Driver = "siemens-s7",
            Host = "127.0.0.1",
            Port = 102,
            ProtocolOptions = new Dictionary<string, string>
            {
                ["plc"] = "S1200",
                ["connect-timeout-ms"] = "3000"
            }
        };

        var client = provider.Create(config);

        Assert.NotNull(client);
        Assert.IsType<HslPlcClientService>(client);
    }

    [Fact]
    public void Create_ShouldRejectDriverAliases()
    {
        var provider = new HslStandardPlcDriverProvider();
        var config = new DeviceConfig
        {
            PlcCode = "PLC01",
            Driver = "siemens_s7",
            Host = "127.0.0.1",
            Port = 102
        };

        Assert.Throws<NotSupportedException>(() => provider.Create(config));
    }

    [Fact]
    public void Create_ShouldAcceptDocumentedProtocolOptionNames()
    {
        var provider = new HslStandardPlcDriverProvider();
        var config = new DeviceConfig
        {
            PlcCode = "PLC01",
            Driver = "lsis-fast-enet",
            Host = "127.0.0.1",
            Port = 2004,
            ProtocolOptions = new Dictionary<string, string>
            {
                ["cpuType"] = "XGK",
                ["slotNo"] = "3",
                ["connect-timeout-ms"] = "3000",
                ["receive-timeout-ms"] = "4000"
            }
        };

        var client = provider.Create(config);

        Assert.NotNull(client);
        Assert.IsType<HslPlcClientService>(client);
    }

    [Theory]
    [InlineData("siemens-s7", 1102)]
    [InlineData("yamatake-digitron-cpl-over-tcp", 10001)]
    public void Create_ShouldApplyConfiguredEndpointToUnderlyingDevice(string driver, ushort port)
    {
        var provider = new HslStandardPlcDriverProvider();
        var config = new DeviceConfig
        {
            PlcCode = "PLC01",
            Driver = driver,
            Host = "10.0.0.5",
            Port = port,
            ProtocolOptions = new Dictionary<string, string>()
        };

        var client = Assert.IsType<HslPlcClientService>(provider.Create(config));
        var device = GetUnderlyingDevice(client);

        Assert.Equal(config.Host, device.IpAddress);
        Assert.Equal(port, Convert.ToUInt16(device.Port));
    }

    private static DeviceTcpNet GetUnderlyingDevice(HslPlcClientService client)
    {
        var prop = typeof(HslPlcClientService).GetProperty("Device",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(prop);

        return Assert.IsAssignableFrom<DeviceTcpNet>(prop!.GetValue(client));
    }
}

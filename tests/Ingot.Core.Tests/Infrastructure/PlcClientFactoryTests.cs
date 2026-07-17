using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Clients;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class PlcClientFactoryTests
{
    [Fact]
    public void Constructor_ShouldRejectDuplicateDriverRegistrations()
    {
        var providers = new IPlcDriverProvider[]
        {
            new FakeDriverProvider("melsec-a1e"),
            new FakeDriverProvider("melsec-a1e")
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new PlcClientFactory(providers));

        Assert.Contains("melsec-a1e", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDriverProvider : IPlcDriverProvider
    {
        public FakeDriverProvider(string driver)
        {
            SupportedDrivers = new[] { driver };
        }

        public IReadOnlyCollection<string> SupportedDrivers { get; }

        public IPlcClientService Create(DeviceConfig config) => new FakePlcClientService();
    }

    private sealed class FakePlcClientService : PlcClientServiceBase
    {
        public override Task ConnectCloseAsync() => Task.CompletedTask;
        public override IPStatus IpAddressPing() => IPStatus.Success;
    }
}

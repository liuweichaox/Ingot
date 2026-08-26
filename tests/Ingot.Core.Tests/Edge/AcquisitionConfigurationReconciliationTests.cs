// 验证边缘组件 AcquisitionConfigurationReconciliation 的协议、状态和失败边界。

using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.Application.Options;
using Ingot.Edge.ConnectorHost.Acquisition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionConfigurationReconciliationTests
{
    [Fact]
    public async Task Upgrade_ShouldWaitForSafeProcessExecutionBoundary()
    {
        var status = new AcquisitionStatus();
        var runner = new ControllableRunner(status);
        var service = CreateService(status, runner);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var first = Deployment("press", 1);
            await service.SynchronizeWorkersAsync(
                [first], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);
            status.RecordProcessExecutionState("press@1", true);

            await service.SynchronizeWorkersAsync(
                [Deployment("press", 2)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            var waiting = status.Get();
            Assert.Contains(waiting.Tasks, item => item.ConfigurationKey == "press@1");
            Assert.DoesNotContain(waiting.Tasks, item => item.ConfigurationKey == "press@2");
            Assert.Equal(
                AcquisitionApplicationStates.WaitingForProcessExecutionBoundary,
                Assert.Single(waiting.Deployments).State);

            status.RecordProcessExecutionState("press@1", false);
            await service.SynchronizeWorkersAsync(
                [Deployment("press", 2)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            var applied = status.Get();
            Assert.DoesNotContain(applied.Tasks, item => item.ConfigurationKey == "press@1");
            Assert.Contains(applied.Tasks, item =>
                item.ConfigurationKey == "press@2" && item.State == "running");
            Assert.Equal(AcquisitionApplicationStates.Applied, Assert.Single(applied.Deployments).State);
            Assert.Equal(2, Assert.Single(applied.Deployments).AppliedVersion);
        }
        finally
        {
            await service.StopAllWorkersAsync();
        }
    }

    [Fact]
    public async Task UpgradeWithoutHealthySample_ShouldRestorePreviousVersion()
    {
        var status = new AcquisitionStatus();
        var runner = new ControllableRunner(status);
        runner.FailingVersions.Add(2);
        var service = CreateService(status, runner, startupHealthTimeoutMs: 1000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await service.SynchronizeWorkersAsync(
                [Deployment("press", 1)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            await service.SynchronizeWorkersAsync(
                [Deployment("press", 2)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            await WaitUntilAsync(
                () => status.Get().Tasks.Any(item =>
                    item.ConfigurationKey == "press@1" && item.State == "running"),
                cancellation.Token);
            var rolledBack = status.Get();
            Assert.DoesNotContain(rolledBack.Tasks, item => item.ConfigurationKey == "press@2");
            Assert.Contains(rolledBack.Tasks, item => item.ConfigurationKey == "press@1");
            var application = Assert.Single(rolledBack.Deployments);
            Assert.Equal(2, application.DesiredVersion);
            Assert.Equal(1, application.AppliedVersion);
            Assert.Equal(AcquisitionApplicationStates.Rollback, application.State);
            Assert.Contains("启动健康期限", application.LastError);
        }
        finally
        {
            await service.StopAllWorkersAsync();
        }
    }

    [Fact]
    public async Task UpgradeWithTransportReadsButNoValidSnapshot_ShouldRestorePreviousVersion()
    {
        var status = new AcquisitionStatus();
        var runner = new ControllableRunner(status);
        runner.ReadOnlyVersions.Add(2);
        var service = CreateService(status, runner, startupHealthTimeoutMs: 1000);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await service.SynchronizeWorkersAsync(
                [Deployment("press", 1)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            await service.SynchronizeWorkersAsync(
                [Deployment("press", 2)], "EDGE-001", AcquisitionConfigurationSources.Cache, cancellation.Token);

            var application = Assert.Single(status.Get().Deployments);
            Assert.Equal(AcquisitionApplicationStates.Rollback, application.State);
            Assert.Equal(1, application.AppliedVersion);
            Assert.DoesNotContain(status.Get().Tasks, item => item.ConfigurationKey == "press@2");
        }
        finally
        {
            await service.StopAllWorkersAsync();
        }
    }

    [Fact]
    public async Task FailedDeviceTask_ShouldNotStopOtherDeviceTask()
    {
        var status = new AcquisitionStatus();
        var runner = new ControllableRunner(status);
        runner.FailingProfiles.Add("broken");
        var service = CreateService(status, runner);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await service.SynchronizeWorkersAsync(
                [Deployment("healthy", 1), Deployment("broken", 1)],
                "EDGE-001",
                AcquisitionConfigurationSources.Cache,
                cancellation.Token);
            await WaitUntilAsync(
                () => status.Get().Tasks.Any(item =>
                    item.ConfigurationKey == "broken@1" && item.State == "degraded"),
                cancellation.Token);

            var snapshot = status.Get();
            Assert.Contains(snapshot.Tasks, item =>
                item.ConfigurationKey == "healthy@1" && item.State == "running");
            Assert.Contains(snapshot.Tasks, item =>
                item.ConfigurationKey == "broken@1" && item.State == "degraded");
        }
        finally
        {
            await service.StopAllWorkersAsync();
        }
    }

    [Fact]
    public async Task PlatformUnavailableOnStartup_ShouldRunLastKnownGoodCachedDeployment()
    {
        var status = new AcquisitionStatus();
        var runner = new ControllableRunner(status);
        var clients = new ThrowingHttpClientFactory();
        var service = CreateService(
            status,
            runner,
            clients: clients,
            cache: new NullDeploymentCache([Deployment("cached-press", 4)]),
            reporting: new EdgeReportingOptions
            {
                EnablePlatformReporting = true,
                PlatformApiBaseUrl = "http://platform/",
                EnableEventShipping = false
            });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cancellation.Token);
        try
        {
            await WaitUntilAsync(
                () => status.Get().Tasks.Any(item =>
                    item.ConfigurationKey == "cached-press@4" && item.State == "running"),
                cancellation.Token);
            var snapshot = status.Get();
            Assert.Equal(AcquisitionConfigurationSources.Cache, snapshot.ConfigurationSource);
            Assert.Contains("最后一次成功配置", snapshot.LastError);
            Assert.Equal(4, Assert.Single(snapshot.Deployments).AppliedVersion);
        }
        finally
        {
            await service.StopAsync(cancellation.Token);
        }
    }

    private static HttpPollingAcquisitionHostedService CreateService(
        AcquisitionStatus status,
        IAcquisitionProtocolRunner runner,
        int startupHealthTimeoutMs = 30000,
        IHttpClientFactory? clients = null,
        IAcquisitionDeploymentCache? cache = null,
        EdgeReportingOptions? reporting = null)
    {
        clients ??= new ThrowingHttpClientFactory();
        var securityOptions = Options.Create(new AcquisitionSecurityOptions
        {
            AllowPrivateNetworkHttpTargets = true,
            AllowPrivateNetworkTargets = true
        });
        var secrets = new EnvironmentAcquisitionSecretResolver(securityOptions);
        var egressPolicy = new AcquisitionHttpEgressPolicy(securityOptions);
        return new HttpPollingAcquisitionHostedService(
            clients,
            new NullEventSink(),
            new FixedEdgeIdentity(),
            Options.Create(new HttpPollingAcquisitionOptions
            {
                StartupHealthTimeoutMs = startupHealthTimeoutMs
            }),
            Options.Create(reporting ?? new EdgeReportingOptions
            {
                EnablePlatformReporting = false,
                EnableEventShipping = false
            }),
            cache ?? new NullDeploymentCache(),
            new AcquisitionProbeService(
                clients,
                secrets,
                egressPolicy),
            secrets,
            [runner],
            egressPolicy,
            status,
            NullLogger<HttpPollingAcquisitionHostedService>.Instance);
    }

    private static AcquisitionDeployment Deployment(string taskId, int version) => new()
    {
        Task = new IngestionTask
        {
            TaskId = taskId,
            Version = version,
            Name = taskId,
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.ModbusTcp,
            DataModelId = "generic",
            Source = $"connector/{taskId}",
            SubjectId = taskId,
            TimestampMode = "edge-received",
            ModbusTcp = new ModbusTcpConnection
            {
                Host = "127.0.0.1",
                PollIntervalMs = 1
            },
            Execution = new AcquisitionExecutionOptions
            {
                TimeoutMs = 1000,
                ReconnectDelayMs = 100
            },
            ValueMappings =
            [
                new AcquisitionValueMapping
                {
                    DataItemCode = "temperature.actual",
                    SourcePath = "holding-register:0:int16",
                    SourceDataType = "int16"
                }
            ]
        },
        DataModel = new ProcessDataModel
        {
            ModelId = "generic",
            Name = "Generic",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "temperature.actual",
                        DisplayName = "Temperature",
                        DataType = "double"
                    }
                ]
            }
        }
    };

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
            await Task.Delay(10, ct);
    }

    private sealed class ControllableRunner(AcquisitionStatus status) : IAcquisitionProtocolRunner
    {
        public string Protocol => AcquisitionProtocols.ModbusTcp;
        public HashSet<int> FailingVersions { get; } = [];
        public HashSet<int> ReadOnlyVersions { get; } = [];
        public HashSet<string> FailingProfiles { get; } = new(StringComparer.Ordinal);

        public async Task RunAsync(
            string configurationKey,
            AcquisitionDeployment deployment,
            string normalizedSource,
            CancellationToken ct)
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            if (FailingVersions.Contains(deployment.Task.Version) ||
                FailingProfiles.Contains(deployment.Task.TaskId))
                throw new IOException("simulated device connection failure");
            if (ReadOnlyVersions.Contains(deployment.Task.Version))
            {
                status.RecordReadSuccess(configurationKey, DateTimeOffset.UtcNow);
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return;
            }
            status.RecordProcessExecutionState(configurationKey, false);
            status.RecordReadSuccess(configurationKey, DateTimeOffset.UtcNow);
            status.RecordValidSnapshot(configurationKey, DateTimeOffset.UtcNow, processSpecification: null);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
    }

    private sealed class FixedEdgeIdentity : IEdgeIdentityProvider
    {
        public string GetEdgeId() => "EDGE-001";
    }

    private sealed class NullDeploymentCache(
        IReadOnlyList<AcquisitionDeployment>? deployments = null) : IAcquisitionDeploymentCache
    {
        public Task<IReadOnlyList<AcquisitionDeployment>?> LoadAsync(
            string edgeId,
            CancellationToken ct = default)
            => Task.FromResult(deployments);

        public Task SaveAsync(
            string edgeId,
            IReadOnlyList<AcquisitionDeployment> deployments,
            CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ThrowingHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name = "")
            => new(new ThrowingHandler());

        private sealed class ThrowingHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
                => throw new InvalidOperationException("HTTP is not expected in this test.");
        }
    }

    private sealed class NullEventSink : IEventSink
    {
        public ValueTask<ProductionEvent> EmitAsync(
            ProductionEvent evt,
            CancellationToken ct = default)
            => ValueTask.FromResult(evt);

        public ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
            IReadOnlyList<ProductionEvent> events,
            CancellationToken ct = default)
            => ValueTask.FromResult(events);
    }
}

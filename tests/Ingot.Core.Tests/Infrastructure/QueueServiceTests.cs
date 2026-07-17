using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class QueueServiceTests
{
    [Fact]
    public async Task PublishAsync_ShouldDropFailedBatchAndContinueProcessingLaterBatches()
    {
        var storage = new FakeStorage();
        var queue = new QueueService(
            storage,
            NullLogger<QueueService>.Instance,
            Options.Create(new AcquisitionOptions
            {
                QueueService = new QueueServiceOptions
                {
                    FlushIntervalSeconds = 3600
                }
            }),
            new FakeDeviceConfigService(batchSize: 2));

        await queue.PublishAsync(CreateMessage("PLC01", "CH01", "sensor", quality: "drop"));
        await queue.PublishAsync(CreateMessage("PLC01", "CH01", "sensor", quality: "healthy-a"));
        await queue.PublishAsync(CreateMessage("PLC01", "CH01", "sensor", quality: "healthy-b"));
        await queue.PublishAsync(CreateMessage("PLC01", "CH01", "sensor", quality: "healthy-c"));

        Assert.Equal(2, storage.AttemptedBatchCount);
        Assert.Equal(["healthy-b", "healthy-c"], storage.SavedMessages.Select(m => m.DataValues["quality"]).OrderBy(x => x));

        await queue.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ShouldFlushRemainingMessages()
    {
        var storage = new FakeStorage();
        var queue = new QueueService(
            storage,
            NullLogger<QueueService>.Instance,
            Options.Create(new AcquisitionOptions
            {
                QueueService = new QueueServiceOptions
                {
                    FlushIntervalSeconds = 3600
                }
            }),
            new FakeDeviceConfigService(batchSize: 10));

        await queue.PublishAsync(CreateMessage("PLC01", "CH01", "sensor", quality: "healthy"));
        await queue.DisposeAsync();

        Assert.Single(storage.SavedMessages);
        Assert.Equal("healthy", storage.SavedMessages.Single().DataValues["quality"]);
    }

    private static DataMessage CreateMessage(string plcCode, string channelCode, string measurement, string quality)
    {
        var message = DataMessage.Create(
            Guid.NewGuid().ToString(),
            measurement,
            plcCode,
            channelCode,
            EventType.Data,
            DateTimeOffset.UtcNow);

        message.AddDataValue("quality", quality);
        return message;
    }

    private sealed class FakeStorage : IDataStorageService
    {
        public ConcurrentBag<DataMessage> SavedMessages { get; } = new();

        public int AttemptedBatchCount => _attemptedBatchCount;

        private int _attemptedBatchCount;

        public Task<bool> SaveBatchAsync(List<DataMessage> dataPoints)
        {
            Interlocked.Increment(ref _attemptedBatchCount);

            if (dataPoints.Any(static msg => Equals(msg.DataValues["quality"], "drop")))
                return Task.FromResult(false);

            foreach (var message in dataPoints)
                SavedMessages.Add(message);

            return Task.FromResult(true);
        }
    }

    private sealed class FakeDeviceConfigService : IDeviceConfigService
    {
        private readonly int _batchSize;

        public FakeDeviceConfigService(int batchSize)
        {
            _batchSize = batchSize;
        }

        public event EventHandler<ConfigChangedEventArgs>? ConfigChanged
        {
            add { }
            remove { }
        }

        public Task<List<DeviceConfig>> GetConfigs()
        {
            return Task.FromResult(new List<DeviceConfig>
            {
                new()
                {
                    SourceCode = "PLC01",
                    Channels =
                    [
                        new AcquisitionChannel
                        {
                            ChannelCode = "CH01",
                            Measurement = "sensor",
                            BatchSize = _batchSize
                        }
                    ]
                }
            });
        }

        public Task<ConfigValidationResult> ValidateConfigAsync(DeviceConfig config)
        {
            return Task.FromResult(new ConfigValidationResult { IsValid = true });
        }
    }
}

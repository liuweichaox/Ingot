using System;
using System.IO;
using System.Threading.Tasks;
using Ingot.Infrastructure.DeviceConfigs;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class DeviceConfigValidationRunnerTests
{
    [Fact]
    public async Task ValidateDirectoryAsync_ShouldReportInvalidFiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "invalid.json"), """
            {
              "SchemaVersion": 3,
              "IsEnabled": true,
              "PlcCode": "",
              "Driver": "",
              "Host": "",
              "Port": 0,
              "HeartbeatMonitorRegister": "",
              "HeartbeatPollingInterval": 0,
              "Channels": []
            }
            """);

            var runner = new DeviceConfigValidationRunner();
            var summary = await runner.ValidateDirectoryAsync(directory);

            Assert.False(summary.IsValid);
            Assert.Single(summary.Files);
            Assert.False(summary.Files[0].IsValid);
            Assert.Contains(summary.Files[0].Errors, error => error.Contains("SchemaVersion"));
            Assert.Contains(summary.Files[0].Errors, error => error.Contains("Driver"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateDirectoryAsync_ShouldAcceptValidFiles()
    {
        var directory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "valid.json"), """
            {
              "SchemaVersion": 1,
              "IsEnabled": true,
              "PlcCode": "PLC01",
              "Driver": "melsec-a1e",
              "Host": "plc01.local",
              "Port": 502,
              "HeartbeatMonitorRegister": "D100",
              "HeartbeatPollingInterval": 5000,
              "Channels": [
                {
                  "Measurement": "sensor",
                  "ChannelCode": "CH01",
                  "EnableBatchRead": false,
                  "BatchSize": 1,
                  "AcquisitionInterval": 100,
                  "AcquisitionMode": "Always",
                  "Metrics": [
                    {
                      "MetricLabel": "temperature",
                      "FieldName": "temperature",
                      "Register": "D6000",
                      "Index": 0,
                      "DataType": "short"
                    }
                  ]
                }
              ]
            }
            """);

            var runner = new DeviceConfigValidationRunner();
            var summary = await runner.ValidateDirectoryAsync(directory);

            Assert.True(summary.IsValid);
            Assert.Single(summary.Files);
            Assert.True(summary.Files[0].IsValid);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateDirectoryAsync_ShouldRejectDuplicateSourceCodes()
    {
        var directory = CreateTempDirectory();
        try
        {
            const string duplicateConfig = """
            {
              "SchemaVersion": 1,
              "IsEnabled": true,
              "PlcCode": "PLC-DUP",
              "Driver": "melsec-a1e",
              "Host": "127.0.0.1",
              "Port": 502,
              "HeartbeatMonitorRegister": "D100",
              "HeartbeatPollingInterval": 5000,
              "Channels": [
                {
                  "Measurement": "sensor",
                  "ChannelCode": "CH01",
                  "EnableBatchRead": false,
                  "BatchSize": 1,
                  "AcquisitionInterval": 100,
                  "AcquisitionMode": "Always",
                  "Metrics": [
                    {
                      "MetricLabel": "temperature",
                      "FieldName": "temperature",
                      "Register": "D6000",
                      "Index": 0,
                      "DataType": "short"
                    }
                  ]
                }
              ]
            }
            """;

            await File.WriteAllTextAsync(Path.Combine(directory, "first.json"), duplicateConfig);
            await File.WriteAllTextAsync(Path.Combine(directory, "second.json"), duplicateConfig);

            var runner = new DeviceConfigValidationRunner();
            var summary = await runner.ValidateDirectoryAsync(directory);

            Assert.False(summary.IsValid);
            Assert.Equal(2, summary.Files.Count);
            Assert.All(summary.Files, file =>
            {
                Assert.False(file.IsValid);
                Assert.Contains(file.Errors, error => error.Contains("重复 SourceCode"));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateDirectoryAsync_ShouldRejectUnknownConfigurationMembers()
    {
        var directory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "typo.json"), """
            {
              "SchemaVersion": 1,
              "IsEnabled": true,
              "PlcCode": "PLC01",
              "Driver": "melsec-a1e",
              "Host": "127.0.0.1",
              "Port": 502,
              "HeartbeatMonitorRegister": "D100",
              "HeartbeatPollingInterval": 5000,
              "Channnels": []
            }
            """);

            var runner = new DeviceConfigValidationRunner();
            var summary = await runner.ValidateDirectoryAsync(directory);

            Assert.False(summary.IsValid);
            Assert.Contains(
                summary.Files[0].Errors,
                error => error.Contains("Channnels", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateDirectoryAsync_ShouldRejectUnknownNestedChannelMembers()
    {
        var directory = CreateTempDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "nested-typo.json"), """
            {
              "SchemaVersion": 1,
              "IsEnabled": true,
              "PlcCode": "PLC01",
              "Driver": "melsec-a1e",
              "Host": "127.0.0.1",
              "Port": 502,
              "HeartbeatMonitorRegister": "D100",
              "HeartbeatPollingInterval": 5000,
              "Channels": [
                {
                  "ChannnelCode": "CH01",
                  "Measurement": "sensor",
                  "EnableBatchRead": false,
                  "BatchSize": 1,
                  "AcquisitionInterval": 100,
                  "AcquisitionMode": "Always",
                  "Metrics": []
                }
              ]
            }
            """);

            var runner = new DeviceConfigValidationRunner();
            var summary = await runner.ValidateDirectoryAsync(directory);

            Assert.False(summary.IsValid);
            Assert.Contains(
                summary.Files[0].Errors,
                error => error.Contains("ChannnelCode", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ingot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}

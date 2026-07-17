using System;
using System.Collections.Generic;
using System.IO;
using Ingot.Domain.Events;
using Ingot.Infrastructure.Acquisition;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class EdgeStateStoreTests
{
    [Fact]
    public void StartCycle_ShouldPersistAndRecoverActiveCycle()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"acq-state-{Guid.NewGuid():N}.db");
        try
        {
            var configuration = BuildConfiguration(dbPath);
            var manager1 = new EdgeStateStore(configuration, NullLogger<EdgeStateStore>.Instance);

            var cycle = manager1.StartCycle("PLC01", "CH01", "production");

            var manager2 = new EdgeStateStore(configuration, NullLogger<EdgeStateStore>.Instance);
            var restored = manager2.GetActiveCycle("PLC01", "CH01", "production");

            Assert.NotNull(restored);
            Assert.Equal(cycle.CycleId, restored!.CycleId);

            var ended = manager2.EndCycle("PLC01", "CH01", "production");
            Assert.NotNull(ended);

            var manager3 = new EdgeStateStore(configuration, NullLogger<EdgeStateStore>.Instance);
            Assert.Null(manager3.GetActiveCycle("PLC01", "CH01", "production"));
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
        }
    }

    [Fact]
    public async Task Context_ShouldPersistAndReturnSelectedSnapshot()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"edge-state-{Guid.NewGuid():N}.db");
        try
        {
            var configuration = BuildConfiguration(dbPath);
            var asset = new ObjectRef("equipment", "POL-03");
            var first = new EdgeStateStore(configuration, NullLogger<EdgeStateStore>.Instance);

            await first.SetAsync(asset, "material_lot", "LOT-01");
            await first.SetAsync(asset, "tooling", "TOOL-07");

            var reopened = new EdgeStateStore(configuration, NullLogger<EdgeStateStore>.Instance);
            var snapshot = reopened.Snapshot(asset, ["material_lot"]);

            Assert.Equal("LOT-01", snapshot["material_lot"]);
            Assert.DoesNotContain("tooling", snapshot);
        }
        finally
        {
            TryDelete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
        }
    }

    [Fact]
    public async Task FailedPersistence_ShouldNotPublishContextOrCycleToMemory()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"edge-state-failure-{Guid.NewGuid():N}.db");
        try
        {
            var store = new EdgeStateStore(
                BuildConfiguration(dbPath),
                NullLogger<EdgeStateStore>.Instance);
            File.Delete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
            SqliteConnection.ClearAllPools();
            Directory.CreateDirectory(dbPath);
            var asset = new ObjectRef("equipment", "POL-03");

            await Assert.ThrowsAnyAsync<Exception>(
                () => store.SetAsync(asset, "material_lot", "LOT-FAILED"));
            Assert.Null(store.Get(asset, "material_lot"));

            Assert.ThrowsAny<Exception>(
                () => store.StartCycle("PLC01", "CH01", "production"));
            Assert.Null(store.GetActiveCycle("PLC01", "CH01", "production"));
        }
        finally
        {
            if (Directory.Exists(dbPath))
                Directory.Delete(dbPath, recursive: true);
            TryDelete(dbPath);
            TryDelete($"{dbPath}-wal");
            TryDelete($"{dbPath}-shm");
        }
    }

    private static IConfiguration BuildConfiguration(string dbPath)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Acquisition:StateStore:DatabasePath"] = dbPath
            })
            .Build();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore cleanup failures in temp path
        }
    }
}

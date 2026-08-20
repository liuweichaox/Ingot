using Ingot.Platform.Infrastructure.Services;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeRegistryCancellationTests
{
    [Fact]
    public async Task ListAsync_PropagatesCancellationBeforeDatabaseIo()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=ingot;Username=ingot;Password=ingot;Timeout=1");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EdgeRegistry(dataSource).ListAsync(cancellation.Token));
    }
}

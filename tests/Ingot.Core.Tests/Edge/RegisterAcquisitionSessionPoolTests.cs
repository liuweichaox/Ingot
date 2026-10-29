// 验证寄存器采集会话的共享、串行、取消、重连与协议兼容边界。
using System.Net;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class RegisterAcquisitionSessionPoolTests
{
    [Fact]
    public async Task ModbusGatewayRoutesDifferentUnitsThroughOneSerializedConnection()
    {
        var listener = StartListener(out var port);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            var accepted = 0;
            var observedUnits = new List<byte>();
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token);
                Interlocked.Increment(ref accepted);
                var stream = client.GetStream();
                for (var index = 0; index < 2; index++)
                {
                    var request = await ReadExactlyAsync(stream, 12, timeout.Token);
                    Assert.Equal((byte)3, request[7]);
                    observedUnits.Add(request[6]);
                    await stream.WriteAsync(new byte[]
                    {
                        request[0], request[1], 0, 0, 0, 5, request[6], 3, 2, 0, request[6]
                    }, timeout.Token);
                }
            }, timeout.Token);
            await using var pool = new RegisterAcquisitionSessionPool();
            async Task<RegisterAcquisitionSessionPool.Lease> Acquire()
                => await pool.AcquireAsync(AcquisitionProtocols.ModbusTcp,
                    "127.0.0.1", port, string.Empty, "gateway", 1, "gateway-config",
                    ct => ConnectAsync(port, ct), timeout.Token);
            await using var first = await Acquire();
            await using var second = await Acquire();

            Task<ushort[]> ReadUnit(RegisterAcquisitionSessionPool.Lease lease, byte unit)
                => lease.ExecuteModbusAsync(2000,
                    (master, ct) => master.ReadHoldingRegistersAsync(unit, 0, 1).WaitAsync(ct), timeout.Token);
            var values = await Task.WhenAll(ReadUnit(first, 1), ReadUnit(second, 2));

            Assert.Equal((ushort)1, Assert.Single(values[0]));
            Assert.Equal((ushort)2, Assert.Single(values[1]));
            await server;
            Assert.Equal(new byte[] { 1, 2 }, observedUnits.Order().ToArray());
            Assert.Equal(1, Volatile.Read(ref accepted));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("binary")]
    public async Task MelsecSnapshotUsesConfiguredWireCodec(string codec)
    {
        var listener = StartListener(out var port);
        try
        {
            var server = ServeOneMelsecWordReadAsync(listener, codec, 42, CancellationToken.None);
            await using var pool = new RegisterAcquisitionSessionPool();
            await using var lease = await AcquireMelsecAsync(pool, port, codec);

            var values = await lease.ExecuteMelsecAsync(
                2000,
                (stream, ct) => MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
                    stream,
                    MelsecConnection(codec),
                    WordPlan(),
                    ct: ct));

            Assert.Equal((ushort)42, values["D:0:uint16"]);
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ProbeAndWorkerLeasesShareOneSocketAndSerializeWholeSnapshots()
    {
        var listener = StartListener(out var port);
        try
        {
            var accepted = 0;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref accepted);
                var stream = client.GetStream();
                for (var index = 0; index < 2; index++)
                {
                    _ = await ReadExactlyAsync(stream, 12, CancellationToken.None);
                    await stream.WriteAsync(new byte[] { 0x81, 0x00, 0x2A, 0x00 });
                }
                var closed = new byte[1];
                Assert.Equal(0, await stream.ReadAsync(closed));
            });

            await using var pool = new RegisterAcquisitionSessionPool();
            await using var probeLease = await AcquireMelsecAsync(pool, port, "binary");
            await using var workerLease = await AcquireMelsecAsync(pool, port, "binary");
            var active = 0;
            var maximumActive = 0;

            async Task<Dictionary<string, object?>> ReadSnapshot(
                RegisterAcquisitionSessionPool.Lease lease,
                CancellationToken ct)
                => await lease.ExecuteMelsecAsync(
                    2000,
                    async (stream, readToken) =>
                    {
                        var current = Interlocked.Increment(ref active);
                        UpdateMaximum(ref maximumActive, current);
                        try
                        {
                            await Task.Delay(30, readToken);
                            return await MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
                                stream,
                                MelsecConnection("binary"),
                                WordPlan(),
                                ct: readToken);
                        }
                        finally
                        {
                            Interlocked.Decrement(ref active);
                        }
                    },
                    ct);

            var results = await Task.WhenAll(
                ReadSnapshot(probeLease, CancellationToken.None),
                ReadSnapshot(workerLease, CancellationToken.None));

            Assert.All(results, values => Assert.Equal((ushort)42, values["D:0:uint16"]));
            Assert.Equal(1, maximumActive);
            Assert.Equal(1, Volatile.Read(ref accepted));
            await probeLease.DisposeAsync();
            Assert.False(server.IsCompleted);
            await workerLease.DisposeAsync();
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CancellationWhileWaitingForSessionGateDoesNotResetSocket()
    {
        var listener = StartListener(out var port);
        var closeServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var accepted = 0;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref accepted);
                await closeServer.Task;
            });
            await using var pool = new RegisterAcquisitionSessionPool();
            await using var firstLease = await AcquireMelsecAsync(pool, port, "binary");
            await using var secondLease = await AcquireMelsecAsync(pool, port, "binary");
            var operationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseOperation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = firstLease.ExecuteMelsecAsync(
                3000,
                async (_, ct) =>
                {
                    operationEntered.TrySetResult(true);
                    await releaseOperation.Task.WaitAsync(ct);
                    return true;
                });
            await operationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondLease.ExecuteMelsecAsync(
                2000,
                (_, _) => Task.FromResult(true),
                cancellation.Token));

            releaseOperation.TrySetResult(true);
            Assert.True(await first);
            Assert.True(await secondLease.ExecuteMelsecAsync(
                2000,
                (stream, _) => Task.FromResult(stream.CanRead)));
            Assert.Equal(1, Volatile.Read(ref accepted));
            closeServer.TrySetResult(true);
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            closeServer.TrySetResult(true);
            listener.Stop();
        }
    }

    [Fact]
    public async Task DisposedLeaseQueuedOnSessionGateIsRejected()
    {
        var listener = StartListener(out var port);
        var closeServer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var accepted = 0;
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref accepted);
                await closeServer.Task;
            });
            var connectCount = 0;
            await using var pool = new RegisterAcquisitionSessionPool();
            async Task<TcpClient> Connect(CancellationToken ct)
            {
                Interlocked.Increment(ref connectCount);
                return await ConnectAsync(port, ct);
            }

            await using var activeLease = await pool.AcquireAsync(
                AcquisitionProtocols.MelsecA1E,
                "127.0.0.1",
                port,
                "binary",
                null,
                null,
                "test-source",
                Connect);
            var queuedLease = await pool.AcquireAsync(
                AcquisitionProtocols.MelsecA1E,
                "127.0.0.1",
                port,
                "binary",
                null,
                null,
                "test-source",
                Connect);
            var operationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseOperation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NetworkStream? activeStream = null;
            var activeOperation = activeLease.ExecuteMelsecAsync(
                3000,
                async (stream, ct) =>
                {
                    activeStream = stream;
                    operationEntered.TrySetResult(true);
                    await releaseOperation.Task.WaitAsync(ct);
                    return true;
                });
            await operationEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            var queuedCallbackCalled = false;
            var queuedOperation = queuedLease.ExecuteMelsecAsync(
                2000,
                (_, _) =>
                {
                    queuedCallbackCalled = true;
                    return Task.FromResult(true);
                });
            await queuedLease.DisposeAsync();
            releaseOperation.TrySetResult(true);

            Assert.True(await activeOperation);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queuedOperation);
            Assert.False(queuedCallbackCalled);
            Assert.Throws<ObjectDisposedException>(() =>
            {
                _ = queuedLease.ExecuteMelsecAsync(1000, (_, _) => Task.FromResult(true));
            });
            Assert.True(await activeLease.ExecuteMelsecAsync(
                2000,
                (stream, _) => Task.FromResult(ReferenceEquals(activeStream, stream))));
            Assert.Equal(1, Volatile.Read(ref connectCount));
            Assert.Equal(1, Volatile.Read(ref accepted));
            closeServer.TrySetResult(true);
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            closeServer.TrySetResult(true);
            listener.Stop();
        }
    }

    [Fact]
    public async Task ReconnectUsesConnectPolicyFromLeaseThatNeedsTheConnection()
    {
        var listener = StartListener(out var port);
        try
        {
            var server = Task.Run(async () =>
            {
                using (var firstClient = await listener.AcceptTcpClientAsync())
                {
                    var closed = new byte[1];
                    Assert.Equal(0, await firstClient.GetStream().ReadAsync(closed));
                }

                using var secondClient = await listener.AcceptTcpClientAsync();
            });
            var firstConnects = 0;
            var secondConnects = 0;
            await using var pool = new RegisterAcquisitionSessionPool();
            await using var firstLease = await pool.AcquireAsync(
                AcquisitionProtocols.MelsecA1E,
                "127.0.0.1",
                port,
                "binary",
                null,
                null,
                "source-v1",
                async ct =>
                {
                    Interlocked.Increment(ref firstConnects);
                    return await ConnectAsync(port, ct);
                });
            await using var secondLease = await pool.AcquireAsync(
                AcquisitionProtocols.MelsecA1E,
                "127.0.0.1",
                port,
                "binary",
                null,
                null,
                "source-v1",
                async ct =>
                {
                    Interlocked.Increment(ref secondConnects);
                    return await ConnectAsync(port, ct);
                });

            await Assert.ThrowsAsync<IOException>(() => firstLease.ExecuteMelsecAsync(
                2000,
                (_, _) => Task.FromException<bool>(new IOException("reset the first socket"))));
            await firstLease.DisposeAsync();
            Assert.True(await secondLease.ExecuteMelsecAsync(
                2000,
                (stream, _) => Task.FromResult(stream.CanRead)));

            Assert.Equal(1, Volatile.Read(ref firstConnects));
            Assert.Equal(1, Volatile.Read(ref secondConnects));
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task CancellationDuringReadResetsSocketAndNextSnapshotReconnects()
    {
        var listener = StartListener(out var port);
        try
        {
            var firstRequestRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var accepted = 0;
            var server = Task.Run(async () =>
            {
                using (var firstClient = await listener.AcceptTcpClientAsync())
                {
                    Interlocked.Increment(ref accepted);
                    var firstStream = firstClient.GetStream();
                    _ = await ReadExactlyAsync(firstStream, 12, CancellationToken.None);
                    firstRequestRead.TrySetResult(true);
                    var byteBuffer = new byte[1];
                    Assert.Equal(0, await firstStream.ReadAsync(byteBuffer));
                }

                using var secondClient = await listener.AcceptTcpClientAsync();
                Interlocked.Increment(ref accepted);
                var secondStream = secondClient.GetStream();
                _ = await ReadExactlyAsync(secondStream, 12, CancellationToken.None);
                await secondStream.WriteAsync(new byte[] { 0x81, 0x00, 0x2A, 0x00 });
            });

            await using var pool = new RegisterAcquisitionSessionPool();
            await using var lease = await AcquireMelsecAsync(pool, port, "binary");
            using var cancellation = new CancellationTokenSource();
            var interruptedRead = lease.ExecuteMelsecAsync(
                3000,
                (stream, ct) => MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
                    stream,
                    MelsecConnection("binary"),
                    WordPlan(),
                    ct: ct),
                cancellation.Token);
            await firstRequestRead.Task.WaitAsync(TimeSpan.FromSeconds(2));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => interruptedRead);

            var values = await lease.ExecuteMelsecAsync(
                2000,
                (stream, ct) => MelsecA1EAcquisitionRunner.ReadSnapshotAsync(
                    stream,
                    MelsecConnection("binary"),
                    WordPlan(),
                    ct: ct));

            Assert.Equal((ushort)42, values["D:0:uint16"]);
            Assert.Equal(2, Volatile.Read(ref accepted));
            await server.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task DifferentEndpointsUseIndependentSessionGates()
    {
        var firstListener = StartListener(out var firstPort);
        var secondListener = StartListener(out var secondPort);
        var closeServers = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var firstServer = HoldOneConnectionAsync(firstListener, closeServers.Task);
            var secondServer = HoldOneConnectionAsync(secondListener, closeServers.Task);
            await using var pool = new RegisterAcquisitionSessionPool();
            await using var firstLease = await AcquireMelsecAsync(pool, firstPort, "binary");
            await using var secondLease = await AcquireMelsecAsync(pool, secondPort, "binary");
            var active = 0;
            var maximumActive = 0;

            async Task<bool> HoldSession(RegisterAcquisitionSessionPool.Lease lease)
                => await lease.ExecuteMelsecAsync(
                    2000,
                    async (stream, ct) =>
                    {
                        var current = Interlocked.Increment(ref active);
                        UpdateMaximum(ref maximumActive, current);
                        try
                        {
                            await Task.Delay(80, ct);
                            return stream.CanRead;
                        }
                        finally
                        {
                            Interlocked.Decrement(ref active);
                        }
                    });

            var results = await Task.WhenAll(HoldSession(firstLease), HoldSession(secondLease));
            Assert.All(results, value => Assert.True(value));
            Assert.Equal(2, maximumActive);
            closeServers.TrySetResult(true);
            await Task.WhenAll(firstServer, secondServer).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            closeServers.TrySetResult(true);
            firstListener.Stop();
            secondListener.Stop();
        }
    }

    [Fact]
    public async Task IncompatibleCodecProtocolAndSourceVersionAreRejected()
    {
        await using var pool = new RegisterAcquisitionSessionPool();
        await using var lease = await AcquireMelsecAsync(
            pool,
            55001,
            "ascii",
            dataSourceId: "plc-a-source",
            dataSourceVersion: 3,
            sourceVersionFingerprint: "immutable-v3");

        var codecError = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AcquireMelsecAsync(pool, 55001, "binary"));
        Assert.Contains("通信数据码不兼容", codecError.Message);

        var protocolError = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await pool.AcquireAsync(
                AcquisitionProtocols.ModbusTcp,
                "127.0.0.1",
                55001,
                string.Empty,
                null,
                null,
                string.Empty,
                _ => throw new InvalidOperationException("不会连接")));
        Assert.Contains("不能同时使用", protocolError.Message);

        var versionError = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AcquireMelsecAsync(
                pool,
                55001,
                "ascii",
                dataSourceId: "plc-a-source",
                dataSourceVersion: 3,
                sourceVersionFingerprint: "changed-v3"));
        Assert.Contains("创建新版本", versionError.Message);
    }

    [Fact]
    public async Task SameSourceVersionCanServeDifferentLogicalSubjectsAndUnitIds()
    {
        await using var pool = new RegisterAcquisitionSessionPool();
        var connection = new ModbusTcpConnection
        {
            Host = "127.0.0.1",
            Port = 55002,
            UnitId = 1
        };
        var firstTask = SourceTask("cavity-1");
        var secondTask = SourceTask("cavity-2");
        var secondConnection = connection with { UnitId = 2 };
        var firstFingerprint = RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, firstTask);
        var secondFingerprint = RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(secondConnection, secondTask);
        Assert.Equal(firstFingerprint, secondFingerprint);

        await using var first = await pool.AcquireAsync(
            AcquisitionProtocols.ModbusTcp,
            connection.Host,
            connection.Port,
            string.Empty,
            "shared-plc-source",
            4,
            firstFingerprint,
            _ => throw new InvalidOperationException("The lazy session must not connect during acquire."));
        await using var second = await pool.AcquireAsync(
            AcquisitionProtocols.ModbusTcp,
            connection.Host,
            connection.Port,
            string.Empty,
            "shared-plc-source",
            4,
            secondFingerprint,
            _ => throw new InvalidOperationException("The shared session must not connect during acquire."));
    }

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static async Task<RegisterAcquisitionSessionPool.Lease> AcquireMelsecAsync(
        RegisterAcquisitionSessionPool pool,
        int port,
        string codec,
        string? dataSourceId = null,
        int? dataSourceVersion = null,
        string sourceVersionFingerprint = "test-source")
        => await pool.AcquireAsync(
            AcquisitionProtocols.MelsecA1E,
            "127.0.0.1",
            port,
            codec,
            dataSourceId,
            dataSourceVersion,
            sourceVersionFingerprint,
            ct => ConnectAsync(port, ct));

    private static async Task<TcpClient> ConnectAsync(int port, CancellationToken ct)
    {
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, ct);
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task ServeOneMelsecWordReadAsync(
        TcpListener listener,
        string codec,
        ushort value,
        CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        _ = await ReadExactlyAsync(stream, codec == "ascii" ? 24 : 12, ct);
        byte[] payload;
        if (codec == "ascii")
            payload = System.Text.Encoding.ASCII.GetBytes($"0000{value:X4}");
        else
            payload = [0x81, 0x00, (byte)value, (byte)(value >> 8)];
        await stream.WriteAsync(payload, ct);
    }

    private static async Task HoldOneConnectionAsync(TcpListener listener, Task close)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await close;
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int count, CancellationToken ct)
    {
        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, ct);
        return bytes;
    }

    private static McA1EConnection MelsecConnection(string codec)
        => new() { Host = "127.0.0.1", Port = 5551, DataCode = codec };

    private static IngestionTask SourceTask(string subjectId)
        => new()
        {
            TaskId = subjectId,
            Name = subjectId,
            EdgeId = "edge-a",
            DataModelId = "model-a",
            Source = "connector/plc-a",
            SubjectId = subjectId
        };

    private static IReadOnlyList<MelsecA1EAcquisitionRunner.McRead> WordPlan()
    {
        const string path = "D:0:uint16";
        Assert.True(AcquisitionSelectors.TryParseMelsec(path, out var point, out var error), error);
        return MelsecA1EAcquisitionRunner.BuildReadPlan(
            new Dictionary<string, AcquisitionSelectors.MelsecPoint> { [path] = point! },
            maxMergeGap: 0);
    }

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref maximum);
            if (candidate <= current || Interlocked.CompareExchange(ref maximum, candidate, current) == current)
                return;
        }
    }
}

// Verifies the acquisition client's binary wire frames and rejection paths without physical PLCs.

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge.Protocols;

public sealed class IndustrialProtocolFrameTests
{
    [Fact]
    public async Task MelsecMc3E_QlWordRead_EmitsQnACompatibleBinaryFrame()
    {
        var point = ParseMelsecPoint("D:100:uint16", MelsecMc3EFamilies.QL);
        var stream = new ScriptedDuplexStream(BuildMc3EResponse([0x00, 0x00, 0x34, 0x12]), maxReadChunk: 2);
        var plan = Mc3EPlan("D:100:uint16", point, count: 1);

        var values = await MelsecMc3EProtocolSupport.ReadSnapshotAsync(
            stream,
            new Mc3EConnection { Family = MelsecMc3EFamilies.QL },
            [plan],
            CancellationToken.None);

        Assert.Equal(
            new byte[] { 0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0C, 0x00, 0x10, 0x00, 0x01, 0x04, 0x00, 0x00, 0x64, 0x00, 0x00, 0xA8, 0x01, 0x00 },
            stream.WrittenBytes);
        Assert.Equal((ushort)0x1234, Assert.IsType<ushort>(values["D:100:uint16"]));
    }

    [Fact]
    public async Task MelsecMc3E_IqRWideAddress_UsesFourByteAddressAndReadSubcommand()
    {
        var point = ParseMelsecPoint("D:305419896:uint16", MelsecMc3EFamilies.IqR);
        var stream = new ScriptedDuplexStream(BuildMc3EResponse([0x00, 0x00, 0x34, 0x12]));
        var plan = Mc3EPlan("D:305419896:uint16", point, count: 1);

        await MelsecMc3EProtocolSupport.ReadSnapshotAsync(
            stream,
            new Mc3EConnection { Family = MelsecMc3EFamilies.IqR },
            [plan],
            CancellationToken.None);

        Assert.Equal(
            new byte[] { 0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0E, 0x00, 0x10, 0x00, 0x01, 0x04, 0x02, 0x00, 0x78, 0x56, 0x34, 0x12, 0xA8, 0x00, 0x01, 0x00 },
            stream.WrittenBytes);
    }

    [Fact]
    public async Task MelsecMc3E_IqRBitRead_UsesBitSubcommandAndDecodesPackedValue()
    {
        var point = ParseMelsecPoint("X:10:boolean", MelsecMc3EFamilies.IqR);
        var stream = new ScriptedDuplexStream(BuildMc3EResponse([0x00, 0x00, 0x10]));
        var plan = Mc3EPlan("X:10:boolean", point, count: 1);

        var values = await MelsecMc3EProtocolSupport.ReadSnapshotAsync(
            stream,
            new Mc3EConnection { Family = MelsecMc3EFamilies.IqR },
            [plan],
            CancellationToken.None);

        Assert.Equal(
            new byte[] { 0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00, 0x0E, 0x00, 0x10, 0x00, 0x01, 0x04, 0x03, 0x00, 0x10, 0x00, 0x00, 0x00, 0x9C, 0x00, 0x01, 0x00 },
            stream.WrittenBytes);
        Assert.True((bool)values["X:10:boolean"]!);
    }

    [Fact]
    public async Task MelsecMc3E_NonzeroEndCode_IsRejected()
    {
        var point = ParseMelsecPoint("D:100:uint16", MelsecMc3EFamilies.QL);
        var stream = new ScriptedDuplexStream(BuildMc3EResponse([0x51, 0xC0]));

        var error = await Assert.ThrowsAsync<IOException>(() => MelsecMc3EProtocolSupport.ReadSnapshotAsync(
            stream,
            new Mc3EConnection { Family = MelsecMc3EFamilies.QL },
            [Mc3EPlan("D:100:uint16", point, count: 1)],
            CancellationToken.None));

        Assert.Contains("0xC051", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OmronFinsTcp_HandshakeAndMemoryRead_EmitExpectedTransportAndCommandFrames()
    {
        var handshakePayload = new byte[] { 0x00, 0x00, 0x00, 0x12, 0x00, 0x00, 0x00, 0x34 };
        var readPayload = new byte[]
        {
            0xC0, 0x00, 0x02, 0x00, 0x12, 0x00, 0x00, 0x34, 0x00,
            0x01, 0x01, 0x01, 0x00, 0x00, 0x12, 0x34
        };
        var incoming = BuildFinsTcpFrame(1, 0, handshakePayload)
            .Concat(BuildFinsTcpFrame(2, 0, readPayload))
            .ToArray();
        var stream = new ScriptedDuplexStream(incoming, maxReadChunk: 3);
        var session = await OmronFinsTcpSession.CreateAsync(stream, CancellationToken.None);

        const string path = "DM:291:uint16";
        var point = ParseOmronPoint(path);
        var values = await OmronFinsTcpProtocolSupport.ReadSnapshotAsync(
            session,
            new Dictionary<string, AcquisitionSelectors.OmronFinsPoint> { [path] = point },
            CancellationToken.None);

        var handshakeRequest = BuildFinsTcpFrame(0, 0, [0x00, 0x00, 0x00, 0x00]);
        var readRequest = BuildFinsTcpFrame(2, 0,
        [
            0x80, 0x00, 0x02, 0x00, 0x34, 0x00, 0x00, 0x12, 0x00,
            0x01, 0x01, 0x01, 0x82, 0x01, 0x23, 0x00, 0x00, 0x01
        ]);
        Assert.Equal(handshakeRequest.Concat(readRequest).ToArray(), stream.WrittenBytes);
        Assert.Equal((ushort)0x1234, Assert.IsType<ushort>(values[path]));
    }

    [Fact]
    public async Task OmronFinsTcp_ResponseWithWrongServiceId_IsRejected()
    {
        var handshake = BuildFinsTcpFrame(1, 0, [0x00, 0x00, 0x00, 0x12, 0x00, 0x00, 0x00, 0x34]);
        var response = BuildFinsTcpFrame(2, 0,
        [
            0xC0, 0x00, 0x02, 0x00, 0x12, 0x00, 0x00, 0x34, 0x00,
            0x02, 0x01, 0x01, 0x00, 0x00, 0x34, 0x12
        ]);
        var stream = new ScriptedDuplexStream(handshake.Concat(response).ToArray());
        var session = await OmronFinsTcpSession.CreateAsync(stream, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            session.ReadMemoryAreaAsync(0x82, 0x0123, 0, 1, CancellationToken.None));
    }

    [Fact]
    public async Task OmronFinsUdp_MemoryRead_EmitsExpectedNodesServiceIdAndAddress()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        using var client = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        client.Connect((IPEndPoint)server.Client.LocalEndPoint!);
        using var session = new OmronFinsUdpSession(client, clientNodeAddress: 0x12, serverNodeAddress: 0x34);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        const string path = "DM:4660:uint16";
        var point = ParseOmronPoint(path);
        byte[]? request = null;
        var responder = Task.Run(async () =>
        {
            var datagram = await server.ReceiveAsync(timeout.Token);
            request = datagram.Buffer;
            var response = new byte[]
            {
                0xC0, 0x00, 0x02, 0x00, 0x12, 0x00, 0x00, 0x34, 0x00,
                0x01, 0x01, 0x01, 0x00, 0x00, 0x56, 0x78
            };
            await server.SendAsync(response, datagram.RemoteEndPoint, timeout.Token);
        }, timeout.Token);

        var values = await OmronFinsUdpProtocolSupport.ReadSnapshotAsync(
            session,
            new Dictionary<string, AcquisitionSelectors.OmronFinsPoint> { [path] = point },
            1000,
            timeout.Token);
        await responder;

        Assert.Equal(
            new byte[] { 0x80, 0x00, 0x02, 0x00, 0x34, 0x00, 0x00, 0x12, 0x00, 0x01, 0x01, 0x01, 0x82, 0x12, 0x34, 0x00, 0x00, 0x01 },
            request);
        Assert.Equal((ushort)0x5678, Assert.IsType<ushort>(values[path]));
    }

    [Fact]
    public void ModbusUint16Decoder_PreservesDeclaredScalarType()
    {
        var value = ModbusTcpAcquisitionRunner.Decode([0x1234], new AcquisitionValueMapping
        {
            DataItemCode = "temperature.raw",
            SourcePath = "holding-register:0:uint16",
            SourceDataType = "uint16",
            ModbusArea = "holding-register",
            ModbusAddress = 0,
            ModbusQuantity = 1
        });

        Assert.Equal((ushort)0x1234, Assert.IsType<ushort>(value));
    }

    private static AcquisitionSelectors.MelsecMc3EPoint ParseMelsecPoint(string selector, string family)
    {
        Assert.True(AcquisitionSelectors.TryParseMelsecMc3E(selector, null, family, out var point, out var error), error);
        return point!;
    }

    private static AcquisitionSelectors.OmronFinsPoint ParseOmronPoint(string selector)
    {
        Assert.True(AcquisitionSelectors.TryParseOmronFins(selector, null, null, out var point, out var error), error);
        return point!;
    }

    private static MelsecMc3EProtocolSupport.Mc3ERead Mc3EPlan(
        string path,
        AcquisitionSelectors.MelsecMc3EPoint point,
        int count)
        => new(point.Device.DeviceCode, point.UsesBitRead, point.Address, count,
            [new KeyValuePair<string, AcquisitionSelectors.MelsecMc3EPoint>(path, point)]);

    private static byte[] BuildMc3EResponse(byte[] body)
    {
        var frame = new byte[9 + body.Length];
        frame[0] = 0xD0;
        frame[1] = 0x00;
        frame[2] = 0x00;
        frame[3] = 0xFF;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), 0x03FF);
        frame[6] = 0x00;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(7, 2), checked((ushort)body.Length));
        body.CopyTo(frame, 9);
        return frame;
    }

    private static byte[] BuildFinsTcpFrame(uint command, uint errorCode, byte[] payload)
    {
        var frame = new byte[16 + payload.Length];
        "FINS"u8.CopyTo(frame);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), checked((uint)(8 + payload.Length)));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), command);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12, 4), errorCode);
        payload.CopyTo(frame, 16);
        return frame;
    }

    private sealed class ScriptedDuplexStream(byte[] incoming, int maxReadChunk = int.MaxValue) : Stream
    {
        private readonly MemoryStream _written = new();
        private int _readOffset;

        public byte[] WrittenBytes => _written.ToArray();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var copied = Math.Min(Math.Min(count, maxReadChunk), incoming.Length - _readOffset);
            if (copied <= 0) return 0;
            incoming.AsSpan(_readOffset, copied).CopyTo(buffer.AsSpan(offset, copied));
            _readOffset += copied;
            return copied;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(buffer.Length, maxReadChunk), incoming.Length - _readOffset);
            if (count <= 0) return ValueTask.FromResult(0);
            incoming.AsMemory(_readOffset, count).CopyTo(buffer);
            _readOffset += count;
            return ValueTask.FromResult(count);
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Write(byte[] buffer, int offset, int count)
            => _written.Write(buffer, offset, count);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _written.Write(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _written.Write(buffer, offset, count);
            return Task.CompletedTask;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

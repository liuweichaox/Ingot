using System;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using HslCommunication;
using HslCommunication.Core.Device;

namespace Ingot.Infrastructure.Clients;

/// <summary>
///     标准 Hsl PLC 客户端。统一包装 HslCommunication 的 DeviceTcpNet 读写能力。
/// </summary>
public sealed class HslPlcClientService(DeviceTcpNet device) : PlcClientServiceBase
{
    private DeviceTcpNet Device { get; } = device;

    public override Task ConnectCloseAsync() => Device.ConnectCloseAsync();

    public override IPStatus IpAddressPing() => Device.IpAddressPing();

    private async Task<PlcWriteResult> WriteAsync(Func<Task<OperateResult>> write)
    {
        var res = await write().ConfigureAwait(false);
        return new PlcWriteResult { IsSuccess = res.IsSuccess, Message = res.Message };
    }

    public override Task<PlcWriteResult> WriteUShortAsync(string address, ushort value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteUIntAsync(string address, uint value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteULongAsync(string address, ulong value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteShortAsync(string address, short value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteIntAsync(string address, int value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteLongAsync(string address, long value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteFloatAsync(string address, float value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteDoubleAsync(string address, double value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteStringAsync(string address, string value) => WriteAsync(() => Device.WriteAsync(address, value));
    public override Task<PlcWriteResult> WriteBoolAsync(string address, bool value) => WriteAsync(() => Device.WriteAsync(address, value));

    public override async Task<PlcReadResult> ReadAsync(string address, ushort length)
    {
        var res = await Device.ReadAsync(address, length).ConfigureAwait(false);
        return new PlcReadResult
        {
            IsSuccess = res.IsSuccess,
            Content = res.Content ?? Array.Empty<byte>(),
            Message = res.Message
        };
    }

    private static async Task<T> ReadAsync<T>(Task<OperateResult<T>> readTask)
    {
        var res = await readTask.ConfigureAwait(false);
        if (!res.IsSuccess)
            throw new InvalidOperationException($"PLC read failed: {res.Message}");
        return res.Content;
    }

    public override Task<ushort> ReadUShortAsync(string address) => ReadAsync(Device.ReadUInt16Async(address));
    public override Task<uint> ReadUIntAsync(string address) => ReadAsync(Device.ReadUInt32Async(address));
    public override Task<ulong> ReadULongAsync(string address) => ReadAsync(Device.ReadUInt64Async(address));
    public override Task<short> ReadShortAsync(string address) => ReadAsync(Device.ReadInt16Async(address));
    public override Task<int> ReadIntAsync(string address) => ReadAsync(Device.ReadInt32Async(address));
    public override Task<long> ReadLongAsync(string address) => ReadAsync(Device.ReadInt64Async(address));
    public override Task<float> ReadFloatAsync(string address) => ReadAsync(Device.ReadFloatAsync(address));
    public override Task<double> ReadDoubleAsync(string address) => ReadAsync(Device.ReadDoubleAsync(address));
    public override async Task<string> ReadStringAsync(
        string address,
        ushort length,
        Encoding encoding)
    {
        // HSL 的 ReadStringAsync(length) 会把 length 直接传给协议读取；在字寻址 PLC
        // （例如 Mitsubishi）中可能返回 length * 2 字节。Ingot 配置的 StringByteLength
        // 明确定义为字节数，因此在适配器边界截取并只解码所请求的字节，避免读入相邻寄存器。
        var res = await Device.ReadAsync(address, length).ConfigureAwait(false);
        if (!res.IsSuccess)
            throw new InvalidOperationException($"PLC read failed: {res.Message}");

        var content = res.Content ?? Array.Empty<byte>();
        if (content.Length < length)
            throw new InvalidDataException(
                $"PLC 字符串读取长度不足：Address={address}, ExpectedBytes={length}, ActualBytes={content.Length}");

        return Device.ByteTransform.TransString(content, 0, length, encoding);
    }
    public override Task<bool> ReadBoolAsync(string address) => ReadAsync(Device.ReadBoolAsync(address));

    public override ushort TransUShort(byte[] buffer, int index) => Device.ByteTransform.TransUInt16(buffer, index);
    public override uint TransUInt(byte[] buffer, int index) => Device.ByteTransform.TransUInt32(buffer, index);
    public override ulong TransULong(byte[] buffer, int index) => Device.ByteTransform.TransUInt64(buffer, index);
    public override short TransShort(byte[] buffer, int index) => Device.ByteTransform.TransInt16(buffer, index);
    public override int TransInt(byte[] buffer, int index) => Device.ByteTransform.TransInt32(buffer, index);
    public override long TransLong(byte[] buffer, int index) => Device.ByteTransform.TransInt64(buffer, index);
    public override float TransFloat(byte[] buffer, int index) => Device.ByteTransform.TransSingle(buffer, index);
    public override double TransDouble(byte[] buffer, int index) => Device.ByteTransform.TransDouble(buffer, index);
    public override string TransString(byte[] buffer, int index, int length, Encoding encoding) => Device.ByteTransform.TransString(buffer, index, length, encoding);
    public override bool TransBool(byte[] buffer, int index) => Device.ByteTransform.TransBool(buffer, index);
}

using System;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using Ingot.Domain.Models;

namespace Ingot.Application.Abstractions;

/// <summary>
///     连接生命周期能力：探测、关闭。
/// </summary>
public interface IPlcConnectionClient
{
    Task ConnectCloseAsync();
    IPStatus IpAddressPing();
}

/// <summary>
///     原始块读取能力。
/// </summary>
public interface IPlcRawReadClient
{
    Task<PlcReadResult> ReadAsync(string address, ushort length);
}

/// <summary>
///     类型化读取能力。
/// </summary>
public interface IPlcTypedReadClient
{
    Task<ushort> ReadUShortAsync(string address);
    Task<uint> ReadUIntAsync(string address);
    Task<ulong> ReadULongAsync(string address);
    Task<short> ReadShortAsync(string address);
    Task<int> ReadIntAsync(string address);
    Task<long> ReadLongAsync(string address);
    Task<float> ReadFloatAsync(string address);
    Task<double> ReadDoubleAsync(string address);
    Task<string> ReadStringAsync(string address, ushort length, Encoding encoding);
    Task<bool> ReadBoolAsync(string address);
}

/// <summary>
///     类型化写入能力。
/// </summary>
public interface IPlcTypedWriteClient
{
    Task<PlcWriteResult> WriteUShortAsync(string address, ushort value);
    Task<PlcWriteResult> WriteUIntAsync(string address, uint value);
    Task<PlcWriteResult> WriteULongAsync(string address, ulong value);
    Task<PlcWriteResult> WriteShortAsync(string address, short value);
    Task<PlcWriteResult> WriteIntAsync(string address, int value);
    Task<PlcWriteResult> WriteLongAsync(string address, long value);
    Task<PlcWriteResult> WriteFloatAsync(string address, float value);
    Task<PlcWriteResult> WriteDoubleAsync(string address, double value);
    Task<PlcWriteResult> WriteStringAsync(string address, string value);
    Task<PlcWriteResult> WriteBoolAsync(string address, bool value);
}

/// <summary>
///     批量字节解码能力。
/// </summary>
public interface IPlcBufferDecoder
{
    ushort TransUShort(byte[] buffer, int index);
    uint TransUInt(byte[] buffer, int index);
    ulong TransULong(byte[] buffer, int index);
    short TransShort(byte[] buffer, int index);
    int TransInt(byte[] buffer, int index);
    long TransLong(byte[] buffer, int index);
    float TransFloat(byte[] buffer, int index);
    double TransDouble(byte[] buffer, int index);
    string TransString(byte[] buffer, int index, int length, Encoding encoding);
    bool TransBool(byte[] buffer, int index);
}

/// <summary>
///     采集主链路真正依赖的读取能力。
/// </summary>
public interface IPlcDataAccessClient : IPlcRawReadClient, IPlcTypedReadClient, IPlcBufferDecoder;

/// <summary>
///     兼容当前框架的聚合客户端接口。
/// </summary>
public interface IPlcClientService : IPlcConnectionClient, IPlcTypedWriteClient, IPlcDataAccessClient;

/// <summary>
///     PLC 客户端基类。第三方驱动可以按需覆盖自身真正支持的能力，而不是一次实现完整大接口。
/// </summary>
public abstract class PlcClientServiceBase : IPlcClientService
{
    public virtual Task ConnectCloseAsync() => Task.CompletedTask;

    public virtual IPStatus IpAddressPing() => IPStatus.Unknown;

    public virtual Task<PlcWriteResult> WriteUShortAsync(string address, ushort value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteUIntAsync(string address, uint value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteULongAsync(string address, ulong value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteShortAsync(string address, short value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteIntAsync(string address, int value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteLongAsync(string address, long value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteFloatAsync(string address, float value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteDoubleAsync(string address, double value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteStringAsync(string address, string value) => NotSupportedWrite();
    public virtual Task<PlcWriteResult> WriteBoolAsync(string address, bool value) => NotSupportedWrite();

    public virtual Task<PlcReadResult> ReadAsync(string address, ushort length) => NotSupportedRead<PlcReadResult>();
    public virtual Task<ushort> ReadUShortAsync(string address) => NotSupportedRead<ushort>();
    public virtual Task<uint> ReadUIntAsync(string address) => NotSupportedRead<uint>();
    public virtual Task<ulong> ReadULongAsync(string address) => NotSupportedRead<ulong>();
    public virtual Task<short> ReadShortAsync(string address) => NotSupportedRead<short>();
    public virtual Task<int> ReadIntAsync(string address) => NotSupportedRead<int>();
    public virtual Task<long> ReadLongAsync(string address) => NotSupportedRead<long>();
    public virtual Task<float> ReadFloatAsync(string address) => NotSupportedRead<float>();
    public virtual Task<double> ReadDoubleAsync(string address) => NotSupportedRead<double>();
    public virtual Task<string> ReadStringAsync(string address, ushort length, Encoding encoding) => NotSupportedRead<string>();
    public virtual Task<bool> ReadBoolAsync(string address) => NotSupportedRead<bool>();

    public virtual ushort TransUShort(byte[] buffer, int index) => ThrowNotSupported<ushort>();
    public virtual uint TransUInt(byte[] buffer, int index) => ThrowNotSupported<uint>();
    public virtual ulong TransULong(byte[] buffer, int index) => ThrowNotSupported<ulong>();
    public virtual short TransShort(byte[] buffer, int index) => ThrowNotSupported<short>();
    public virtual int TransInt(byte[] buffer, int index) => ThrowNotSupported<int>();
    public virtual long TransLong(byte[] buffer, int index) => ThrowNotSupported<long>();
    public virtual float TransFloat(byte[] buffer, int index) => ThrowNotSupported<float>();
    public virtual double TransDouble(byte[] buffer, int index) => ThrowNotSupported<double>();
    public virtual string TransString(byte[] buffer, int index, int length, Encoding encoding) => ThrowNotSupported<string>();
    public virtual bool TransBool(byte[] buffer, int index) => ThrowNotSupported<bool>();

    protected static Task<PlcWriteResult> NotSupportedWrite() =>
        Task.FromException<PlcWriteResult>(new NotSupportedException("当前 PLC 客户端未实现该写入能力。"));

    protected static Task<T> NotSupportedRead<T>() =>
        Task.FromException<T>(new NotSupportedException("当前 PLC 客户端未实现该读取能力。"));

    protected static T ThrowNotSupported<T>() =>
        throw new NotSupportedException("当前 PLC 客户端未实现该缓冲区解码能力。");
}

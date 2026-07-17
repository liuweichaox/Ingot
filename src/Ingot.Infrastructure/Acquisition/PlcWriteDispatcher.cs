using System;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     根据数据类型将写入请求分发到对应的 PLC 写入方法。
/// </summary>
internal static class PlcWriteDispatcher
{
    public static Task<PlcWriteResult> WriteAsync(IPlcTypedWriteClient client, string address, object value,
        string dataType)
    {
        return dataType switch
        {
            "ushort" => client.WriteUShortAsync(address, Convert.ToUInt16(value)),
            "uint" => client.WriteUIntAsync(address, Convert.ToUInt32(value)),
            "ulong" => client.WriteULongAsync(address, Convert.ToUInt64(value)),
            "short" => client.WriteShortAsync(address, Convert.ToInt16(value)),
            "int" => client.WriteIntAsync(address, Convert.ToInt32(value)),
            "long" => client.WriteLongAsync(address, Convert.ToInt64(value)),
            "float" => client.WriteFloatAsync(address, Convert.ToSingle(value)),
            "double" => client.WriteDoubleAsync(address, Convert.ToDouble(value)),
            "string" => client.WriteStringAsync(address, Convert.ToString(value) ?? string.Empty),
            "bool" => client.WriteBoolAsync(address, Convert.ToBoolean(value)),
            _ => Task.FromResult(new PlcWriteResult
            {
                IsSuccess = false,
                Message = $"不支持的数据类型: {dataType}"
            })
        };
    }
}

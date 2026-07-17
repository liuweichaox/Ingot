using System.Text.Json;
using Ingot.Application.Abstractions;
using Ingot.Contracts;
using Ingot.Domain.Models;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Edge.Agent.Controllers;

/// <summary>
///     数据采集控制器
/// </summary>
[ApiController]
[Route("api/acquisition")]
public class AcquisitionController(IAcquisitionService acquisitionService) : ControllerBase
{
    /// <summary>
    ///     获取所有 Plc 连接状态
    /// </summary>
    /// <returns></returns>
    [HttpGet("plc-connections")]
    public IActionResult GetPlcConnections()
        => Ok(acquisitionService.GetPlcConnections());

    /// <summary>
    ///     写入 PLC 寄存器
    /// </summary>
    /// <param name="request">写入请求</param>
    [HttpPost]
    public async Task<IActionResult> WriteRegister([FromBody] PlcWriteRequest? request, CancellationToken ct)
    {
        // 输入验证
        if (request == null) return BadRequest(new { error = "请求体不能为空" });

        if (string.IsNullOrWhiteSpace(request.SourceCode)) return BadRequest(new { error = "SourceCode 不能为空" });

        if (request.Items.Count == 0) return BadRequest(new { error = "写入项列表不能为空" });

        // 验证每个写入项
        foreach (var item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Address)) return BadRequest(new { error = "寄存器地址不能为空" });
            if (string.IsNullOrWhiteSpace(item.DataType)) return BadRequest(new { error = "数据类型不能为空" });
            if (item.Value == null) return BadRequest(new { error = "写入值不能为空" });
        }

        var results = new List<PlcWriteResult>(request.Items.Count);
        foreach (var item in request.Items)
        {
            var value = ConvertJsonValue(item.Value, item.DataType)!;
            var result = await acquisitionService.WritePlcAsync(
                request.SourceCode,
                item.Address,
                value,
                item.DataType,
                ct);
            results.Add(result);
        }

        var allSuccess = results.All(r => r.IsSuccess);

        if (allSuccess) return Ok(results);

        return BadRequest(results);
    }

    private static object? ConvertJsonValue(object? value, string dataType)
    {
        if (value == null) return null;

        // 如果 value 是 JsonElement，需要提取实际值
        if (value is JsonElement jsonElement)
        {
            return dataType switch
            {
                "ushort" => jsonElement.GetUInt16(),
                "uint" => jsonElement.GetUInt32(),
                "ulong" => jsonElement.GetUInt64(),
                "short" => jsonElement.GetInt16(),
                "int" => jsonElement.GetInt32(),
                "long" => jsonElement.GetInt64(),
                "float" => jsonElement.GetSingle(),
                "double" => jsonElement.GetDouble(),
                "string" => jsonElement.GetString(),
                "bool" => jsonElement.GetBoolean(),
                _ => value
            };
        }

        return value;
    }
}

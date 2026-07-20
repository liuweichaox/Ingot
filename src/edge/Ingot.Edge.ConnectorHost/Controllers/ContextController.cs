using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Edge.ConnectorHost.Controllers;

/// <summary>
///     边缘业务上下文管理。事件发出时按规则 ContextKeys 获取快照。
/// </summary>
[ApiController]
[Route("api/v1/context")]
public sealed class ContextController(IEdgeContextStore contextStore) : ControllerBase
{
    [HttpGet("{subjectType}/{subjectId}")]
    public IActionResult Get(
        string subjectType,
        string subjectId,
        [FromQuery] string[] keys)
    {
        if (keys.Length == 0)
            return BadRequest(new { error = "至少提供一个 keys 查询参数。" });

        var subject = new ObjectRef(subjectType, subjectId);
        return Ok(new
        {
            subject,
            values = contextStore.Snapshot(subject, keys)
        });
    }

    [HttpPut("{subjectType}/{subjectId}")]
    public async Task<IActionResult> Set(
        string subjectType,
        string subjectId,
        [FromBody] Dictionary<string, string>? values,
        CancellationToken ct)
    {
        if (values is not { Count: > 0 })
            return BadRequest(new { error = "上下文值不能为空。" });

        var subject = new ObjectRef(subjectType, subjectId);
        foreach (var pair in values)
            await contextStore.SetAsync(subject, pair.Key, pair.Value, ct).ConfigureAwait(false);

        return Ok(new
        {
            subject,
            values = contextStore.Snapshot(subject, values.Keys.ToArray())
        });
    }
}

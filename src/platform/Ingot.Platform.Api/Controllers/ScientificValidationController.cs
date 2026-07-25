using System.Text.Json;
using Ingot.Contracts.ProcessImprovement;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessImprovement;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/dataset-quality-validations")]
public sealed class ScientificValidationController(
    IProcessImprovementStore store,
    ScientificValidationRunner runner,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new
           {
               data = await store.ListScientificValidationReportsAsync(ct).ConfigureAwait(false)
           });

    [HttpPost]
    [RequestSizeLimit(104_857_600)]
    public async Task<IActionResult> Run(
        IFormFile file,
        [FromForm] string manifestJson,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (file.Length <= 0 || file.Length > 100 * 1024 * 1024)
            return BadRequest(new { error = "科研验证数据文件必须在 1 字节到 100 MiB 之间。" });
        ScientificValidationDatasetManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ScientificValidationDatasetManifest>(
                manifestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            return BadRequest(new { error = $"科研验证清单 JSON 无效：{exception.Message}" });
        }
        if (manifest is null)
            return BadRequest(new { error = "科研验证清单不能为空。" });
        try
        {
            await using var content = file.OpenReadStream();
            return Ok(await runner.RunAsync(
                content,
                file.FileName,
                manifest,
                ResolveUserId()!,
                ct).ConfigureAwait(false));
        }
        catch (Exception exception) when (
            exception is ProcessImprovementRuleException or InvalidDataException)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

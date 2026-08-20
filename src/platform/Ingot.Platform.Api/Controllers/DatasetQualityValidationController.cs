using Ingot.Platform.Application.ResearchAssets;
using System.Text.Json;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/dataset-quality-validations")]
public sealed class DatasetQualityValidationController(
    IResearchAssetStore store,
    IDatasetQualityValidationService runner,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedResearchAssetRead() ??
           Ok(new
           {
               data = await store.ListDatasetQualityValidationReportsAsync(ct).ConfigureAwait(false)
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
            return InvalidRequest("数据集质量验证数据文件必须在 1 字节到 100 MiB 之间。");
        DatasetQualityValidationDatasetManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<DatasetQualityValidationDatasetManifest>(
                manifestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException exception)
        {
            return InvalidRequest($"数据集质量验证清单 JSON 无效：{exception.Message}");
        }
        if (manifest is null)
            return InvalidRequest("数据集质量验证清单不能为空。");
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
            exception is ResearchAssetRuleException or InvalidDataException)
        {
            return StateConflict(exception.Message);
        }
    }
}


using Ingot.Contracts.Manufacturing;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Manufacturing;
using Ingot.Platform.Application.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/tooling-component-types")]
public sealed class ToolingComponentTypesController(
    ManufacturingContextApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListComponentTypesAsync(ct).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ToolingComponentTypeDefinition? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        return Ok(await store.UpsertComponentTypeAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{componentTypeCode}")]
    public Task<IActionResult> Delete(string componentTypeCode, CancellationToken ct)
        => DeleteAsync(() => store.DeleteComponentTypeAsync(componentTypeCode.Trim().ToLowerInvariant(), ct));

    private async Task<IActionResult> DeleteAsync(Func<Task<bool>> action)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await action().ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }
}

[ApiController]
[Route("api/v1/tooling-types")]
public sealed class ToolingTypesController(
    ManufacturingContextApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListToolingTypesAsync(ct).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ToolingTypeDefinition? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        return await ExecuteAsync(() => store.CreateToolingTypeAsync(normalized!, ct)).ConfigureAwait(false);
    }

    [HttpDelete("{toolingTypeCode}/{version:int}")]
    public async Task<IActionResult> Delete(string toolingTypeCode, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return await store.DeleteToolingTypeAsync(toolingTypeCode.Trim().ToLowerInvariant(), version, ct)
                    .ConfigureAwait(false)
                ? NoContent()
                : ResourceNotFound();
        }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action().ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }
}

[ApiController]
[Route("api/v1/tooling-components")]
public sealed class ToolingComponentsController(
    ManufacturingContextApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? componentTypeCode, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new
        {
            data = await store.ListComponentsAsync(componentTypeCode, ct).ConfigureAwait(false)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ToolingComponent? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        try { return Ok(await store.UpsertComponentAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpDelete("{componentId}")]
    public async Task<IActionResult> Delete(string componentId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteComponentAsync(componentId.Trim(), ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }
}

[ApiController]
[Route("api/v1/tooling-assemblies")]
public sealed class ToolingAssembliesController(
    ManufacturingContextApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListAssembliesAsync(ct).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ToolingAssembly? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        try { return Ok(await store.UpsertAssemblyAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpDelete("{toolingAssemblyId}")]
    public async Task<IActionResult> Delete(string toolingAssemblyId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteAssemblyAsync(toolingAssemblyId.Trim(), ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpGet("revisions")]
    public async Task<IActionResult> ListAllRevisions(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListAssemblyRevisionsAsync(null, ct).ConfigureAwait(false) });
    }

    [HttpGet("{toolingAssemblyId}/revisions")]
    public async Task<IActionResult> ListRevisions(string toolingAssemblyId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new
        {
            data = await store.ListAssemblyRevisionsAsync(toolingAssemblyId, ct).ConfigureAwait(false)
        });
    }

    [HttpPost("{toolingAssemblyId}/revisions")]
    public async Task<IActionResult> CreateRevision(
        string toolingAssemblyId,
        [FromBody] CreateToolingAssemblyRevisionRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var candidate = request is null
            ? null
            : new ToolingAssemblyRevision
            {
                ToolingAssemblyId = toolingAssemblyId.Trim(),
                ToolingTypeVersion = request.ToolingTypeVersion,
                Revision = 1,
                Members = request.Members,
                CreatedBy = ResolveUserId(),
                CreatedAt = DateTimeOffset.UtcNow
            };
        if (!ManufacturingContextValidator.TryValidate(candidate, out var normalized, out var error))
            return InvalidRequest(error);
        try { return Ok(await store.CreateAssemblyRevisionAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpDelete("revisions/{assemblyRevisionId:guid}")]
    public async Task<IActionResult> DeleteRevision(Guid assemblyRevisionId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteAssemblyRevisionAsync(assemblyRevisionId, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }
}

[ApiController]
[Route("api/v1/tooling-installations")]
public sealed class ToolingInstallationsController(
    ManufacturingContextApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? siteId,
        [FromQuery] string? equipmentId,
        [FromQuery] bool activeOnly,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: true, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        return Ok(new
        {
            data = await store.ListInstallationsAsync(authorizedSiteId, equipmentId, activeOnly, ct).ConfigureAwait(false)
        });
    }

    [HttpPost]
    public Task<IActionResult> Install([FromBody] ReplaceToolingInstallationRequest? request, CancellationToken ct)
        => Replace(request, ct);

    [HttpPost("~/api/v1/tooling-installations:replace")]
    public async Task<IActionResult> Replace([FromBody] ReplaceToolingInstallationRequest? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (request is null)
            return InvalidRequest("工装替换请求不能为空。");
        var siteFailure = ResolveSiteScope(request.SiteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        var candidate = new ToolingInstallation
        {
            SiteId = authorizedSiteId!,
            EquipmentId = request.EquipmentId,
            AssemblyRevisionId = request.AssemblyRevisionId,
            InstalledAt = request.InstalledAt,
            Source = request.Source,
            CommandId = request.CommandId,
            Actor = ResolveUserId()
        };
        if (!ManufacturingContextValidator.TryValidate(candidate, out var normalized, out var error))
            return InvalidRequest(error);
        try { return Ok(await store.ReplaceInstallationAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpPost("{installationId:guid}:remove")]
    public async Task<IActionResult> Remove(
        Guid installationId,
        [FromBody] CloseIntervalRequest? request,
        [FromQuery] string? siteId,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        try
        {
            var item = await store.RemoveInstallationAsync(
                authorizedSiteId!,
                installationId,
                request?.At ?? DateTimeOffset.UtcNow,
                ResolveUserId(),
                ct).ConfigureAwait(false);
            return item is null ? ResourceNotFound() : Ok(item);
        }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpDelete("{installationId:guid}")]
    public async Task<IActionResult> Delete(Guid installationId, [FromQuery] string? siteId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        try { return await store.DeleteInstallationAsync(authorizedSiteId!, installationId, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    private IActionResult? ResolveSiteScope(string? requestedSiteId, bool allowAllForAdministrator, out string? siteId)
    {
        var failure = PlatformSiteScope.Resolve(ResolveIdentity()!, requestedSiteId, allowAllForAdministrator, out siteId);
        return failure switch
        {
            SiteScopeFailure.Forbidden => AuthorizationDenied("当前身份无权访问该站点。", ("siteId", requestedSiteId)),
            SiteScopeFailure.Missing => InvalidRequest("必须指定当前身份有权访问的 siteId。"),
            _ => null
        };
    }
}

[ApiController]
[Route("api/v1/production-contexts")]
public sealed class ProductionContextsController(
    ManufacturingContextApplication store,
    ProcessConfigurationApplication processConfigurations,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? siteId,
        [FromQuery] string? equipmentId,
        [FromQuery] bool activeOnly,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: true, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        return Ok(new
        {
            data = await store.ListProductionContextsAsync(authorizedSiteId, equipmentId, activeOnly, ct).ConfigureAwait(false)
        });
    }

    [HttpGet("current/{equipmentId}")]
    public async Task<IActionResult> Current(string equipmentId, [FromQuery] string? siteId, [FromQuery] DateTimeOffset? at, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        var item = await store.ResolveAsync(authorizedSiteId!, equipmentId, at ?? DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return item is null ? ResourceNotFound() : Ok(item);
    }

    [HttpPost]
    public Task<IActionResult> Start([FromBody] ReplaceProductionContextRequest? request, CancellationToken ct)
        => Replace(request, ct);

    [HttpPost("~/api/v1/production-contexts:replace")]
    public async Task<IActionResult> Replace([FromBody] ReplaceProductionContextRequest? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (request is null)
            return InvalidRequest("生产切换请求不能为空。");
        var siteFailure = ResolveSiteScope(request.SiteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        var candidate = new ProductionContext
        {
            SiteId = authorizedSiteId!,
            EquipmentId = request.EquipmentId,
            ProductFamilyCode = request.ProductFamilyCode,
            ProductCode = request.ProductCode,
            ProcessSpecificationId = request.ProcessSpecificationId,
            ProcessSpecificationVersion = request.ProcessSpecificationVersion,
            ToolingInstallationId = request.ToolingInstallationId,
            ValidFrom = request.ValidFrom,
            Source = request.Source,
            CommandId = request.CommandId,
            ExternalOrderRef = request.ExternalOrderRef,
            ExternalBatchRef = request.ExternalBatchRef,
            MaterialLotRef = request.MaterialLotRef,
            MaterialSpecification = request.MaterialSpecification,
            MaintenanceStatus = request.MaintenanceStatus,
            CalibrationStatus = request.CalibrationStatus,
            CalibrationRef = request.CalibrationRef,
            CalibrationValidUntil = request.CalibrationValidUntil,
            Actor = ResolveUserId()
        };
        if (!ManufacturingContextValidator.TryValidate(candidate, out var normalized, out var error))
            return InvalidRequest(error);
        if (!int.TryParse(normalized!.ProcessSpecificationVersion, out var processSpecificationVersion) || processSpecificationVersion < 1)
            return InvalidRequest("ProcessSpecificationVersion 必须是已发布工艺规范的正整数版本。");
        var processSpecification = await processConfigurations.GetProcessSpecificationAsync(
            normalized.ProcessSpecificationId.Trim().ToLowerInvariant(), processSpecificationVersion, ct).ConfigureAwait(false);
        if (processSpecification is null || processSpecification.Status != ConfigurationStatuses.Published)
            return InvalidRequest("生产上下文必须引用已发布的工艺规范版本。");
        var selectorContext = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product_family_code"] = normalized.ProductFamilyCode,
            ["product_code"] = normalized.ProductCode,
            ["equipment_id"] = normalized.EquipmentId
        };
        if (!ProcessAnalysisResolver.MatchesSelector(processSpecification.ContextSelector, selectorContext))
            return InvalidRequest("工艺规范的适用条件与当前产品或设备不匹配。");
        try { return Ok(await store.ReplaceProductionContextAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpPost("{contextId:guid}:close")]
    public async Task<IActionResult> Close(
        Guid contextId,
        [FromBody] CloseIntervalRequest? request,
        [FromQuery] string? siteId,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        try
        {
            var item = await store.CloseProductionContextAsync(
                authorizedSiteId!,
                contextId,
                request?.At ?? DateTimeOffset.UtcNow,
                ResolveUserId(),
                ct).ConfigureAwait(false);
            return item is null ? ResourceNotFound() : Ok(item);
        }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    [HttpDelete("{contextId:guid}")]
    public async Task<IActionResult> Delete(Guid contextId, [FromQuery] string? siteId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var siteFailure = ResolveSiteScope(siteId, allowAllForAdministrator: false, out var authorizedSiteId);
        if (siteFailure is not null)
            return siteFailure;
        try { return await store.DeleteProductionContextAsync(authorizedSiteId!, contextId, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound(); }
        catch (InvalidOperationException ex) { return StateConflict(ex.Message); }
    }

    private IActionResult? ResolveSiteScope(string? requestedSiteId, bool allowAllForAdministrator, out string? siteId)
    {
        var failure = PlatformSiteScope.Resolve(ResolveIdentity()!, requestedSiteId, allowAllForAdministrator, out siteId);
        return failure switch
        {
            SiteScopeFailure.Forbidden => AuthorizationDenied("当前身份无权访问该站点。", ("siteId", requestedSiteId)),
            SiteScopeFailure.Missing => InvalidRequest("必须指定当前身份有权访问的 siteId。"),
            _ => null
        };
    }
}

public sealed record CloseIntervalRequest(DateTimeOffset? At);

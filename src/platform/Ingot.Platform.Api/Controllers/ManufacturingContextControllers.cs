using Ingot.Contracts.Manufacturing;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/tooling-component-types")]
public sealed class ToolingComponentTypesController(
    IManufacturingContextStore store,
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
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingComponentTypeDefinition? normalized, out var error))
            return BadRequest(new { error });
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
        try { return await action().ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

[ApiController]
[Route("api/v1/tooling-types")]
public sealed class ToolingTypesController(
    IManufacturingContextStore store,
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
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingTypeDefinition? normalized, out var error))
            return BadRequest(new { error });
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
                : NotFound();
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        try { return Ok(await action().ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

[ApiController]
[Route("api/v1/tooling-components")]
public sealed class ToolingComponentsController(
    IManufacturingContextStore store,
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
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingComponent? normalized, out var error))
            return BadRequest(new { error });
        try { return Ok(await store.UpsertComponentAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("{componentId}")]
    public async Task<IActionResult> Delete(string componentId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteComponentAsync(componentId.Trim(), ct).ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

[ApiController]
[Route("api/v1/tooling-assemblies")]
public sealed class ToolingAssembliesController(
    IManufacturingContextStore store,
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
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingAssembly? normalized, out var error))
            return BadRequest(new { error });
        try { return Ok(await store.UpsertAssemblyAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("{moldId}")]
    public async Task<IActionResult> Delete(string moldId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteAssemblyAsync(moldId.Trim(), ct).ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpGet("revisions")]
    public async Task<IActionResult> ListAllRevisions(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListAssemblyRevisionsAsync(null, ct).ConfigureAwait(false) });
    }

    [HttpGet("{moldId}/revisions")]
    public async Task<IActionResult> ListRevisions(string moldId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new
        {
            data = await store.ListAssemblyRevisionsAsync(moldId, ct).ConfigureAwait(false)
        });
    }

    [HttpPost("{moldId}/revisions")]
    public async Task<IActionResult> CreateRevision(
        string moldId,
        [FromBody] ToolingAssemblyRevision? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        request = request is null ? null : request with { MoldId = moldId.Trim() };
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingAssemblyRevision? normalized, out var error))
            return BadRequest(new { error });
        try { return Ok(await store.CreateAssemblyRevisionAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("revisions/{assemblyRevisionId:guid}")]
    public async Task<IActionResult> DeleteRevision(Guid assemblyRevisionId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteAssemblyRevisionAsync(assemblyRevisionId, ct).ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

[ApiController]
[Route("api/v1/tooling-installations")]
public sealed class ToolingInstallationsController(
    IManufacturingContextStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? machineId,
        [FromQuery] bool activeOnly,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new
        {
            data = await store.ListInstallationsAsync(machineId, activeOnly, ct).ConfigureAwait(false)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Install([FromBody] ToolingInstallation? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out ToolingInstallation? normalized, out var error))
            return BadRequest(new { error });
        try { return Ok(await store.CreateInstallationAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("{installationId:guid}:remove")]
    public async Task<IActionResult> Remove(
        Guid installationId,
        [FromBody] CloseIntervalRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            var item = await store.RemoveInstallationAsync(
                installationId,
                request?.At ?? DateTimeOffset.UtcNow,
                request?.Actor,
                ct).ConfigureAwait(false);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("{installationId:guid}")]
    public async Task<IActionResult> Delete(Guid installationId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteInstallationAsync(installationId, ct).ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

[ApiController]
[Route("api/v1/production-contexts")]
public sealed class ProductionContextsController(
    IManufacturingContextStore store,
    IProcessConfigurationStore processConfigurations,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? machineId,
        [FromQuery] bool activeOnly,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new
        {
            data = await store.ListProductionContextsAsync(machineId, activeOnly, ct).ConfigureAwait(false)
        });
    }

    [HttpGet("current/{machineId}")]
    public async Task<IActionResult> Current(string machineId, [FromQuery] DateTimeOffset? at, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var item = await store.ResolveAsync(machineId, at ?? DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Start([FromBody] ProductionContext? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ManufacturingContextValidator.TryValidate(request, out ProductionContext? normalized, out var error))
            return BadRequest(new { error });
        if (!int.TryParse(normalized!.RecipeVersion, out var recipeVersion) || recipeVersion < 1)
            return BadRequest(new { error = "RecipeVersion 必须是已发布配方的正整数版本。" });
        var recipe = await processConfigurations.GetRecipeVersionAsync(
            normalized.RecipeId.Trim().ToLowerInvariant(), recipeVersion, ct).ConfigureAwait(false);
        if (recipe is null || recipe.Status != ConfigurationStatuses.Published)
            return BadRequest(new { error = "生产准备必须引用已发布的配方版本。" });
        var recipeContext = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product_series"] = normalized.ProductSeries,
            ["product_code"] = normalized.ProductCode,
            ["machine_id"] = normalized.MachineId
        };
        if (!ProcessAnalysisResolver.MatchesSelector(recipe.ContextSelector, recipeContext))
            return BadRequest(new { error = "配方的适用条件与当前产品或设备不匹配。" });
        try { return Ok(await store.StartProductionContextAsync(normalized!, ct).ConfigureAwait(false)); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpPost("{contextId:guid}:close")]
    public async Task<IActionResult> Close(
        Guid contextId,
        [FromBody] CloseIntervalRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            var item = await store.CloseProductionContextAsync(
                contextId,
                request?.At ?? DateTimeOffset.UtcNow,
                request?.Actor,
                ct).ConfigureAwait(false);
            return item is null ? NotFound() : Ok(item);
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("{contextId:guid}")]
    public async Task<IActionResult> Delete(Guid contextId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try { return await store.DeleteProductionContextAsync(contextId, ct).ConfigureAwait(false) ? NoContent() : NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}

public sealed record CloseIntervalRequest(DateTimeOffset? At, string? Actor);

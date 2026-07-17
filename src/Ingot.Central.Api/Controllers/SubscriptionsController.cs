using System.Text.RegularExpressions;
using Ingot.Central.Api.Webhooks;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/subscriptions")]
public sealed partial class SubscriptionsController(
    IWebhookSubscriptionStore store) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateWebhookSubscriptionRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "请求不能为空。" });
        if (!TryValidateEndpoint(request.Endpoint, out var endpoint, out var endpointError))
            return BadRequest(new { error = endpointError });
        if (request.StartAfterIngestId is < 0)
            return BadRequest(new { error = "StartAfterIngestId 不能小于 0。" });
        if (request.EventTypes is null)
            return BadRequest(new { error = "EventTypes 不能为空。" });
        if (request.EventTypes.Any(static value => !EventTypePattern().IsMatch(value)))
            return BadRequest(new { error = "EventTypes 包含非法事件类型。" });
        if (request.Context is null)
            return BadRequest(new { error = "Context 不能为空。" });
        if (request.Context.Keys.Any(static key => !ContextKeyPattern().IsMatch(key)))
            return BadRequest(new { error = "Context 包含非法键。" });

        var created = await store.CreateAsync(
            request with
            {
                Endpoint = endpoint!.ToString(),
                Name = string.IsNullOrWhiteSpace(request.Name)
                    ? endpoint.Host
                    : request.Name.Trim()
            },
            ct).ConfigureAwait(false);
        var view = WebhookSubscriptionView.From(created);
        return CreatedAtAction(nameof(Get), new { subscriptionId = created.SubscriptionId }, view);
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var items = await store.ListAsync(ct).ConfigureAwait(false);
        return Ok(new
        {
            data = items.Select(WebhookSubscriptionView.From),
            count = items.Count
        });
    }

    [HttpGet("{subscriptionId:guid}")]
    public async Task<IActionResult> Get(Guid subscriptionId, CancellationToken ct)
    {
        var item = await store.GetAsync(subscriptionId, ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(WebhookSubscriptionView.From(item));
    }

    [HttpPut("{subscriptionId:guid}/enabled")]
    public async Task<IActionResult> SetEnabled(
        Guid subscriptionId,
        [FromBody] SetSubscriptionEnabledRequest? request,
        CancellationToken ct)
    {
        if (request is null)
            return BadRequest(new { error = "请求不能为空。" });
        return await store.SetEnabledAsync(subscriptionId, request.Enabled, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }

    [HttpDelete("{subscriptionId:guid}")]
    public async Task<IActionResult> Delete(Guid subscriptionId, CancellationToken ct)
        => await store.DeleteAsync(subscriptionId, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();

    private static bool TryValidateEndpoint(
        string? value,
        out Uri? endpoint,
        out string? error)
    {
        endpoint = null;
        error = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            error = "Endpoint 必须是绝对 HTTP/HTTPS URL。";
            return false;
        }
        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "Endpoint 不能在 URL 中包含用户名或密码。";
            return false;
        }

        endpoint = parsed;
        return true;
    }

    [GeneratedRegex("^[a-z0-9]+(?:[._-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();

    [GeneratedRegex("^[A-Za-z0-9_.-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex ContextKeyPattern();
}

public sealed record SetSubscriptionEnabledRequest(bool Enabled);

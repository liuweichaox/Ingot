namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed record CreateWebhookSubscriptionRequest
{
    public string Name { get; init; } = string.Empty;

    public required string Endpoint { get; init; }

    public IReadOnlyList<string> EventTypes { get; init; } = [];

    public string? SubjectType { get; init; }

    public string? SubjectId { get; init; }

    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();

    /// <summary>可选 HMAC-SHA256 密钥。响应和列表永不回显。</summary>
    public string? Secret { get; init; }

    /// <summary>缺省从订阅创建后的新事件开始；设为 0 可重放全部历史。</summary>
    public long? StartAfterIngestId { get; init; }
}

public sealed record WebhookSubscription
{
    public required Guid SubscriptionId { get; init; }
    public required string Name { get; init; }
    public required Uri Endpoint { get; init; }
    public IReadOnlyList<string> EventTypes { get; init; } = [];
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
    public string? Secret { get; init; }
    public long Cursor { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveFailures { get; init; }
}

public sealed record WebhookSubscriptionView
{
    public required Guid SubscriptionId { get; init; }
    public required string Name { get; init; }
    public required string Endpoint { get; init; }
    public IReadOnlyList<string> EventTypes { get; init; } = [];
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
    public bool HasSecret { get; init; }
    public long Cursor { get; init; }
    public bool Enabled { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastSuccessAt { get; init; }
    public string? LastError { get; init; }
    public int ConsecutiveFailures { get; init; }

    public static WebhookSubscriptionView From(WebhookSubscription subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        Name = subscription.Name,
        Endpoint = subscription.Endpoint.ToString(),
        EventTypes = subscription.EventTypes,
        SubjectType = subscription.SubjectType,
        SubjectId = subscription.SubjectId,
        Context = subscription.Context,
        HasSecret = !string.IsNullOrEmpty(subscription.Secret),
        Cursor = subscription.Cursor,
        Enabled = subscription.Enabled,
        CreatedAt = subscription.CreatedAt,
        LastSuccessAt = subscription.LastSuccessAt,
        LastError = subscription.LastError,
        ConsecutiveFailures = subscription.ConsecutiveFailures
    };
}

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed partial class ResearchRollbackDrillService(IProcessResearchStore store)
{
    public async Task<ResearchRollbackDrill> RecordAsync(
        Guid projectId,
        ResearchRollbackDrillRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is not (ResearchProjectStatuses.Active or ResearchProjectStatuses.Validating))
            throw new ProcessResearchRuleException("停止与回退演练只能记录在 active 或 validating 项目中。");
        var conductedAt = request.ConductedAt == default ? DateTimeOffset.UtcNow : request.ConductedAt;
        if (conductedAt > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ProcessResearchRuleException("演练时间不能晚于当前时间。");
        var expected = NormalizeActions(request.ExpectedActions, "预期动作");
        var observed = NormalizeActions(request.ObservedActions, "实际动作");
        var evidenceHash = Required(request.EvidenceContentHash, "证据内容哈希", 64)
            .ToLowerInvariant();
        if (!Sha256Pattern().IsMatch(evidenceHash))
            throw new ProcessResearchRuleException("证据内容哈希必须是 64 位 SHA-256 十六进制字符串。");
        var actor = Required(userId, "演练人", 240).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        var body = new
        {
            ProjectId = projectId,
            ProjectRevision = project.Revision,
            Name = Required(request.Name, "演练名称", 240),
            Scenario = Required(request.Scenario, "演练场景", 4000),
            StopTrigger = Required(request.StopTrigger, "停止触发条件", 4000),
            RollbackTarget = Required(request.RollbackTarget, "回退目标", 4000),
            ExpectedActions = expected,
            ObservedActions = observed,
            request.Passed,
            EvidenceReference = Required(request.EvidenceReference, "演练证据引用", 1000),
            EvidenceContentHash = evidenceHash,
            ConductedBy = actor,
            ConductedAt = conductedAt
        };
        var drill = new ResearchRollbackDrill
        {
            DrillId = Guid.CreateVersion7(),
            ProjectId = projectId,
            ProjectRevision = project.Revision,
            Name = body.Name,
            Scenario = body.Scenario,
            StopTrigger = body.StopTrigger,
            RollbackTarget = body.RollbackTarget,
            ExpectedActions = body.ExpectedActions,
            ObservedActions = body.ObservedActions,
            Passed = body.Passed,
            EvidenceReference = body.EvidenceReference,
            EvidenceContentHash = body.EvidenceContentHash,
            RecordHash = Hash(body),
            ConductedBy = actor,
            ConductedAt = conductedAt,
            RecordedAt = now
        };
        var saved = await store.CreateRollbackDrillAsync(drill, ct).ConfigureAwait(false);
        await AuditAsync(saved, "recorded", actor, ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ResearchRollbackDrill> ReviewAsync(
        Guid drillId,
        string userId,
        CancellationToken ct = default)
    {
        var drill = await store.GetRollbackDrillAsync(drillId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("停止与回退演练不存在。");
        if (drill.Status == ResearchRollbackDrillStatuses.Reviewed)
            return drill;
        var actor = Required(userId, "复核人", 240).ToLowerInvariant();
        if (string.Equals(drill.ConductedBy, actor, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("演练执行人不能复核自己的停止与回退演练。");
        var reviewed = drill with
        {
            Status = ResearchRollbackDrillStatuses.Reviewed,
            ReviewedBy = actor,
            ReviewedAt = DateTimeOffset.UtcNow
        };
        var saved = await store.ReviewRollbackDrillAsync(reviewed, ct).ConfigureAwait(false);
        await AuditAsync(saved, "reviewed", actor, ct).ConfigureAwait(false);
        return saved;
    }

    private async Task AuditAsync(
        ResearchRollbackDrill drill,
        string action,
        string actor,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = drill.ProjectId,
            ResourceType = "rollback-drill",
            ResourceId = drill.DrillId.ToString(),
            Action = action,
            ToStatus = drill.Status,
            UserId = actor,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

    private static IReadOnlyList<string> NormalizeActions(IReadOnlyList<string> values, string field)
    {
        var result = values.Select(value => Required(value, field, 1000))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (result.Length == 0)
            throw new ProcessResearchRuleException($"{field}至少包含一项。 ");
        return result;
    }

    private static string Required(string? value, string field, int maximumLength)
    {
        var result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且最长 {maximumLength} 个字符。");
        return result;
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    [GeneratedRegex("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}

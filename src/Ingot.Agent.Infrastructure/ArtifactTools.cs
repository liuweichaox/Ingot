using System.Text.Json;
using Ingot.Contracts.Agents;

namespace Ingot.Agent.Infrastructure;

internal static class AgentArtifactToolSupport
{
    internal static AnalysisToolResult ArtifactResult(AgentArtifact artifact, string summary, params string[] limitations)
        => new()
        {
            Tool = artifact.Kind,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(artifact),
            Evidence =
            [
                new EvidenceRef
                {
                    Kind = "agent-artifact",
                    Id = artifact.ArtifactId,
                    Label = $"{artifact.Title} v{artifact.Version}",
                    Url = $"/api/v1/agent/artifacts/{artifact.ArtifactId}"
                }
            ],
            Limitations = limitations
        };

}

public sealed class DraftConnectorSpecificationTool(IAgentArtifactStore store) : IAnalysisTool
{
    private static readonly string[] RequiredConditions =
    [
        "protocol", "endpoint", "dataContract", "samplingPolicy", "successCriteria"
    ];

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "draft_connector_specification",
        Version = "1.0.0",
        Surface = ProductSurfaces.Agent,
        Purpose = RunPurposes.ConnectorCodeGeneration,
        Access = AgentToolAccess.ArtifactWrite,
        Description = "创建协议无关的采集连接器规格；信息不足时列出对话中必须补齐的条件，不连接设备。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                artifactId = new { type = "string", description = "Existing specification artifact to revise" },
                name = new { type = "string" },
                sourceCode = new { type = "string" },
                protocol = new { type = "string" },
                endpoint = new { type = "string" },
                authentication = new { type = "string" },
                dataContract = new { type = "string" },
                samplingPolicy = new { type = "string" },
                successCriteria = new { type = "string" },
                allowedNetworkTargets = new { type = "string", description = "Comma-separated explicit allowlist" }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var artifactId = call.Arguments.GetValueOrDefault("artifactId")?.Trim();
        ConnectorSpecification? existing = null;
        if (!string.IsNullOrWhiteSpace(artifactId))
        {
            var existingArtifact = await store.GetAsync(context.ActorId, artifactId, ct).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("找不到当前 Actor 可访问的连接器规格制品。");
            if (existingArtifact.Kind != AgentArtifactKinds.ConnectorSpecification)
                throw new ArgumentException("artifactId 不是连接器规格制品。");
            existing = JsonSerializer.Deserialize<ConnectorSpecification>(existingArtifact.Content,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        string? Merged(string key, string? oldValue = null)
            => call.Arguments.GetValueOrDefault(key)?.Trim() is { Length: > 0 } value ? value : oldValue;

        var values = RequiredConditions.ToDictionary(
            static key => key,
            key => Merged(key, key switch
            {
                "protocol" => existing?.Protocol,
                "endpoint" => existing?.Endpoint,
                "dataContract" => existing?.DataContract,
                "samplingPolicy" => existing?.SamplingPolicy,
                "successCriteria" => existing?.SuccessCriteria,
                _ => null
            }),
            StringComparer.Ordinal);
        var missing = values.Where(static pair => string.IsNullOrWhiteSpace(pair.Value))
            .Select(static pair => pair.Key)
            .ToArray();
        var specification = new ConnectorSpecification
        {
            Name = Merged("name", existing?.Name)
                   ?? throw new ArgumentException("首次创建规格时必须提供 name。"),
            SourceCode = Merged("sourceCode", existing?.SourceCode)
                         ?? throw new ArgumentException("首次创建规格时必须提供 sourceCode。"),
            Protocol = values["protocol"] ?? string.Empty,
            Endpoint = values["endpoint"] ?? string.Empty,
            Authentication = Merged("authentication", existing?.Authentication) ?? "none",
            DataContract = values["dataContract"] ?? string.Empty,
            SamplingPolicy = values["samplingPolicy"] ?? string.Empty,
            SuccessCriteria = values["successCriteria"] ?? string.Empty,
            AllowedNetworkTargets = Merged("allowedNetworkTargets",
                    existing is null ? null : string.Join(',', existing.AllowedNetworkTargets))
                ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [],
            MissingConditions = missing,
            Status = missing.Length == 0
                ? ConnectorSpecificationStatuses.ReadyForBuild
                : ConnectorSpecificationStatuses.NeedsInput
        };
        var content = JsonSerializer.Serialize(specification, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true
        });
        var metadata = JsonSerializer.SerializeToElement(new
        {
            specification.Status,
            specification.MissingConditions
        });
        var artifact = await store.SaveAsync(
            context.ActorId,
            context.RunId,
            AgentArtifactKinds.ConnectorSpecification,
            specification.Name,
            "json",
            content,
            metadata,
            ct).ConfigureAwait(false);
        var summary = missing.Length == 0
            ? $"连接器规格《{specification.Name}》已就绪，可以进入本地沙箱构建与测试。"
            : $"连接器规格《{specification.Name}》仍需补齐：{string.Join("、", missing)}。";
        return AgentArtifactToolSupport.ArtifactResult(
            artifact,
            summary,
            missing.Length == 0
                ? "连接器尚未构建、连接或发布；发布前必须通过测试并由操作者批准。"
                : "信息不足，Agent 不得尝试连接设备。 ");
    }
}

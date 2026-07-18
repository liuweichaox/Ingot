using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using static Ingot.Connector.Builder.ConnectorWorkspaceToolSupport;

namespace Ingot.Connector.Builder;

public sealed class CreateConnectorWorkspaceTool(
    IConnectorWorkspaceService workspaces,
    IAgentArtifactStore artifacts) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "create_connector_workspace",
        AgentToolAccess.WorkspaceWrite,
        "根据已完整的连接器规格创建受控源码工作区和可编译的 .NET 10 模板。",
        ["specificationArtifactId", "packageName"],
        new { specificationArtifactId = new { type = "string" }, packageName = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        var specificationArtifactId = Required(call, "specificationArtifactId");
        var artifact = await artifacts.GetAsync(context.ActorId, specificationArtifactId, ct).ConfigureAwait(false)
                       ?? throw new KeyNotFoundException("找不到当前操作者可访问的连接器规格。 ");
        if (artifact.Kind != AgentArtifactKinds.ConnectorSpecification)
            throw new ArgumentException("specificationArtifactId 不是连接器规格。 ");
        var specification = JsonSerializer.Deserialize<ConnectorSpecification>(artifact.Content, JsonOptions);
        if (specification?.Status != ConnectorSpecificationStatuses.ReadyForBuild)
            throw new InvalidOperationException("连接器规格尚未完整，不能创建工作区。 ");

        var workspace = await workspaces.CreateAsync(
            context.ActorId,
            context.RunId,
            specificationArtifactId,
            Required(call, "packageName"),
            ct).ConfigureAwait(false);
        return Result(Definition.Name, $"已创建连接器工作区 {workspace.WorkspaceId}。", workspace,
            WorkspaceEvidence(workspace));
    }
}

public sealed class ListConnectorWorkspaceFilesTool(IConnectorWorkspaceService workspaces) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "list_connector_workspace_files", AgentToolAccess.Read, "列出当前操作者的连接器工作区文件。",
        ["workspaceId"], new { workspaceId = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var id = Required(call, "workspaceId");
        var workspace = await workspaces.GetAsync(context.ActorId, id, ct).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("连接器工作区不存在。 ");
        var files = await workspaces.ListFilesAsync(context.ActorId, id, ct).ConfigureAwait(false);
        return Result(Definition.Name, $"工作区包含 {files.Count} 个可见文件。", new { workspace, files }, WorkspaceEvidence(workspace));
    }
}

public sealed class ReadConnectorWorkspaceFileTool(IConnectorWorkspaceService workspaces) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "read_connector_workspace_file", AgentToolAccess.Read, "读取连接器工作区内的单个 UTF-8 文本文件。",
        ["workspaceId", "path"], new { workspaceId = new { type = "string" }, path = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var id = Required(call, "workspaceId");
        var workspace = await workspaces.GetAsync(context.ActorId, id, ct).ConfigureAwait(false)
                        ?? throw new KeyNotFoundException("连接器工作区不存在。 ");
        var path = Required(call, "path");
        var content = await workspaces.ReadFileAsync(context.ActorId, id, path, ct).ConfigureAwait(false);
        return Result(Definition.Name, $"已读取 {path}。", new { workspaceId = id, path, content }, WorkspaceEvidence(workspace));
    }
}

public sealed class WriteConnectorWorkspaceFileTool(IConnectorWorkspaceService workspaces) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "write_connector_workspace_file", AgentToolAccess.WorkspaceWrite,
        "在受控连接器工作区中创建或完整替换一个 UTF-8 文本文件；禁止绝对路径、路径穿越和内部目录。",
        ["workspaceId", "path", "content"],
        new
        {
            workspaceId = new { type = "string" },
            path = new { type = "string" },
            content = new { type = "string" }
        });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var id = Required(call, "workspaceId");
        var path = Required(call, "path");
        var workspace = await workspaces.WriteFileAsync(context.ActorId, id, path, Required(call, "content"), ct)
            .ConfigureAwait(false);
        return Result(Definition.Name, $"已写入 {path}，工作区修订号 {workspace.Revision}。", workspace,
            WorkspaceEvidence(workspace));
    }
}

public sealed class BuildConnectorWorkspaceTool(IConnectorWorkspaceService workspaces) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "build_connector_workspace", AgentToolAccess.ProcessExecute,
        "使用固定的 dotnet build 命令在无网络受限容器中构建连接器；不接受任意 Shell 命令。",
        ["workspaceId"], new { workspaceId = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var (workspace, result) = await workspaces.BuildAsync(context.ActorId, Required(call, "workspaceId"), ct)
            .ConfigureAwait(false);
        return Result(Definition.Name, result.Succeeded ? "连接器构建成功。" : "连接器构建失败，需要根据输出修复代码。",
            new { workspace, result }, WorkspaceEvidence(workspace), result.Succeeded ? [] : ["构建未通过，禁止打包。"]);
    }
}

public sealed class TestConnectorWorkspaceTool(IConnectorWorkspaceService workspaces) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "test_connector_workspace", AgentToolAccess.ProcessExecute,
        "对已成功构建的连接器运行固定测试入口；失败结果返回 Agent 用于继续修复。",
        ["workspaceId"], new { workspaceId = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var (workspace, result) = await workspaces.TestAsync(context.ActorId, Required(call, "workspaceId"), ct)
            .ConfigureAwait(false);
        return Result(Definition.Name, result.Succeeded ? "连接器测试通过，等待操作者批准打包。" : "连接器测试失败，需要继续修复。",
            new { workspace, result }, WorkspaceEvidence(workspace),
            result.Succeeded ? ["测试通过不等于已批准打包。"] : ["测试未通过，禁止批准或打包。"]);
    }
}

public sealed class PackageConnectorWorkspaceTool(
    IConnectorWorkspaceService workspaces,
    IAgentArtifactStore artifacts) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = ToolDefinition(
        "package_connector_workspace", AgentToolAccess.ArtifactWrite,
        "仅在测试通过且操作者已显式批准后，将工作区打包为内容寻址制品。",
        ["workspaceId"], new { workspaceId = new { type = "string" } });

    public async Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
    {
        var (workspace, package) = await workspaces.PackageAsync(context.ActorId, Required(call, "workspaceId"), ct)
            .ConfigureAwait(false);
        var artifact = await artifacts.SaveAsync(
            context.ActorId,
            context.RunId,
            AgentArtifactKinds.ConnectorPackage,
            package.PackageName,
            "zip-reference",
            JsonSerializer.Serialize(package, JsonOptions),
            JsonSerializer.SerializeToElement(new { package.WorkspaceId, package.Sha256, package.SizeBytes }),
            ct).ConfigureAwait(false);
        return Result(Definition.Name, $"已生成连接器包 {package.PackageName}，SHA-256 {package.Sha256}。",
            new { workspace, package, artifactId = artifact.ArtifactId },
            [WorkspaceEvidence(workspace), new EvidenceRef
            {
                Kind = "agent-artifact", Id = artifact.ArtifactId, Label = artifact.Title,
                Url = $"/api/v1/agent/artifacts/{artifact.ArtifactId}"
            }]);
    }
}

internal static class ConnectorWorkspaceToolSupport
{
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static AnalysisToolDefinition ToolDefinition(
        string name,
        string access,
        string description,
        string[] required,
        object properties)
        => new()
        {
            Name = name,
            Version = "1.0.0",
            Surface = ProductSurfaces.Agent,
            Purpose = RunPurposes.ConnectorCodeGeneration,
            Access = access,
            Description = description,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                required,
                properties,
                additionalProperties = false
            })
        };

    internal static string Required(AnalysisToolCall call, string name)
        => call.Arguments.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"缺少必填参数: {name}");

    internal static EvidenceRef WorkspaceEvidence(ConnectorWorkspaceSnapshot workspace)
        => new()
        {
            Kind = "connector-workspace",
            Id = workspace.WorkspaceId,
            Label = workspace.PackageName,
            Url = $"/api/v1/connector-workspaces/{workspace.WorkspaceId}"
        };

    internal static AnalysisToolResult Result(
        string tool,
        string summary,
        object data,
        EvidenceRef evidence,
        IReadOnlyList<string>? limitations = null)
        => Result(tool, summary, data, [evidence], limitations);

    internal static AnalysisToolResult Result(
        string tool,
        string summary,
        object data,
        IReadOnlyList<EvidenceRef> evidence,
        IReadOnlyList<string>? limitations = null)
        => new()
        {
            Tool = tool,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(data, JsonOptions),
            Evidence = evidence,
            Limitations = limitations ?? []
        };
}

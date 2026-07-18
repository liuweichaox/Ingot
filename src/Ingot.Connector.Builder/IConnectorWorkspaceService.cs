using Ingot.Contracts.Agents;

namespace Ingot.Connector.Builder;

public interface IConnectorWorkspaceService
{
    Task<ConnectorWorkspaceSnapshot> CreateAsync(
        string actorId,
        string runId,
        string specificationArtifactId,
        string packageName,
        CancellationToken ct = default);

    Task<ConnectorWorkspaceSnapshot?> GetAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListFilesAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<string> ReadFileAsync(
        string actorId,
        string workspaceId,
        string relativePath,
        CancellationToken ct = default);

    Task<ConnectorWorkspaceSnapshot> WriteFileAsync(
        string actorId,
        string workspaceId,
        string relativePath,
        string content,
        CancellationToken ct = default);

    Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorCommandResult Result)> BuildAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorCommandResult Result)> TestAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<(ConnectorWorkspaceSnapshot Workspace, ConnectorPackageDescriptor Package)> PackageAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<ConnectorPackageDownload> OpenPackageAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);

    Task<ConnectorWorkspaceSnapshot> ApprovePackagingAsync(
        string actorId,
        string workspaceId,
        CancellationToken ct = default);
}

public sealed record ConnectorPackageDownload
{
    public required Stream Content { get; init; }

    public required string FileName { get; init; }

    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }
}

public interface IConnectorCommandRunner
{
    Task<ConnectorCommandResult> RunAsync(
        string workspacePath,
        string operation,
        CancellationToken ct = default);
}

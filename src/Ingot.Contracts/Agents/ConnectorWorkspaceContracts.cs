namespace Ingot.Contracts.Agents;

public static class ConnectorWorkflowStages
{
    public const string Intake = "intake";
    public const string AwaitingSpecificationApproval = "awaiting-specification-approval";
    public const string Coding = "coding";
    public const string Testing = "testing";
    public const string Fixing = "fixing";
    public const string AwaitingPackageApproval = "awaiting-package-approval";
    public const string Packaged = "packaged";
}

public static class ConnectorWorkspaceStatuses
{
    public const string Ready = "ready";
    public const string Building = "building";
    public const string BuildFailed = "build-failed";
    public const string Built = "built";
    public const string Testing = "testing";
    public const string TestFailed = "test-failed";
    public const string AwaitingPackageApproval = "awaiting-package-approval";
    public const string PackageApproved = "package-approved";
    public const string Packaged = "packaged";
}

public sealed record ConnectorWorkspaceSnapshot
{
    public required string WorkspaceId { get; init; }

    public required string ActorId { get; init; }

    public required string RunId { get; init; }

    public required string SpecificationArtifactId { get; init; }

    public required string PackageName { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public int Revision { get; init; }

    public ConnectorCommandResult? LastBuild { get; init; }

    public ConnectorCommandResult? LastTest { get; init; }

    public string? PackageSha256 { get; init; }

    public string? PackageApprovedBy { get; init; }

    public DateTimeOffset? PackageApprovedAt { get; init; }
}

public sealed record ConnectorCommandResult
{
    public required string Operation { get; init; }

    public required bool Succeeded { get; init; }

    public required int ExitCode { get; init; }

    public required long DurationMilliseconds { get; init; }

    public required string Output { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}

public sealed record ConnectorPackageDescriptor
{
    public required string WorkspaceId { get; init; }

    public required string PackageName { get; init; }

    public required string Sha256 { get; init; }

    public required long SizeBytes { get; init; }

    public required string RelativePath { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

namespace Ingot.Connector.Builder;

public sealed class ConnectorBuilderOptions
{
    public string WorkspaceRoot { get; set; } = "Data/connector-workspaces";

    public string ArtifactRoot { get; set; } = "Data/connector-packages";

    public string ContainerCommand { get; set; } = "docker";

    public string ContainerWorkspaceVolume { get; set; } = string.Empty;

    public string DotnetSdkImage { get; set; } = "mcr.microsoft.com/dotnet/sdk:10.0";

    public int CommandTimeoutSeconds { get; set; } = 120;

    public int MaxFileBytes { get; set; } = 512 * 1024;

    public int MaxWorkspaceFiles { get; set; } = 256;

    public long MaxWorkspaceBytes { get; set; } = 8 * 1024 * 1024;

    public int MaxOutputCharacters { get; set; } = 32_000;
}

namespace Ingot.Contracts.Acquisition;

/// <summary>Defines the only secret-reference form accepted by Edge acquisition.</summary>
public static class AcquisitionSecretReferencePolicy
{
    private const string EnvironmentPrefix = "env:";

    private static readonly string[] ProtectedEnvironmentPrefixes =
    [
        "ACQUISITION__",
        "ASPNETCORE_",
        "AUTHENTICATION__",
        "AWS_",
        "AZURE_",
        "CONNECTIONSTRINGS__",
        "CONNECTORHOST__",
        "DOTNET_",
        "EDGE__",
        "GOOGLE_",
        "OPENAI_",
        "OTEL_"
    ];

    public static bool TryParseEnvironmentReference(
        string? reference,
        out string environmentVariable,
        out string error)
    {
        environmentVariable = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(reference) ||
            !reference.StartsWith(EnvironmentPrefix, StringComparison.OrdinalIgnoreCase))
        {
            error = "采集凭据引用必须使用 env:变量名 格式。";
            return false;
        }

        var name = reference[EnvironmentPrefix.Length..].Trim();
        if (!IsValidEnvironmentVariableName(name))
        {
            error = "采集凭据引用包含无效的环境变量名。";
            return false;
        }
        if (IsProtectedEnvironmentVariable(name))
        {
            error = "采集凭据不能引用 Edge 运行时、平台、云或连接字符串命名空间。";
            return false;
        }

        environmentVariable = name;
        return true;
    }

    public static bool IsProtectedEnvironmentVariable(string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           ProtectedEnvironmentPrefixes.Any(prefix =>
               name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static bool IsValidEnvironmentVariableName(string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           name.Length <= 256 &&
           (char.IsLetter(name[0]) || name[0] == '_') &&
           name.All(static character => char.IsLetterOrDigit(character) || character == '_');
}

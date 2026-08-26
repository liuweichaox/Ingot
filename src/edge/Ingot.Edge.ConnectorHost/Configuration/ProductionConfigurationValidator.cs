// 在 Edge 启动前校验生产配置、凭据许可和采集出站边界。
namespace Ingot.Edge.ConnectorHost.Configuration;

using Ingot.Contracts.Acquisition;

public static class ProductionConfigurationValidator
{
    private const int MinimumSecretLength = 24;

    public static void Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        RequireSecret(configuration["ConnectorHost:IngestToken"], "ConnectorHost:IngestToken", errors);
        RequireSecret(configuration["ConnectorHost:LocalApiToken"], "ConnectorHost:LocalApiToken", errors);
        if (SecretsEqual(
                configuration["ConnectorHost:LocalApiToken"],
                configuration["ConnectorHost:IngestToken"]))
        {
            errors.Add("ConnectorHost:LocalApiToken must be distinct from ConnectorHost:IngestToken.");
        }

        var platformReportingEnabled = configuration.GetValue<bool>("Edge:EnablePlatformReporting", true);
        var eventShippingEnabled = configuration.GetValue<bool>("Edge:EnableEventShipping");
        if ((platformReportingEnabled || eventShippingEnabled) &&
            !IsStableId(configuration["Edge:SiteId"]))
        {
            errors.Add(
                "Edge:SiteId is required and must contain 1-128 letters, digits, dots, underscores, or hyphens.");
        }
        if (platformReportingEnabled)
        {
            if (string.IsNullOrWhiteSpace(configuration["Edge:EdgeId"]))
                errors.Add("Edge:EdgeId is required and must remain stable for the lifetime of the installed node.");

            var platformApiBaseUrl = configuration["Edge:PlatformApiBaseUrl"];
            if (!Uri.TryCreate(platformApiBaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add("Edge:PlatformApiBaseUrl must be an absolute HTTP or HTTPS URL.");
            }

            var publicBaseUrl = configuration["Edge:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(publicBaseUrl) &&
                (!Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri) ||
                 (publicUri.Scheme != Uri.UriSchemeHttp && publicUri.Scheme != Uri.UriSchemeHttps)))
            {
                errors.Add("Edge:PublicBaseUrl must be an absolute HTTP or HTTPS URL.");
            }
        }

        if (configuration.GetValue<bool>("Acquisition:AllowLocalFallbackWhenPlatformAvailable"))
        {
            errors.Add(
                "Acquisition:AllowLocalFallbackWhenPlatformAvailable must be false in production; " +
                "use the persisted last-known-good platform deployment instead.");
        }
        if (string.IsNullOrWhiteSpace(configuration["Acquisition:DeploymentCachePath"]))
            errors.Add("Acquisition:DeploymentCachePath is required in production.");
        ValidateAcquisitionSecurity(configuration, errors);
        var startupHealthTimeoutMs = configuration.GetValue(
            "Acquisition:StartupHealthTimeoutMs",
            30000);
        if (startupHealthTimeoutMs is < 1000 or > 300000)
            errors.Add("Acquisition:StartupHealthTimeoutMs must be between 1000 and 300000 milliseconds.");

        if (eventShippingEnabled)
        {
            RequireSecret(configuration["Edge:EventIngestToken"], "Edge:EventIngestToken", errors);
            if (SecretsEqual(
                    configuration["ConnectorHost:LocalApiToken"],
                    configuration["Edge:EventIngestToken"]))
            {
                errors.Add("ConnectorHost:LocalApiToken must be distinct from Edge:EventIngestToken.");
            }
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid production configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
    }

    private static void RequireSecret(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinimumSecretLength || IsPlaceholder(value))
        {
            errors.Add($"{key} must contain at least {MinimumSecretLength} characters and must not be a placeholder.");
        }
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("change-this-", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("verification-", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("replace-with-", StringComparison.OrdinalIgnoreCase);

    private static bool SecretsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        string.Equals(left, right, StringComparison.Ordinal);

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !char.IsLetterOrDigit(value[0]))
            return false;
        return value.All(static character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static void ValidateAcquisitionSecurity(
        IConfiguration configuration,
        ICollection<string> errors)
    {
        var secretNames = configuration
            .GetSection("Acquisition:Security:AllowedSecretEnvironmentVariables")
            .Get<string[]>() ?? [];
        foreach (var name in secretNames)
        {
            if (!AcquisitionSecretReferencePolicy.IsValidEnvironmentVariableName(name) ||
                AcquisitionSecretReferencePolicy.IsProtectedEnvironmentVariable(name))
            {
                errors.Add(
                    "Acquisition:Security:AllowedSecretEnvironmentVariables contains an invalid or protected name.");
                break;
            }
        }

        ValidateHostAllowlist(configuration, "AllowedHttpHosts", errors);
        ValidateHostAllowlist(configuration, "AllowedNetworkHosts", errors);
    }

    private static void ValidateHostAllowlist(
        IConfiguration configuration,
        string optionName,
        ICollection<string> errors)
    {
        var hosts = configuration.GetSection($"Acquisition:Security:{optionName}").Get<string[]>() ?? [];
        foreach (var host in hosts)
        {
            if (string.IsNullOrWhiteSpace(host) || host.Length > 253 ||
                host.Contains('/') || host.Contains(':') && !System.Net.IPAddress.TryParse(host, out _))
            {
                errors.Add($"Acquisition:Security:{optionName} must contain host names or IP literals without schemes or paths.");
                break;
            }
            if (System.Net.IPAddress.TryParse(host, out var address) && IsForbiddenAcquisitionAddress(address))
            {
                errors.Add(
                    $"Acquisition:Security:{optionName} cannot allow loopback, link-local, unspecified, or multicast addresses.");
                break;
            }
        }
    }

    private static bool IsForbiddenAcquisitionAddress(System.Net.IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (System.Net.IPAddress.IsLoopback(address) ||
            address.Equals(System.Net.IPAddress.Any) ||
            address.Equals(System.Net.IPAddress.IPv6Any) ||
            address.Equals(System.Net.IPAddress.None) ||
            address.Equals(System.Net.IPAddress.IPv6None) ||
            address.IsIPv6Multicast ||
            address.IsIPv6LinkLocal)
            return true;
        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254 || bytes[0] is >= 224 and <= 239;
    }

}

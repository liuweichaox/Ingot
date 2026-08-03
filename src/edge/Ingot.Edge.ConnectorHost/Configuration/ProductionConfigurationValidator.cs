namespace Ingot.Edge.ConnectorHost.Configuration;

public static class ProductionConfigurationValidator
{
    private const int MinimumSecretLength = 24;

    public static void Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        RequireSecret(configuration["ConnectorHost:IngestToken"], "ConnectorHost:IngestToken", errors);

        var platformReportingEnabled = configuration.GetValue<bool?>("Edge:EnablePlatformReporting")
            ?? configuration.GetValue<bool>("Edge:EnableCentralReporting");
        if (platformReportingEnabled)
        {
            if (string.IsNullOrWhiteSpace(configuration["Edge:EdgeId"]))
                errors.Add("Edge:EdgeId is required and must remain stable for the lifetime of the installed node.");

            var platformApiBaseUrl = configuration["Edge:PlatformApiBaseUrl"]
                ?? configuration["Edge:CentralApiBaseUrl"];
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
        var startupHealthTimeoutMs = configuration.GetValue(
            "Acquisition:StartupHealthTimeoutMs",
            30000);
        if (startupHealthTimeoutMs is < 1000 or > 300000)
            errors.Add("Acquisition:StartupHealthTimeoutMs must be between 1000 and 300000 milliseconds.");

        if (configuration.GetValue<bool>("Edge:EnableEventShipping"))
            RequireSecret(configuration["Edge:EventIngestToken"], "Edge:EventIngestToken", errors);

        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid production configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
    }

    private static void RequireSecret(string? value, string key, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinimumSecretLength)
        {
            errors.Add($"{key} must contain at least {MinimumSecretLength} characters.");
        }
    }
}

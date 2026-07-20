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
            var platformApiBaseUrl = configuration["Edge:PlatformApiBaseUrl"]
                ?? configuration["Edge:CentralApiBaseUrl"];
            if (!Uri.TryCreate(platformApiBaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                errors.Add("Edge:PlatformApiBaseUrl must be an absolute HTTP or HTTPS URL.");
            }
        }

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

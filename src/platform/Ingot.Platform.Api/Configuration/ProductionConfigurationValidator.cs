// 在 Platform API 启动前校验认证、密钥和生产依赖配置。
namespace Ingot.Platform.Api.Configuration;

public static class ProductionConfigurationValidator
{
    private const int MinimumSecretLength = 24;

    public static void Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Events")))
            errors.Add("ConnectionStrings:Events is required.");

        RequireProtectedMap(configuration, "EventIngest", "EdgeTokens", errors);
        RequireEdgeSiteBindings(configuration, errors);
        RequireEdgeDiagnosticsBindings(configuration, errors);

        var authMode = configuration["Authentication:Mode"] ?? "Local";
        if (!string.Equals(authMode, "Local", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(authMode, "Oidc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(authMode, "Disabled", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Authentication:Mode must be 'Local', 'Oidc', or 'Disabled'.");
        }
        if (string.Equals(authMode, "Disabled", StringComparison.OrdinalIgnoreCase) &&
            !IsInsecureDemoAllowed(configuration))
        {
            errors.Add(
                "Authentication:Mode 'Disabled' is forbidden in production unless " +
                "Authentication:AllowInsecureDemo=true is explicitly set for an isolated demo.");
        }
        var seedAdminPassword = configuration["Authentication:Local:SeedAdminPassword"];
        if (IsPlaceholder(seedAdminPassword))
            errors.Add("Authentication:Local:SeedAdminPassword must not use a placeholder value.");
        if (IsPlaceholder(configuration.GetConnectionString("Events")))
            errors.Add("ConnectionStrings:Events must not contain a placeholder password.");
        if (string.Equals(authMode, "Oidc", StringComparison.OrdinalIgnoreCase))
        {
            RequireValue(configuration, "Authentication:Authority", errors);
            RequireValue(configuration, "Authentication:Audience", errors);
            RequireValue(configuration, "Authentication:Oidc:ClientId", errors);
            RequireValue(configuration, "Authentication:Oidc:AllowedOrigins", errors);
            ValidateOidcConfiguration(configuration, errors);
        }

        RequireValue(configuration, "InspectionAttachments:ArchiveRootPath", errors);
        RequireValue(configuration, "ProcessKnowledge:ArchiveRootPath", errors);
        RequireValue(configuration, "DataProtection:KeysPath", errors);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(static origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("Cors:AllowedOrigins must contain absolute HTTP or HTTPS origins.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException($"Invalid production configuration:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
    }

    private static void RequireProtectedMap(
        IConfiguration configuration,
        string sectionName,
        string mapName,
        ICollection<string> errors)
    {
        if (!configuration.GetValue<bool>($"{sectionName}:RequireToken"))
        {
            errors.Add($"{sectionName}:RequireToken must be true.");
            return;
        }

        var entries = configuration.GetSection($"{sectionName}:{mapName}").GetChildren().ToArray();
        if (entries.Length == 0)
        {
            errors.Add($"{sectionName}:{mapName} must contain at least one credential.");
            return;
        }

        if (entries.Any(static entry => !IsStrongSecret(entry.Value)))
            errors.Add($"Every {sectionName}:{mapName} credential must contain at least {MinimumSecretLength} characters and must not be a placeholder.");
    }

    private static bool IsStrongSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= MinimumSecretLength &&
        !IsPlaceholder(value);

    private static void RequireEdgeSiteBindings(
        IConfiguration configuration,
        ICollection<string> errors)
    {
        var tokenEdges = configuration.GetSection("EventIngest:EdgeTokens")
            .GetChildren()
            .Select(static entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siteEntries = configuration.GetSection("EventIngest:EdgeSites").GetChildren().ToArray();
        if (siteEntries.Length == 0)
        {
            errors.Add("EventIngest:EdgeSites must bind every EdgeId to one SiteId.");
            return;
        }

        var siteEdges = siteEntries
            .Select(static entry => entry.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!tokenEdges.SetEquals(siteEdges))
            errors.Add("EventIngest:EdgeSites and EventIngest:EdgeTokens must contain the same EdgeId keys.");
        if (siteEntries.Any(static entry => !IsStableId(entry.Value)))
            errors.Add("Every EventIngest:EdgeSites value must be a valid SiteId.");
    }

    private static void RequireEdgeDiagnosticsBindings(
        IConfiguration configuration,
        ICollection<string> errors)
    {
        var ingestEdges = configuration.GetSection("EventIngest:EdgeTokens")
            .GetChildren()
            .ToDictionary(static entry => entry.Key, static entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        var diagnosticTokens = configuration.GetSection("EdgeDiagnostics:EdgeTokens")
            .GetChildren()
            .ToArray();
        var diagnosticUrls = configuration.GetSection("EdgeDiagnostics:EdgeBaseUrls")
            .GetChildren()
            .ToArray();
        if (!ingestEdges.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(diagnosticTokens.Select(static entry => entry.Key)) ||
            diagnosticTokens.Any(entry =>
                !IsStrongSecret(entry.Value) ||
                ingestEdges.TryGetValue(entry.Key, out var ingestToken) &&
                string.Equals(entry.Value, ingestToken, StringComparison.Ordinal)))
        {
            errors.Add(
                "EdgeDiagnostics:EdgeTokens must contain one strong, dedicated credential for every EventIngest EdgeId.");
        }
        if (!ingestEdges.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(diagnosticUrls.Select(static entry => entry.Key)) ||
            diagnosticUrls.Any(static entry => !IsSafeDiagnosticBaseUrl(entry.Value)))
        {
            errors.Add(
                "EdgeDiagnostics:EdgeBaseUrls must contain one trusted absolute HTTP or HTTPS URL for every EventIngest EdgeId.");
        }
    }

    private static bool IsSafeDiagnosticBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) &&
        string.IsNullOrEmpty(uri.Fragment);

    private static bool IsStableId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !char.IsLetterOrDigit(value[0]))
            return false;
        return value.All(static character =>
            char.IsLetterOrDigit(character) || character is '.' or '_' or '-');
    }

    private static bool IsInsecureDemoAllowed(IConfiguration configuration) =>
        configuration.GetValue<bool>("Authentication:AllowInsecureDemo") ||
        configuration.GetValue<bool>("INGOT_ALLOW_INSECURE_DEMO");

    private static void ValidateOidcConfiguration(
        IConfiguration configuration,
        ICollection<string> errors)
    {
        var authorityValue = configuration["Authentication:Authority"];
        if (!string.IsNullOrWhiteSpace(authorityValue) &&
            (!Uri.TryCreate(authorityValue, UriKind.Absolute, out var authority) ||
             authority.Scheme is not ("http" or "https") ||
             !string.IsNullOrEmpty(authority.UserInfo) ||
             !string.IsNullOrEmpty(authority.Query) ||
             !string.IsNullOrEmpty(authority.Fragment) ||
             configuration.GetValue("Authentication:RequireHttpsMetadata", true) &&
             authority.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(
                "Authentication:Authority must be a trusted absolute OIDC issuer URL; HTTPS is required when metadata HTTPS is enabled.");
        }

        var clientId = configuration["Authentication:Oidc:ClientId"];
        if (IsPlaceholder(clientId))
            errors.Add("Authentication:Oidc:ClientId must not use a placeholder value.");

        var scope = configuration["Authentication:Oidc:Scope"] ?? "openid profile";
        if (!scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("openid", StringComparer.Ordinal))
        {
            errors.Add("Authentication:Oidc:Scope must include 'openid'.");
        }

        var allowedOrigins = (configuration["Authentication:Oidc:AllowedOrigins"] ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var requireHttps = configuration.GetValue("Authentication:RequireHttpsMetadata", true);
        var parsedOrigins = new List<Uri>();
        foreach (var originValue in allowedOrigins)
        {
            if (!Uri.TryCreate(originValue, UriKind.Absolute, out var origin) ||
                origin.Scheme is not ("http" or "https") ||
                requireHttps && origin.Scheme != Uri.UriSchemeHttps ||
                !string.IsNullOrEmpty(origin.UserInfo) ||
                !string.IsNullOrEmpty(origin.Query) ||
                !string.IsNullOrEmpty(origin.Fragment) ||
                origin.AbsolutePath != "/")
            {
                errors.Add(
                    "Authentication:Oidc:AllowedOrigins must contain only absolute provider origins without wildcards, paths, user info, query, or fragment.");
                parsedOrigins.Clear();
                break;
            }
            parsedOrigins.Add(origin);
        }
        if (Uri.TryCreate(authorityValue, UriKind.Absolute, out var configuredAuthority) &&
            parsedOrigins.Count > 0 &&
            parsedOrigins.All(origin => Uri.Compare(
                origin,
                configuredAuthority,
                UriComponents.SchemeAndServer,
                UriFormat.Unescaped,
                StringComparison.OrdinalIgnoreCase) != 0))
        {
            errors.Add("Authentication:Oidc:AllowedOrigins must include the configured authority origin.");
        }
    }

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Contains("change-this-", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("verification-", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("replace-with-", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireValue(
        IConfiguration configuration,
        string key,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            errors.Add($"{key} is required when its product entry point is enabled in production.");
    }
}

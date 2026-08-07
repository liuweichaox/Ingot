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

        // 认证模式：Local（内置账户）、Oidc（外部 IdP）或 Disabled（本地演示固定 operator 身份）。
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
        }

        RequireValue(configuration, "InspectionAttachments:ArchiveRootPath", errors);
        RequireValue(configuration, "ProcessKnowledge:ArchiveRootPath", errors);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(static origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("Cors:AllowedOrigins must contain absolute HTTP or HTTPS origins.");
        }

        if (configuration.GetValue<bool>("Chat:Enabled"))
        {
            if (!string.Equals(configuration["Chat:Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase))
                errors.Add("Chat:Provider must be OpenAI when Chat is enabled in production.");
            RequireValue(configuration, "Chat:FastModel", errors);
            RequireValue(configuration, "Chat:ReasoningModel", errors);
            RequireValue(configuration, "OPENAI_API_KEY", errors);
            if (IsPlaceholder(configuration["OPENAI_API_KEY"]))
                errors.Add("OPENAI_API_KEY must not use a placeholder value.");
            var baseUrl = configuration["Chat:BaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrl))
            {
                try
                {
                    _ = Ingot.Agent.Providers.OpenAiCompatibleCapabilityProbe.BuildModelsUri(baseUrl);
                }
                catch (InvalidOperationException exception)
                {
                    errors.Add(exception.Message);
                }
            }
            RequireChatDataScopes(configuration, errors);
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

    private static bool IsInsecureDemoAllowed(IConfiguration configuration) =>
        configuration.GetValue<bool>("Authentication:AllowInsecureDemo") ||
        configuration.GetValue<bool>("INGOT_ALLOW_INSECURE_DEMO");

    private static bool IsPlaceholder(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = value.Trim();
        return normalized.Contains("change-this-", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("verification-", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("replace-with-", StringComparison.OrdinalIgnoreCase);
    }

    private static void RequireChatDataScopes(IConfiguration configuration, ICollection<string> errors)
    {
        var scopes = configuration.GetSection("ChatDataAccess:Users").GetChildren().ToArray();
        if (scopes.Length == 0)
        {
            errors.Add("ChatDataAccess:Users must contain at least one platform user scope.");
            return;
        }

        foreach (var scope in scopes)
        {
            var allowAll = scope.GetValue<bool>("AllowAll");
            var edgeIds = scope.GetSection("EdgeIds").Get<string[]>() ?? [];
            if (!allowAll && edgeIds.All(static edgeId => string.IsNullOrWhiteSpace(edgeId)))
                errors.Add($"ChatDataAccess:Users:{scope.Key} must allow all data or list at least one EdgeId.");
        }
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

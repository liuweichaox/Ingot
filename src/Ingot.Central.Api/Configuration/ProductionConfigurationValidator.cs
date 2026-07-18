namespace Ingot.Central.Api.Configuration;

public static class ProductionConfigurationValidator
{
    private const int MinimumSecretLength = 24;

    public static void Validate(IConfiguration configuration)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Events")))
            errors.Add("ConnectionStrings:Events is required.");

        RequireProtectedMap(configuration, "EventIngest", "EdgeTokens", errors);
        RequireProtectedMap(configuration, "InspectionSubmission", "ActorTokens", errors);

        var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length == 0 || origins.Any(static origin =>
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            errors.Add("Cors:AllowedOrigins must contain absolute HTTP or HTTPS origins.");
        }

        if (configuration.GetValue<bool>("Agent:Enabled"))
        {
            RequireProtectedMap(configuration, "Agent", "ActorTokens", errors);
            if (!string.Equals(configuration["Agent:Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase))
                errors.Add("Agent:Provider must be OpenAI when Agent is enabled in production.");
            RequireValue(configuration, "Agent:FastModel", errors);
            RequireValue(configuration, "Agent:ReasoningModel", errors);
            RequireValue(configuration, "OPENAI_API_KEY", errors);
            RequireValue(configuration, "ConnectorBuilder:ContainerWorkspaceVolume", errors);
            RequirePackagingApprovers(configuration, errors);
        }

        if (configuration.GetValue<bool>("Chat:Enabled"))
        {
            RequireProtectedMap(configuration, "Chat", "ActorTokens", errors);
            if (!string.Equals(configuration["Chat:Provider"], "OpenAI", StringComparison.OrdinalIgnoreCase))
                errors.Add("Chat:Provider must be OpenAI when Chat is enabled in production.");
            RequireValue(configuration, "Chat:FastModel", errors);
            RequireValue(configuration, "Chat:ReasoningModel", errors);
            RequireValue(configuration, "OPENAI_API_KEY", errors);
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
            errors.Add($"Every {sectionName}:{mapName} credential must contain at least {MinimumSecretLength} characters.");
    }

    private static bool IsStrongSecret(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length >= MinimumSecretLength;

    private static void RequireChatDataScopes(IConfiguration configuration, ICollection<string> errors)
    {
        var scopes = configuration.GetSection("ChatDataAccess:Actors").GetChildren().ToArray();
        foreach (var actor in configuration.GetSection("Chat:ActorTokens").GetChildren())
        {
            var scope = scopes.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, actor.Key, StringComparison.OrdinalIgnoreCase));
            if (scope is null)
            {
                errors.Add($"ChatDataAccess:Actors:{actor.Key} is required.");
                continue;
            }

            var allowAll = scope.GetValue<bool>("AllowAll");
            var edgeIds = scope.GetSection("EdgeIds").Get<string[]>() ?? [];
            if (!allowAll && edgeIds.All(static edgeId => string.IsNullOrWhiteSpace(edgeId)))
                errors.Add($"ChatDataAccess:Actors:{actor.Key} must allow all data or list at least one EdgeId.");
        }
    }

    private static void RequirePackagingApprovers(IConfiguration configuration, ICollection<string> errors)
    {
        var actors = configuration.GetSection("Agent:ActorTokens").GetChildren()
            .Select(static actor => actor.Key)
            .ToArray();
        var approvers = configuration.GetSection("Agent:PackagingApprovers").Get<string[]>() ?? [];
        if (approvers.Length == 0)
        {
            errors.Add("Agent:PackagingApprovers must contain at least one Actor.");
            return;
        }
        if (approvers.Any(approver => !actors.Contains(approver, StringComparer.OrdinalIgnoreCase)))
            errors.Add("Every Agent:PackagingApprovers entry must reference a configured Agent Actor.");
    }

    private static void RequireValue(
        IConfiguration configuration,
        string key,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(configuration[key]))
            errors.Add($"{key} is required when Agent is enabled in production.");
    }
}

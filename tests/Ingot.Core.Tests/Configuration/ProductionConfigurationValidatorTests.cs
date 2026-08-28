using Ingot.Agent;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using EdgeValidator = Ingot.Edge.ConnectorHost.Configuration.ProductionConfigurationValidator;
using PlatformValidator = Ingot.Platform.Api.Configuration.ProductionConfigurationValidator;

namespace Ingot.Core.Tests.Configuration;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Platform_RejectsMissingCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>());
        Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
    }

    [Fact]
    public void Platform_AcceptsCompleteConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["Authentication:Authority"] = "https://identity.example.com",
            ["Authentication:Audience"] = "ingot-platform",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_AcceptsPlatformIdentityDataScope()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["Authentication:Authority"] = "https://identity.example.com",
            ["Authentication:Audience"] = "ingot-platform",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["ChatDataAccess:Users:analyst:EdgeIds:0"] = "EDGE-001",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_AcceptsArbitraryCompatibleProviderLabelAndProtocol()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["Authentication:Authority"] = "https://identity.example.com",
            ["Authentication:Audience"] = "ingot-platform",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "FutureCompatibleVendor",
            ["Chat:Protocol"] = "ChatCompletions",
            ["Chat:BaseUrl"] = "https://models.example.com/api",
            ["Chat:FastModel"] = "vendor-fast",
            ["Chat:ReasoningModel"] = "vendor-reasoning",
            ["ChatDataAccess:Users:analyst:EdgeIds:0"] = "EDGE-001",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_RejectsUnsupportedProtocol()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "AnyCompatibleProvider",
            ["Chat:Protocol"] = "VendorSpecificProtocol",
            ["Chat:FastModel"] = "vendor-fast",
            ["Chat:ReasoningModel"] = "vendor-reasoning",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));

        Assert.Contains("Chat:Protocol must be Responses or ChatCompletions", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Chat_RejectsMissingPlatformUserScopes()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("ChatDataAccess:Users must contain at least one platform user scope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatDefaults_MatchPublishedLimits()
    {
        var chat = new Ingot.Agent.ChatOptions();

        Assert.Equal(8, chat.MaxToolCalls);
        Assert.Equal(60, chat.MaxRunSeconds);
    }

    [Fact]
    public void MechanismDraftGeneration_FailsClosedWithoutProviderConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["MechanismDraftGeneration:Enabled"] = "true",
            ["MechanismDraftGeneration:BaseUrl"] = "not-a-url",
            ["MechanismDraftGeneration:Model"] = "",
            ["INGOT_MECHANISM_DRAFT_API_KEY"] = "replace-with-local-service-token"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("MechanismDraftGeneration:Model", error.Message, StringComparison.Ordinal);
        Assert.Contains("MechanismDraftGeneration:BaseUrl", error.Message, StringComparison.Ordinal);
        Assert.Contains(
            "INGOT_MECHANISM_DRAFT_API_KEY must not use a placeholder",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsShortCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "short-connector-token",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "short-edge-token"
        });

        Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
    }

    [Fact]
    public void ConnectorHost_AcceptsCompleteConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["ConnectorHost:LocalApiToken"] = "local-api-token-with-at-least-24-characters",
            ["Edge:SiteId"] = "SITE-001",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json"
        });

        EdgeValidator.Validate(configuration);
    }

    [Fact]
    public void ConnectorHost_AcceptsDedicatedAcquisitionSecretAndHttpHostAllowlists()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["ConnectorHost:LocalApiToken"] = "local-api-token-with-at-least-24-characters",
            ["Edge:SiteId"] = "SITE-001",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json",
            ["Acquisition:Security:AllowedSecretEnvironmentVariables:0"] = "DEVICE_API_TOKEN",
            ["Acquisition:Security:AllowedHttpHosts:0"] = "device.example.internal",
            ["Acquisition:Security:AllowedNetworkHosts:0"] = "broker.example.internal"
        });

        EdgeValidator.Validate(configuration);
    }

    [Fact]
    public void ConnectorHost_RejectsForbiddenAcquisitionAllowlistLiteral()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["ConnectorHost:LocalApiToken"] = "local-api-token-with-at-least-24-characters",
            ["Edge:SiteId"] = "SITE-001",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json",
            ["Acquisition:Security:AllowedNetworkHosts:0"] = "169.254.169.254"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));

        Assert.Contains("cannot allow loopback, link-local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsProtectedAcquisitionRuntimeSecretAllowlistEntry()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["ConnectorHost:LocalApiToken"] = "local-api-token-with-at-least-24-characters",
            ["Edge:SiteId"] = "SITE-001",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json",
            ["Acquisition:Security:AllowedSecretEnvironmentVariables:0"] = "EDGE__EVENTINGESTTOKEN"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));

        Assert.Contains(
            "AllowedSecretEnvironmentVariables contains an invalid or protected name",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsCredentialReuseAcrossTrustBoundaries()
    {
        const string reusedToken = "one-token-reused-across-trust-boundaries";
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-registration-token-at-least-24-chars",
            ["ConnectorHost:LocalApiToken"] = reusedToken,
            ["Edge:SiteId"] = "SITE-001",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = reusedToken,
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("must be distinct", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsInvalidPublicBaseUrl()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:PublicBaseUrl"] = "connector-host:8001",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("Edge:PublicBaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_RejectsMissingUnifiedIdentityProvider()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["Authentication:Mode"] = "Oidc",     // OIDC 模式下才要求 Authority/Audience
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("Authentication:Authority", error.Message, StringComparison.Ordinal);
        Assert.Contains("Authentication:Oidc:ClientId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_AcceptsCompleteOidcClientConfiguration()
    {
        var configuration = Build(CompletePlatformOidcConfiguration());

        PlatformValidator.Validate(configuration);
    }

    [Theory]
    [InlineData("https://identity.example.com/tenant")]
    [InlineData("https://*.example.com")]
    [InlineData("https://identity.example.com;script-src *")]
    public void Platform_RejectsUnsafeOidcCspOrigins(string allowedOrigins)
    {
        var values = CompletePlatformOidcConfiguration();
        values["Authentication:Oidc:AllowedOrigins"] = allowedOrigins;

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(Build(values)));
        Assert.Contains("Authentication:Oidc:AllowedOrigins", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_AcceptsLocalAuthWithoutIdentityProvider()
    {
        // 内置本地认证（默认 Local 模式）不需要外部 OIDC —— 这是消除强制 OIDC 部署摩擦的关键。
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
            // 无 Authentication:Mode → 默认 Local；无 Authority/Audience 也应通过校验
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Platform_RejectsDisabledAuthUnlessExplicitlyAllowed()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["Authentication:Mode"] = "Disabled",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("Authentication:Mode 'Disabled'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_AcceptsDisabledAuthOnlyWhenExplicitlyAllowed()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot;Password=random-production-secret",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["Authentication:Mode"] = "Disabled",
            ["Authentication:AllowInsecureDemo"] = "true",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Platform_RejectsEdgeWithoutSiteBinding()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("EventIngest:EdgeSites", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_RejectsUnpinnedOrMissingEdgeDiagnosticsCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://user:secret@attacker.invalid/path?redirect=1",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("EdgeDiagnostics:EdgeBaseUrls", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_RejectsReusedEventIngestCredentialForDiagnostics()
    {
        const string reusedToken = "one-token-reused-across-trust-boundaries";
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = reusedToken,
            ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
            ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = reusedToken,
            ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("dedicated credential", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsMissingSiteIdentity()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("Edge:SiteId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectorHost_RejectsMalformedSiteIdentity()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:SiteId"] = "factory/site",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("Edge:SiteId", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_RejectsPlaceholderCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot;Password=change-this-database-password",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "change-this-edge-ingest-token",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("placeholder", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConnectorHost_RejectsSilentLocalFallbackInProduction()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json",
            ["Acquisition:AllowLocalFallbackWhenPlatformAvailable"] = "true"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("AllowLocalFallbackWhenPlatformAvailable must be false", error.Message);
    }

    [Fact]
    public void ConnectorHost_RejectsUnsafeAcquisitionStartupHealthTimeout()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:EdgeId"] = "EDGE-001",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters",
            ["Acquisition:DeploymentCachePath"] = "/data/acquisition-deployments.json",
            ["Acquisition:StartupHealthTimeoutMs"] = "500"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("Acquisition:StartupHealthTimeoutMs", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledChat_DoesNotConstructProviderClient()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Chat:Enabled"] = "false",
            ["Chat:Provider"] = "OpenAI"
        });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IAgentRunStore, UnavailableAgentRunStore>();
        services.AddLogging();
        Ingot.Agent.ServiceCollectionExtensions.AddIngotAgentCore(services, configuration);
        Ingot.Agent.Providers.ServiceCollectionExtensions.AddIngotAgentProviders(services, configuration);
        using var provider = services.BuildServiceProvider();

        var capabilities = provider.GetRequiredService<Ingot.Agent.IAgentRuntime>()
            .GetCapabilities(Ingot.Contracts.Agents.ProductEntryPoints.Chat);

        Assert.False(capabilities.Enabled);
        Assert.Empty(capabilities.Modes);
    }

    private static IConfiguration Build(IReadOnlyDictionary<string, string?> values)
    {
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["DataProtection:KeysPath"] = "/tmp/ingot-test-data-protection"
        };
        foreach (var pair in values)
            merged[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(merged).Build();
    }

    private static Dictionary<string, string?> CompletePlatformOidcConfiguration() => new()
    {
        ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
        ["EventIngest:RequireToken"] = "true",
        ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
        ["EventIngest:EdgeSites:EDGE-001"] = "SITE-001",
        ["EdgeDiagnostics:EdgeTokens:EDGE-001"] = "diagnostics-token-with-at-least-24-characters",
        ["EdgeDiagnostics:EdgeBaseUrls:EDGE-001"] = "http://edge-001:8001",
        ["Authentication:Mode"] = "Oidc",
        ["Authentication:Authority"] = "https://identity.example.com/tenant",
        ["Authentication:Audience"] = "ingot-platform-api",
        ["Authentication:Oidc:ClientId"] = "ingot-platform-spa",
        ["Authentication:Oidc:Scope"] = "openid profile ingot-platform-api",
        ["Authentication:Oidc:AllowedOrigins"] = "https://identity.example.com",
        ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
        ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
        ["Chat:Enabled"] = "false",
        ["Cors:AllowedOrigins:0"] = "https://ingot.example.com"
    };

    private sealed class UnavailableAgentRunStore : IAgentRunStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(string entryPoint, string userId, DateTimeOffset? before, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(string entryPoint, string userId, string conversationId, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteConversationAsync(string entryPoint, string userId, string conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentStreamEvent> AppendEventAsync(string runId, string type, object? data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(string runId, long afterSequence, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    }
}

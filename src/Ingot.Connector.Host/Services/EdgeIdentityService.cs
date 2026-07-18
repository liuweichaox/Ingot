using Ingot.Application.Abstractions;
using Ingot.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Connector.Host.Services;

/// <summary>
/// 负责解析 EdgeId（用于中心注册/心跳）。宿主实现 IEdgeIdentityProvider，供基础设施实现消费。
/// </summary>
public sealed class EdgeIdentityService : IEdgeIdentityProvider
{
    private readonly EdgeReportingOptions _options;
    private readonly ILogger<EdgeIdentityService> _logger;

    public EdgeIdentityService(IOptions<EdgeReportingOptions> options, ILogger<EdgeIdentityService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public string GetEdgeId()
    {
        if (string.IsNullOrWhiteSpace(_options.EdgeId))
        {
            const string message = "未配置 Edge:EdgeId（已要求必须通过配置文件提供）";
            _logger.LogError(message);
            throw new InvalidOperationException(message);
        }

        return _options.EdgeId.Trim();
    }
}


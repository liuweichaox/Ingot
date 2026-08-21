using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ingot.Edge.Application.Abstractions;

public interface ILogViewService
{

    Task<(List<LogEntry> Entries, int TotalCount)> GetLogsAsync(
        string? level = null,
        string? keyword = null,
        string? audience = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    List<string> GetAvailableLevels();
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Audience { get; set; } = LogAudiences.System;
    public string Category { get; set; } = "系统";
}

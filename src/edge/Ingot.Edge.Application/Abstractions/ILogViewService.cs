using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     日志查看服务接口
/// </summary>
public interface ILogViewService
{
    /// <summary>
    ///     获取日志条目列表
    /// </summary>
    /// <param name="level">日志级别过滤（可选）</param>
    /// <param name="keyword">关键词搜索（可选）</param>
    /// <param name="skip">跳过条数</param>
    /// <param name="take">获取条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>日志条目列表和总数</returns>
    Task<(List<LogEntry> Entries, int TotalCount)> GetLogsAsync(
        string? level = null,
        string? keyword = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     获取可用的日志级别列表
    /// </summary>
    /// <returns>日志级别列表</returns>
    List<string> GetAvailableLevels();
}

/// <summary>
///     日志条目
/// </summary>
public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Exception { get; set; }
    public string Source { get; set; } = string.Empty;
}
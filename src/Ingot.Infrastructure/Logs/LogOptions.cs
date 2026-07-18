namespace Ingot.Infrastructure.Logs;

/// <summary>
///     SQLite 日志的配置选项（原寄居于 SqliteLogViewService.cs，拆分为独立文件）。
/// </summary>
public class LogOptions
{
    /// <summary>
    ///     SQLite 数据库路径
    ///     支持相对路径（相对于应用程序目录）和绝对路径
    ///     默认值：Data/logs.db
    /// </summary>
    public string DatabasePath { get; set; } = "Data/logs.db";

    /// <summary>
    ///     SQLite log retention in days. Values less than or equal to 0 disable cleanup.
    /// </summary>
    public int RetentionDays { get; set; } = 30;
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ingot.Application.Abstractions;

/// <summary>
///     定义数据采集操作的接口规范。
/// </summary>
public interface IAcquisitionService : IDisposable
{
    /// <summary>
    ///     开始采集任务。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    Task StartCollectionTasks();

    /// <summary>
    ///     停止采集任务。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    Task StopCollectionTasks();

    /// <summary>
    ///     获取所有 PLC 连接信息。
    /// </summary>
    /// <returns>所有 PLC 连接信息的只读集合（按 PLC 编码排序）。</returns>
    IReadOnlyCollection<PlcConnectionStatus> GetPlcConnections();

    /// <summary>
    ///     写入 PLC 寄存器。
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="address">寄存器地址</param>
    /// <param name="value">写入值</param>
    /// <param name="dataType">数据类型</param>
    /// <param name="ct">可选的取消标记</param>
    /// <returns>表示写入结果的任务。</returns>
    Task<PlcWriteResult> WritePlcAsync(string sourceCode, string address, object value, string dataType,
        CancellationToken ct = default);
}

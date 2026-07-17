using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ingot.Application.Abstractions;

/// <summary>
///     数据存储服务。
/// </summary>
public interface IDataStorageService
{
    /// <summary>
    ///     批量保存数据消息。
    /// </summary>
    /// <param name="dataPoints">需要保存的数据消息列表</param>
    /// <returns>成功返回 true，失败返回 false。</returns>
    Task<bool> SaveBatchAsync(List<DataMessage> dataPoints);
}
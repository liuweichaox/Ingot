namespace Ingot.Domain.Models;

/// <summary>
///     通道采集模式。
/// </summary>
public enum AcquisitionMode
{
    /// <summary>
    ///     无条件持续采集。
    /// </summary>
    Always,

    /// <summary>
    ///     基于条件触发的采集。
    /// </summary>
    Conditional
}

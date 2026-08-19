namespace Ingot.Domain.Events;

/// <summary>
///     生产事件生成时实际生效的版本化配置。Kind 用于区分采集任务、分析计划等配置族，
///     Id 与 Version 共同指向不可变配置快照。
/// </summary>
public sealed record AppliedConfigurationRef(
    string Kind,
    string Id,
    int Version);

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>表示配方优化工作流的业务规则拒绝，供 API 映射为可处理的客户端错误。</summary>
public sealed class ProcessResearchRuleException(string message) : InvalidOperationException(message);

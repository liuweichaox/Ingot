using System.Security.Cryptography;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

public sealed class ResearchExperimentDesignService(
    IProcessResearchStore store,
    IProcessOptimizerClient optimizer)
{
    public async Task<ResearchExperimentDesignPreview> PreviewAsync(
        Guid projectId,
        ResearchExperimentDesignRequest request,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目不能生成新的实验设计。");
        var method = request.DesignMethod?.Trim().ToLowerInvariant();
        if (method is not (ResearchDesignMethods.FullFactorial or
            ResearchDesignMethods.FractionalFactorial or
            ResearchDesignMethods.ResponseSurface or
            ResearchDesignMethods.LatinHypercube))
            throw new ProcessResearchRuleException("请选择可生成的经典实验设计方法。");
        var requestedCodes = request.VariableCodes
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedCodes.Length == 0)
            throw new ProcessResearchRuleException("实验设计至少需要选择一个可控变量。");
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (requestedCodes.Any(code => !controls.ContainsKey(code)))
            throw new ProcessResearchRuleException("实验设计只能使用项目中已定义且带范围的可控变量。");
        var variables = requestedCodes.Select(code => controls[code]).ToArray();
        if (variables.Any(value => value.LowerLimit is null || value.UpperLimit is null ||
                                   value.UpperLimit <= value.LowerLimit))
            throw new ProcessResearchRuleException("每个实验变量都必须定义有效的上下限。");
        var seed = request.RandomizationSeed == 0
            ? RandomNumberGenerator.GetInt32(1, int.MaxValue)
            : request.RandomizationSeed;
        var response = await optimizer.DesignAsync(
            new OptimizerDesignCall
            {
                Method = method,
                Variables = variables.Select(value => new OptimizerVariableInput(
                    value.Code,
                    value.LowerLimit!.Value,
                    value.UpperLimit!.Value,
                    value.Unit)).ToArray(),
                Levels = request.Levels,
                Replicates = request.ReplicatesPerCondition,
                BlockCount = request.BlockCount,
                SampleCount = request.SampleCount,
                ResponseSurfaceFamily = request.ResponseSurfaceFamily,
                Seed = seed
            },
            ct).ConfigureAwait(false);
        return new ResearchExperimentDesignPreview
        {
            DesignMethod = method,
            RandomizationSeed = response.Seed,
            Warnings = response.Warnings,
            AliasStructure = response.AliasStructure,
            ResponseSurfaceFamily = response.ResponseSurfaceFamily,
            RunPlan = response.Runs.Select(run => new ExperimentRunPlan
            {
                ExecutionKey = run.ExecutionKey,
                Sequence = run.Sequence,
                BlockKey = run.BlockKey,
                ReplicateKey = run.ReplicateKey,
                Factors = variables.Select(variable => new ExperimentFactorSetting
                {
                    VariableCode = variable.Code,
                    Value = run.Params[variable.Code],
                    Unit = variable.Unit
                }).ToArray()
            }).ToArray()
        };
    }
}

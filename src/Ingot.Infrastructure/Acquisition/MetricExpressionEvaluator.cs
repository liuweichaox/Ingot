using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ingot.Domain.Models;
using Microsoft.Extensions.Logging;
using NCalc;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     指标表达式求值器。将指标配置中的 EvalExpression 应用到已读取的数据值。
/// </summary>
internal static class MetricExpressionEvaluator
{
    public static async Task EvaluateAsync(DataMessage dataMessage, IReadOnlyList<Metric>? metrics, ILogger logger)
    {
        if (metrics == null)
            return;

        foreach (var dataValue in dataMessage.DataValues.ToList())
        {
            var originalValue = dataValue.Value;
            if (!IsSupportedNumericType(originalValue))
                continue;

            var metric = metrics.SingleOrDefault(metric => metric.FieldName == dataValue.Key);
            if (metric == null || originalValue is null || string.IsNullOrWhiteSpace(metric.EvalExpression))
                continue;

            try
            {
                var expression = new AsyncExpression(metric.EvalExpression)
                {
                    Parameters = { ["value"] = originalValue }
                };
                var evaluatedValue = await expression.EvaluateAsync().ConfigureAwait(false);
                dataMessage.UpdateDataValue(dataValue.Key, evaluatedValue ?? 0, originalValue);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "表达式计算失败 [{Field}]: {Expression}", dataValue.Key, metric.EvalExpression);
            }
        }
    }

    private static bool IsSupportedNumericType(object? value)
    {
        return value is ushort or uint or ulong or short or int or long or float or double or decimal;
    }
}

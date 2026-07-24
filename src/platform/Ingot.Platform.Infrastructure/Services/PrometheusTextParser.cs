using System.Globalization;
using System.Text.RegularExpressions;

namespace Ingot.Platform.Infrastructure.Services;

/// <summary>
///     将 Prometheus 格式的指标文本解析为结构化字典。
/// </summary>
public static class PrometheusTextParser
{
    public static Dictionary<string, object> Parse(string prometheusText)
    {
        var result = new Dictionary<string, object>();
        var lines = prometheusText.Split('\n');

        string? currentMetric = null;
        string? currentType = null;
        string? currentHelp = null;
        var metricData = new List<Dictionary<string, object>>();

        void CommitCurrentMetric()
        {
            if (currentMetric is not null && metricData.Count > 0)
                result[currentMetric] = new { type = currentType, help = currentHelp, data = metricData };
            metricData = [];
        }

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (trimmed.StartsWith("# HELP"))
            {
                var match = Regex.Match(trimmed, @"# HELP\s+(\S+)\s+(.+)");
                if (match.Success)
                {
                    var metricName = match.Groups[1].Value;
                    if (!string.Equals(currentMetric, metricName, StringComparison.Ordinal))
                    {
                        CommitCurrentMetric();
                        currentMetric = metricName;
                        currentType = null;
                    }
                    currentHelp = match.Groups[2].Value;
                }

                continue;
            }

            if (trimmed.StartsWith("# TYPE"))
            {
                var match = Regex.Match(trimmed, @"# TYPE\s+(\S+)\s+(\S+)");
                if (match.Success)
                {
                    var metricName = match.Groups[1].Value;
                    if (!string.Equals(currentMetric, metricName, StringComparison.Ordinal))
                    {
                        CommitCurrentMetric();
                        currentMetric = metricName;
                        currentHelp = null;
                    }
                    currentType = match.Groups[2].Value;
                }

                continue;
            }

            if (trimmed.StartsWith('#'))
                continue;

            var metricMatch = Regex.Match(trimmed, @"^([^{]+)(?:\{([^}]+)\})?\s+(.+)$");
            if (!metricMatch.Success) continue;

            var labelsStr = metricMatch.Groups[2].Value;
            var value = metricMatch.Groups[3].Value;

            var dataPoint = new Dictionary<string, object>
            {
                ["value"] = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var numValue)
                    ? numValue
                    : value
            };

            if (!string.IsNullOrEmpty(labelsStr))
            {
                var labels = new Dictionary<string, string>();
                foreach (var label in labelsStr.Split(','))
                {
                    var parts = label.Split('=');
                    if (parts.Length == 2) labels[parts[0].Trim()] = parts[1].Trim().Trim('"');
                }

                dataPoint["labels"] = labels;
            }

            metricData.Add(dataPoint);
        }

        CommitCurrentMetric();

        return result;
    }
}

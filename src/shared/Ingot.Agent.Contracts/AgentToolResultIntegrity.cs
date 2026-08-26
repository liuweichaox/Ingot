// 计算 Agent 工具结果的规范内容哈希以检测持久化后篡改。
using System.Security.Cryptography;
using System.Text.Json;

namespace Ingot.Contracts.Agents;

public static class AgentToolResultIntegrity
{
    public static string ComputeContentHash(AgentToolResultSnapshot result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return ComputeContentHash(
            result.Tool,
            result.Version,
            result.Summary,
            result.Data,
            result.RelatedRecords,
            result.Limitations,
            result.Outcome);
    }

    public static string ComputeContentHash(
        string tool,
        string version,
        string summary,
        JsonElement data,
        IReadOnlyList<RelatedRecordRef> relatedRecords,
        IReadOnlyList<string> limitations,
        string outcome)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Tool = tool,
            Version = version,
            Summary = summary,
            Data = data,
            RelatedRecords = relatedRecords,
            Limitations = limitations,
            Outcome = outcome
        });
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    public static bool HasValidContentHash(AgentToolResultSnapshot result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(result.ContentHash) || result.ContentHash.Length != 64)
            return false;
        try
        {
            var actual = Convert.FromHexString(result.ContentHash);
            var expected = Convert.FromHexString(ComputeContentHash(result));
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }
}

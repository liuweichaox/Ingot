using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Ingot.Agent;

/// <summary>
///     统一的数字溯源工具。回答、假设与主张里的数字必须能在工具结果中找到
///     “归一化数值相等”的来源，而不是脆弱的子串匹配（子串匹配会让 "1" 命中 "100"）。
///     不同书写形式（1.50 / 1.5 / 1e0 / 150e-2）会归一到同一个键，
///     既堵住子串误报的漏洞，又减少纯格式差异导致的误杀。
/// </summary>
internal static partial class NumberGrounding
{
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_])[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    /// <summary>抽取来源文本中的全部数字并归一化为可比对的集合。</summary>
    public static IReadOnlySet<string> ExtractNormalized(string? source)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(source))
            return set;
        foreach (Match match in NumberPattern().Matches(source))
            set.Add(Normalize(match.Value));
        return set;
    }

    /// <summary>
    ///     校验文本里出现的每一个数字都能在来源集合中找到归一化相等项。无数字视为通过。
    /// </summary>
    public static bool IsGrounded(
        string? text,
        IReadOnlySet<string> sourceNumbers,
        out string? unsupportedRaw)
    {
        unsupportedRaw = null;
        if (string.IsNullOrEmpty(text))
            return true;
        foreach (Match match in NumberPattern().Matches(text))
        {
            if (!sourceNumbers.Contains(Normalize(match.Value)))
            {
                unsupportedRaw = match.Value;
                return false;
            }
        }

        return true;
    }

    /// <summary>把单个数字字面量归一化为 “尾数e指数” 形式，消除等值的书写差异。</summary>
    public static string Normalize(string value)
    {
        var span = value.AsSpan();
        var negative = false;
        if (span.Length > 0 && span[0] is '+' or '-')
        {
            negative = span[0] == '-';
            span = span[1..];
        }

        var exponentIndex = span.IndexOfAny('e', 'E');
        var mantissa = exponentIndex >= 0 ? span[..exponentIndex] : span;
        var exponent = exponentIndex >= 0
            ? BigInteger.Parse(span[(exponentIndex + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
            : BigInteger.Zero;
        var decimalIndex = mantissa.IndexOf('.');
        var fractionalDigits = decimalIndex >= 0 ? mantissa.Length - decimalIndex - 1 : 0;
        var digits = mantissa.ToString().Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0)
            return "0";

        var trailingZeros = digits.Length - digits.TrimEnd('0').Length;
        if (trailingZeros > 0)
        {
            digits = digits[..^trailingZeros];
            exponent += trailingZeros;
        }

        exponent -= fractionalDigits;
        return $"{(negative ? "-" : string.Empty)}{digits}e{exponent.ToString(CultureInfo.InvariantCulture)}";
    }
}

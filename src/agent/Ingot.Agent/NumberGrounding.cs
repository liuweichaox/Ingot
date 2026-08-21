using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Ingot.Agent;

internal static partial class NumberGrounding
{
    [GeneratedRegex(
        @"(?<![\p{L}\p{N}_])[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex NumberPattern();

    public static IReadOnlySet<string> ExtractNormalized(string? source)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(source))
            return set;
        foreach (Match match in NumberPattern().Matches(source))
            set.Add(Normalize(match.Value));
        return set;
    }

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

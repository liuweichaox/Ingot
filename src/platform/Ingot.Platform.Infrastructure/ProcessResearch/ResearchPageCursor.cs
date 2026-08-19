using System.Globalization;
using System.Text;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public static class ResearchPageCursor
{
    public static string Encode(DateTimeOffset timestamp, Guid id)
    {
        var raw = $"{timestamp.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture)}|{id:D}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out DateTimeOffset timestamp, out Guid id)
    {
        timestamp = default;
        id = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160)
            return false;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(normalized)).Split('|');
            if (parts.Length != 2 ||
                !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ||
                !Guid.TryParse(parts[1], out id))
                return false;
            timestamp = new DateTimeOffset(new DateTime(ticks, DateTimeKind.Utc));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

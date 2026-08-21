using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Ingot.Contracts.Acquisition;

public static class IngestionConfigurationCsv
{
    private const int MaximumCsvCharacters = 10 * 1024 * 1024;
    private const int MaximumCellCharacters = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SourceHeaders =
    [
        "dataSourceId", "version", "name", "status", "edgeId", "protocol",
        "sourceKey", "subjectType", "subjectId", "connectionJson"
    ];
    private static readonly string[] BindingHeaders =
    [
        "taskId", "version", "name", "status", "templateId", "templateVersion",
        "dataSourceId", "dataSourceVersion"
    ];

    public static string WriteDataSources(IEnumerable<DataSourceInstance> values)
        => Write(SourceHeaders, values.Select(value => new[]
        {
            value.DataSourceId,
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.Name,
            value.Status,
            value.EdgeId,
            value.Protocol,
            value.SourceKey,
            value.SubjectType,
            value.SubjectId,
            ConnectionJson(value)
        }));

    public static string WriteBindings(IEnumerable<IngestionTaskBinding> values)
        => Write(BindingHeaders, values.Select(value => new[]
        {
            value.TaskId,
            value.Version.ToString(CultureInfo.InvariantCulture),
            value.Name,
            value.Status,
            value.TemplateId,
            value.TemplateVersion.ToString(CultureInfo.InvariantCulture),
            value.DataSourceId,
            value.DataSourceVersion.ToString(CultureInfo.InvariantCulture)
        }));

    public static IReadOnlyList<DataSourceInstance> ReadDataSources(string text)
    {
        var rows = Read(text, SourceHeaders);
        return rows.Select((row, index) =>
        {
            try
            {
                var protocol = row[5].Trim().ToLowerInvariant();
                var connection = row[9];
                return new DataSourceInstance
                {
                    DataSourceId = row[0],
                    Version = PositiveInt(row[1], "version"),
                    Name = row[2],
                    Status = row[3],
                    EdgeId = row[4],
                    Protocol = protocol,
                    SourceKey = row[6],
                    SubjectType = row[7],
                    SubjectId = row[8],
                    HttpPolling = protocol == AcquisitionProtocols.HttpPolling
                        ? ParseConnection<HttpPollingConnection>(connection)
                        : null,
                    Mqtt = protocol == AcquisitionProtocols.Mqtt
                        ? ParseConnection<MqttConnection>(connection)
                        : null,
                    OpcUa = protocol == AcquisitionProtocols.OpcUa
                        ? ParseConnection<OpcUaConnection>(connection)
                        : null,
                    ModbusTcp = protocol == AcquisitionProtocols.ModbusTcp
                        ? ParseConnection<ModbusTcpConnection>(connection)
                        : null,
                    MelsecA1E = protocol == AcquisitionProtocols.MelsecA1E
                        ? ParseConnection<McA1EConnection>(connection)
                        : null
                };
            }
            catch (Exception exception) when (exception is JsonException or FormatException or InvalidDataException)
            {
                throw new InvalidDataException($"数据源 CSV 第 {index + 2} 行无效：{exception.Message}", exception);
            }
        }).ToArray();
    }

    public static IReadOnlyList<IngestionTaskBinding> ReadBindings(string text)
    {
        var rows = Read(text, BindingHeaders);
        return rows.Select((row, index) =>
        {
            try
            {
                return new IngestionTaskBinding
                {
                    TaskId = row[0],
                    Version = PositiveInt(row[1], "version"),
                    Name = row[2],
                    Status = row[3],
                    TemplateId = row[4],
                    TemplateVersion = PositiveInt(row[5], "templateVersion"),
                    DataSourceId = row[6],
                    DataSourceVersion = PositiveInt(row[7], "dataSourceVersion")
                };
            }
            catch (Exception exception) when (exception is FormatException or InvalidDataException)
            {
                throw new InvalidDataException($"任务绑定 CSV 第 {index + 2} 行无效：{exception.Message}", exception);
            }
        }).ToArray();
    }

    private static string ConnectionJson(DataSourceInstance value)
        => value.Protocol switch
        {
            AcquisitionProtocols.HttpPolling => JsonSerializer.Serialize(value.HttpPolling, JsonOptions),
            AcquisitionProtocols.Mqtt => JsonSerializer.Serialize(value.Mqtt, JsonOptions),
            AcquisitionProtocols.OpcUa => JsonSerializer.Serialize(value.OpcUa, JsonOptions),
            AcquisitionProtocols.ModbusTcp => JsonSerializer.Serialize(value.ModbusTcp, JsonOptions),
            AcquisitionProtocols.MelsecA1E => JsonSerializer.Serialize(value.MelsecA1E, JsonOptions),
            _ => "null"
        };

    private static T ParseConnection<T>(string value)
        => JsonSerializer.Deserialize<T>(value, JsonOptions)
           ?? throw new InvalidDataException("connectionJson 不能为空。");

    private static int PositiveInt(string value, string field)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidDataException($"{field} 必须是正整数。");

    private static string Write(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers));
        foreach (var row in rows)
            builder.AppendLine(string.Join(',', row.Select(Escape)));
        return builder.ToString();
    }

    private static string Escape(string? value)
    {
        var text = ProtectSpreadsheetCell(value ?? string.Empty);
        return text.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? text
            : $"\"{text.Replace("\"", "\"\"")}\"";
    }

    private static IReadOnlyList<string[]> Read(string text, IReadOnlyList<string> expectedHeaders)
    {
        var rows = ParseRows(text);
        if (rows.Count == 0)
            throw new InvalidDataException("CSV 不能为空。");
        var headers = rows[0].Select(static item => item.Trim().TrimStart('\uFEFF')).ToArray();
        if (!headers.SequenceEqual(expectedHeaders, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException($"CSV 表头必须是：{string.Join(',', expectedHeaders)}。");
        var result = rows.Skip(1).Where(static row => row.Any(static cell => !string.IsNullOrWhiteSpace(cell))).ToArray();
        var bad = result.FirstOrDefault(row => row.Length != expectedHeaders.Count);
        if (bad is not null)
            throw new InvalidDataException($"CSV 每行必须包含 {expectedHeaders.Count} 列。");
        return result.Select(static row => row.Select(UnprotectSpreadsheetCell).ToArray()).ToArray();
    }

    private static IReadOnlyList<string[]> ParseRows(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCsvCharacters)
            throw new InvalidDataException($"CSV 不能超过 {MaximumCsvCharacters} 个字符。");
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var quoteClosed = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else if (character == '"') { quoted = false; quoteClosed = true; }
                else field.Append(character);
                if (field.Length > MaximumCellCharacters)
                    throw new InvalidDataException($"CSV 单元格不能超过 {MaximumCellCharacters} 个字符。");
                continue;
            }
            if (quoteClosed && character is not (',' or '\r' or '\n'))
                throw new InvalidDataException("CSV 引号字段结束后只能出现分隔符或换行。 ");
            if (character == '"')
            {
                if (field.Length > 0 || quoteClosed)
                    throw new InvalidDataException("CSV 未加引号的字段中不能出现引号。");
                quoted = true;
                continue;
            }
            if (character == ',') { row.Add(field.ToString()); field.Clear(); quoteClosed = false; continue; }
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(field.ToString()); field.Clear(); quoteClosed = false; rows.Add(row.ToArray()); row.Clear();
                continue;
            }
            field.Append(character);
            if (field.Length > MaximumCellCharacters)
                throw new InvalidDataException($"CSV 单元格不能超过 {MaximumCellCharacters} 个字符。");
        }
        if (quoted) throw new InvalidDataException("CSV 存在未闭合的引号字段。");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    private static string ProtectSpreadsheetCell(string value)
        => value.Length > 0 &&
           (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ||
            value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' or '\t' or '\r')
            ? $"'{value}"
            : value;

    private static string UnprotectSpreadsheetCell(string value)
        => value.Length > 1 && value[0] == '\'' &&
           (value[1] is '=' or '+' or '-' or '@' or '\t' or '\r' ||
            value.Length > 2 && value[1] == '\'' && value[2] is '=' or '+' or '-' or '@' or '\t' or '\r')
            ? value[1..]
            : value;
}

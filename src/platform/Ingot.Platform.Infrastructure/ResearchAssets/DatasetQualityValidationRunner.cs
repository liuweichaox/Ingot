using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Ingot.Contracts.ResearchAssets;
using MatFileHandler;
using Microsoft.VisualBasic.FileIO;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class DatasetQualityValidationRunner(IResearchAssetStore store)
{
    public const string Version = "cross-industry-validation-v1";
    private const int MaximumRows = 1_000_000;

    public async Task<DatasetQualityValidationReport> RunAsync(
        Stream content,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        string userId,
        CancellationToken ct = default)
    {
        var report = await EvaluateAsync(
            content,
            fileName,
            manifest,
            userId,
            ct).ConfigureAwait(false);
        return await store.SaveDatasetQualityValidationReportAsync(report, ct).ConfigureAwait(false);
    }

    public static async Task<DatasetQualityValidationReport> EvaluateAsync(
        Stream content,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        string userId,
        CancellationToken ct = default)
    {
        ValidateManifest(manifest);
        await using var copy = new MemoryStream();
        await content.CopyToAsync(copy, ct).ConfigureAwait(false);
        var bytes = copy.ToArray();
        var sourceHash = Hash(bytes);
        var issues = new List<string>();
        if (!string.IsNullOrWhiteSpace(manifest.ExpectedSha256) &&
            !string.Equals(sourceHash, manifest.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            issues.Add("原始文件 SHA-256 与数据清单不一致。");
        var excludedSampleCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var dataQualityNotes = new List<string>();
        var rows = ReadRows(
            bytes,
            fileName,
            manifest,
            excludedSampleCounts,
            dataQualityNotes,
            ct);
        if (rows.Count < 10)
            issues.Add("有效数据行少于 10，不能形成数据集质量验证证据。");
        var requiredColumns = manifest.SignalColumns
            .Concat(manifest.OutcomeColumns)
            .Append(manifest.ProcessExecutionColumn)
            .Append(manifest.TimestampColumn)
            .Append(manifest.PhaseColumn)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var headers = rows.Count == 0
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : rows[0].Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var required in requiredColumns.Where(required => !headers.Contains(required!)))
            issues.Add($"缺少清单声明的列 {required}。");
        var signalProfiles = Profile(rows, manifest.SignalColumns);
        var outcomeProfiles = Profile(rows, manifest.OutcomeColumns);
        foreach (var profile in signalProfiles)
        {
            if (profile.NumericCount < Math.Max(1, rows.Count * manifest.MinimumSignalNumericCoverage))
                issues.Add($"信号列 {profile.Column} 的有效数值覆盖率低于清单阈值。");
        }
        foreach (var profile in outcomeProfiles)
        {
            if (profile.NumericCount < Math.Max(1, rows.Count * manifest.MinimumOutcomeNumericCoverage))
                issues.Add($"结果列 {profile.Column} 的有效数值覆盖率低于清单阈值。");
        }
        var chronologyViolations = CountChronologyViolations(rows, manifest);
        var maximumDifference = CompareStreamAndBatch(rows, manifest);
        if (maximumDifference > 1e-10)
            issues.Add($"流式与批式统计不一致，最大绝对差为 {maximumDifference:R}。");
        if (!manifest.IsMeasuredData)
            issues.Add("数据清单未声明为真实测量数据。");
        var report = new DatasetQualityValidationReport
        {
            ReportId = Guid.CreateVersion7(),
            DatasetId = NormalizeId(manifest.DatasetId),
            DatasetVersion = manifest.Version,
            Industry = manifest.Industry.Trim(),
            Process = manifest.Process.Trim(),
            Status = issues.Count == 0
                ? DatasetQualityValidationStatuses.Passed
                : DatasetQualityValidationStatuses.Rejected,
            ResearchClaimsAllowed = issues.Count == 0 && manifest.IsMeasuredData,
            SourceSha256 = sourceHash,
            ManifestSha256 = ManifestHash(manifest),
            RowCount = rows.Count,
            ProcessExecutionCount = CountProcessExecutions(rows, manifest.ProcessExecutionColumn),
            ChronologyViolationCount = chronologyViolations,
            StreamBatchMaximumDifference = maximumDifference,
            SignalProfiles = signalProfiles,
            OutcomeProfiles = outcomeProfiles,
            ExcludedSampleCounts = excludedSampleCounts,
            DataQualityNotes = dataQualityNotes,
            Issues = issues,
            RunnerVersion = Version,
            RunBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        return report;
    }

    private static List<IReadOnlyDictionary<string, string>> ReadRows(
        byte[] bytes,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        IDictionary<string, long> excludedSampleCounts,
        ICollection<string> dataQualityNotes,
        CancellationToken ct)
    {
        var extension = Path.GetExtension(fileName);
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            return ReadCsv(bytes, ct);
        if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase))
            return ReadWorkbook(bytes, manifest.SheetName, manifest.HeaderRowCount, ct);
        if (extension.Equals(".mat", StringComparison.OrdinalIgnoreCase))
            return ReadMat(bytes, manifest, excludedSampleCounts, dataQualityNotes, ct);
        throw new ResearchAssetRuleException("数据集质量验证当前只接收 CSV、XLSX、XLSM 或 MATLAB Level 5 MAT 原始数据。");
    }

    private static List<IReadOnlyDictionary<string, string>> ReadCsv(
        byte[] bytes,
        CancellationToken ct)
    {
        using var content = new MemoryStream(bytes);
        using var reader = new StreamReader(content, Encoding.UTF8, true);
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields() ??
                      throw new InvalidDataException("CSV 缺少表头。");
        EnsureUniqueHeaders(headers);
        var result = new List<IReadOnlyDictionary<string, string>>();
        while (!parser.EndOfData)
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= MaximumRows)
                throw new InvalidDataException($"数据行超过上限 {MaximumRows}。");
            var values = parser.ReadFields() ?? [];
            if (values.All(string.IsNullOrWhiteSpace))
                continue;
            result.Add(headers.Select((header, index) => new
                {
                    Header = header.Trim(),
                    Value = index < values.Length ? values[index].Trim() : ""
                })
                .ToDictionary(static item => item.Header, static item => item.Value,
                    StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    private static List<IReadOnlyDictionary<string, string>> ReadWorkbook(
        byte[] bytes,
        string? sheetName,
        int headerRowCount,
        CancellationToken ct)
    {
        using var content = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(content);
        var worksheet = string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheets.FirstOrDefault()
            : workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (worksheet is null)
            throw new InvalidDataException($"找不到工作表 {sheetName ?? "(first)"}。");
        var used = worksheet.RangeUsed() ??
                   throw new InvalidDataException("工作表没有有效数据。");
        var rows = used.RowsUsed().ToArray();
        if (headerRowCount <= 0 || headerRowCount >= rows.Length)
            throw new InvalidDataException("表头行数必须大于零且小于工作表有效行数。");
        var columnCount = used.ColumnCount();
        var headerLevels = new string[headerRowCount, columnCount];
        for (var headerRow = 0; headerRow < headerRowCount; headerRow++)
        {
            var carried = "";
            for (var column = 0; column < columnCount; column++)
            {
                var raw = rows[headerRow].Cell(column + 1).GetFormattedString().Trim();
                if (raw.Length > 0)
                    carried = raw;
                headerLevels[headerRow, column] = raw.Length > 0 ? raw : carried;
            }
        }
        var headers = Enumerable.Range(0, columnCount)
            .Select(column => string.Join(
                ".",
                Enumerable.Range(0, headerRowCount)
                    .Select(row => headerLevels[row, column])
                    .Where(static part => part.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        EnsureUniqueHeaders(headers);
        var result = new List<IReadOnlyDictionary<string, string>>();
        foreach (var row in rows.Skip(headerRowCount))
        {
            ct.ThrowIfCancellationRequested();
            if (result.Count >= MaximumRows)
                throw new InvalidDataException($"数据行超过上限 {MaximumRows}。");
            var values = row.Cells(1, columnCount)
                .Select(static cell => cell.GetFormattedString().Trim()).ToArray();
            if (values.All(string.IsNullOrWhiteSpace))
                continue;
            result.Add(headers.Select((header, index) => new
                {
                    Header = header,
                    Value = values[index]
                })
                .ToDictionary(static item => item.Header, static item => item.Value,
                    StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    private static void EnsureUniqueHeaders(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0 || headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
            throw new InvalidDataException("数据表头不能为空或重复。");
    }

    private static List<IReadOnlyDictionary<string, string>> ReadMat(
        byte[] bytes,
        DatasetQualityValidationDatasetManifest manifest,
        IDictionary<string, long> excludedSampleCounts,
        ICollection<string> dataQualityNotes,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifest.MatVariableName))
            throw new InvalidDataException("MAT 数据清单必须声明 matVariableName。");
        using var content = new MemoryStream(bytes);
        var file = new MatFileReader(content).Read();
        var variable = file[manifest.MatVariableName];
        if (variable.Value is not IStructureArray structure)
            throw new InvalidDataException($"MAT 变量 {manifest.MatVariableName} 不是结构数组。");
        var requested = manifest.SignalColumns.Concat(manifest.OutcomeColumns)
            .Append(manifest.ProcessExecutionColumn)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var field in requested.Where(field =>
                     !structure.FieldNames.Contains(field!, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"MAT 结构缺少字段 {field}。");
        var result = new List<IReadOnlyDictionary<string, string>>(structure.Count);
        for (var index = 0; index < structure.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in requested)
            {
                var values = structure[field!, index].ConvertToDoubleArray() ??
                             throw new InvalidDataException($"MAT 字段 {field} 不是数值数组。");
                var finite = values.Where(double.IsFinite).ToArray();
                if (manifest.ValidSignalRanges.TryGetValue(field!, out var validRange))
                {
                    var before = finite.Length;
                    finite = finite.Where(value =>
                            (!validRange.Minimum.HasValue || value >= validRange.Minimum.Value) &&
                            (!validRange.Maximum.HasValue || value <= validRange.Maximum.Value))
                        .ToArray();
                    var excluded = before - finite.Length;
                    if (excluded > 0)
                    {
                        excludedSampleCounts.TryGetValue(field!, out var total);
                        excludedSampleCounts[field!] = total + excluded;
                    }
                }
                if (finite.Length == 0)
                {
                    row[field!] = "";
                    continue;
                }
                var online = finite.Aggregate(default(OnlineMean), static (current, value) => current.Add(value));
                var batchMean = finite.Average();
                if (Math.Abs(online.Mean - batchMean) >
                    1e-10 * (1 + Math.Max(Math.Abs(online.Mean), Math.Abs(batchMean))))
                    throw new InvalidDataException($"MAT 字段 {field} 的流批均值校验失败。");
                row[field!] = batchMean.ToString("R", CultureInfo.InvariantCulture);
                row[$"{field}.minimum"] = finite.Min().ToString("R", CultureInfo.InvariantCulture);
                row[$"{field}.maximum"] = finite.Max().ToString("R", CultureInfo.InvariantCulture);
                row[$"{field}.rms"] = Math.Sqrt(finite.Average(static value => value * value))
                    .ToString("R", CultureInfo.InvariantCulture);
                row[$"{field}.count"] = finite.Length.ToString(CultureInfo.InvariantCulture);
            }
            result.Add(row);
        }
        foreach (var (field, count) in excludedSampleCounts.OrderBy(static pair => pair.Key))
        {
            var range = manifest.ValidSignalRanges[field];
            dataQualityNotes.Add(
                $"信号 {field} 有 {count} 个样本超出清单有效范围并被排除；依据：{range.Basis}");
        }
        return result;
    }

    private static IReadOnlyList<ScientificColumnProfile> Profile(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        IReadOnlyList<string> columns)
        => columns.Distinct(StringComparer.OrdinalIgnoreCase).Select(column =>
        {
            var present = rows.Select(row => row.GetValueOrDefault(column))
                .Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
            var numeric = present.Select(ParseNumber).Where(static value => value.HasValue)
                .Select(static value => value!.Value).ToArray();
            return new ScientificColumnProfile
            {
                Column = column,
                PresentCount = present.Length,
                NumericCount = numeric.Length,
                Minimum = numeric.Length == 0 ? null : numeric.Min(),
                Maximum = numeric.Length == 0 ? null : numeric.Max(),
                Mean = numeric.Length == 0 ? null : numeric.Average()
            };
        }).ToArray();

    private static long CountProcessExecutions(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        string? executionColumn)
        => string.IsNullOrWhiteSpace(executionColumn)
            ? rows.Count
            : rows.Select(row => row.GetValueOrDefault(executionColumn) ?? "")
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .LongCount();

    private static long CountChronologyViolations(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        DatasetQualityValidationDatasetManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.TimestampColumn))
            return 0;
        var lastByProcessExecution = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        long violations = 0;
        foreach (var row in rows)
        {
            var raw = row.GetValueOrDefault(manifest.TimestampColumn);
            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var timestamp))
                continue;
            var execution = string.IsNullOrWhiteSpace(manifest.ProcessExecutionColumn)
                ? "_dataset"
                : row.GetValueOrDefault(manifest.ProcessExecutionColumn) ?? "";
            if (lastByProcessExecution.TryGetValue(execution, out var last) && timestamp < last)
                violations++;
            lastByProcessExecution[execution] = timestamp;
        }
        return violations;
    }

    private static double CompareStreamAndBatch(
        IReadOnlyList<IReadOnlyDictionary<string, string>> rows,
        DatasetQualityValidationDatasetManifest manifest)
    {
        var streams = new Dictionary<(string ProcessExecution, string Signal), OnlineMean>();
        var batches = new Dictionary<(string ProcessExecution, string Signal), List<double>>();
        foreach (var row in rows)
        {
            var execution = string.IsNullOrWhiteSpace(manifest.ProcessExecutionColumn)
                ? "_dataset"
                : row.GetValueOrDefault(manifest.ProcessExecutionColumn) ?? "";
            foreach (var signal in manifest.SignalColumns)
            {
                var value = ParseNumber(row.GetValueOrDefault(signal));
                if (!value.HasValue)
                    continue;
                var key = (execution, signal);
                streams.TryGetValue(key, out var online);
                streams[key] = online.Add(value.Value);
                if (!batches.TryGetValue(key, out var batch))
                    batches[key] = batch = [];
                batch.Add(value.Value);
            }
        }
        return batches.Count == 0
            ? 0
            : batches.Max(pair => Math.Abs(pair.Value.Average() - streams[pair.Key].Mean));
    }

    private static double? ParseNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed)
            ? parsed
            : double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.CurrentCulture, out parsed) && double.IsFinite(parsed)
                ? parsed
                : null;
    }

    private static void ValidateManifest(DatasetQualityValidationDatasetManifest value)
    {
        if (string.IsNullOrWhiteSpace(value.DatasetId) || value.Version <= 0 ||
            value.HeaderRowCount <= 0 ||
            value.MinimumSignalNumericCoverage is <= 0 or > 1 ||
            value.MinimumOutcomeNumericCoverage is <= 0 or > 1 ||
            string.IsNullOrWhiteSpace(value.Industry) ||
            string.IsNullOrWhiteSpace(value.Process) ||
            string.IsNullOrWhiteSpace(value.DataKind) ||
            string.IsNullOrWhiteSpace(value.SourceUri) ||
            string.IsNullOrWhiteSpace(value.License) ||
            string.IsNullOrWhiteSpace(value.Citation) ||
            value.SignalColumns.Count == 0)
            throw new ResearchAssetRuleException("数据集质量验证清单缺少数据标识、来源、许可、引用或信号列。");
        if (!Uri.TryCreate(value.SourceUri, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ResearchAssetRuleException("数据集质量验证来源必须是可追溯的 HTTP(S) 地址。");
        if (!string.IsNullOrWhiteSpace(value.RetrievalUri) &&
            (!Uri.TryCreate(value.RetrievalUri, UriKind.Absolute, out var retrievalUri) ||
             retrievalUri.Scheme is not ("http" or "https")))
            throw new ResearchAssetRuleException("数据集质量验证下载地址必须是可追溯的 HTTP(S) 地址。");
        if (value.ExpectedSha256 is { Length: > 0 and not 64 })
            throw new ResearchAssetRuleException("期望 SHA-256 必须是 64 位十六进制字符串。");
        foreach (var (column, range) in value.ValidSignalRanges)
        {
            if (!value.SignalColumns.Contains(column, StringComparer.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(range.Basis) ||
                range.Minimum.HasValue && range.Maximum.HasValue &&
                range.Minimum.Value >= range.Maximum.Value)
                throw new ResearchAssetRuleException("信号有效范围必须对应已声明信号，并包含依据和合法上下界。");
        }
    }

    private static string ManifestHash(DatasetQualityValidationDatasetManifest value)
        => Hash(JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));

    private static string NormalizeId(string value) => value.Trim().ToLowerInvariant();
    private static string Hash(byte[] value)
        => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private readonly record struct OnlineMean(long Count, double Mean)
    {
        public OnlineMean Add(double value)
        {
            var count = Count + 1;
            return new OnlineMean(count, Mean + (value - Mean) / count);
        }
    }
}

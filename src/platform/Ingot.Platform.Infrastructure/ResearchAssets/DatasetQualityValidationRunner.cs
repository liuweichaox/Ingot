using System.Buffers;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;
using MatFileHandler;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class DatasetQualityValidationRunner(IResearchAssetStore store) : IDatasetQualityValidationService
{
    public const string Version = "cross-industry-validation-v2";
    private const long MaximumInputBytes = 100L * 1024 * 1024;
    private const int MaximumRows = 1_000_000;
    private const int MaximumColumns = 256;
    private const int MaximumHeaderRows = 16;
    private const int MaximumCellCharacters = 32 * 1024;
    private const int MaximumRowCharacters = 1024 * 1024;
    private const int MaximumDistinctExecutions = 100_000;
    private const int MaximumComparisonGroups = 250_000;
    private const long MaximumWorkbookUncompressedBytes = 64L * 1024 * 1024;
    private const long MaximumWorkbookEntryBytes = 32L * 1024 * 1024;
    private const int MaximumWorkbookEntries = 4_096;
    private const long MaximumWorkbookCells = 1_000_000;
    private const long MaximumMatInputBytes = 32L * 1024 * 1024;
    private const long MaximumMatValuesPerField = 2_000_000;
    private const long MaximumMatValues = 10_000_000;

    public async Task<DatasetQualityValidationReport> RunAsync(
        Stream content,
        string fileName,
        DatasetQualityValidationDatasetManifest manifest,
        string userId,
        CancellationToken ct = default)
    {
        var report = await EvaluateAsync(content, fileName, manifest, userId, ct).ConfigureAwait(false);
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
        var extension = GetSupportedExtension(fileName);
        var staged = await StageAsync(content, extension, ct).ConfigureAwait(false);
        try
        {
            var issues = new List<string>();
            if (!string.IsNullOrWhiteSpace(manifest.ExpectedSha256) &&
                !string.Equals(staged.Sha256, manifest.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                issues.Add("原始文件 SHA-256 与数据清单不一致。");

            var accumulator = new ValidationAccumulator(manifest);
            ReadRows(staged, extension, manifest, accumulator, ct);
            return accumulator.CreateReport(staged.Sha256, issues, userId);
        }
        finally
        {
            TryDelete(staged.FilePath);
        }
    }

    private static async Task<StagedInput> StageAsync(
        Stream content,
        string extension,
        CancellationToken ct)
    {
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "ingot-dataset-quality-validation");
        Directory.CreateDirectory(stagingDirectory);
        var filePath = Path.Combine(stagingDirectory, $"{Guid.NewGuid():N}{extension}");
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var destination = new FileStream(
                filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            long length = 0;
            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                length += read;
                if (length > MaximumInputBytes)
                    throw new InvalidDataException($"数据文件超过上限 {MaximumInputBytes / 1024 / 1024} MiB。");
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            if (length == 0)
                throw new InvalidDataException("数据文件不能为空。");
            await destination.FlushAsync(ct).ConfigureAwait(false);
            return new StagedInput(
                filePath,
                length,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            TryDelete(filePath);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void ReadRows(
        StagedInput staged,
        string extension,
        DatasetQualityValidationDatasetManifest manifest,
        ValidationAccumulator accumulator,
        CancellationToken ct)
    {
        if (extension == ".csv")
        {
            ReadCsv(staged.FilePath, accumulator, ct);
            return;
        }
        if (extension is ".xlsx" or ".xlsm")
        {
            ReadWorkbook(staged.FilePath, manifest.SheetName, manifest.HeaderRowCount, accumulator, ct);
            return;
        }
        if (staged.Length > MaximumMatInputBytes)
            throw new InvalidDataException($"MAT 文件超过安全解析上限 {MaximumMatInputBytes / 1024 / 1024} MiB。");
        ReadMat(staged.FilePath, manifest, accumulator, ct);
    }

    private static void ReadCsv(
        string filePath,
        ValidationAccumulator accumulator,
        CancellationToken ct)
    {
        using var content = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        using var reader = new StreamReader(content, Encoding.UTF8, true, 64 * 1024);
        var fields = new List<string>();
        var field = new StringBuilder();
        var state = CsvFieldState.Unquoted;
        var rowCharacters = 0;
        var rowOpen = false;
        var headersRead = false;
        var skipLineFeed = false;
        var buffer = ArrayPool<char>.Shared.Rent(16 * 1024);

        void Append(char character)
        {
            if (field.Length >= MaximumCellCharacters)
                throw new InvalidDataException(
                    $"单元格内容超过上限 {MaximumCellCharacters} 个字符。");
            rowCharacters++;
            if (rowCharacters > MaximumRowCharacters)
                throw new InvalidDataException($"单行内容超过上限 {MaximumRowCharacters} 个字符。");
            field.Append(character);
        }

        void CompleteField()
        {
            if (fields.Count >= MaximumColumns)
                throw new InvalidDataException($"数据表头或数据行超过 {MaximumColumns} 列。");
            fields.Add(field.ToString());
            field.Clear();
            state = CsvFieldState.Unquoted;
        }

        void CompleteRow()
        {
            CompleteField();
            var values = fields.ToArray();
            fields.Clear();
            rowCharacters = 0;
            rowOpen = false;
            if (!headersRead)
            {
                accumulator.SetHeaders(values.Select(static header => header.Trim()).ToArray());
                headersRead = true;
            }
            else
            {
                accumulator.AddRow(values);
            }
        }

        try
        {
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                for (var index = 0; index < read; index++)
                {
                    var character = buffer[index];
                    if (skipLineFeed)
                    {
                        skipLineFeed = false;
                        if (character == '\n')
                            continue;
                    }
                    rowOpen = true;
                    if (state == CsvFieldState.Quoted)
                    {
                        if (character == '"')
                            state = CsvFieldState.AfterQuote;
                        else
                            Append(character);
                        continue;
                    }
                    if (state == CsvFieldState.AfterQuote)
                    {
                        if (character == '"')
                        {
                            Append(character);
                            state = CsvFieldState.Quoted;
                            continue;
                        }
                        if (character is ' ' or '\t')
                        {
                            Append(character);
                            continue;
                        }
                        if (character == ',')
                        {
                            CompleteField();
                            continue;
                        }
                        if (character is '\r' or '\n')
                        {
                            CompleteRow();
                            skipLineFeed = character == '\r';
                            continue;
                        }
                        throw new InvalidDataException("CSV 引号字段结束后存在无效字符。");
                    }
                    if (character == ',')
                    {
                        CompleteField();
                    }
                    else if (character is '\r' or '\n')
                    {
                        CompleteRow();
                        skipLineFeed = character == '\r';
                    }
                    else if (character == '"' && field.Length == 0)
                    {
                        state = CsvFieldState.Quoted;
                    }
                    else
                    {
                        Append(character);
                    }
                }
            }
            if (state == CsvFieldState.Quoted)
                throw new InvalidDataException("CSV 引号字段未闭合。");
            if (rowOpen)
                CompleteRow();
            if (!headersRead)
                throw new InvalidDataException("CSV 缺少表头。");
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static void ReadWorkbook(
        string filePath,
        string? sheetName,
        int headerRowCount,
        ValidationAccumulator accumulator,
        CancellationToken ct)
    {
        ValidateWorkbookArchive(filePath);
        using var workbook = new XLWorkbook(filePath);
        var worksheet = string.IsNullOrWhiteSpace(sheetName)
            ? workbook.Worksheets.FirstOrDefault()
            : workbook.Worksheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase));
        if (worksheet is null)
            throw new InvalidDataException($"找不到工作表 {sheetName ?? "(first)"}。");
        var used = worksheet.RangeUsed() ?? throw new InvalidDataException("工作表没有有效数据。");
        var columnCount = used.ColumnCount();
        var rowCount = used.RowCount();
        if (columnCount > MaximumColumns)
            throw new InvalidDataException($"工作表列数超过上限 {MaximumColumns}。");
        if ((long)columnCount * rowCount > MaximumWorkbookCells)
            throw new InvalidDataException($"工作表有效单元格跨度超过上限 {MaximumWorkbookCells}。");
        if (headerRowCount <= 0 || headerRowCount > MaximumHeaderRows || headerRowCount >= rowCount)
            throw new InvalidDataException(
                $"表头行数必须在 1 到 {MaximumHeaderRows} 之间且小于工作表有效行数。");

        using var rows = used.RowsUsed().GetEnumerator();
        var headerLevels = new string[headerRowCount, columnCount];
        for (var headerRow = 0; headerRow < headerRowCount; headerRow++)
        {
            if (!rows.MoveNext())
                throw new InvalidDataException("工作表缺少清单声明的表头行。");
            var carried = "";
            for (var column = 0; column < columnCount; column++)
            {
                var raw = rows.Current.Cell(column + 1).GetFormattedString().Trim();
                EnsureCellWidth(raw);
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
        accumulator.SetHeaders(headers);
        while (rows.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            var values = new string[columnCount];
            for (var column = 0; column < columnCount; column++)
                values[column] = rows.Current.Cell(column + 1).GetFormattedString().Trim();
            accumulator.AddRow(values);
        }
    }

    private static void ValidateWorkbookArchive(string filePath)
    {
        using var content = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, false);
        if (archive.Entries.Count > MaximumWorkbookEntries)
            throw new InvalidDataException($"工作簿压缩包条目数超过上限 {MaximumWorkbookEntries}。");
        long totalLength = 0;
        foreach (var entry in archive.Entries)
        {
            if (entry.Length > MaximumWorkbookEntryBytes)
                throw new InvalidDataException(
                    $"工作簿解压条目 {entry.FullName} 超过上限 {MaximumWorkbookEntryBytes / 1024 / 1024} MiB。");
            totalLength += entry.Length;
            if (totalLength > MaximumWorkbookUncompressedBytes)
                throw new InvalidDataException(
                    $"工作簿解压后大小超过上限 {MaximumWorkbookUncompressedBytes / 1024 / 1024} MiB。");
        }
    }

    private static void ReadMat(
        string filePath,
        DatasetQualityValidationDatasetManifest manifest,
        ValidationAccumulator accumulator,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(manifest.MatVariableName))
            throw new InvalidDataException("MAT 数据清单必须声明 matVariableName。");
        using var content = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.SequentialScan);
        var file = new MatFileReader(content).Read();
        var variable = file[manifest.MatVariableName];
        if (variable.Value is not IStructureArray structure)
            throw new InvalidDataException($"MAT 变量 {manifest.MatVariableName} 不是结构数组。");
        if (structure.Count > MaximumRows)
            throw new InvalidDataException($"数据行超过上限 {MaximumRows}。");
        var requested = manifest.SignalColumns.Concat(manifest.OutcomeColumns)
            .Append(manifest.ProcessExecutionColumn)
            .Append(manifest.TimestampColumn)
            .Append(manifest.PhaseColumn)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static item => item!)
            .ToArray();
        foreach (var field in requested.Where(field =>
                     !structure.FieldNames.Contains(field, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException($"MAT 结构缺少字段 {field}。");
        accumulator.SetHeaders(requested);
        long totalValues = 0;
        for (var index = 0; index < structure.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            var row = new string[requested.Length];
            for (var fieldIndex = 0; fieldIndex < requested.Length; fieldIndex++)
            {
                var field = requested[fieldIndex];
                var values = structure[field, index].ConvertToDoubleArray() ??
                             throw new InvalidDataException($"MAT 字段 {field} 不是数值数组。");
                if (values.LongLength > MaximumMatValuesPerField)
                    throw new InvalidDataException(
                        $"MAT 字段 {field} 的数值数量超过单字段上限 {MaximumMatValuesPerField}。");
                totalValues += values.LongLength;
                if (totalValues > MaximumMatValues)
                    throw new InvalidDataException($"MAT 数值总量超过上限 {MaximumMatValues}。");

                var statistics = new NumericStatistics();
                long excluded = 0;
                foreach (var value in values)
                {
                    if (!double.IsFinite(value))
                        continue;
                    if (manifest.ValidSignalRanges.TryGetValue(field, out var validRange) &&
                        (validRange.Minimum.HasValue && value < validRange.Minimum.Value ||
                         validRange.Maximum.HasValue && value > validRange.Maximum.Value))
                    {
                        excluded++;
                        continue;
                    }
                    statistics.Add(value);
                }
                if (excluded > 0)
                    accumulator.AddExcludedSamples(field, excluded);
                if (statistics.Count > 0)
                {
                    if (statistics.MeanDifference > RelativeTolerance(
                            statistics.OnlineMean, statistics.BatchMean))
                        throw new InvalidDataException($"MAT 字段 {field} 的流批均值校验失败。");
                    row[fieldIndex] = statistics.BatchMean.ToString("R", CultureInfo.InvariantCulture);
                }
            }
            accumulator.AddRow(row);
        }
        accumulator.AddMatRangeNotes();
    }

    private static string GetSupportedExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension is ".csv" or ".xlsx" or ".xlsm" or ".mat"
            ? extension
            : throw new ResearchAssetRuleException(
                "数据集质量验证当前只接收 CSV、XLSX、XLSM 或 MATLAB Level 5 MAT 原始数据。");
    }

    private static void EnsureUniqueHeaders(IReadOnlyList<string> headers)
    {
        if (headers.Count == 0 || headers.Count > MaximumColumns ||
            headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Count)
            throw new InvalidDataException($"数据表头不能为空、重复或超过 {MaximumColumns} 列。");
        foreach (var header in headers)
            EnsureCellWidth(header);
    }

    private static void EnsureCellWidth(string value)
    {
        if (value.Length > MaximumCellCharacters)
            throw new InvalidDataException($"单元格内容超过上限 {MaximumCellCharacters} 个字符。");
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
            value.HeaderRowCount <= 0 || value.HeaderRowCount > MaximumHeaderRows ||
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
        var declaredColumns = value.SignalColumns.Concat(value.OutcomeColumns)
            .Append(value.ProcessExecutionColumn)
            .Append(value.TimestampColumn)
            .Append(value.PhaseColumn)
            .Where(static column => !string.IsNullOrWhiteSpace(column))
            .Select(static column => column!)
            .ToArray();
        if (declaredColumns.Length > MaximumColumns ||
            declaredColumns.Any(static column => column.Length > MaximumCellCharacters))
            throw new ResearchAssetRuleException($"清单声明列数或列名宽度超过安全上限 {MaximumColumns} 列。");
        if (!Uri.TryCreate(value.SourceUri, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
            throw new ResearchAssetRuleException("数据集质量验证来源必须是可追溯的 HTTP(S) 地址。");
        if (!string.IsNullOrWhiteSpace(value.RetrievalUri) &&
            (!Uri.TryCreate(value.RetrievalUri, UriKind.Absolute, out var retrievalUri) ||
             retrievalUri.Scheme is not ("http" or "https")))
            throw new ResearchAssetRuleException("数据集质量验证下载地址必须是可追溯的 HTTP(S) 地址。");
        if (!string.IsNullOrWhiteSpace(value.ExpectedSha256) &&
            (value.ExpectedSha256.Length != 64 ||
             value.ExpectedSha256.Any(static character => !Uri.IsHexDigit(character))))
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
    private static double RelativeTolerance(double left, double right)
        => 1e-10 * (1 + Math.Max(Math.Abs(left), Math.Abs(right)));

    private static void TryDelete(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
            // 临时目录的系统清理作为兜底，不能覆盖原始校验结果。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private sealed class ValidationAccumulator
    {
        private readonly DatasetQualityValidationDatasetManifest manifest;
        private readonly Dictionary<string, ColumnStatistics> profiles = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> processExecutions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, DateTimeOffset> lastTimestampByExecution = new(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<string, ComparisonStatistics>> comparisons =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> excludedSampleCounts = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> dataQualityNotes = [];
        private Dictionary<string, int>? columnIndices;
        private string[] headers = [];
        private long comparisonGroupCount;

        public ValidationAccumulator(DatasetQualityValidationDatasetManifest manifest)
        {
            this.manifest = manifest;
            foreach (var column in manifest.SignalColumns.Concat(manifest.OutcomeColumns)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
                profiles[column] = new ColumnStatistics(column);
        }

        public long RowCount { get; private set; }
        public long ChronologyViolations { get; private set; }

        public void SetHeaders(string[] value)
        {
            if (columnIndices is not null)
                throw new InvalidOperationException("数据表头只能设置一次。");
            EnsureUniqueHeaders(value);
            headers = value;
            columnIndices = value.Select((header, index) => (header, index))
                .ToDictionary(static item => item.header, static item => item.index,
                    StringComparer.OrdinalIgnoreCase);
        }

        public void AddRow(IReadOnlyList<string> values)
        {
            if (columnIndices is null)
                throw new InvalidOperationException("必须先设置数据表头。");
            if (values.Count > headers.Length)
                throw new InvalidDataException("数据行字段数超过表头列数。");
            var rowCharacters = 0;
            var hasValue = false;
            foreach (var value in values)
            {
                EnsureCellWidth(value);
                rowCharacters += value.Length;
                if (rowCharacters > MaximumRowCharacters)
                    throw new InvalidDataException($"单行内容超过上限 {MaximumRowCharacters} 个字符。");
                hasValue |= !string.IsNullOrWhiteSpace(value);
            }
            if (!hasValue)
                return;
            if (RowCount >= MaximumRows)
                throw new InvalidDataException($"数据行超过上限 {MaximumRows}。");
            RowCount++;

            var execution = string.IsNullOrWhiteSpace(manifest.ProcessExecutionColumn)
                ? "_dataset"
                : GetValue(values, manifest.ProcessExecutionColumn);
            if (!string.IsNullOrWhiteSpace(manifest.ProcessExecutionColumn) &&
                execution.Length > 0 && processExecutions.Add(execution) &&
                processExecutions.Count > MaximumDistinctExecutions)
                throw new InvalidDataException($"不同过程执行标识超过上限 {MaximumDistinctExecutions}。");

            ObserveChronology(values, execution);
            foreach (var profile in profiles.Values)
                profile.Add(GetValue(values, profile.Column));
            foreach (var signal in manifest.SignalColumns.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var parsed = ParseNumber(GetValue(values, signal));
                if (parsed.HasValue)
                    AddComparison(execution, signal, parsed.Value);
            }
        }

        public void AddExcludedSamples(string field, long count)
        {
            excludedSampleCounts.TryGetValue(field, out var current);
            excludedSampleCounts[field] = current + count;
        }

        public void AddMatRangeNotes()
        {
            foreach (var (field, count) in excludedSampleCounts.OrderBy(static pair => pair.Key))
            {
                var range = manifest.ValidSignalRanges[field];
                dataQualityNotes.Add(
                    $"信号 {field} 有 {count} 个样本超出清单有效范围并被排除；依据：{range.Basis}");
            }
        }

        public DatasetQualityValidationReport CreateReport(
            string sourceHash,
            List<string> issues,
            string userId)
        {
            if (columnIndices is null)
                throw new InvalidDataException("数据文件没有可用表头。");
            if (RowCount < 10)
                issues.Add("有效数据行少于 10，不能形成数据集质量验证证据。");
            var requiredColumns = manifest.SignalColumns
                .Concat(manifest.OutcomeColumns)
                .Append(manifest.ProcessExecutionColumn)
                .Append(manifest.TimestampColumn)
                .Append(manifest.PhaseColumn)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var required in requiredColumns.Where(required => !columnIndices.ContainsKey(required!)))
                issues.Add($"缺少清单声明的列 {required}。");

            var signalProfiles = CreateProfiles(manifest.SignalColumns);
            var outcomeProfiles = CreateProfiles(manifest.OutcomeColumns);
            foreach (var profile in signalProfiles)
            {
                if (profile.NumericCount < Math.Max(1, RowCount * manifest.MinimumSignalNumericCoverage))
                    issues.Add($"信号列 {profile.Column} 的有效数值覆盖率低于清单阈值。");
            }
            foreach (var profile in outcomeProfiles)
            {
                if (profile.NumericCount < Math.Max(1, RowCount * manifest.MinimumOutcomeNumericCoverage))
                    issues.Add($"结果列 {profile.Column} 的有效数值覆盖率低于清单阈值。");
            }
            var maximumDifference = comparisons.Values.SelectMany(static value => value.Values)
                .Select(static value => value.MeanDifference)
                .DefaultIfEmpty(0)
                .Max();
            if (maximumDifference > 1e-10)
                issues.Add($"流式与批式统计不一致，最大绝对差为 {maximumDifference:R}。");
            if (!manifest.IsMeasuredData)
                issues.Add("数据清单未声明为真实测量数据。");

            return new DatasetQualityValidationReport
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
                RowCount = RowCount,
                ProcessExecutionCount = string.IsNullOrWhiteSpace(manifest.ProcessExecutionColumn)
                    ? RowCount
                    : processExecutions.Count,
                ChronologyViolationCount = ChronologyViolations,
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
        }

        private string GetValue(IReadOnlyList<string> values, string? column)
        {
            if (string.IsNullOrWhiteSpace(column) || columnIndices is null ||
                !columnIndices.TryGetValue(column, out var index) || index >= values.Count)
                return "";
            return values[index].Trim();
        }

        private void ObserveChronology(IReadOnlyList<string> values, string execution)
        {
            if (string.IsNullOrWhiteSpace(manifest.TimestampColumn))
                return;
            var raw = GetValue(values, manifest.TimestampColumn);
            if (!DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var timestamp))
                return;
            if (lastTimestampByExecution.TryGetValue(execution, out var last) && timestamp < last)
                ChronologyViolations++;
            lastTimestampByExecution[execution] = timestamp;
        }

        private void AddComparison(string execution, string signal, double value)
        {
            if (!comparisons.TryGetValue(execution, out var bySignal))
            {
                bySignal = new Dictionary<string, ComparisonStatistics>(StringComparer.OrdinalIgnoreCase);
                comparisons[execution] = bySignal;
            }
            if (!bySignal.TryGetValue(signal, out var statistics))
            {
                comparisonGroupCount++;
                if (comparisonGroupCount > MaximumComparisonGroups)
                    throw new InvalidDataException($"过程执行与信号比较分组超过上限 {MaximumComparisonGroups}。");
                statistics = new ComparisonStatistics();
                bySignal[signal] = statistics;
            }
            statistics.Add(value);
        }

        private IReadOnlyList<ScientificColumnProfile> CreateProfiles(IReadOnlyList<string> columns)
            => columns.Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(column => profiles.TryGetValue(column, out var profile)
                    ? profile.CreateProfile()
                    : new ScientificColumnProfile { Column = column })
                .ToArray();
    }

    private sealed class ColumnStatistics(string column)
    {
        private readonly NumericStatistics numeric = new();

        public string Column { get; } = column;
        public long PresentCount { get; private set; }

        public void Add(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            PresentCount++;
            var parsed = ParseNumber(value);
            if (parsed.HasValue)
                numeric.Add(parsed.Value);
        }

        public ScientificColumnProfile CreateProfile()
            => new()
            {
                Column = Column,
                PresentCount = PresentCount,
                NumericCount = numeric.Count,
                Minimum = numeric.Count == 0 ? null : numeric.Minimum,
                Maximum = numeric.Count == 0 ? null : numeric.Maximum,
                Mean = numeric.Count == 0 ? null : numeric.BatchMean
            };
    }

    private sealed class ComparisonStatistics
    {
        private OnlineMean online;
        private double sum;

        public long Count { get; private set; }
        public double MeanDifference => Count == 0 ? 0 : Math.Abs(sum / Count - online.Mean);

        public void Add(double value)
        {
            online = online.Add(value);
            sum += value;
            Count++;
        }
    }

    private sealed class NumericStatistics
    {
        private OnlineMean online;
        private double sum;

        public long Count { get; private set; }
        public double Minimum { get; private set; } = double.PositiveInfinity;
        public double Maximum { get; private set; } = double.NegativeInfinity;
        public double OnlineMean => online.Mean;
        public double BatchMean => Count == 0 ? 0 : sum / Count;
        public double MeanDifference => Math.Abs(BatchMean - OnlineMean);

        public void Add(double value)
        {
            online = online.Add(value);
            sum += value;
            Count++;
            Minimum = Math.Min(Minimum, value);
            Maximum = Math.Max(Maximum, value);
        }
    }

    private readonly record struct OnlineMean(long Count, double Mean)
    {
        public OnlineMean Add(double value)
        {
            var count = Count + 1;
            return new OnlineMean(count, Mean + (value - Mean) / count);
        }
    }

    private enum CsvFieldState
    {
        Unquoted,
        Quoted,
        AfterQuote
    }

    private sealed record StagedInput(string FilePath, long Length, string Sha256);
}

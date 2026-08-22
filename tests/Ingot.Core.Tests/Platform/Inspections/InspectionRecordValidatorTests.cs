using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Application.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform.Inspections;

/// <summary>
/// 检验执行关联验证器的单元测试。
/// </summary>
public class InspectionRecordValidatorTests
{
    private sealed class TestBoundaryStore : IExecutionBoundaryStore
    {
        private readonly Dictionary<(string siteId, string sourceExecutionId), ExecutionBoundary> _boundaries;

        public TestBoundaryStore()
        {
            _boundaries = new();
        }

        public void AddBoundary(ExecutionBoundary boundary)
        {
            _boundaries[(boundary.SiteId, boundary.SourceExecutionId)] = boundary;
        }

        public Task<ExecutionBoundary?> GetBoundaryAsync(string siteId, string sourceExecutionId, CancellationToken ct)
        {
            _boundaries.TryGetValue((siteId, sourceExecutionId), out var boundary);
            return Task.FromResult(boundary);
        }

        public Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(string siteId, DateTimeOffset? from, DateTimeOffset? to, int limit = 100, int offset = 0, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExecutionBoundary>>(Array.Empty<ExecutionBoundary>());

        public Task<bool> ReplayFailedProjectionAsync(string siteId, string sourceExecutionId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private readonly TestBoundaryStore _boundaryStore;
    private readonly InspectionExecutionReferenceValidator _validator;

    public InspectionRecordValidatorTests()
    {
        _boundaryStore = new TestBoundaryStore();
        _validator = new InspectionExecutionReferenceValidator(_boundaryStore);
    }

    [Fact]
    public async Task ValidateExecutionReference_WithValidBoundary_ReturnsSuccess()
    {
        // Arrange
        var siteId = "site-001";
        var executionId = "exec-123";
        var inspectionTime = DateTimeOffset.UtcNow;

        var boundary = new ExecutionBoundary
        {
            ExecutionId = Guid.CreateVersion7().ToString(),
            SiteId = siteId,
            EdgeId = "edge-001",
            SourceExecutionId = executionId,
            StartedAt = inspectionTime.AddMinutes(-10),
            EndedAt = inspectionTime.AddMinutes(10),
            Status = ExecutionBoundaryStatus.Completed,
            EventCount = 10,
            MinIngestId = 1,
            MaxIngestId = 10,
            CreatedAt = inspectionTime.AddMinutes(-20),
            UpdatedAt = inspectionTime.AddMinutes(-10)
        };

        _boundaryStore.AddBoundary(boundary);

        // Act
        var (isValid, reason) = await _validator.ValidateExecutionReferenceAsync(
            siteId, executionId, inspectionTime, CancellationToken.None);

        // Assert
        Assert.True(isValid);
        Assert.Null(reason);
    }

    [Fact]
    public async Task ValidateExecutionReference_WithNonexistentBoundary_ReturnsFail()
    {
        // Arrange
        var siteId = "site-001";
        var executionId = "exec-nonexistent";
        var inspectionTime = DateTimeOffset.UtcNow;

        // Act
        var (isValid, reason) = await _validator.ValidateExecutionReferenceAsync(
            siteId, executionId, inspectionTime, CancellationToken.None);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(reason);
        Assert.Contains("不存在", reason);
    }

    [Fact]
    public async Task ValidateExecutionReference_WithTimeBeforeStart_ReturnsFail()
    {
        // Arrange
        var siteId = "site-001";
        var executionId = "exec-123";
        var startTime = DateTimeOffset.UtcNow;
        var inspectionTime = startTime.AddHours(-1); // 运行开始前 1 小时

        var boundary = new ExecutionBoundary
        {
            ExecutionId = Guid.CreateVersion7().ToString(),
            SiteId = siteId,
            EdgeId = "edge-001",
            SourceExecutionId = executionId,
            StartedAt = startTime,
            EndedAt = startTime.AddHours(1),
            Status = ExecutionBoundaryStatus.Completed,
            EventCount = 10,
            MinIngestId = 1,
            MaxIngestId = 10,
            CreatedAt = startTime.AddHours(-2),
            UpdatedAt = startTime
        };

        _boundaryStore.AddBoundary(boundary);

        // Act
        var (isValid, reason) = await _validator.ValidateExecutionReferenceAsync(
            siteId, executionId, inspectionTime, CancellationToken.None);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(reason);
        Assert.Contains("早于", reason);
    }

    [Fact]
    public async Task ValidateExecutionReference_WithTimeAfterEnd_ReturnsFail()
    {
        // Arrange
        var siteId = "site-001";
        var executionId = "exec-123";
        var startTime = DateTimeOffset.UtcNow;
        var endTime = startTime.AddHours(1);
        var inspectionTime = endTime.AddHours(2); // 运行结束后 2 小时

        var boundary = new ExecutionBoundary
        {
            ExecutionId = Guid.CreateVersion7().ToString(),
            SiteId = siteId,
            EdgeId = "edge-001",
            SourceExecutionId = executionId,
            StartedAt = startTime,
            EndedAt = endTime,
            Status = ExecutionBoundaryStatus.Completed,
            EventCount = 10,
            MinIngestId = 1,
            MaxIngestId = 10,
            CreatedAt = startTime.AddHours(-2),
            UpdatedAt = endTime
        };

        _boundaryStore.AddBoundary(boundary);

        // Act
        var (isValid, reason) = await _validator.ValidateExecutionReferenceAsync(
            siteId, executionId, inspectionTime, CancellationToken.None);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(reason);
        Assert.Contains("晚于", reason);
    }

    [Fact]
    public async Task ValidateBatchConsistency_WithConsistentRecords_ReturnsSuccess()
    {
        // Arrange
        var executionId = "exec-123";
        var baseTime = DateTimeOffset.UtcNow;

        var records = new List<InspectionRecordInput>
        {
            new() { ExecutionId = executionId, InspectionTime = baseTime, Data = new() },
            new() { ExecutionId = executionId, InspectionTime = baseTime.AddMinutes(5), Data = new() },
            new() { ExecutionId = executionId, InspectionTime = baseTime.AddMinutes(10), Data = new() }
        };

        // Act
        var (isConsistent, reason) = await _validator.ValidateBatchConsistencyAsync(
            records, "site-001", CancellationToken.None);

        // Assert
        Assert.True(isConsistent);
        Assert.Null(reason);
    }

    [Fact]
    public async Task ValidateBatchConsistency_WithMultipleExecutionIds_ReturnsFail()
    {
        // Arrange
        var baseTime = DateTimeOffset.UtcNow;

        var records = new List<InspectionRecordInput>
        {
            new() { ExecutionId = "exec-1", InspectionTime = baseTime, Data = new() },
            new() { ExecutionId = "exec-2", InspectionTime = baseTime.AddMinutes(5), Data = new() }
        };

        // Act
        var (isConsistent, reason) = await _validator.ValidateBatchConsistencyAsync(
            records, "site-001", CancellationToken.None);

        // Assert
        Assert.False(isConsistent);
        Assert.NotNull(reason);
        Assert.Contains("多个运行", reason);
    }

    [Fact]
    public async Task ValidateBatchConsistency_WithLargeTimeGap_ReturnsFail()
    {
        // Arrange
        var executionId = "exec-123";
        var baseTime = DateTimeOffset.UtcNow;

        var records = new List<InspectionRecordInput>
        {
            new() { ExecutionId = executionId, InspectionTime = baseTime, Data = new() },
            new() { ExecutionId = executionId, InspectionTime = baseTime.AddHours(2), Data = new() } // 间隔 2 小时
        };

        // Act
        var (isConsistent, reason) = await _validator.ValidateBatchConsistencyAsync(
            records, "site-001", CancellationToken.None);

        // Assert
        Assert.False(isConsistent);
        Assert.NotNull(reason);
        Assert.Contains("跨度过大", reason);
    }

    [Fact]
    public async Task ValidateBatchConsistency_WithEmptyRecords_ReturnsSuccess()
    {
        // Arrange
        var records = new List<InspectionRecordInput>();

        // Act
        var (isConsistent, reason) = await _validator.ValidateBatchConsistencyAsync(
            records, "site-001", CancellationToken.None);

        // Assert
        Assert.True(isConsistent);
        Assert.Null(reason);
    }
}

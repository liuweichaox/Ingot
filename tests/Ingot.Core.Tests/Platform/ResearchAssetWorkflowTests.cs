// 验证平台组件 ResearchAssetWorkflow 的成功、拒绝和安全边界。

using Ingot.Agent;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentContracts = Ingot.Contracts.Agents;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchAssetWorkflowTests
{
    [Fact]
    public async Task MechanismFusion_FourModes_AreVersionedAndDeterministic()
    {
        var store = new MemoryStore();
        var service = new MechanismModelService(store);
        var model = await service.SaveModelDraftAsync(new MechanismModelVersion
        {
            ModelId = "heat-transfer",
            Version = 1,
            Name = "可审计传热近似式",
            EquationKind = "affine",
            Inputs =
            [
                new MechanismVariableDefinition
                {
                    Code = "temperature",
                    Unit = "°C",
                    ValidMinimum = 0,
                    ValidMaximum = 1000
                }
            ],
            Output = new MechanismVariableDefinition
            {
                Code = "response",
                Unit = "unit",
                ValidMinimum = 0,
                ValidMaximum = 3000
            },
            Coefficients = new Dictionary<string, double> { ["temperature"] = 2 },
            Intercept = 1,
            ScientificBasis = "受控适用域内的线性化机理近似。"
        }, "scientist");
        await service.ChangeModelStatusAsync(model.ModelId, model.Version, "validated", "reviewer");
        await service.ChangeModelStatusAsync(model.ModelId, model.Version, "active", "reviewer");

        var expected = new Dictionary<string, double?>
        {
            [MechanismFusionModes.Calibration] = 15,
            [MechanismFusionModes.PostProcessing] = 11,
            [MechanismFusionModes.MechanismAsFeature] = 10,
            [MechanismFusionModes.Ensemble] = 9.25
        };
        foreach (var (mode, expectedValue) in expected)
        {
            var fusion = await service.SaveFusionDraftAsync(new MechanismFusionDefinition
            {
                FusionId = $"fusion-{mode}",
                Version = 1,
                Name = mode,
                Mode = mode,
                MechanismModelId = model.ModelId,
                MechanismModelVersion = model.Version,
                OutputCode = "response",
                CalibrationScale = 2,
                CalibrationOffset = 1,
                PostProcessingGain = 0.5,
                MechanismReference = 5,
                MechanismWeight = 0.25,
                MechanismFeatureCode = "mechanism.response"
            }, "scientist");
            await service.ChangeFusionStatusAsync(fusion.FusionId, 1, "validated", "reviewer");
            await service.ChangeFusionStatusAsync(fusion.FusionId, 1, "active", "reviewer");
            var result = await service.ExecuteAsync(new MechanismFusionExecutionRequest
            {
                FusionId = fusion.FusionId,
                FusionVersion = 1,
                MechanismInputs = new Dictionary<string, double> { ["temperature"] = 3 },
                DataPrediction = 10
            });

            Assert.Equal(7, result.MechanismPrediction, 12);
            Assert.Equal(expectedValue, result.FusedPrediction);
            Assert.Equal(64, result.ExecutionHash.Length);
            if (mode == MechanismFusionModes.MechanismAsFeature)
                Assert.Equal(7, result.AugmentedFeatures["mechanism.response"], 12);
        }
    }

    [Fact]
    public async Task KnowledgeSearch_ReturnsOnlyReviewedRecordsFromReviewedSources()
    {
        var store = new MemoryStore();
        var sourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = sourceId,
                SiteCode = "site-a",
                Title = "保压阶段作业指导书",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://test",
                Sha256 = "abc",
                MediaType = "application/pdf",
                FileName = "holding.pdf",
                SizeBytes = 10,
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Category = "constraint",
                PageOrSheet = "12",
                Content = "保压阶段温度不得超过 185 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Content = "未经复核的温度建议。",
                HumanReviewed = false,
                CreatedBy = "assistant",
                CreatedAt = DateTimeOffset.UtcNow
            });
        var otherSourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = otherSourceId,
                SiteCode = "site-b",
                Title = "其他站点作业指导书",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://other",
                Sha256 = "def",
                MediaType = "application/pdf",
                FileName = "other.pdf",
                SizeBytes = 10,
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = otherSourceId,
                Content = "保压温度上限为 999 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
                CreatedAt = DateTimeOffset.UtcNow
            });
        var tool = new SearchProcessKnowledgeTool(store);

        var result = await tool.ExecuteAsync(
            new AgentContracts.AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["query"] = "保压温度上限" }
            },
            new AgentExecutionContext
            {
                RunId = "run-1",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                AccessScope = SiteScope("site-a"),
                Request = new AgentContracts.CreateChatRunRequest
                {
                    Question = "保压温度上限是多少？",
                }
            });

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        var records = result.Data.GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Contains("185", records[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task KnowledgeSearch_WithoutAResolvableSite_ReturnsNoRecords()
    {
        var store = new MemoryStore();
        var sourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = sourceId,
                SiteCode = "site-a",
                Title = "站点知识",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://scoped",
                Sha256 = "abc",
                MediaType = "text/plain",
                FileName = "scoped.txt",
                SizeBytes = 10,
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Content = "保压温度上限为 185 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
                CreatedAt = DateTimeOffset.UtcNow
            });
        var tool = new SearchProcessKnowledgeTool(store);

        var result = await tool.ExecuteAsync(
            new AgentContracts.AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["query"] = "保压温度上限" }
            },
            new AgentExecutionContext
            {
                RunId = "run-unscoped",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                AccessScope = SiteScope("site-a", "site-b"),
                Request = new AgentContracts.CreateChatRunRequest { Question = "保压温度上限是多少？" }
            });

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Equal(0, result.Data.GetProperty("records").GetArrayLength());
    }

    [Fact]
    public async Task DeterministicPlanner_UsesKnowledgeSearchForSiteInstructionQuestion()
    {
        var tool = new SearchProcessKnowledgeTool(new MemoryStore());
        var planner = new DeterministicModelClient();

        var result = await planner.ResolveIntentAsync(
            new AgentContracts.CreateChatRunRequest
            {
                Question = "作业指导书规定的保压温度上限是多少？"
            },
            [tool.Definition]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("search_process_knowledge", call.Tool);
        Assert.Equal("作业指导书规定的保压温度上限是多少？", call.Arguments["query"]);
    }

    [Fact]
    public async Task KnowledgeSearch_HybridResult_UsesFragmentLevelEvidenceReference()
    {
        var sourceId = Guid.CreateVersion7();
        var recordId = Guid.CreateVersion7();
        var source = new KnowledgeSource
        {
            SourceId = sourceId,
            Title = "保压阶段作业指导书",
            Status = KnowledgeSourceStatuses.Reviewed,
            StorageRef = "process-knowledge://test",
            Sha256 = "source-sha",
            MediaType = "application/pdf",
            FileName = "holding.pdf",
            SizeBytes = 10,
            UploadedBy = "engineer",
            UploadedAt = DateTimeOffset.UtcNow
        };
        var record = new KnowledgeRecord
        {
            RecordId = recordId,
            SourceId = sourceId,
            Content = "保压阶段温度不得超过 185 °C。",
            HumanReviewed = true,
            CreatedBy = "engineer",
            CreatedAt = DateTimeOffset.UtcNow,
            ReviewedBy = "engineer",
            ReviewedAt = DateTimeOffset.UtcNow,
            Citation = new KnowledgeCitation
            {
                LocationKind = "page",
                PageNumber = 12,
                ContentHash = "fragment-sha"
            }
        };
        var tool = new SearchProcessKnowledgeTool(
            new MemoryStore(),
            new StaticProcessKnowledgeSearch(new ProcessKnowledgeSearchResult
            {
                RetrievalMode = "hybrid",
                Hits =
                [
                    new ProcessKnowledgeSearchHit
                    {
                        Source = source,
                        Record = record,
                        Score = 0.91,
                        RetrievalMethod = "hybrid"
                    }
                ]
            }));

        var result = await tool.ExecuteAsync(
            new AgentContracts.AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["query"] = "保压温度上限" }
            },
            new AgentExecutionContext
            {
                RunId = "run-hybrid",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                AccessScope = SiteScope("site-a"),
                Request = new AgentContracts.CreateChatRunRequest
                {
                    Question = "保压温度上限是多少？",
                }
            });

        var reference = Assert.Single(result.RelatedRecords);
        Assert.Equal("process-knowledge-record", reference.Kind);
        Assert.Equal(recordId.ToString(), reference.Id);
        Assert.Equal(
            $"/knowledge/process-materials?sourceId={sourceId:D}&recordId={recordId:D}",
            reference.Url);
        Assert.Equal("hybrid", result.Data.GetProperty("retrievalMode").GetString());
        Assert.Equal("fragment-sha", result.Data.GetProperty("records")[0]
            .GetProperty("citation").GetProperty("ContentHash").GetString());
    }

    [Fact]
    public async Task KnowledgeSearch_UnauthorizedSite_FailsClosedBeforeSearch()
    {
        var search = new RecordingProcessKnowledgeSearch();
        var tool = new SearchProcessKnowledgeTool(new MemoryStore(), search);

        var result = await tool.ExecuteAsync(
            new AgentContracts.AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?>
                {
                    ["query"] = "保压温度上限",
                    ["siteId"] = "site-b"
                }
            },
            new AgentExecutionContext
            {
                RunId = "run-unauthorized",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                AccessScope = SiteScope("site-a"),
                Request = new AgentContracts.CreateChatRunRequest
                {
                    Question = "保压温度上限是多少？",
                }
            });

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.False(search.WasCalled);
    }

    [Fact]
    public async Task KnowledgeEmbeddingWorker_ProcessesEveryReviewedRecordAcrossPages()
    {
        var sourceId = Guid.CreateVersion7();
        var store = new MemoryStore();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = sourceId,
                Title = "大批量知识来源",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://large",
                Sha256 = "source-sha",
                MediaType = "text/plain",
                FileName = "large.txt",
                SizeBytes = 1,
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            Enumerable.Range(0, 501).Select(index => new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Content = $"\r\n  reviewed fragment {index}  \r\n",
                HumanReviewed = true,
                CreatedBy = "engineer",
                CreatedAt = DateTimeOffset.UtcNow,
                Citation = new KnowledgeCitation
                {
                    LocationKind = "page",
                    PageNumber = index + 1,
                    ContentHash = "caller-supplied-stale-hash"
                }
            }).ToArray());
        var jobs = new RecordingEmbeddingJobStore();
        var embeddings = new RecordingEmbeddingClient();
        var worker = new KnowledgeEmbeddingWorker(
            store,
            jobs,
            embeddings,
            Options.Create(new KnowledgeEmbeddingWorkerOptions { RecordPageSize = 100 }),
            NullLogger<KnowledgeEmbeddingWorker>.Instance);
        var job = new KnowledgeEmbeddingJob(sourceId, "engineer", embeddings.Model, Guid.CreateVersion7(), 7, 1);

        await worker.ProcessJobAsync(job, CancellationToken.None);

        Assert.Equal(501, jobs.UpsertedRecords.Count);
        Assert.Equal(501, embeddings.EmbeddedContents.Count);
        Assert.All(embeddings.EmbeddedContents, content => Assert.DoesNotContain('\r', content));
        Assert.All(embeddings.EmbeddedContents, content => Assert.Equal(content.Trim(), content));
        Assert.True(jobs.Completed);
    }

    [Fact]
    public async Task KnowledgeEmbeddingBackfill_ReconcilesPeriodically()
    {
        var jobs = new RecordingEmbeddingJobStore();
        var service = new KnowledgeEmbeddingBackfillService(
            jobs,
            new RecordingEmbeddingClient(),
            Options.Create(new KnowledgeEmbeddingWorkerOptions
            {
                ReconciliationInterval = TimeSpan.FromMilliseconds(20)
            }),
            NullLogger<KnowledgeEmbeddingBackfillService>.Instance);

        await service.StartAsync(CancellationToken.None);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(2);
        while (jobs.EnqueueMissingCallCount < 2 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(10);
        await service.StopAsync(CancellationToken.None);

        Assert.True(jobs.EnqueueMissingCallCount >= 2);
    }

    [Fact]
    public void KnowledgeEmbeddingTimeouts_FallbackAndRetryUnlessCallerIsStopping()
    {
        Assert.True(PostgresProcessKnowledgeSearch.ShouldFallbackToKeyword(
            new TaskCanceledException("embedding timeout"), CancellationToken.None));
        Assert.True(KnowledgeEmbeddingWorker.IsRetryable(
            new TaskCanceledException("embedding timeout"), CancellationToken.None));

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.False(PostgresProcessKnowledgeSearch.ShouldFallbackToKeyword(
            new OperationCanceledException(canceled.Token), canceled.Token));
        Assert.False(KnowledgeEmbeddingWorker.IsRetryable(
            new OperationCanceledException(canceled.Token), canceled.Token));
    }

    [Fact]
    public void KnowledgeContentFingerprint_NormalizesContentAndIgnoresCallerHash()
    {
        var record = new KnowledgeRecord
        {
            SourceId = Guid.CreateVersion7(),
            Content = "\r\n  stable content  \r\n",
            Citation = new KnowledgeCitation
            {
                LocationKind = "page",
                ContentHash = "stale"
            }
        };

        var normalized = KnowledgeContentFingerprint.NormalizeAndStamp(record);

        Assert.Equal("stable content", normalized.Content);
        Assert.Equal(KnowledgeContentFingerprint.ComputeHash("stable content"), normalized.Citation!.ContentHash);
        Assert.NotEqual("stale", normalized.Citation.ContentHash);
    }

    private sealed class MemoryStore : IResearchAssetStore
    {
        private readonly Dictionary<(string, int), MechanismModelVersion> _mechanismModels = [];
        private readonly Dictionary<(string, int), MechanismFusionDefinition> _mechanismFusions = [];
        private readonly Dictionary<Guid, KnowledgeSource> _knowledgeSources = [];
        private readonly Dictionary<Guid, KnowledgeRecord> _knowledgeRecords = [];
        public List<ResearchAssetAuditEntry> Audit { get; } = [];

        public void SeedKnowledge(KnowledgeSource source, params KnowledgeRecord[] records)
        {
            _knowledgeSources[source.SourceId] = source;
            foreach (var record in records)
                _knowledgeRecords[record.RecordId] = record;
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<MechanismModelVersion> SaveMechanismModelAsync(MechanismModelVersion value, CancellationToken ct = default) { _mechanismModels[(value.ModelId, value.Version)] = value; return Task.FromResult(value); }
        public Task<MechanismModelVersion?> GetMechanismModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult(_mechanismModels.GetValueOrDefault((modelId, version)));
        public Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MechanismModelVersion>>(_mechanismModels.Values.ToArray());
        public Task<ResearchAssetPage<MechanismModelVersion>> ListMechanismModelsPageAsync(int limit, string? cursor, CancellationToken ct = default) => Task.FromResult(new ResearchAssetPage<MechanismModelVersion> { Data = _mechanismModels.Values.Take(limit).ToArray() });
        public Task<MechanismFusionDefinition> SaveMechanismFusionAsync(MechanismFusionDefinition value, CancellationToken ct = default) { _mechanismFusions[(value.FusionId, value.Version)] = value; return Task.FromResult(value); }
        public Task<MechanismFusionDefinition?> GetMechanismFusionAsync(string fusionId, int version, CancellationToken ct = default) => Task.FromResult(_mechanismFusions.GetValueOrDefault((fusionId, version)));
        public Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MechanismFusionDefinition>>(_mechanismFusions.Values.ToArray());
        public Task<ResearchAssetPage<MechanismFusionDefinition>> ListMechanismFusionsPageAsync(int limit, string? cursor, CancellationToken ct = default) => Task.FromResult(new ResearchAssetPage<MechanismFusionDefinition> { Data = _mechanismFusions.Values.Take(limit).ToArray() });
        public Task<KnowledgeSource> AddKnowledgeSourceAsync(Stream content, string title, string sourceKind, string fileName, string mediaType, string siteCode, IReadOnlyDictionary<string, string> contextSelector, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult(_knowledgeSources.GetValueOrDefault(sourceId));
        public Task<KnowledgeSource?> FindKnowledgeSourceByHashAsync(string siteCode, string sha256, CancellationToken ct = default) => Task.FromResult(_knowledgeSources.Values.FirstOrDefault(value => string.Equals(value.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase) && string.Equals(value.Sha256, sha256, StringComparison.OrdinalIgnoreCase)));
        public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeSource>>(_knowledgeSources.Values.ToArray());
        public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(string siteCode, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeSource>>(_knowledgeSources.Values.Where(value => string.Equals(value.SiteCode, siteCode, StringComparison.OrdinalIgnoreCase)).ToArray());
        public async Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesPageAsync(string siteCode, int limit, string? cursor, CancellationToken ct = default) => new() { Data = (await ListKnowledgeSourcesAsync(siteCode, ct)).Take(limit).ToArray() };
        public async Task<ResearchAssetPage<KnowledgeSource>> ListReviewedKnowledgeSourcesPageAsync(string siteCode, string? processSpecificationId, int limit, string? cursor, CancellationToken ct = default)
        {
            var sources = await ListKnowledgeSourcesAsync(siteCode, ct);
            var filtered = sources.Where(static source => source.Status == KnowledgeSourceStatuses.Reviewed)
                .Where(source =>
                {
                    var scoped = source.ContextSelector.Where(pair => string.Equals(pair.Key.Trim(), MechanismKnowledgeService.ProcessSpecificationDimension, StringComparison.OrdinalIgnoreCase)).Select(static pair => pair.Value).ToArray();
                    if (scoped.Length == 0) return true;
                    return scoped.Length == 1 && !string.IsNullOrWhiteSpace(processSpecificationId) && string.Equals(scoped[0].Trim(), processSpecificationId.Trim(), StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(static source => source.ReviewedAt)
                .ThenByDescending(static source => source.SourceId)
                .Take(limit)
                .ToArray();
            return new ResearchAssetPage<KnowledgeSource> { Data = filtered };
        }
        public async Task<ResearchAssetPage<KnowledgeSource>> ListKnowledgeSourcesForManagementPageAsync(string siteCode, int limit, string? cursor, CancellationToken ct = default)
        {
            var sources = await ListKnowledgeSourcesAsync(siteCode, ct);
            return new ResearchAssetPage<KnowledgeSource>
            {
                Data = sources.OrderByDescending(static source => source.UploadedAt).ThenByDescending(static source => source.SourceId).Take(limit).ToArray()
            };
        }
        public Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeSource> ReplaceExtractedKnowledgeRecordsAsync(KnowledgeSource source, IReadOnlyList<KnowledgeRecord> records, CancellationToken ct = default) => throw new NotSupportedException();
        public Task EnqueueKnowledgeExtractionAsync(Guid sourceId, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeExtractionJob?> ClaimKnowledgeExtractionAsync(TimeSpan leaseTimeout, CancellationToken ct = default) => Task.FromResult<KnowledgeExtractionJob?>(null);
        public Task<bool> RenewKnowledgeExtractionLeaseAsync(Guid sourceId, Guid leaseId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CompleteKnowledgeExtractionAsync(Guid sourceId, Guid leaseId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<KnowledgeExtractionFailureDisposition?> FailKnowledgeExtractionAsync(Guid sourceId, Guid leaseId, string error, bool retryable, int maxAttempts, TimeSpan retryDelay, CancellationToken ct = default) => Task.FromResult<KnowledgeExtractionFailureDisposition?>(null);
        public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeRecord>>(_knowledgeRecords.Values.Where(item => item.SourceId == sourceId).ToArray());
        public Task<ResearchAssetPage<KnowledgeRecord>> ListKnowledgeRecordsForEmbeddingPageAsync(Guid sourceId, int limit, string? cursor, CancellationToken ct = default)
        {
            Guid? after = string.IsNullOrWhiteSpace(cursor) ? null : Guid.Parse(cursor);
            var ordered = _knowledgeRecords.Values
                .Where(item => item.SourceId == sourceId && (after is null || item.RecordId.CompareTo(after.Value) > 0))
                .OrderBy(static item => item.RecordId)
                .Take(Math.Max(1, limit) + 1)
                .ToArray();
            var hasMore = ordered.Length > Math.Max(1, limit);
            var page = hasMore ? ordered[..^1] : ordered;
            return Task.FromResult(new ResearchAssetPage<KnowledgeRecord>
            {
                Data = page,
                NextCursor = hasMore ? page[^1].RecordId.ToString() : null
            });
        }
        public Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default) { Audit.Add(value); return Task.CompletedTask; }
        public Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(string resourceType, string resourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ResearchAssetAuditEntry>>(Audit.Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId).ToArray());
    }

    private sealed class StaticProcessKnowledgeSearch(ProcessKnowledgeSearchResult result) : IProcessKnowledgeSearch
    {
        public Task<ProcessKnowledgeSearchResult> SearchAsync(
            ProcessKnowledgeSearchRequest request,
            CancellationToken ct = default)
            => Task.FromResult(result);
    }

    private sealed class RecordingProcessKnowledgeSearch : IProcessKnowledgeSearch
    {
        public bool WasCalled { get; private set; }

        public Task<ProcessKnowledgeSearchResult> SearchAsync(
            ProcessKnowledgeSearchRequest request,
            CancellationToken ct = default)
        {
            WasCalled = true;
            return Task.FromResult(new ProcessKnowledgeSearchResult());
        }
    }

    private sealed class RecordingEmbeddingClient : IKnowledgeEmbeddingClient
    {
        public bool IsConfigured => true;
        public string Model => "test-embedding";
        public int Dimensions => 3;
        public List<string> EmbeddedContents { get; } = [];

        public Task<KnowledgeEmbedding> EmbedAsync(string content, CancellationToken ct = default)
        {
            EmbeddedContents.Add(content);
            return Task.FromResult(new KnowledgeEmbedding
            {
                Model = Model,
                Values = [0.1f, 0.2f, 0.3f]
            });
        }
    }

    private sealed class RecordingEmbeddingJobStore : IKnowledgeEmbeddingJobStore
    {
        public List<KnowledgeRecord> UpsertedRecords { get; } = [];
        public int EnqueueMissingCallCount { get; private set; }
        public bool Completed { get; private set; }

        public Task EnqueueAsync(Guid sourceId, string requestedBy, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<int> EnqueueMissingAsync(CancellationToken ct = default)
        {
            EnqueueMissingCallCount++;
            return Task.FromResult(0);
        }

        public Task<KnowledgeEmbeddingJob?> ClaimAsync(TimeSpan leaseTimeout, CancellationToken ct = default)
            => Task.FromResult<KnowledgeEmbeddingJob?>(null);

        public Task<bool> RenewLeaseAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task<bool> CompleteAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default)
        {
            Completed = true;
            return Task.FromResult(true);
        }

        public Task<KnowledgeEmbeddingFailureDisposition?> FailAsync(
            KnowledgeEmbeddingJob job,
            string error,
            bool retryable,
            int maxAttempts,
            TimeSpan retryDelay,
            CancellationToken ct = default)
            => Task.FromResult<KnowledgeEmbeddingFailureDisposition?>(
                retryable
                    ? KnowledgeEmbeddingFailureDisposition.RetryScheduled
                    : KnowledgeEmbeddingFailureDisposition.DeadLettered);

        public Task<bool> UpsertAsync(
            KnowledgeEmbeddingJob job,
            KnowledgeRecord record,
            KnowledgeEmbedding embedding,
            CancellationToken ct = default)
        {
            UpsertedRecords.Add(record);
            return Task.FromResult(true);
        }
    }

    private static AgentAccessScope SiteScope(params string[] siteIds)
        => new() { SiteIds = new HashSet<string>(siteIds, StringComparer.OrdinalIgnoreCase) };


}

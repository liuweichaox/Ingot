using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Ingot.Agent;

public static class AgentTelemetry
{
    public const string SourceName = "Ingot.Agent";

    public static readonly ActivitySource ActivitySource = new(SourceName);
    private static readonly Meter Meter = new(SourceName);

    public static readonly Counter<long> Runs = Meter.CreateCounter<long>("ingot.agent.runs");
    public static readonly Counter<long> DiscussionParticipantFailures =
        Meter.CreateCounter<long>("ingot.agent.discussion.participant_failures");
    public static readonly Histogram<double> RunDuration =
        Meter.CreateHistogram<double>("ingot.agent.run.duration", "s");
    public static readonly Histogram<long> ModelTokens =
        Meter.CreateHistogram<long>("ingot.agent.model.tokens", "{token}");
    public static readonly Histogram<double> ModelDuration =
        Meter.CreateHistogram<double>("ingot.agent.model.duration", "ms");
    public static readonly Histogram<double> ToolDuration =
        Meter.CreateHistogram<double>("ingot.agent.tool.duration", "ms");
}

using System;
using Ingot.Domain.Models;
using Xunit;

namespace Ingot.Core.Tests.Domain;

public sealed class DataMessageTests
{
    [Fact]
    public void CreateDiagnostic_ShouldSeparateDiagnosticAndBusinessEventSemantics()
    {
        var timestamp = DateTimeOffset.UtcNow;

        var message = DataMessage.CreateDiagnostic(
            "cycle-001",
            "production_diagnostic",
            "PLC01",
            "CH01",
            DiagnosticEventType.Interrupted,
            timestamp);

        Assert.Null(message.EventType);
        Assert.Equal(DiagnosticEventType.Interrupted, message.DiagnosticType);
        Assert.Equal(timestamp, message.Timestamp);
        Assert.Equal("production_diagnostic", message.Measurement);
    }
}

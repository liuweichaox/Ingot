using System.Collections.Generic;
using Ingot.Domain.Models;

namespace Ingot.Infrastructure.Queues;

internal sealed record PendingBatch(string BatchKey, string Measurement, List<DataMessage> Messages);

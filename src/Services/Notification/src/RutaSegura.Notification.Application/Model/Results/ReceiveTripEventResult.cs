using RutaSegura.Notification.Domain.Model.Aggregates;

namespace RutaSegura.Notification.Application.Model.Results;

public sealed record ReceiveTripEventResult(int Created, int Duplicates, string? SkippedReason = null);

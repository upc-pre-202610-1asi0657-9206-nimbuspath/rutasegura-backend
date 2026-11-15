namespace RutaSegura.Notification.Application.Model.Results;

/// <summary>Accepted significa aceptacion del canal; no demuestra que el usuario recibio o leyo el aviso.</summary>
public sealed record ChannelSendResult(bool Accepted, bool Retryable, string? ProviderMessageId, string? ErrorCode);

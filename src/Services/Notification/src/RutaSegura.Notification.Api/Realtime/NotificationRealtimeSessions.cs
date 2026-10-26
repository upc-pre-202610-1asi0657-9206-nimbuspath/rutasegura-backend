using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using RutaSegura.Notification.Api.Security;

namespace RutaSegura.Notification.Api.Realtime;

public sealed class NotificationRealtimeSessionOptions
{
    public TimeSpan ValidationInterval { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxConnectionsPerUser { get; set; } = 20;
    public int MaxConcurrentValidations { get; set; } = 8;
    public void Validate()
    {
        if (ValidationInterval < TimeSpan.FromMilliseconds(100) || ValidationInterval > TimeSpan.FromMinutes(1) ||
            MaxConnectionsPerUser is < 1 or > 100 || MaxConcurrentValidations is < 1 or > 32)
            throw new InvalidOperationException("Configuracion de sesiones realtime invalida.");
    }
}

/// <summary>Registro efimero por replica. Retiene JWT solo en memoria, hasta desconexion/revocacion/vencimiento.</summary>
public sealed class NotificationRealtimeSessions(IServiceScopeFactory scopes, IConfiguration configuration,
    NotificationRealtimeSessionOptions options, TimeProvider clock)
{
    private sealed class Session(string id, Guid org, Guid user, string role, string token, DateTimeOffset expires, Action abort)
    {
        public string Id { get; } = id;
        public Guid Org { get; } = org;
        public Guid User { get; } = user;
        public string Role { get; } = role;
        public string Token { get; } = token;
        public DateTimeOffset Expires { get; } = expires;
        public Action Abort { get; } = abort;
        public SemaphoreSlim Validation { get; } = new(1, 1);
        public override string ToString() => "NotificationSession [redacted]";
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly object _membership = new();

    public bool Register(HubCallerContext context, string token)
    {
        var user = context.User!;
        if (string.IsNullOrWhiteSpace(token) || !long.TryParse(user.FindFirstValue("exp"), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)) return false;
        DateTimeOffset expires;
        try { expires = DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { return false; }
        if (expires <= clock.GetUtcNow()) return false;
        var org = NotificationIdentity.OrganizationId(user);
        var userId = NotificationIdentity.UserId(user);
        lock (_membership)
        {
            if (_sessions.Values.Count(s => s.Org == org && s.User == userId) >= options.MaxConnectionsPerUser) return false;
            return _sessions.TryAdd(context.ConnectionId, new(context.ConnectionId, org, userId,
                user.FindFirstValue(ClaimTypes.Role)!, token, expires, context.Abort));
        }
    }
    public void Remove(string id) { lock (_membership) _sessions.TryRemove(id, out _); }

    public Task ValidateAllAsync(CancellationToken ct) => ValidateAsync(_sessions.Values.ToArray(), ct);
    public Task ValidateRecipientAsync(Guid org, Guid user, CancellationToken ct) =>
        ValidateAsync(_sessions.Values.Where(s => s.Org == org && s.User == user).ToArray(), ct);

    private Task ValidateAsync(Session[] sessions, CancellationToken ct) => Parallel.ForEachAsync(sessions,
        new ParallelOptions { MaxDegreeOfParallelism = options.MaxConcurrentValidations, CancellationToken = ct }, async (session, token) =>
        {
            await session.Validation.WaitAsync(token);
            try
            {
                if (!_sessions.ContainsKey(session.Id)) return;
                var valid = session.Expires > clock.GetUtcNow();
                if (valid && configuration.GetValue("Iam:AccountValidation:Enabled", true))
                {
                    await using var scope = scopes.CreateAsyncScope();
                    valid = await scope.ServiceProvider.GetRequiredService<INotificationIamAccountVerifier>()
                        .VerifyAsync(session.Token, session.User, session.Org, session.Role, token) == NotificationIamAccountState.Active;
                }
                if (!valid) { Remove(session.Id); session.Abort(); }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { Remove(session.Id); session.Abort(); }
            finally { session.Validation.Release(); }
        });
}

public sealed class NotificationRealtimeSessionMonitor(NotificationRealtimeSessions sessions,
    NotificationRealtimeSessionOptions options, ILogger<NotificationRealtimeSessionMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await sessions.ValidateAllAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("No se pudo completar la verificacion de sesiones de Notification."); }
            try { await Task.Delay(options.ValidationInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using RutaSegura.BuildingBlocks.Domain;
using RutaSegura.Notification.Infrastructure.Persistence.EntityFrameworkCore.Repositories;

namespace RutaSegura.Notification.Api.Errors;

public sealed class NotificationExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            NotificationPersistenceConflictException conflict => (409, conflict.Code),
            DomainException domain => (400, domain.Code),
            HttpRequestException or TimeoutException => (503, "NOTIFICATION_DEPENDENCY_UNAVAILABLE"),
            OperationCanceledException when !context.RequestAborted.IsCancellationRequested => (503, "NOTIFICATION_DEPENDENCY_TIMEOUT"),
            _ => (500, "NOTIFICATION_INTERNAL_ERROR")
        };
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        context.Response.ContentType = "application/problem+json";
        await System.Text.Json.JsonSerializer.SerializeAsync(context.Response.Body, new ProblemDetails
        {
            Status = status, Title = status >= 500 ? "El servicio no puede completar la operacion en este momento." : "La operacion no pudo completarse.",
            Extensions = { ["code"] = code }
        }, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web), cancellationToken);
        return true;
    }
}

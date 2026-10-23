using System.Text.Json;
using RutaSegura.Notification.Application.Model.Results;
using RutaSegura.Notification.Interfaces.Rest.Resources;
using RutaSegura.Notification.Interfaces.Rest.Transform;

namespace RutaSegura.Notification.Tests.Interfaces;

public sealed class AssemblerTests
{
    [Fact]
    public void AnnouncementResourceMapsAllFieldsToCommand_WithoutSupplyingTrustedIdentity()
    {
        var resource = new CreateAnnouncementResource(Guid.NewGuid(), "Aviso", "Cambio de horario", Guid.NewGuid());
        var command = CreateAnnouncementCommandFromResourceAssembler.ToCommandFromResource(resource);

        command.RequestId.ShouldBe(resource.RequestId);
        command.Title.ShouldBe(resource.Title);
        command.Body.ShouldBe(resource.Body);
        command.RouteId.ShouldBe(resource.RouteId);
        typeof(CreateAnnouncementResource).GetProperties().Select(p => p.Name).OrderBy(n => n)
            .ShouldBe(new[] { "Body", "RequestId", "RouteId", "Title" });
    }

    [Fact]
    public void NotificationResourcePreservesTheAuthorizedResult_WithoutDomainEntities()
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var result = new NotificationResult(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "Boarding", "Normal", "Abordaje confirmado", "Se confirmo el abordaje.",
            occurredAt, occurredAt.AddSeconds(5), occurredAt.AddMinutes(1));

        var resource = NotificationResourceFromResultAssembler.ToResourceFromResult(result);

        // Iguales valores publicados, pero tipos separados en la frontera REST.
        JsonSerializer.Serialize(resource).ShouldBe(JsonSerializer.Serialize(result));
        typeof(NotificationResource).ShouldNotBe(typeof(NotificationResult));
        typeof(NotificationResource).GetProperties().ShouldAllBe(p =>
            p.PropertyType.Assembly.GetName().Name != "RutaSegura.Notification.Domain");
    }

    [Fact]
    public void AnnouncementResultMapsToResourceIncludingIdempotentResponse()
    {
        var result = new CreateAnnouncementResult(Guid.NewGuid(), false, 0);
        var resource = AnnouncementResourceFromResultAssembler.ToResourceFromResult(result);

        resource.AnnouncementId.ShouldBe(result.AnnouncementId);
        resource.Created.ShouldBeFalse();
        resource.NotificationsCreated.ShouldBe(0);
    }
}

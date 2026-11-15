using System.Text.Json;
using RutaSegura.Notification.Application.Contracts.V1;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.ValueObjects;

namespace RutaSegura.Notification.Tests.Contracts;

public sealed class ContractAndBoundaryTests
{
    [Fact]
    public void PayloadCarriesOrganizationAndStableBusinessReferences_WithoutPersonalDetailsOrGps()
    {
        var organization = Guid.NewGuid();
        var trip = Guid.NewGuid();
        var student = Guid.NewGuid();
        var payload = new StudentBoardedV1(organization, trip, student, Guid.NewGuid());
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(payload, options);
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("organizationId").GetGuid().ShouldBe(organization);
        document.RootElement.GetProperty("tripId").GetGuid().ShouldBe(trip);
        document.RootElement.GetProperty("studentId").GetGuid().ShouldBe(student);
        document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(p => p)
            .ShouldBe(new[] { "organizationId", "stopId", "studentId", "tripId" });
        JsonSerializer.Deserialize<StudentBoardedV1>(json, options).ShouldBe(payload);
        TripNotificationContracts.MessageType(payload).ShouldBe("trip.student-boarded.v1");
    }

    [Fact]
    public void DomainDoesNotReferenceInfrastructureOrOtherServices()
    {
        var names = typeof(NotificationContent).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        names.ShouldAllBe(n => n.StartsWith("System.", StringComparison.Ordinal) || n == "RutaSegura.BuildingBlocks.Domain");
    }

    [Fact]
    public void ApplicationDoesNotReferenceBrokerOrmAspNetOrOtherServices()
    {
        var names = typeof(ReceiveTripEventCommand).Assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();
        names.ShouldAllBe(n => n.StartsWith("System.", StringComparison.Ordinal) ||
            n == "RutaSegura.Notification.Domain" || n == "RutaSegura.BuildingBlocks.Application" || n == "RutaSegura.BuildingBlocks.Domain" ||
            (n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) && n.EndsWith(".Abstractions", StringComparison.Ordinal)));
    }
}

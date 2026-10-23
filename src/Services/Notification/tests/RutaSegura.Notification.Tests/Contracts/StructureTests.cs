using System.Reflection;
using System.Xml.Linq;
using RutaSegura.Notification.Application.CommandServices;
using RutaSegura.Notification.Application.Model.Commands;
using RutaSegura.Notification.Application.Model.Queries;
using RutaSegura.Notification.Application.QueryServices;
using RutaSegura.Notification.Domain.Model.Aggregates;
using RutaSegura.Notification.Domain.Model.Entities;
using RutaSegura.Notification.Domain.Model.ValueObjects;
using RutaSegura.Notification.Domain.Repositories;
using RutaSegura.Notification.Interfaces.Rest.Resources;
using NotificationEntity = RutaSegura.Notification.Domain.Model.Aggregates.Notification;

namespace RutaSegura.Notification.Tests.Contracts;

public sealed class StructureTests
{
    [Fact]
    public void DomainTypesAreGroupedByDddResponsibility()
    {
        typeof(NotificationEntity).Namespace.ShouldBe("RutaSegura.Notification.Domain.Model.Aggregates");
        typeof(Announcement).Namespace.ShouldBe(typeof(NotificationEntity).Namespace);
        typeof(NotificationDelivery).Namespace.ShouldBe("RutaSegura.Notification.Domain.Model.Entities");
        typeof(NotificationContent).Namespace.ShouldBe("RutaSegura.Notification.Domain.Model.ValueObjects");
        typeof(NotificationKey).Namespace.ShouldBe(typeof(NotificationContent).Namespace);
        typeof(INotificationRepository).Namespace.ShouldBe("RutaSegura.Notification.Domain.Repositories");
        typeof(IAnnouncementRepository).Assembly.ShouldBe(typeof(NotificationEntity).Assembly);
    }

    [Fact]
    public void CommandAndQueryServicesPublishEveryPhaseOneUseCase()
    {
        typeof(INotificationCommandService).GetMethods().Select(m => m.GetParameters()[0].ParameterType)
            .ShouldBe(new[] { typeof(ReceiveTripEventCommand), typeof(CreateAnnouncementCommand), typeof(MarkNotificationAsReadCommand),
                typeof(RegisterPushSubscriptionCommand), typeof(DisablePushSubscriptionCommand) }, ignoreOrder: true);
        typeof(INotificationQueryService).GetMethods().Select(m => m.GetParameters()[0].ParameterType)
            .ShouldBe(new[] { typeof(GetMyNotificationsQuery), typeof(GetMyNotificationQuery), typeof(GetMyPushSubscriptionsQuery) }, ignoreOrder: true);
        typeof(CreateAnnouncementCommand).Assembly.ShouldBe(typeof(INotificationCommandService).Assembly);
    }

    [Fact]
    public void InterfacesDoesNotReferenceDomainDirectlyOrAnyOtherService()
    {
        var assembly = typeof(NotificationResource).Assembly;
        assembly.GetName().Name.ShouldBe("RutaSegura.Notification.Interfaces");
        assembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("RutaSegura.", StringComparison.Ordinal))
            .ShouldAllBe(a => a.Name == "RutaSegura.Notification.Application" ||
                a.Name!.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal));
        var resources = assembly.GetTypes().Where(t => t.Namespace == "RutaSegura.Notification.Interfaces.Rest.Resources");
        resources.ShouldNotBeEmpty();
        resources.SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .ShouldAllBe(p => p.PropertyType.Assembly != typeof(NotificationEntity).Assembly);
    }

    [Fact]
    public void SolutionAndProjectReferencesUseApiAsTheIndependentHost()
    {
        var root = RepositoryRoot();
        const string projectPath = "src/Services/Notification/src/RutaSegura.Notification.Api/RutaSegura.Notification.Api.csproj";
        var solution = XDocument.Load(Path.Combine(root, "RutaSegura.slnx"));
        solution.Descendants("Project").Select(e => (string?)e.Attribute("Path")).ShouldContain(projectPath);
        var project = XDocument.Load(Path.Combine(root, projectPath));
        ((string?)project.Root!.Attribute("Sdk")).ShouldBe("Microsoft.NET.Sdk.Web");
        project.Descendants("ProjectReference").Select(e => (string?)e.Attribute("Include"))
            .ShouldBe(new[] {
                "../RutaSegura.Notification.Application/RutaSegura.Notification.Application.csproj",
                "../RutaSegura.Notification.Infrastructure/RutaSegura.Notification.Infrastructure.csproj",
                "../RutaSegura.Notification.Interfaces/RutaSegura.Notification.Interfaces.csproj"
            }, ignoreOrder: true);
        solution.Descendants("Project").Select(e => (string?)e.Attribute("Path"))
            .ShouldContain("src/Services/Notification/src/RutaSegura.Notification.Interfaces/RutaSegura.Notification.Interfaces.csproj");
    }

    [Fact]
    public void InterfacesContainsOnlyRestCode_AndApiOwnsRuntimeConfiguration()
    {
        var serviceRoot = Path.Combine(RepositoryRoot(), "src", "Services", "Notification", "src");
        var interfacesRoot = Path.Combine(serviceRoot, "RutaSegura.Notification.Interfaces");
        var apiRoot = Path.Combine(serviceRoot, "RutaSegura.Notification.Api");
        var project = XDocument.Load(Path.Combine(interfacesRoot, "RutaSegura.Notification.Interfaces.csproj"));
        ((string?)project.Root!.Attribute("Sdk")).ShouldBe("Microsoft.NET.Sdk");
        project.Descendants("ProjectReference").Select(e => (string?)e.Attribute("Include"))
            .ShouldBe(new[] { "../RutaSegura.Notification.Application/RutaSegura.Notification.Application.csproj" });
        project.Descendants("PackageReference").ShouldBeEmpty();

        foreach (var runtimeFile in new[] { "Program.cs", "appsettings.json", Path.Combine("Properties", "launchSettings.json") })
        {
            File.Exists(Path.Combine(apiRoot, runtimeFile)).ShouldBeTrue();
            File.Exists(Path.Combine(interfacesRoot, runtimeFile)).ShouldBeFalse();
        }

        var sourcePaths = Directory.GetFiles(interfacesRoot, "*.cs", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(interfacesRoot, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("bin/", StringComparison.Ordinal) && !p.StartsWith("obj/", StringComparison.Ordinal)).ToArray();
        sourcePaths.ShouldNotBeEmpty();
        sourcePaths.ShouldAllBe(p => p.StartsWith("Rest/", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RutaSegura.slnx"))) return directory.FullName;
        throw new DirectoryNotFoundException("No se encontro la raiz del repositorio.");
    }
}

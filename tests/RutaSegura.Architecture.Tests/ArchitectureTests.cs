using System.Xml.Linq;
using Mono.Cecil;

namespace RutaSegura.Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] Services = ["TripTracking", "Notification"];
    private static readonly string[] Layers = ["Domain", "Application", "Infrastructure", "Api"];

    private static string Root
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "RutaSegura.slnx")))
                    return directory.FullName;
            throw new InvalidOperationException("Repository root not found.");
        }
    }

    private static string Configuration => new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
    private static string[] Projects => Directory.GetFiles(Path.Combine(Root, "src"), "*.csproj", SearchOption.AllDirectories)
        .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") &&
                    !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                    !p.Contains("_to_delete") &&
                    !p.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")).ToArray();

    private static string Binary(string project)
    {
        var name = Path.GetFileNameWithoutExtension(project);
        var fromPackageBuild = Path.Combine(Root, "artifacts", "package-consumers", "bin", name, Configuration, "net10.0", name + ".dll");
        // Test execution in package mode itself is isolated under this directory.
        if (AppContext.BaseDirectory.Contains("package-consumers"))
            return fromPackageBuild;
        return Path.Combine(Path.GetDirectoryName(project)!, "bin", Configuration, "net10.0", name + ".dll");
    }

    [Fact]
    public void EveryServiceHasFourLayersAndBelongsToSolution()
    {
        var solution = XDocument.Load(Path.Combine(Root, "RutaSegura.slnx"));
        var registered = solution.Descendants("Project").Select(e => (string)e.Attribute("Path")!).ToHashSet();
        foreach (var service in Services)
        foreach (var layer in Layers)
        {
            var name = $"RutaSegura.{service}.{layer}";
            var project = Assert.Single(Projects, p => Path.GetFileNameWithoutExtension(p) == name);
            Assert.Contains(Path.GetRelativePath(Root, project).Replace('\\', '/'), registered);
            Assert.True(File.Exists(Binary(project)), $"Missing compiled layer: {name}");
        }
        Assert.Single(Projects, p => Path.GetFileNameWithoutExtension(p) == "RutaSegura.ApiGateway");
        var restProject = Assert.Single(Projects, p => Path.GetFileNameWithoutExtension(p) == "RutaSegura.Notification.Interfaces");
        Assert.Contains(Path.GetRelativePath(Root, restProject).Replace('\\', '/'), registered);
        Assert.True(File.Exists(Binary(restProject)), "Missing compiled REST layer: RutaSegura.Notification.Interfaces");
    }

    [Fact]
    public void EvaluatedAssembliesRespectCleanArchitectureAndServiceBoundaries()
    {
        foreach (var project in Projects)
        {
            using var assembly = AssemblyDefinition.ReadAssembly(Binary(project));
            Assert.Empty(ArchitectureRules.AssemblyViolations(assembly));
        }
    }

    [Fact]
    public void DeclaredReferencesCannotBypassRulesEvenIfUnusedInCompiledCode()
    {
        foreach (var project in Projects)
        {
            var name = Path.GetFileNameWithoutExtension(project);
            var xml = XDocument.Load(project);
            foreach (var reference in xml.Descendants("ProjectReference"))
                Assert.True(ArchitectureRules.IsAllowedReference(name,
                    Path.GetFileNameWithoutExtension(((string)reference.Attribute("Include")!).Replace('\\', '/'))),
                    $"{name}: {reference}");
            foreach (var package in xml.Descendants("PackageReference"))
                Assert.True(ArchitectureRules.IsAllowedReference(name, (string)package.Attribute("Include")!), $"{name}: {package}");
            if (name.EndsWith(".Domain") || name.EndsWith(".Application"))
            {
                Assert.Equal("Microsoft.NET.Sdk", (string?)xml.Root!.Attribute("Sdk"));
                Assert.Empty(xml.Descendants("FrameworkReference"));
            }
        }
    }

    [Theory]
    [InlineData("RutaSegura.TripTracking.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("RutaSegura.TripTracking.Domain", "RutaSegura.TripTracking.Infrastructure")]
    [InlineData("RutaSegura.TripTracking.Application", "RabbitMQ.Client")]
    [InlineData("RutaSegura.Notification.Application", "Microsoft.AspNetCore.App")]
    [InlineData("RutaSegura.Notification.Infrastructure", "RutaSegura.TripTracking.Domain")]
    [InlineData("RutaSegura.Notification.Interfaces", "RutaSegura.BuildingBlocks.Testing")]
    [InlineData("RutaSegura.Notification.Interfaces", "RutaSegura.Notification.Domain")]
    [InlineData("RutaSegura.Notification.Interfaces", "RutaSegura.Notification.Infrastructure")]
    [InlineData("RutaSegura.Notification.Interfaces", "RutaSegura.Notification.Api")]
    [InlineData("RutaSegura.BuildingBlocks.Application", "RutaSegura.TripTracking.Domain")]
    [InlineData("RutaSegura.BuildingBlocks.Messaging", "RabbitMQ.Client")]
    [InlineData("RutaSegura.ApiGateway", "RutaSegura.TripTracking.Application")]
    public void GuardRejectsForbiddenCompiledAssemblyReference(string source, string target)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(source, new Version(1, 0)), source, ModuleKind.Dll);
        assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference(target, new Version(1, 0)));
        Assert.Single(ArchitectureRules.AssemblyViolations(assembly));
    }
}

using System.Xml.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace RutaSegura.Architecture.Tests;

public sealed class ArchitectureTests
{
    private static readonly string[] Services = ["IAM", "Administration", "TripTracking", "Notification"];
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

    [Fact]
    public void AdministrationModulesArePresentAndDoNotReferenceEachOthersInternals()
    {
        foreach (var layer in Layers)
        {
            var project = Assert.Single(Projects, p => Path.GetFileNameWithoutExtension(p) == $"RutaSegura.Administration.{layer}");
            using var assembly = AssemblyDefinition.ReadAssembly(Binary(project));
            Assert.Empty(ArchitectureRules.ModuleViolations(assembly));
            if (layer is "Domain" or "Application")
                foreach (var module in new[] { "Roster", "RoutePlanning", "Subscription" })
                    Assert.Contains(assembly.MainModule.Types, t => t.Namespace == $"RutaSegura.Administration.{layer}.{module}");
        }
    }

    [Theory]
    [InlineData("RutaSegura.IAM.Domain", "Microsoft.EntityFrameworkCore")]
    [InlineData("RutaSegura.IAM.Domain", "RutaSegura.IAM.Infrastructure")]
    [InlineData("RutaSegura.IAM.Application", "RabbitMQ.Client")]
    [InlineData("RutaSegura.IAM.Application", "Microsoft.AspNetCore.App")]
    [InlineData("RutaSegura.Administration.Infrastructure", "RutaSegura.IAM.Domain")]
    [InlineData("RutaSegura.Notification.Api", "RutaSegura.BuildingBlocks.Testing")]
    [InlineData("RutaSegura.BuildingBlocks.Application", "RutaSegura.TripTracking.Domain")]
    [InlineData("RutaSegura.BuildingBlocks.Messaging", "RabbitMQ.Client")]
    [InlineData("RutaSegura.ApiGateway", "RutaSegura.IAM.Application")]
    public void GuardRejectsForbiddenCompiledAssemblyReference(string source, string target)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(source, new Version(1, 0)), source, ModuleKind.Dll);
        assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference(target, new Version(1, 0)));
        Assert.Single(ArchitectureRules.AssemblyViolations(assembly));
    }

    [Theory]
    [InlineData("Domain", "RoutePlanning")]
    [InlineData("Application", "Subscription")]
    [InlineData("Infrastructure", "Roster")]
    public void GuardRejectsForbiddenModuleDependencyInMethodBody(string layer, string targetModule)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        var sourceModule = targetModule == "Roster" ? "RoutePlanning" : "Roster";
        var source = new TypeDefinition($"RutaSegura.Administration.{layer}.{sourceModule}", "Caller",
            TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        var target = new TypeDefinition($"RutaSegura.Administration.Domain.{targetModule}", "PrivateEntity",
            TypeAttributes.Public, assembly.MainModule.TypeSystem.Object);
        assembly.MainModule.Types.Add(source);
        assembly.MainModule.Types.Add(target);
        var method = new MethodDefinition("UseOtherModule", MethodAttributes.Public | MethodAttributes.Static,
            assembly.MainModule.TypeSystem.Void);
        source.Methods.Add(method);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken, target));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Assert.Single(ArchitectureRules.ModuleViolations(assembly));
    }

    [Fact]
    public void GuardPermitsPublishedApplicationContractButRejectsGenericEntityLeak()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)), "Fixture", ModuleKind.Dll);
        var caller = new TypeDefinition("RutaSegura.Administration.Application.Roster", "Caller", TypeAttributes.Public,
            assembly.MainModule.TypeSystem.Object);
        var contract = new TypeReference("RutaSegura.Administration.Application.Subscription.Contracts", "IPlanLimits",
            assembly.MainModule, assembly.MainModule);
        assembly.MainModule.Types.Add(caller);
        caller.Fields.Add(new FieldDefinition("Contract", FieldAttributes.Public, contract));
        Assert.Empty(ArchitectureRules.ModuleViolations(assembly));
        var list = new GenericInstanceType(assembly.MainModule.ImportReference(typeof(List<>)));
        list.GenericArguments.Add(new TypeReference("RutaSegura.Administration.Domain.Subscription", "Plan",
            assembly.MainModule, assembly.MainModule));
        caller.Fields.Add(new FieldDefinition("LeakedEntities", FieldAttributes.Public, list));
        Assert.Single(ArchitectureRules.ModuleViolations(assembly));
    }
}

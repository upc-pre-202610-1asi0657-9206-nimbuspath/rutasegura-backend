using Mono.Cecil;
using Monolith.Stakeholder.Domain.Model.Aggregates;

namespace RutaSegura.Monolith.Stakeholder.Tests.Architecture;

/// <summary>
///     DDD layering of the Stakeholder bounded context inside the monolith, verified on the compiled IL
///     (same approach as tests/RutaSegura.Architecture.Tests): Domain ← Application ← Infrastructure/Interfaces.
/// </summary>
public sealed class StakeholderLayeringTests
{
    private const string Context = "Monolith.Stakeholder";

    private static readonly Lazy<AssemblyDefinition> MonolithAssembly =
        new(() => AssemblyDefinition.ReadAssembly(typeof(Student).Assembly.Location));

    private static readonly string[] Frameworks =
        ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Cortex.Mediator", "RabbitMQ", "MySql"];

    public static TheoryData<string, string> ForbiddenDependencies => new()
    {
        // Domain knows nothing about the outer layers, other bounded contexts or frameworks.
        { $"{Context}.Domain", $"{Context}.Application" },
        { $"{Context}.Domain", $"{Context}.Infrastructure" },
        { $"{Context}.Domain", $"{Context}.Interfaces" },
        { $"{Context}.Domain", "Monolith.Iam" },
        { $"{Context}.Domain", "Monolith.Administration" },
        // Application orchestrates the domain through ports; adapters live in outer layers.
        { $"{Context}.Application", $"{Context}.Infrastructure" },
        { $"{Context}.Application", $"{Context}.Interfaces" },
        // Iam and Route Planning are reached through ACL ports (IGuardianDirectory, IRoutePlanningService).
        { $"{Context}.Application", "Monolith.Iam" },
        { $"{Context}.Application", "Monolith.Administration" },
        // Adapters (Infrastructure) and REST (Interfaces) are independent outer layers.
        { $"{Context}.Infrastructure", $"{Context}.Interfaces" },
        { $"{Context}.Interfaces", $"{Context}.Infrastructure" }
    };

    /// <summary>The only parts of Iam another bounded context may use: its published facade and security contracts.</summary>
    private static readonly string[] IamPublishedLanguage = ["Monolith.Iam.Interfaces.ACL", "Monolith.Iam.Interfaces.Security"];

    [Theory]
    [MemberData(nameof(ForbiddenDependencies))]
    public void Layer_DoesNotDependOn(string layer, string forbidden)
    {
        var violations = TypesIn(layer)
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => IsIn(EffectiveNamespace(pair.dependency), forbidden))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData($"{Context}.Domain")]
    [InlineData($"{Context}.Application")]
    public void Layer_DoesNotUseInfrastructureFrameworks(string layer)
    {
        var violations = TypesIn(layer)
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => Frameworks.Any(framework => IsIn(EffectiveNamespace(pair.dependency), framework)))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Context_HasTheFourLayers()
    {
        TypesIn($"{Context}.Domain").ShouldNotBeEmpty();
        TypesIn($"{Context}.Application").ShouldNotBeEmpty();
        TypesIn($"{Context}.Infrastructure").ShouldNotBeEmpty();
        TypesIn($"{Context}.Interfaces").ShouldNotBeEmpty();
    }

    [Fact]
    public void Context_UsesIamOnlyThroughItsPublishedFacadeAndSecurityContracts()
    {
        var violations = TypesIn(Context)
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => IsIn(EffectiveNamespace(pair.dependency), "Monolith.Iam") &&
                           !IamPublishedLanguage.Any(allowed => IsIn(EffectiveNamespace(pair.dependency), allowed)))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    /// <summary>Route Planning is used only through its published facade (Monolith.Administration.Interfaces.ACL).</summary>
    [Fact]
    public void Context_UsesAdministrationOnlyThroughItsPublishedFacade()
    {
        var violations = TypesIn(Context)
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => IsIn(EffectiveNamespace(pair.dependency), "Monolith.Administration") &&
                           !IsIn(EffectiveNamespace(pair.dependency), "Monolith.Administration.Interfaces.ACL"))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    /// <summary>The dependency goes one way: Route Planning knows nothing about parents and children.</summary>
    [Fact]
    public void Administration_DoesNotDependOnStakeholder()
    {
        var violations = TypesIn("Monolith.Administration")
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => IsIn(EffectiveNamespace(pair.dependency), Context))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void Guard_DetectsAForbiddenDependencyInAMethodBody()
    {
        // Self-test: a fixture type whose method touches an Infrastructure type must be reported.
        using var fixture = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Fixture", new Version(1, 0)),
            "Fixture", ModuleKind.Dll);
        var module = fixture.MainModule;
        var domainType = new TypeDefinition($"{Context}.Domain.Model.Aggregates", "Leaky", TypeAttributes.Public,
            module.TypeSystem.Object);
        var adapter = new TypeDefinition($"{Context}.Infrastructure.Persistence", "Adapter", TypeAttributes.Public,
            module.TypeSystem.Object);
        module.Types.Add(domainType);
        module.Types.Add(adapter);
        var method = new MethodDefinition("Use", MethodAttributes.Public | MethodAttributes.Static,
            module.TypeSystem.Void);
        method.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldtoken, adapter));
        method.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Pop));
        method.Body.Instructions.Add(Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ret));
        domainType.Methods.Add(method);

        Dependencies(domainType).ShouldContain(dependency => dependency.FullName == adapter.FullName);
    }

    // ── IL inspection ────────────────────────────────────────────────────────

    private static List<TypeDefinition> TypesIn(string layer) =>
        AllTypes(MonolithAssembly.Value.MainModule.Types).Where(type => IsIn(EffectiveNamespace(type), layer)).ToList();

    private static bool IsIn(string ns, string prefix) =>
        ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal);

    private static string EffectiveNamespace(TypeReference type) =>
        !string.IsNullOrEmpty(type.Namespace) ? type.Namespace :
        type.DeclaringType is not null ? EffectiveNamespace(type.DeclaringType) : string.Empty;

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in AllTypes(type.NestedTypes))
                yield return nested;
        }
    }

    private static IEnumerable<TypeReference> Expand(TypeReference? type)
    {
        if (type is null) yield break;
        yield return type;
        if (type is TypeSpecification specification)
            foreach (var element in Expand(specification.ElementType))
                yield return element;
        if (type is GenericInstanceType generic)
            foreach (var argument in generic.GenericArguments)
            foreach (var element in Expand(argument))
                yield return element;
    }

    private static IEnumerable<TypeReference> Dependencies(TypeDefinition type)
    {
        foreach (var dependency in Expand(type.BaseType)) yield return dependency;
        foreach (var contract in type.Interfaces)
        foreach (var dependency in Expand(contract.InterfaceType))
            yield return dependency;
        foreach (var field in type.Fields)
        foreach (var dependency in Expand(field.FieldType))
            yield return dependency;
        foreach (var property in type.Properties)
        foreach (var dependency in Expand(property.PropertyType))
            yield return dependency;
        foreach (var attribute in type.CustomAttributes)
        foreach (var dependency in Expand(attribute.AttributeType))
            yield return dependency;

        foreach (var method in type.Methods)
        {
            foreach (var dependency in Expand(method.ReturnType)) yield return dependency;
            foreach (var parameter in method.Parameters)
            foreach (var dependency in Expand(parameter.ParameterType))
                yield return dependency;
            if (!method.HasBody) continue;
            foreach (var variable in method.Body.Variables)
            foreach (var dependency in Expand(variable.VariableType))
                yield return dependency;
            foreach (var handler in method.Body.ExceptionHandlers)
            foreach (var dependency in Expand(handler.CatchType))
                yield return dependency;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is TypeReference referenced)
                    foreach (var dependency in Expand(referenced))
                        yield return dependency;
                if (instruction.Operand is MemberReference member)
                    foreach (var dependency in Expand(member.DeclaringType))
                        yield return dependency;
                if (instruction.Operand is MethodReference called)
                {
                    foreach (var dependency in Expand(called.ReturnType)) yield return dependency;
                    foreach (var parameter in called.Parameters)
                    foreach (var dependency in Expand(parameter.ParameterType))
                        yield return dependency;
                }
            }
        }
    }
}

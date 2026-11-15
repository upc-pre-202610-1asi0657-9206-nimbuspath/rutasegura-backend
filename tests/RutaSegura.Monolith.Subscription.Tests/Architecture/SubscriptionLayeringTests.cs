using Mono.Cecil;
using Monolith.Subscription.Domain.Model.Aggregates;

namespace RutaSegura.Monolith.Subscription.Tests.Architecture;

/// <summary>
///     DDD layering of the Subscription bounded context inside the monolith, verified on the compiled IL (same
///     approach as the Administration and Stakeholder suites): Domain ← Application ← Infrastructure/Interfaces, and the
///     other contexts reach Subscription only through its published facade.
/// </summary>
public sealed class SubscriptionLayeringTests
{
    private const string Context = "Monolith.Subscription";
    private const string PublishedLanguage = "Monolith.Subscription.Interfaces.ACL";

    private static readonly Lazy<AssemblyDefinition> MonolithAssembly =
        new(() => AssemblyDefinition.ReadAssembly(typeof(TenantSubscription).Assembly.Location));

    private static readonly string[] Frameworks =
        ["Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Cortex.Mediator", "RabbitMQ", "MySql"];

    public static TheoryData<string, string> ForbiddenDependencies => new()
    {
        // Domain knows nothing about the outer layers, other bounded contexts or frameworks.
        { $"{Context}.Domain", $"{Context}.Application" },
        { $"{Context}.Domain", $"{Context}.Infrastructure" },
        { $"{Context}.Domain", $"{Context}.Interfaces" },
        { $"{Context}.Domain", "Monolith.Iam" },
        // Application orchestrates the domain through ports; adapters live in outer layers.
        { $"{Context}.Application", $"{Context}.Infrastructure" },
        { $"{Context}.Application", $"{Context}.Interfaces" },
        { $"{Context}.Application", "Monolith.Iam" },
        // Adapters (Infrastructure) and REST (Interfaces) are independent outer layers.
        { $"{Context}.Infrastructure", $"{Context}.Interfaces" },
        { $"{Context}.Interfaces", $"{Context}.Infrastructure" },
        // Subscription is the supplier: it never depends on its customers.
        { Context, "Monolith.Administration" },
        { Context, "Monolith.Stakeholder" }
    };

    /// <summary>The only parts of Iam Subscription may use: its published facade and security contracts.</summary>
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

    [Theory]
    [InlineData("Monolith.Administration")]
    [InlineData("Monolith.Stakeholder")]
    [InlineData("Monolith.Iam")]
    public void OtherContexts_UseSubscriptionOnlyThroughItsFacade(string otherContext)
    {
        var violations = TypesIn(otherContext)
            .SelectMany(type => Dependencies(type).Select(dependency => (type, dependency)))
            .Where(pair => IsIn(EffectiveNamespace(pair.dependency), Context) &&
                           !IsIn(EffectiveNamespace(pair.dependency), PublishedLanguage))
            .Select(pair => $"{pair.type.FullName} -> {pair.dependency.FullName}")
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void RoutePlanning_ReadsThePlanLimitsFromSubscription()
    {
        var adapter = TypesIn("Monolith.Administration.Infrastructure.Subscription");

        adapter.ShouldNotBeEmpty();
        adapter.SelectMany(Dependencies).ShouldContain(dependency =>
            dependency.FullName == $"{PublishedLanguage}.ISubscriptionContextFacade");
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

using Mono.Cecil;

namespace RutaSegura.Architecture.Tests;

/// <summary>Inspecciona referencias y tipos de los binarios compilados; no carga sus dependencias.</summary>
internal static class ArchitectureRules
{
    private static readonly string[] Services = ["IAM", "Administration", "TripTracking", "Notification"];
    private static readonly string[] Modules = ["Roster", "RoutePlanning", "Subscription"];

    internal static bool IsAllowedReference(string source, string target)
    {
        if (target == "RutaSegura.BuildingBlocks.Testing")
            return false;
        if ((source.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal) || source == "RutaSegura.ApiGateway") &&
            Services.Any(s => target.StartsWith($"RutaSegura.{s}.", StringComparison.Ordinal)))
            return false;
        var owner = Services.FirstOrDefault(s => source.StartsWith($"RutaSegura.{s}.", StringComparison.Ordinal));
        if (owner is not null && Services.Any(s => s != owner && target.StartsWith($"RutaSegura.{s}.", StringComparison.Ordinal)))
            return false;
        var system = target == "netstandard" || target == "mscorlib" || target == "System" ||
            target.StartsWith("System.", StringComparison.Ordinal);
        if (source == "RutaSegura.BuildingBlocks.Messaging")
            return system;
        if (source.EndsWith(".Domain", StringComparison.Ordinal))
            return system || target == "RutaSegura.BuildingBlocks.Domain";
        if (source.EndsWith(".Application", StringComparison.Ordinal))
            return system || target == $"RutaSegura.{owner}.Domain" ||
                target is "RutaSegura.BuildingBlocks.Domain" or "RutaSegura.BuildingBlocks.Application" ||
                (target.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) &&
                 target.EndsWith(".Abstractions", StringComparison.Ordinal));
        if (source.EndsWith(".Infrastructure", StringComparison.Ordinal) && target.StartsWith("RutaSegura.", StringComparison.Ordinal))
            return target == $"RutaSegura.{owner}.Domain" || target == $"RutaSegura.{owner}.Application" ||
                target.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal);
        if (source.EndsWith(".Api", StringComparison.Ordinal) && target.StartsWith("RutaSegura.", StringComparison.Ordinal))
            return target == $"RutaSegura.{owner}.Application" || target == $"RutaSegura.{owner}.Infrastructure" ||
                target.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal);
        return true;
    }

    internal static IEnumerable<string> AssemblyViolations(AssemblyDefinition assembly) =>
        assembly.MainModule.AssemblyReferences
            .Where(r => !IsAllowedReference(assembly.Name.Name, r.Name))
            .Select(r => $"{assembly.Name.Name} -> {r.Name}");

    internal static IEnumerable<string> ModuleViolations(AssemblyDefinition assembly)
    {
        foreach (var type in AllTypes(assembly.MainModule.Types))
        {
            var sourceNamespace = EffectiveNamespace(type);
            var source = ModuleOf(sourceNamespace);
            if (source is null)
                continue;
            foreach (var dependency in Dependencies(type))
            {
                var targetNamespace = EffectiveNamespace(dependency);
                var target = ModuleOf(targetNamespace);
                if (target is null || source == target)
                    continue;
                // Only Application's published contracts may cross a module boundary.
                var permitted = !sourceNamespace.Contains(".Domain.", StringComparison.Ordinal) &&
                    targetNamespace.StartsWith($"RutaSegura.Administration.Application.{target}.Contracts", StringComparison.Ordinal) &&
                    (targetNamespace == $"RutaSegura.Administration.Application.{target}.Contracts" ||
                     targetNamespace.StartsWith($"RutaSegura.Administration.Application.{target}.Contracts.", StringComparison.Ordinal));
                if (!permitted)
                    yield return $"{type.FullName} -> {dependency.FullName}";
            }
        }
    }

    private static string? ModuleOf(string ns) => Modules.FirstOrDefault(module =>
        new[] { "Domain", "Application", "Infrastructure", "Api" }.Any(layer =>
            ns == $"RutaSegura.Administration.{layer}.{module}" ||
            ns.StartsWith($"RutaSegura.Administration.{layer}.{module}.", StringComparison.Ordinal)));

    private static string EffectiveNamespace(TypeReference type) =>
        !string.IsNullOrEmpty(type.Namespace) ? type.Namespace :
        type.DeclaringType is not null ? EffectiveNamespace(type.DeclaringType) : "";

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
            foreach (var element in Expand(specification.ElementType)) yield return element;
        if (type is GenericInstanceType generic)
            foreach (var argument in generic.GenericArguments)
                foreach (var element in Expand(argument)) yield return element;
    }

    private static IEnumerable<TypeReference> Dependencies(TypeDefinition type)
    {
        foreach (var dependency in Expand(type.BaseType)) yield return dependency;
        foreach (var contract in type.Interfaces)
            foreach (var dependency in Expand(contract.InterfaceType)) yield return dependency;
        foreach (var parameter in type.GenericParameters)
            foreach (var constraint in parameter.Constraints)
                foreach (var dependency in Expand(constraint.ConstraintType)) yield return dependency;
        foreach (var field in type.Fields)
            foreach (var dependency in Expand(field.FieldType)) yield return dependency;
        foreach (var property in type.Properties)
            foreach (var dependency in Expand(property.PropertyType)) yield return dependency;
        foreach (var @event in type.Events)
            foreach (var dependency in Expand(@event.EventType)) yield return dependency;
        foreach (var attribute in type.CustomAttributes)
            foreach (var dependency in Expand(attribute.AttributeType)) yield return dependency;
        foreach (var method in type.Methods)
        {
            foreach (var dependency in Expand(method.ReturnType)) yield return dependency;
            foreach (var parameter in method.Parameters)
                foreach (var dependency in Expand(parameter.ParameterType)) yield return dependency;
            foreach (var parameter in method.GenericParameters)
                foreach (var constraint in parameter.Constraints)
                    foreach (var dependency in Expand(constraint.ConstraintType)) yield return dependency;
            if (!method.HasBody) continue;
            foreach (var variable in method.Body.Variables)
                foreach (var dependency in Expand(variable.VariableType)) yield return dependency;
            foreach (var handler in method.Body.ExceptionHandlers)
                foreach (var dependency in Expand(handler.CatchType)) yield return dependency;
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is TypeReference referencedType)
                    foreach (var dependency in Expand(referencedType)) yield return dependency;
                if (instruction.Operand is MemberReference member)
                    foreach (var dependency in Expand(member.DeclaringType)) yield return dependency;
                if (instruction.Operand is FieldReference field)
                    foreach (var dependency in Expand(field.FieldType)) yield return dependency;
                if (instruction.Operand is MethodReference called)
                {
                    foreach (var dependency in Expand(called.ReturnType)) yield return dependency;
                    foreach (var parameter in called.Parameters)
                        foreach (var dependency in Expand(parameter.ParameterType)) yield return dependency;
                    if (called is GenericInstanceMethod generic)
                        foreach (var argument in generic.GenericArguments)
                            foreach (var dependency in Expand(argument)) yield return dependency;
                }
            }
        }
    }
}

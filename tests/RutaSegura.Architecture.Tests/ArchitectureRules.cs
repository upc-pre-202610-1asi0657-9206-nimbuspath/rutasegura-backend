using Mono.Cecil;

namespace RutaSegura.Architecture.Tests;

/// <summary>Inspecciona referencias y tipos de los binarios compilados; no carga sus dependencias.</summary>
internal static class ArchitectureRules
{
    // IAM y Administration ya no son microservicios: viven como bounded contexts del monolito
    // (src/Monolith/Iam, src/Monolith/Administration) y sus reglas se prueban en tests/RutaSegura.Monolith.*.
    private static readonly string[] Services = ["TripTracking", "Notification"];

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
        if (source.EndsWith(".Interfaces", StringComparison.Ordinal) && target.StartsWith("RutaSegura.", StringComparison.Ordinal))
            return target == $"RutaSegura.{owner}.Application" ||
                target.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal);
        if (source.EndsWith(".Api", StringComparison.Ordinal) && target.StartsWith("RutaSegura.", StringComparison.Ordinal))
            return target == $"RutaSegura.{owner}.Application" || target == $"RutaSegura.{owner}.Infrastructure" ||
                target == $"RutaSegura.{owner}.Interfaces" ||
                target.StartsWith("RutaSegura.BuildingBlocks.", StringComparison.Ordinal);
        return true;
    }

    internal static IEnumerable<string> AssemblyViolations(AssemblyDefinition assembly) =>
        assembly.MainModule.AssemblyReferences
            .Where(r => !IsAllowedReference(assembly.Name.Name, r.Name))
            .Select(r => $"{assembly.Name.Name} -> {r.Name}");
}

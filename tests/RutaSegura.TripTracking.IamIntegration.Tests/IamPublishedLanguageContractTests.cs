extern alias monolith;

using System.Text.Json;
using RutaSegura.TripTracking.Api.Security;
using IamClaims = monolith::Monolith.Iam.Interfaces.Security.IamClaimTypes;
using IamRoles = monolith::Monolith.Iam.Interfaces.ACL.IamRoles;

namespace RutaSegura.TripTracking.IamIntegration.Tests;

/// <summary>
/// Trip &amp; Tracking no referencia al monolito en producción: mantiene una copia del Published Language de Iam
/// (roles, claims, emisor y audiencia). Estas pruebas fallan si alguno de los dos lados cambia sin el otro.
/// </summary>
public sealed class IamPublishedLanguageContractTests
{
    [Fact]
    public void Roles_AreTheOnesIamIssues()
    {
        Roles.Admin.ShouldBe(IamRoles.Admin);
        Roles.Driver.ShouldBe(IamRoles.Driver);
        Roles.Parent.ShouldBe(IamRoles.Parent);
    }

    [Fact]
    public void Claims_AreReadAsIamWritesThem()
    {
        IamClaimTypes.UserId.ShouldBe(IamClaims.UserId);
        IamClaimTypes.Role.ShouldBe(IamClaims.Role);
        IamClaimTypes.Email.ShouldBe(IamClaims.Email);
        IamClaimTypes.Name.ShouldBe(IamClaims.Name);
        IamClaimTypes.OrganizationId.ShouldBe(IamClaims.OrganizationId);
    }

    [Fact]
    public void DefaultIssuerAndAudience_MatchTheOnesIamWrites()
    {
        var iam = Settings("src/Monolith/appsettings.json").GetProperty("TokenSettings");
        var tripTracking = Settings("src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api/appsettings.json").GetProperty("Jwt");

        tripTracking.GetProperty("Issuer").GetString().ShouldBe(iam.GetProperty("Issuer").GetString());
        tripTracking.GetProperty("Audience").GetString().ShouldBe(iam.GetProperty("Audience").GetString());
    }

    [Fact]
    public void DevelopmentSecrets_LetTripTrackingValidateTheTokensOfTheLocalIam()
    {
        var iam = Settings("src/Monolith/appsettings.Development.json").GetProperty("TokenSettings");
        var tripTracking = Settings("src/Services/Trip-Tracking/src/RutaSegura.TripTracking.Api/appsettings.Development.json");

        tripTracking.GetProperty("Jwt").GetProperty("Secret").GetString().ShouldBe(iam.GetProperty("Secret").GetString());
        tripTracking.GetProperty("Iam").GetProperty("BaseUrl").GetString().ShouldBe("http://localhost:5000",
            "El perfil http del monolito escucha en el puerto 5000");
    }

    [Fact]
    public void DockerCompose_SharesTheSameJwtSettingsAndPointsTripTrackingToIam()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot(), "docker-compose.yml"));

        compose.ShouldContain("TokenSettings__Secret: *jwt-secret");
        compose.ShouldContain("Jwt__Secret: *jwt-secret");
        compose.ShouldContain("Jwt__Issuer: *jwt-issuer");
        compose.ShouldContain("Jwt__Audience: *jwt-audience");
        compose.ShouldContain("Iam__BaseUrl: http://monolith:8080");
    }

    private static JsonElement Settings(string relativePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), relativePath)),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return document.RootElement.Clone();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RutaSegura.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("No se encontró RutaSegura.slnx");
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Monolith.Administration.Infrastructure.Messaging.Outbox;
using Monolith.Stakeholder.Infrastructure.Messaging;
using RutaSegura.Monolith.Administration.IntegrationTests.Support;

namespace RutaSegura.Monolith.Administration.IntegrationTests;

/// <summary>
///     Stakeholder bounded context end to end on the real monolith host: a parent registers children, enrolls them
///     with the code the transport company shares, and Route Planning places them on the stops of their pickup
///     addresses. The administrator and the driver then see the children with name, stop and guardian phone.
/// </summary>
public sealed class StakeholderEndToEndTests : IDisposable
{
    // Points in Surco (Lima): Primavera and its neighbor are ~90 m apart; Olivos is ~700 m away.
    private static readonly (double Lat, double Lng) Primavera = (-12.11234, -76.98567);
    private static readonly (double Lat, double Lng) PrimaveraNeighbor = (-12.11300, -76.98510);
    private static readonly (double Lat, double Lng) Olivos = (-12.10890, -76.99120);
    private static readonly (double Lat, double Lng) Benavides = (-12.12500, -76.99800);

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89
    ];

    private readonly MonolithApp _app = new(maxActiveRoutes: 3);

    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Parent_RegistersChild_EnrollsWithCode_AndTheRouteGetsAStopAtThePickupAddress()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var parent = await SignUpParentAsync("rosa@familia.pe");

        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera, grade: "3.° de primaria");
        valeria.GetProperty("fullName").GetString().ShouldBe("Valeria Quispe");
        valeria.GetProperty("schoolGrade").GetString().ShouldBe("3.° de primaria");
        valeria.GetProperty("pickupLocation").GetProperty("address").GetString().ShouldBe("Av. Primavera 1240, Surco");

        // The parent sees the route behind the code before enrolling.
        var offer = await parent.Get($"/api/v1/stakeholders/route-offers/{code.ToLowerInvariant()}")
            .ExpectAsync(HttpStatusCode.OK);
        offer.GetProperty("routeId").GetGuid().ShouldBe(routeId);
        offer.GetProperty("organizationName").GetString().ShouldNotBeNullOrWhiteSpace();
        offer.GetProperty("direction").GetString().ShouldBe("PICKUP");
        offer.GetProperty("acceptsEnrollments").GetBoolean().ShouldBeTrue();

        var enrollment = await parent.PostJson($"/api/v1/stakeholders/me/students/{valeria.Id()}/enrollments",
            new { enrollmentCode = code }).ExpectAsync(HttpStatusCode.Created);
        enrollment.GetProperty("status").GetString().ShouldBe("ACTIVE");
        enrollment.GetProperty("routeStatus").GetString().ShouldBe("DRAFT");
        var stop = enrollment.GetProperty("stop");
        stop.GetProperty("name").GetString().ShouldBe("Av. Primavera 1240, Surco");
        stop.GetProperty("sequence").GetInt32().ShouldBe(1);

        // Route Planning now has an automatic stop with the child, at the pickup point.
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(1);
        var routeStop = route.GetProperty("stops")[0];
        routeStop.GetProperty("origin").GetString().ShouldBe("ENROLLMENT");
        routeStop.GetProperty("latitude").GetDouble().ShouldBe(Primavera.Lat);
        routeStop.GetProperty("studentIds")[0].GetGuid().ShouldBe(valeria.Id());

        // Trip & Tracking learns the guardian → student link (outbox of stakeholder.events).
        var messages = await _app.WithDbAsync(db =>
            db.Set<StakeholderOutboxMessage>().OrderBy(m => m.CreatedAt).ToListAsync());
        messages.Select(m => m.MessageType).ShouldBe(
            ["stakeholder.guardian-student-linked", "stakeholder.student-enrolled"], ignoreOrder: false);
        var link = JsonDocument.Parse(messages[0].Payload).RootElement;
        link.GetProperty("studentId").GetGuid().ShouldBe(valeria.Id());
        link.GetProperty("guardianId").GetGuid().ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Enrollments_NearbyChildrenShareAStop_FarOnesGetANewStopInTheCheapestPosition()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var rosa = await SignUpParentAsync("rosa@familia.pe");
        var jorge = await SignUpParentAsync("jorge@familia.pe");

        var valeria = await RegisterChildAsync(rosa, "Valeria", "Quispe", Primavera);
        var mateo = await RegisterChildAsync(jorge, "Mateo", "Flores", PrimaveraNeighbor);
        var lucia = await RegisterChildAsync(jorge, "Lucia", "Flores", Benavides);
        var diego = await RegisterChildAsync(rosa, "Diego", "Quispe", Olivos);

        await EnrollAsync(rosa, valeria.Id(), code);
        var mateoEnrollment = await EnrollAsync(jorge, mateo.Id(), code);
        await EnrollAsync(jorge, lucia.Id(), code);
        await EnrollAsync(rosa, diego.Id(), code);

        // Mateo lives ~90 m from Valeria: same stop.
        mateoEnrollment.GetProperty("stop").GetProperty("name").GetString().ShouldBe("Av. Primavera 1240, Surco");

        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(4);
        var stops = route.GetProperty("stops").EnumerateArray().ToList();
        stops.Count.ShouldBe(3);
        stops.Select(s => s.GetProperty("sequence").GetInt32()).ShouldBe([1, 2, 3]);
        stops.ShouldAllBe(s => s.GetProperty("origin").GetString() == "ENROLLMENT");
        // Olivos (north-west of Primavera) is inserted before Benavides (south-west): cheapest insertion.
        stops.Select(s => s.GetProperty("studentIds").GetArrayLength()).Sum().ShouldBe(4);
        stops.Single(s => s.GetProperty("studentIds").GetArrayLength() == 2)
            .GetProperty("studentIds").EnumerateArray().Select(e => e.GetGuid())
            .ShouldBe([valeria.Id(), mateo.Id()], ignoreOrder: true);
    }

    [Fact]
    public async Task NewPickupAddress_MovesTheChild_AndRemovesTheEmptyAutomaticStop()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);
        await EnrollAsync(parent, valeria.Id(), code);

        var moved = await parent.PutJson($"/api/v1/stakeholders/me/students/{valeria.Id()}/pickup-location", new
        {
            address = "Calle Los Olivos 123, Surco",
            reference = "Puerta verde",
            latitude = Olivos.Lat,
            longitude = Olivos.Lng
        }).ExpectAsync(HttpStatusCode.OK);
        moved.GetProperty("pickupLocation").GetProperty("reference").GetString().ShouldBe("Puerta verde");
        moved.GetProperty("enrollments")[0].GetProperty("stop").GetProperty("name").GetString()
            .ShouldBe("Calle Los Olivos 123, Surco");

        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        var stops = route.GetProperty("stops");
        stops.GetArrayLength().ShouldBe(1, "the stop at the old address was automatic and became empty");
        stops[0].GetProperty("latitude").GetDouble().ShouldBe(Olivos.Lat);
    }

    [Fact]
    public async Task ManualStopsOfTheAdministrator_AreReusedAndKept()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        await admin.PostJson($"/api/v1/routes/{routeId}/stops",
                new { name = "Parque Primavera", latitude = Primavera.Lat, longitude = Primavera.Lng })
            .ExpectAsync(HttpStatusCode.OK);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var mateo = await RegisterChildAsync(parent, "Mateo", "Flores", PrimaveraNeighbor);

        var enrollment = await EnrollAsync(parent, mateo.Id(), code);
        enrollment.GetProperty("stop").GetProperty("name").GetString().ShouldBe("Parque Primavera");

        await parent.Delete($"/api/v1/stakeholders/me/students/{mateo.Id()}/enrollments/{enrollment.Id()}")
            .ExpectAsync(HttpStatusCode.OK);
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(0);
        route.GetProperty("stops").GetArrayLength().ShouldBe(1, "manual stops stay when they become empty");
        route.GetProperty("stops")[0].GetProperty("origin").GetString().ShouldBe("MANUAL");
    }

    [Fact]
    public async Task ActiveRoute_EnrollmentPublishesRouteUpdatedForTripTracking()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code, _) = await ActiveRouteAsync(admin, capacity: 15);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Olivos);

        await EnrollAsync(parent, valeria.Id(), code);

        var updated = await _app.WithDbAsync(db => db.Set<OutboxMessage>()
            .Where(m => m.MessageType == "administration.route-updated").ToListAsync());
        updated.Count.ShouldBe(1);
        var snapshot = JsonDocument.Parse(updated[0].Payload).RootElement;
        snapshot.GetProperty("routeId").GetGuid().ShouldBe(routeId);
        snapshot.GetProperty("students").EnumerateArray()
            .ShouldContain(s => s.GetProperty("studentId").GetGuid() == valeria.Id());
        snapshot.GetProperty("stops").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task FullRoute_RejectsTheEnrollment_AndNothingIsSaved()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code, _) = await ActiveRouteAsync(admin, capacity: 1);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);
        var diego = await RegisterChildAsync(parent, "Diego", "Quispe", Olivos);
        await EnrollAsync(parent, valeria.Id(), code);

        var problem = await parent.PostJson($"/api/v1/stakeholders/me/students/{diego.Id()}/enrollments",
            new { enrollmentCode = code }).ExpectAsync(HttpStatusCode.Conflict);
        problem.Code().ShouldBe("RouteFull");

        var diegoNow = await parent.Get($"/api/v1/stakeholders/me/students/{diego.Id()}").ExpectAsync(HttpStatusCode.OK);
        diegoNow.GetProperty("enrollments").GetArrayLength().ShouldBe(0, "the enrollment was rolled back");
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task EnrollmentRules_CodeDirectionAndDuplicates()
    {
        var admin = await _app.AdminAsync();
        var (_, pickupCode) = await DraftRouteAsync(admin, "Ruta 1 - Recojo");
        var (_, otherPickupCode) = await DraftRouteAsync(admin, "Ruta 2 - Recojo");
        var (_, returnCode) = await DraftRouteAsync(admin, "Ruta 1 - Retorno", "RETURN", "13:30:00", "14:40:00");
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);
        var url = $"/api/v1/stakeholders/me/students/{valeria.Id()}/enrollments";

        (await parent.PostJson(url, new { enrollmentCode = "ZZZZ2222" }).ExpectAsync(HttpStatusCode.NotFound))
            .Code().ShouldBe("EnrollmentCodeNotFound");
        await EnrollAsync(parent, valeria.Id(), pickupCode);
        (await parent.PostJson(url, new { enrollmentCode = pickupCode }).ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("StudentAlreadyEnrolled");
        (await parent.PostJson(url, new { enrollmentCode = otherPickupCode }).ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("EnrollmentDirectionTaken");
        var back = await EnrollAsync(parent, valeria.Id(), returnCode);
        back.GetProperty("direction").GetString().ShouldBe("RETURN");
    }

    [Fact]
    public async Task InactiveRoute_DoesNotAcceptEnrollments()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code, _) = await ActiveRouteAsync(admin, capacity: 10);
        await admin.PostJson($"/api/v1/routes/{routeId}/deactivate").ExpectAsync(HttpStatusCode.OK);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);

        (await parent.PostJson($"/api/v1/stakeholders/me/students/{valeria.Id()}/enrollments",
                new { enrollmentCode = code }).ExpectAsync(HttpStatusCode.Conflict))
            .Code().ShouldBe("RouteNotAcceptingEnrollments");
    }

    [Fact]
    public async Task Driver_SeesTheChildrenOfItsRoutes_WithStopAndGuardianPhone_ButNotOtherRoutes()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code, _) = await ActiveRouteAsync(admin, capacity: 15);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        await parent.PutJson("/api/v1/stakeholders/me/profile", new { phoneNumber = "987 654 321", documentNumber = "45781236" })
            .ExpectAsync(HttpStatusCode.OK);
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Olivos, grade: "3.° de primaria");
        await EnrollAsync(parent, valeria.Id(), code);
        var driver = await _app.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);

        var students = await driver.Get($"/api/v1/stakeholders/routes/{routeId}/students").ExpectAsync(HttpStatusCode.OK);
        students.GetArrayLength().ShouldBe(1);
        var row = students[0];
        row.GetProperty("studentId").GetGuid().ShouldBe(valeria.Id());
        row.GetProperty("fullName").GetString().ShouldBe("Valeria Quispe");
        row.GetProperty("schoolGrade").GetString().ShouldBe("3.° de primaria");
        row.GetProperty("stop").GetProperty("sequence").GetInt32().ShouldBe(2);
        row.GetProperty("guardian").GetProperty("phoneNumber").GetString().ShouldBe("987654321");
        row.GetProperty("guardian").GetProperty("fullName").GetString().ShouldBe("Rosa Quispe");
        row.GetProperty("guardian").GetProperty("email").ValueKind.ShouldBe(JsonValueKind.Null, "only the admin sees it");

        var mine = await driver.Get("/api/v1/stakeholders/drivers/me/students").ExpectAsync(HttpStatusCode.OK);
        mine.GetArrayLength().ShouldBe(1);
        mine[0].GetProperty("routeId").GetGuid().ShouldBe(routeId);

        // Another route of the same company, operated by nobody: the driver may not read it.
        var (otherRoute, _) = await DraftRouteAsync(admin, "Ruta 9 - Otra");
        (await driver.Get($"/api/v1/stakeholders/routes/{otherRoute}/students").ExpectAsync(HttpStatusCode.Forbidden))
            .Code().ShouldBe("RouteNotOperatedByDriver");

        // The administrator sees the same child with the guardian e-mail.
        var adminView = await admin.Get($"/api/v1/stakeholders/routes/{routeId}/students").ExpectAsync(HttpStatusCode.OK);
        adminView[0].GetProperty("guardian").GetProperty("email").GetString().ShouldBe("rosa@familia.pe");

        // The vehicle of the route is now readable by the driver (mobile app header).
        var vehicles = await driver.Get("/api/v1/roster/drivers/me/vehicles").ExpectAsync(HttpStatusCode.OK);
        vehicles.GetArrayLength().ShouldBe(1);
        vehicles[0].GetProperty("licensePlate").GetString().ShouldBe("ABC123");
        vehicles[0].GetProperty("routeIds")[0].GetGuid().ShouldBe(routeId);

        // …and the driver no longer sees the enrollment code of its routes.
        var driverRoutes = await driver.Get("/api/v1/roster/drivers/me/routes").ExpectAsync(HttpStatusCode.OK);
        driverRoutes[0].GetProperty("enrollmentCode").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task Photo_OnlyTheGuardianAndTheStaffOfItsRoutesCanDownloadIt()
    {
        var admin = await _app.AdminAsync();
        var (_, code, _) = await ActiveRouteAsync(admin, capacity: 15);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var stranger = await SignUpParentAsync("otro@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Olivos);
        var photoUrl = $"/api/v1/stakeholders/students/{valeria.Id()}/photo";

        // Not an image → 422.
        (await UploadPhotoAsync(parent, valeria.Id(), "hola"u8.ToArray()).ExpectAsync(HttpStatusCode.UnprocessableEntity))
            .Code().ShouldBe("StudentPhotoInvalid");

        var withPhoto = await UploadPhotoAsync(parent, valeria.Id(), TinyPng).ExpectAsync(HttpStatusCode.OK);
        withPhoto.GetProperty("photo").GetProperty("contentType").GetString().ShouldBe("image/png");
        withPhoto.GetProperty("photo").GetProperty("url").GetString().ShouldStartWith(photoUrl);

        using (var own = await parent.GetAsync(photoUrl, TestContext.Current.CancellationToken))
        {
            own.StatusCode.ShouldBe(HttpStatusCode.OK);
            own.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
            (await own.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(TinyPng);
        }

        (await stranger.Get(photoUrl).ExpectAsync(HttpStatusCode.Forbidden)).Code().ShouldBe("StudentAccessDenied");
        var driver = await _app.SignInAsync(SeedAccounts.DriverEmail, SeedAccounts.DriverPassword);
        await driver.Get(photoUrl).ExpectAsync(HttpStatusCode.Forbidden); // not enrolled in its route yet

        await EnrollAsync(parent, valeria.Id(), code);
        using (var driverView = await driver.GetAsync(photoUrl, TestContext.Current.CancellationToken))
            driverView.StatusCode.ShouldBe(HttpStatusCode.OK);
        using (var adminView = await admin.GetAsync(photoUrl, TestContext.Current.CancellationToken))
            adminView.StatusCode.ShouldBe(HttpStatusCode.OK);
        await _app.CreateClient().Get(photoUrl).ExpectAsync(HttpStatusCode.Unauthorized);

        var deleted = await parent.Delete($"/api/v1/stakeholders/me/students/{valeria.Id()}/photo")
            .ExpectAsync(HttpStatusCode.OK);
        deleted.GetProperty("photo").ValueKind.ShouldBe(JsonValueKind.Null);
        (await parent.Get(photoUrl).ExpectAsync(HttpStatusCode.NotFound)).Code().ShouldBe("StudentPhotoNotFound");
    }

    [Fact]
    public async Task Administrator_ListsAndCancelsEnrollmentsOfItsOrganizationOnly()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);
        var enrollment = await EnrollAsync(parent, valeria.Id(), code);

        var list = await admin.Get($"/api/v1/stakeholders/enrollments?routeId={routeId}").ExpectAsync(HttpStatusCode.OK);
        list.GetArrayLength().ShouldBe(1);
        list[0].GetProperty("enrollmentId").GetGuid().ShouldBe(enrollment.Id());

        // Another transport company cannot see or cancel it.
        var (_, _, otherAdmin) = await _app.ProvisionOrganizationAsync("Movilidad Sur", "admin@sur.pe");
        (await otherAdmin.Get("/api/v1/stakeholders/enrollments").ExpectAsync(HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
        (await otherAdmin.PostJson($"/api/v1/stakeholders/enrollments/{enrollment.Id()}/cancel", new { reason = "x" })
            .ExpectAsync(HttpStatusCode.NotFound)).Code().ShouldBe("EnrollmentNotFound");

        var cancelled = await admin.PostJson($"/api/v1/stakeholders/enrollments/{enrollment.Id()}/cancel",
            new { reason = "Servicio impago" }).ExpectAsync(HttpStatusCode.OK);
        cancelled.GetProperty("status").GetString().ShouldBe("CANCELLED");
        cancelled.GetProperty("endReason").GetString().ShouldBe("Servicio impago");

        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(0);
        var child = await parent.Get($"/api/v1/stakeholders/me/students/{valeria.Id()}").ExpectAsync(HttpStatusCode.OK);
        child.GetProperty("enrollments")[0].GetProperty("status").GetString().ShouldBe("CANCELLED");
        child.GetProperty("enrollments")[0].GetProperty("stop").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task RemovingAChild_FreesItsSeat_AndUnlinksItFromTheGuardian()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var parent = await SignUpParentAsync("rosa@familia.pe");
        var valeria = await RegisterChildAsync(parent, "Valeria", "Quispe", Primavera);
        await UploadPhotoAsync(parent, valeria.Id(), TinyPng).ExpectAsync(HttpStatusCode.OK);
        await EnrollAsync(parent, valeria.Id(), code);

        await parent.Delete($"/api/v1/stakeholders/me/students/{valeria.Id()}").ExpectAsync(HttpStatusCode.NoContent);

        await parent.Get($"/api/v1/stakeholders/me/students/{valeria.Id()}").ExpectAsync(HttpStatusCode.NotFound);
        (await parent.Get("/api/v1/stakeholders/me/students").ExpectAsync(HttpStatusCode.OK)).GetArrayLength().ShouldBe(0);
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        route.GetProperty("studentCount").GetInt32().ShouldBe(0);
        route.GetProperty("stops").GetArrayLength().ShouldBe(0);
        var types = await _app.WithDbAsync(db => db.Set<StakeholderOutboxMessage>().Select(m => m.MessageType).ToListAsync());
        types.ShouldContain("stakeholder.guardian-student-unlinked");
        types.ShouldContain("stakeholder.student-enrollment-ended");
        (await _app.WithDbAsync(db => db.Set<global::Monolith.Stakeholder.Infrastructure.Persistence.EFC.StudentPhotoRecord>().CountAsync()))
            .ShouldBe(0);
    }

    [Fact]
    public async Task Security_OnlyParentsManageChildren_AndOnlyTheirOwn()
    {
        var admin = await _app.AdminAsync();
        var rosa = await SignUpParentAsync("rosa@familia.pe");
        var jorge = await SignUpParentAsync("jorge@familia.pe");
        var valeria = await RegisterChildAsync(rosa, "Valeria", "Quispe", Primavera);

        await admin.Get("/api/v1/stakeholders/me/students").ExpectAsync(HttpStatusCode.Forbidden);
        await _app.CreateClient().Get("/api/v1/stakeholders/me/students").ExpectAsync(HttpStatusCode.Unauthorized);
        (await jorge.Get($"/api/v1/stakeholders/me/students/{valeria.Id()}").ExpectAsync(HttpStatusCode.NotFound))
            .Code().ShouldBe("StudentNotFound");
        await jorge.Delete($"/api/v1/stakeholders/me/students/{valeria.Id()}").ExpectAsync(HttpStatusCode.NotFound);
        await rosa.Get("/api/v1/stakeholders/enrollments").ExpectAsync(HttpStatusCode.Forbidden);
        await rosa.Get($"/api/v1/stakeholders/routes/{Guid.NewGuid()}/students").ExpectAsync(HttpStatusCode.Forbidden);

        var invalid = await rosa.PostJson("/api/v1/stakeholders/me/students", new
        {
            firstName = "Ana",
            lastName = "Quispe",
            pickupLocation = new { address = "Av. Primavera 1240", latitude = 0.0, longitude = 0.0 }
        }).ExpectAsync(HttpStatusCode.BadRequest);
        invalid.Code().ShouldBe("InvalidStakeholderData");
    }

    [Fact]
    public async Task EnrollmentCode_CanBeRegenerated_AndTheOldOneStopsWorking()
    {
        var admin = await _app.AdminAsync();
        var (routeId, code) = await DraftRouteAsync(admin);
        var parent = await SignUpParentAsync("rosa@familia.pe");

        var regenerated = await admin.PostJson($"/api/v1/routes/{routeId}/enrollment-code").ExpectAsync(HttpStatusCode.OK);
        var newCode = regenerated.GetProperty("enrollmentCode").GetString()!;
        newCode.ShouldNotBe(code);
        newCode.Length.ShouldBe(8);

        await parent.Get($"/api/v1/stakeholders/route-offers/{code}").ExpectAsync(HttpStatusCode.NotFound);
        await parent.Get($"/api/v1/stakeholders/route-offers/{newCode}").ExpectAsync(HttpStatusCode.OK);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<HttpClient> SignUpParentAsync(string email)
    {
        var name = email.StartsWith("jorge") ? ("Jorge", "Flores") : ("Rosa", "Quispe");
        await _app.CreateClient().PostJson("/api/v1/users", new
        {
            firstName = name.Item1,
            lastName = name.Item2,
            email,
            password = "Password123",
            roleTier = "PARENT"
        }).ExpectAsync(HttpStatusCode.Created);
        return await _app.SignInAsync(email, "Password123");
    }

    private static Task<JsonElement> RegisterChildAsync(HttpClient parent, string firstName, string lastName,
        (double Lat, double Lng) home, string? grade = null)
    {
        var address = home == Primavera ? "Av. Primavera 1240, Surco"
            : home == PrimaveraNeighbor ? "Jr. Las Begonias 210, Surco"
            : home == Olivos ? "Calle Los Olivos 123, Surco"
            : "Av. Benavides 4500, Surco";
        return parent.PostJson("/api/v1/stakeholders/me/students", new
        {
            firstName,
            lastName,
            birthDate = "2017-05-14",
            schoolGrade = grade,
            schoolName = "Colegio San Agustin",
            pickupLocation = new { address, reference = (string?)null, latitude = home.Lat, longitude = home.Lng }
        }).ExpectAsync(HttpStatusCode.Created);
    }

    private static Task<JsonElement> EnrollAsync(HttpClient parent, Guid studentId, string code) =>
        parent.PostJson($"/api/v1/stakeholders/me/students/{studentId}/enrollments", new { enrollmentCode = code })
            .ExpectAsync(HttpStatusCode.Created);

    private static Task<HttpResponseMessage> UploadPhotoAsync(HttpClient parent, Guid studentId, byte[] content)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "photo", "foto.png");
        return parent.PutAsync($"/api/v1/stakeholders/me/students/{studentId}/photo", form,
            TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid RouteId, string Code)> DraftRouteAsync(HttpClient admin,
        string name = "Ruta 3 - Colegio San Agustin", string direction = "PICKUP", string departure = "06:45:00",
        string arrival = "07:50:00")
    {
        var routeId = await admin.CreateRouteAsync(name, departure, arrival, direction);
        var route = await admin.Get($"/api/v1/routes/{routeId}").ExpectAsync(HttpStatusCode.OK);
        return (routeId, route.GetProperty("enrollmentCode").GetString()!);
    }

    /// <summary>Active route with one manual stop (Primavera), the seeded driver and a vehicle of the given capacity.</summary>
    private static async Task<(Guid RouteId, string Code, Guid VehicleId)> ActiveRouteAsync(HttpClient admin,
        int capacity)
    {
        var driverId = await admin.RegisterVerifiedDriverAsync(SeedAccounts.Driver);
        var vehicleId = await admin.RegisterVehicleAsync("ABC-123", capacity);
        var (routeId, code) = await DraftRouteAsync(admin);
        await admin.PostJson($"/api/v1/routes/{routeId}/stops",
                new { name = "Parque Primavera", latitude = Primavera.Lat, longitude = Primavera.Lng })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/assignment", new { driverId, vehicleId })
            .ExpectAsync(HttpStatusCode.OK);
        await admin.PostJson($"/api/v1/routes/{routeId}/activate").ExpectAsync(HttpStatusCode.OK);
        return (routeId, code, vehicleId);
    }
}

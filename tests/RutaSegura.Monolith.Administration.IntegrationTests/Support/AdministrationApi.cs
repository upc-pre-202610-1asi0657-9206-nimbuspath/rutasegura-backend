using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace RutaSegura.Monolith.Administration.IntegrationTests.Support;

/// <summary>Small typed helpers over the Administration REST API, used to arrange scenarios.</summary>
public static class AdministrationApi
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public static async Task<JsonElement> ReadJsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(MonolithApp.Json, Ct);

    public static async Task<JsonElement> ExpectAsync(this Task<HttpResponseMessage> request, HttpStatusCode status)
    {
        using var response = await request;
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.ShouldBe(status, text);
        return string.IsNullOrWhiteSpace(text)
            ? default
            : JsonSerializer.Deserialize<JsonElement>(text, MonolithApp.Json);
    }

    public static Task<HttpResponseMessage> PostJson(this HttpClient client, string url, object? body = null) =>
        client.PostAsJsonAsync(url, body ?? new { }, MonolithApp.Json, Ct);

    public static Task<HttpResponseMessage> PutJson(this HttpClient client, string url, object? body = null) =>
        client.PutAsJsonAsync(url, body ?? new { }, MonolithApp.Json, Ct);

    public static Task<HttpResponseMessage> Get(this HttpClient client, string url) => client.GetAsync(url, Ct);

    public static Task<HttpResponseMessage> Delete(this HttpClient client, string url) => client.DeleteAsync(url, Ct);

    public static Guid Id(this JsonElement json) => json.GetProperty("id").GetGuid();

    public static string Code(this JsonElement problem) => problem.GetProperty("code").GetString()!;

    public static async Task<Guid> RegisterVerifiedDriverAsync(this HttpClient admin, Guid userId,
        string document = "45678912")
    {
        var driver = await admin.PostJson("/api/v1/roster/drivers", new
        {
            userId,
            firstName = "Carlos",
            lastName = "Ramirez",
            documentNumber = document,
            licenseNumber = "Q" + document,
            licenseCategory = "A-IIb",
            licenseExpiresOn = "2030-12-31",
            phoneNumber = "987654321"
        }).ExpectAsync(HttpStatusCode.Created);
        await admin.PostJson($"/api/v1/roster/drivers/{driver.Id()}/verify").ExpectAsync(HttpStatusCode.OK);
        return driver.Id();
    }

    public static async Task<Guid> RegisterVehicleAsync(this HttpClient admin, string plate = "ABC-123",
        int capacity = 15) =>
        (await admin.PostJson("/api/v1/roster/vehicles",
                new { licensePlate = plate, seatingCapacity = capacity, brand = "Toyota", model = "Hiace" })
            .ExpectAsync(HttpStatusCode.Created)).Id();

    public static async Task<Guid> CreateRouteAsync(this HttpClient admin, string name = "Ruta 3 - Colegio San Agustin",
        string departure = "06:45:00", string arrival = "07:50:00", string direction = "PICKUP") =>
        (await admin.PostJson("/api/v1/routes", new
        {
            name,
            direction,
            departureTime = departure,
            estimatedArrivalTime = arrival,
            timeZoneId = (string?)null,
            operatingDays = Weekdays
        }).ExpectAsync(HttpStatusCode.Created)).Id();

    /// <summary>Two stops ~550 m apart; returns the route as the API returns it.</summary>
    public static async Task<JsonElement> AddTwoStopsAsync(this HttpClient admin, Guid routeId)
    {
        await admin.PostJson($"/api/v1/routes/{routeId}/stops",
                new { name = "Av. Primavera 456", address = "Av. Primavera 456, Surco", latitude = -12.11234, longitude = -76.98567 })
            .ExpectAsync(HttpStatusCode.OK);
        return await admin.PostJson($"/api/v1/routes/{routeId}/stops",
                new { name = "Calle Los Olivos 123", latitude = -12.10890, longitude = -76.99120 })
            .ExpectAsync(HttpStatusCode.OK);
    }

    public static Guid StopId(this JsonElement route, int index) =>
        route.GetProperty("stops")[index].GetProperty("id").GetGuid();

    /// <summary>Route with two stops, the given students on the first stop and resources assigned (still DRAFT).</summary>
    public static async Task<Guid> ReadyRouteAsync(this HttpClient admin, Guid driverId, Guid vehicleId,
        string name = "Ruta 3 - Colegio San Agustin", string departure = "06:45:00", string arrival = "07:50:00",
        int students = 2)
    {
        var routeId = await admin.CreateRouteAsync(name, departure, arrival);
        var route = await admin.AddTwoStopsAsync(routeId);
        for (var i = 0; i < students; i++)
            await admin.PutJson($"/api/v1/routes/{routeId}/stops/{route.StopId(0)}/students/{Guid.NewGuid()}")
                .ExpectAsync(HttpStatusCode.OK);
        await admin.PutJson($"/api/v1/routes/{routeId}/assignment", new { driverId, vehicleId })
            .ExpectAsync(HttpStatusCode.OK);
        return routeId;
    }
}

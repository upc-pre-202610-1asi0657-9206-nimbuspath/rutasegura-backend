namespace Monolith.Stakeholder.Domain.Model.ValueObjects;

/// <summary>
///     Where the child is picked up in the morning and dropped off on the way back: the address the parent typed,
///     an optional reference ("puerta verde, frente al parque") and the WGS-84 point chosen on the map.
/// </summary>
public record PickupLocation
{
    public const int AddressMaxLength = 200;
    public const int ReferenceMaxLength = 200;

    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    public PickupLocation()
    {
        Address = string.Empty;
    }

    public PickupLocation(string address, string? reference, double latitude, double longitude)
    {
        var trimmed = (address ?? string.Empty).Trim();
        if (trimmed.Length is < 5 or > AddressMaxLength)
            throw new ArgumentException($"The pickup address is required (5 to {AddressMaxLength} characters).",
                nameof(address));
        var trimmedReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (trimmedReference is { Length: > ReferenceMaxLength })
            throw new ArgumentException($"The reference is too long (max {ReferenceMaxLength} characters).",
                nameof(reference));
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new ArgumentException("Latitude must be between -90 and 90.", nameof(latitude));
        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new ArgumentException("Longitude must be between -180 and 180.", nameof(longitude));
        if (latitude == 0 && longitude == 0)
            throw new ArgumentException("Coordinates (0, 0) are not a valid location.", nameof(latitude));

        Address = trimmed;
        Reference = trimmedReference;
        Latitude = latitude;
        Longitude = longitude;
    }

    public string Address { get; init; }

    public string? Reference { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }
}

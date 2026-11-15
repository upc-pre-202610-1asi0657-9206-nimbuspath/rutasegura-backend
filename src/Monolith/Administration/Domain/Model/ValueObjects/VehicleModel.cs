namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Brand and model of a vehicle, e.g. "Toyota" / "Hiace".</summary>
public record VehicleModel
{
    public const int MaxLength = 50;

    public VehicleModel() : this(string.Empty, string.Empty, false)
    {
    }

    public VehicleModel(string brand, string model) : this(brand, model, true)
    {
    }

    public VehicleModel(string brand, string model, bool validate)
    {
        if (validate)
        {
            if (string.IsNullOrWhiteSpace(brand) || brand.Trim().Length > MaxLength)
                throw new ArgumentException($"Vehicle brand is required (max {MaxLength} characters).", nameof(brand));
            if (string.IsNullOrWhiteSpace(model) || model.Trim().Length > MaxLength)
                throw new ArgumentException($"Vehicle model is required (max {MaxLength} characters).", nameof(model));
        }

        Brand = (brand ?? string.Empty).Trim();
        Model = (model ?? string.Empty).Trim();
    }

    public string Brand { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public override string ToString() => $"{Brand} {Model}".Trim();
}

namespace Monolith.Administration.Domain.Model.ValueObjects;

/// <summary>Number of passenger seats available for students (driver excluded). Used to enforce the aforo.</summary>
public record SeatingCapacity
{
    public const int Min = 1;
    public const int Max = 60;

    public SeatingCapacity() : this(Min, false)
    {
    }

    public SeatingCapacity(int value) : this(value, true)
    {
    }

    public SeatingCapacity(int value, bool validate)
    {
        if (validate && value is < Min or > Max)
            throw new ArgumentException($"Seating capacity must be between {Min} and {Max}.", nameof(value));
        Value = value;
    }

    public int Value { get; init; } = Min;

    public bool CanSeat(int students) => students <= Value;

    public override string ToString() => Value.ToString();
}

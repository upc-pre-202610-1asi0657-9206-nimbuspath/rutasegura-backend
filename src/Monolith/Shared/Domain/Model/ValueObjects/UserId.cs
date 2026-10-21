namespace Monolith.Shared.Domain.Model.ValueObjects;
public record UserId(Guid Identifier)
{
    public UserId() : this(Guid.NewGuid())
    { 
    }
    public static UserId New() => new(Guid.NewGuid());
    public override string ToString() => Identifier.ToString();
}
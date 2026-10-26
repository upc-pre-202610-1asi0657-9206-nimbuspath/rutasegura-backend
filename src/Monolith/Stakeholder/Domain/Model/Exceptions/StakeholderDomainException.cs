namespace Monolith.Stakeholder.Domain.Model.Exceptions;

/// <summary>A business rule of the Stakeholder context was violated.</summary>
public class StakeholderDomainException(StakeholderError error, string message) : Exception(message)
{
    public StakeholderError Error { get; } = error;
}

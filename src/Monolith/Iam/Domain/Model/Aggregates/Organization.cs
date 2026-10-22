using Monolith.Iam.Domain.Model.ValueObjects;
using Monolith.Shared.Domain.Model.ValueObjects;
namespace Monolith.Iam.Domain.Model.Aggregates;

public partial class Organization
{
    /// <summary>Parameterless constructor required by EF Core materialization.</summary>
    protected Organization()
    {
        Id = new OrganizationId();
        Name = new OrganizationName();
        Status = OrganizationStatus.CreateActive();
    }
    /// <summary>Creates a new, active organization.</summary>
    public Organization(OrganizationName name)
    {
        Id = OrganizationId.New();
        Name = name;
        Status = OrganizationStatus.CreateActive();
    }
    public OrganizationId Id { get; private set; }
    public OrganizationName Name { get; private set; }
    public OrganizationStatus Status { get; private set; }
    public void UpdateName(OrganizationName name) => Name = name;

    public void Suspend() => Status = OrganizationStatus.CreateSuspended();

    public void Activate() => Status = OrganizationStatus.CreateActive();

    public bool IsActive() => Status.IsActive();

    public string GetName() => Name.ToString();
}
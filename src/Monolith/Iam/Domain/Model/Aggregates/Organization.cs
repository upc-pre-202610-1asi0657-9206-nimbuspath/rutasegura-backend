namespace Monolith.Iam.Domain.Model.Aggregates;

public partial class Organization
{
    public Organization(string name, string description, string? logoUrl)
    {
        Name = name;
        Description = description;
        LogoUrl = logoUrl;
    }

    public string Name { get; private set; }
    public string Description { get; private set; }

}
namespace Monolith.Iam.Infrastructure.Authorization;

/// <summary>
///     Key of the platform for <c>POST /api/v1/organizations</c> (section <c>Iam:Provisioning</c>). Empty (the
///     default) disables the endpoint. It stands in for the Subscription context, which will provision organizations
///     in-process through <c>IIamContextFacade</c> after a confirmed payment.
/// </summary>
public sealed class IamProvisioningOptions
{
    public const string Section = "Iam:Provisioning";
    public const string HeaderName = "X-Provisioning-Key";
    public const int MinimumKeyLength = 32;

    public string ApiKey { get; set; } = string.Empty;
}

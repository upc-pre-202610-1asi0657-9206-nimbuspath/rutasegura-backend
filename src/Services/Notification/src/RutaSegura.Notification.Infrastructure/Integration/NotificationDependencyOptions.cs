namespace RutaSegura.Notification.Infrastructure.Integration;

public sealed class NotificationDependencyOptions
{
    public const string SectionName = "NotificationDependencies";
    public ServiceEndpoint Administration { get; set; } = new();
    public ServiceEndpoint TripTracking { get; set; } = new();

    public sealed class ServiceEndpoint
    {
        public string BaseUrl { get; set; } = "";
        public string ServiceToken { get; set; } = "";
        public bool AllowInsecureHttp { get; set; }

        public Uri Validate()
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "https" && !(AllowInsecureHttp && uri.Scheme == "http")) ||
                !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                ServiceToken.Length < 32 || ServiceToken.Any(char.IsControl))
                throw new InvalidOperationException("Configura un endpoint interno HTTPS y un ServiceToken de al menos 32 caracteres. HTTP requiere habilitacion explicita de desarrollo.");
            return new Uri(BaseUrl.TrimEnd('/') + "/");
        }
    }
}

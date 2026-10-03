namespace Redeemer.SocialFlow.Api.Development.LinkedIn;

public sealed class LinkedInOptions
{
    public const string Callback = "https://localhost:65474/api/dev/linkedin/callback";
    public const string Scopes = "openid profile w_member_social";
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string StorageDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redeemer.SocialFlow", "LinkedIn");
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

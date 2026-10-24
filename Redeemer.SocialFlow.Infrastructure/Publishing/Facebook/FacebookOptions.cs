namespace Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
public sealed class FacebookOptions
{
    public const string Version = "v26.0";
    public const string Callback = "https://localhost:65474/api/dev/facebook/callback";
    public static readonly string[] RequiredScopes = ["pages_show_list", "pages_read_engagement", "pages_manage_posts"];
    public string AppId { get; set; } = "";
    public string AppSecret { get; set; } = "";
    public bool Enabled { get; set; }
    public bool PublicationEnabled { get; set; }
    public bool WorkerEnabled { get; set; }
    public string StorageDirectory { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redeemer.SocialFlow", "Facebook");
    public bool Configured => NumericId(AppId) && !string.IsNullOrWhiteSpace(AppSecret);
    public static bool NumericId(string? value) => value is { Length: > 0 and <= 80 } && value.All(char.IsAsciiDigit);
}
// Classes deliberately have no generated ToString containing credentials.
public sealed class FacebookPageCredential
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string AccessToken { get; init; }
    public required string[] Tasks { get; init; }
    public bool CanCreate => Tasks.Contains("CREATE_CONTENT") || Tasks.Contains("PROFILE_PLUS_CREATE_CONTENT");
}
public sealed class FacebookConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string AppId { get; init; }
    public required string UserToken { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required string[] Scopes { get; init; }
    public required FacebookPageCredential[] Pages { get; init; }
    public string? SelectedPageId { get; set; }
}
public interface IFacebookConnectionStore
{
    Task<FacebookConnection?> ReadAsync(CancellationToken token = default);
    Task SaveAsync(FacebookConnection connection, CancellationToken token = default);
    Task SelectAsync(Guid connectionId, string pageId, CancellationToken token = default);
    Task DisconnectAsync(Guid connectionId, CancellationToken token = default);
}
public sealed class FacebookAuthorizationException : Exception { }

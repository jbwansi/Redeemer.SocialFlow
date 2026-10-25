using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Redeemer.SocialFlow.Application.Accounts;
using Redeemer.SocialFlow.Domain.Enums;
namespace Redeemer.SocialFlow.Infrastructure.Social;

public abstract class WorkspaceOAuthProvider(HttpClient http, IConfiguration config, TimeProvider clock) : IWorkspaceOAuthProvider, IDisposable
{
    public void Dispose() => http.Dispose();
    public abstract SocialPlatform Platform { get; }
    protected IConfiguration Configuration => config;
    protected IConfigurationSection Settings => config.GetSection($"SocialConnections:Providers:{Platform}");
    protected virtual string ClientId => Settings["ClientId"] ?? "";
    protected virtual string ClientSecret => Settings["ClientSecret"] ?? "";
    protected string Callback => (config["Authentication:PublicOrigin"] ?? "").TrimEnd('/') + $"/api/social-oauth/{(int)Platform}/callback";
    protected string[] Scopes => (Settings["Scopes"] ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    protected DateTimeOffset Now => clock.GetUtcNow();
    // These confidential web flows do not advertise the native-only LinkedIn PKCE extension.
    // The orchestration supports S256 for future adapters with documented web PKCE support.
    public virtual bool SupportsPkce => false;
    public virtual bool Available => Settings.GetValue<bool>("Enabled") && Settings.GetValue<bool>("PermissionsApproved") &&
        ClientId.Length > 0 && ClientSecret.Length > 0 && Scopes.Length > 0 && Uri.TryCreate(Callback, UriKind.Absolute, out var uri) && uri.Scheme == "https";
    public abstract string AuthorizationUrl(string state, string? challenge);
    protected abstract Task<IReadOnlyList<SocialCandidate>> Discover(string code, CancellationToken ct);
    protected abstract Task<SocialCredential> Validate(string id, string type, SocialCredential credential, CancellationToken ct);
    public async Task<IReadOnlyList<SocialCandidate>> DiscoverAsync(string code, string? verifier, CancellationToken ct)
    { try { return await Discover(code, ct); } catch (SocialConnectionException) { throw; } catch { throw Failure(); } }
    public async Task<SocialCredential> ValidateAsync(string id, string type, SocialCredential credential, CancellationToken ct)
    { try { return await Validate(id, type, credential, ct); } catch (SocialConnectionException) { throw; } catch { throw Failure(); } }
    protected static SocialConnectionException Failure() => new(502, "Le fournisseur n’a pas pu valider la connexion. Réessayez ou reconnectez le compte.");
    protected static SocialConnectionException Unauthorized() => new(401, "La connexion nécessite une nouvelle autorisation.");
    protected static string Text(JsonElement value, string field) => value.TryGetProperty(field, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()! : "";
    protected async Task<JsonDocument> Send(HttpRequestMessage message, CancellationToken ct)
    {
        using (message)
        using (var response = await http.SendAsync(message, ct))
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw Unauthorized();
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) {
                // Meta reports expired/revoked grants as HTTP 400 / OAuth code 190.
                try { using var error = JsonDocument.Parse(content); if (error.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("code", out var c) && c.GetInt32() == 190) throw Unauthorized(); }
                catch (JsonException) { }
                throw Failure();
            }
            return JsonDocument.Parse(content);
        }
    }
    protected Task<JsonDocument> Post(string url, Dictionary<string,string> fields, CancellationToken ct) =>
        Send(new(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) }, ct);
    protected Task<JsonDocument> Get(string url, string access, CancellationToken ct, string? version = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        if (version is not null) { request.Headers.Add("LinkedIn-Version", version); request.Headers.Add("X-Restli-Protocol-Version", "2.0.0"); }
        return Send(request, ct);
    }
    protected DateTimeOffset Expiry(JsonElement root)
    {
        if (!root.TryGetProperty("expires_in", out var seconds) || !seconds.TryGetInt64(out var n) || n <= 0 || n > 10 * 365 * 86400L) throw Failure();
        return Now.AddSeconds(n);
    }
}

public sealed class LinkedInWorkspaceOAuthProvider(HttpClient http, IConfiguration config, TimeProvider clock) : WorkspaceOAuthProvider(http, config, clock)
{
    // Explicit opt-in reuses only the application credentials, never historical user tokens.
    // A partial new credential pair must not be combined with a different application's secret.
    private bool UseLegacyCredentials => Settings.GetValue<bool>("UseLegacyClientCredentials") &&
        Settings["ClientId"] is null && Settings["ClientSecret"] is null;
    protected override string ClientId => UseLegacyCredentials ? Configuration["LinkedIn:ClientId"] ?? "" : base.ClientId;
    protected override string ClientSecret => UseLegacyCredentials ? Configuration["LinkedIn:ClientSecret"] ?? "" : base.ClientSecret;
    public string ConfigurationMessage => !Settings.GetValue<bool>("Enabled") ? "LinkedIn est désactivé côté serveur." :
        string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret) ? "Identifiants de l’application LinkedIn manquants côté serveur." :
        !Settings.GetValue<bool>("PermissionsApproved") || !Scopes.Contains("openid") || !Scopes.Contains("profile") ? "Activez les permissions LinkedIn openid et profile pour identifier le compte." :
        !Available ? "Vérifiez l’origine HTTPS et la version API des organisations LinkedIn côté serveur." :
        "Identification du profil disponible. " + (Scopes.Contains("w_member_social") ? "Publication du profil soumise au consentement LinkedIn. " : "Permission de publication du profil non demandée. ") +
        (!Settings.GetValue<bool>("OrganizationsEnabled") ? "Pages d’organisation non activées. " : "Pages d’organisation selon les droits accordés. ") + "Publication automatique désactivée.";
    public override SocialPlatform Platform => SocialPlatform.LinkedIn;
    public override bool Available => base.Available && !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret) && Scopes.Contains("openid") && Scopes.Contains("profile") &&
        (!Settings.GetValue<bool>("OrganizationsEnabled") || Regex.IsMatch(Settings["ApiVersion"] ?? "", "^[0-9]{6}$"));
    public override string AuthorizationUrl(string state, string? challenge) => QueryHelpers.AddQueryString("https://www.linkedin.com/oauth/v2/authorization",
        new Dictionary<string,string?> { ["response_type"]="code", ["client_id"]=ClientId, ["redirect_uri"]=Callback, ["scope"]=string.Join(' ',Scopes), ["state"]=state });
    private SocialCredential Credential(JsonElement token, SocialCredential? prior = null)
    {
        var access = Text(token,"access_token"); if (access.Length == 0) throw Failure();
        var scope = Text(token,"scope");
        // Requested scopes are not evidence of a publishing grant. On refresh only,
        // retain previously confirmed scopes when the provider omits this field.
        var granted = scope.Length == 0 ? prior?.Scopes ?? [] : Uri.UnescapeDataString(scope.Replace('+',' ')).Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var refresh = Text(token,"refresh_token");
        var refreshExpiry = token.TryGetProperty("refresh_token_expires_in", out var deadline) ? Now.AddSeconds(deadline.GetInt64()) : prior?.RefreshExpiresAtUtc;
        return new(access, refresh.Length > 0 ? refresh : prior?.RefreshToken, Expiry(token), refreshExpiry, granted);
    }
    protected override async Task<IReadOnlyList<SocialCandidate>> Discover(string code, CancellationToken ct)
    {
        using var token = await Post("https://www.linkedin.com/oauth/v2/accessToken", new() { ["grant_type"]="authorization_code", ["code"]=code,
            ["client_id"]=ClientId, ["client_secret"]=ClientSecret, ["redirect_uri"]=Callback }, ct);
        var credential = Credential(token.RootElement);
        using var user = await Get("https://api.linkedin.com/v2/userinfo", credential.AccessToken, ct);
        var id = Text(user.RootElement,"sub"); if (id.Length == 0) throw Failure();
        var name = Text(user.RootElement,"name");
        var candidates = new List<SocialCandidate> { new(id, name.Length > 0 ? name : "Profil LinkedIn", "Profile", credential, credential.Scopes.Contains("w_member_social")) };
        if (Settings.GetValue<bool>("OrganizationsEnabled") && credential.Scopes.Any(x => x is "rw_organization_admin" or "r_organization_admin"))
        {
            foreach (var organization in await Organizations(credential, ct)) candidates.Add(new(organization.Id, organization.Name, "Organization", credential, credential.Scopes.Contains("w_organization_social")));
        }
        return candidates;
    }
    private async Task<List<(string Id,string Name)>> Organizations(SocialCredential credential, CancellationToken ct)
    {
        var result = new List<(string,string)>();
        for (var start=0; start<500; start+=100)
        {
            using var page = await Get($"https://api.linkedin.com/rest/organizationAcls?q=roleAssignee&role=ADMINISTRATOR&state=APPROVED&start={start}&count=100", credential.AccessToken, ct, Settings["ApiVersion"]);
            var items = page.RootElement.GetProperty("elements").EnumerateArray().ToArray();
            foreach (var item in items)
            {
                if (Text(item,"state") != "APPROVED" || Text(item,"role") != "ADMINISTRATOR") continue;
                var urn=Text(item,"organization"); var id=urn.Split(':').Last(); if (!Regex.IsMatch(id,"^[0-9]+$")) throw Failure();
                using var org = await Get("https://api.linkedin.com/rest/organizations/"+id, credential.AccessToken, ct, Settings["ApiVersion"]);
                var name=Text(org.RootElement,"localizedName"); result.Add((urn,name.Length>0?name:urn));
            }
            if (items.Length < 100) return result;
        }
        throw new SocialConnectionException(502,"Trop de destinations renvoyées. Réduisez les autorisations du compte avant de réessayer.");
    }
    protected override async Task<SocialCredential> Validate(string id, string type, SocialCredential credential, CancellationToken ct)
    {
        if (credential.ExpiresAtUtc <= Now.AddMinutes(5))
        {
            if (string.IsNullOrEmpty(credential.RefreshToken) || credential.RefreshExpiresAtUtc <= Now) throw Unauthorized();
            using var refreshed = await Post("https://www.linkedin.com/oauth/v2/accessToken", new() { ["grant_type"]="refresh_token", ["refresh_token"]=credential.RefreshToken,
                ["client_id"]=ClientId, ["client_secret"]=ClientSecret }, ct);
            credential = Credential(refreshed.RootElement, credential);
        }
        if (type == "Organization") { if (!(await Organizations(credential, ct)).Any(x=>x.Id==id)) throw Unauthorized(); }
        else { using var user = await Get("https://api.linkedin.com/v2/userinfo", credential.AccessToken, ct); if (Text(user.RootElement,"sub") != id) throw Unauthorized(); }
        return credential;
    }
}

public abstract class MetaWorkspaceOAuthProvider(HttpClient http, IConfiguration config, TimeProvider clock) : WorkspaceOAuthProvider(http, config, clock)
{
    private string Version => Settings["ApiVersion"] ?? "";
    public override bool Available => base.Available && Regex.IsMatch(Version,"^v[0-9]+\\.0$") && Scopes.Contains("pages_show_list") &&
        (Platform != SocialPlatform.Instagram || Scopes.Contains("instagram_basic"));
    private string Endpoint(string path) => "https://graph.facebook.com/"+Version+"/"+path;
    private string Proof(string access) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(ClientSecret),Encoding.UTF8.GetBytes(access))).ToLowerInvariant();
    private Task<JsonDocument> Graph(string path, string access, CancellationToken ct) => Get(Endpoint(path)+(path.Contains('?')?"&":"?")+"appsecret_proof="+Proof(access),access,ct);
    public override string AuthorizationUrl(string state, string? challenge) => QueryHelpers.AddQueryString("https://www.facebook.com/"+Version+"/dialog/oauth",
        new Dictionary<string,string?> { ["client_id"]=ClientId, ["redirect_uri"]=Callback, ["response_type"]="code", ["state"]=state, ["scope"]=string.Join(',',Scopes) });
    protected override async Task<IReadOnlyList<SocialCandidate>> Discover(string code, CancellationToken ct)
    {
        using var initial = await Post(Endpoint("oauth/access_token"), new() { ["client_id"]=ClientId, ["client_secret"]=ClientSecret, ["redirect_uri"]=Callback, ["code"]=code },ct);
        var shortToken=Text(initial.RootElement,"access_token"); if(shortToken.Length==0) throw Failure();
        using var extended = await Post(Endpoint("oauth/access_token"), new() { ["client_id"]=ClientId, ["client_secret"]=ClientSecret, ["grant_type"]="fb_exchange_token", ["fb_exchange_token"]=shortToken },ct);
        var access=Text(extended.RootElement,"access_token"); if(access.Length==0) throw Failure();
        var expiry=Expiry(extended.RootElement);
        using var grants=await Graph("me/permissions",access,ct);
        var granted=grants.RootElement.GetProperty("data").EnumerateArray().Where(x=>Text(x,"status")=="granted").Select(x=>Text(x,"permission")).ToArray();
        if(!granted.Contains("pages_show_list") || Platform==SocialPlatform.Instagram && !granted.Contains("instagram_basic")) throw Unauthorized();
        var result=new List<SocialCandidate>(); string? after=null;
        for(var pageNumber=0;pageNumber<5;pageNumber++)
        {
            var fields=Platform==SocialPlatform.Instagram ? "id,name,access_token,tasks,instagram_business_account{id,username,name}" : "id,name,access_token,tasks";
            using var pages=await Graph("me/accounts?limit=100&fields="+Uri.EscapeDataString(fields)+(after is null?"":"&after="+Uri.EscapeDataString(after)),access,ct);
            foreach(var page in pages.RootElement.GetProperty("data").EnumerateArray())
            {
                var pageToken=Text(page,"access_token"); if(pageToken.Length==0) continue;
                if(Platform==SocialPlatform.Instagram)
                {
                    if(!page.TryGetProperty("instagram_business_account",out var account) || account.ValueKind!=JsonValueKind.Object) continue;
                    var id=Text(account,"id"); if(id.Length==0) continue;
                    var name=Text(account,"username"); if(name.Length==0) name=Text(account,"name");
                    result.Add(new(id,name.Length>0?name:id,"Professional",new(pageToken,null,expiry,null,granted),granted.Contains("instagram_content_publish")));
                }
                else
                {
                    var tasks=page.TryGetProperty("tasks",out var taskList)?taskList.EnumerateArray().Select(x=>x.GetString()).ToArray():[];
                    result.Add(new(Text(page,"id"),Text(page,"name"),"Page",new(pageToken,null,expiry,null,granted),granted.Contains("pages_manage_posts") && tasks.Contains("CREATE_CONTENT")));
                }
            }
            if(!pages.RootElement.TryGetProperty("paging",out var paging) || !paging.TryGetProperty("next",out _)) return result;
            after=Text(paging.GetProperty("cursors"),"after"); if(after.Length==0) throw Failure();
        }
        throw new SocialConnectionException(502,"Trop de destinations renvoyées. Réduisez les autorisations du compte avant de réessayer.");
    }
    protected override async Task<SocialCredential> Validate(string id,string type,SocialCredential credential,CancellationToken ct)
    {
        if(credential.ExpiresAtUtc<=Now) throw Unauthorized();
        using var account=await Graph(Uri.EscapeDataString(id)+"?fields=id",credential.AccessToken,ct);
        if(Text(account.RootElement,"id")!=id) throw Unauthorized();
        return credential;
    }
}
public sealed class FacebookWorkspaceOAuthProvider(HttpClient http,IConfiguration config,TimeProvider clock) : MetaWorkspaceOAuthProvider(http,config,clock)
{ public override SocialPlatform Platform => SocialPlatform.Facebook; }
public sealed class InstagramWorkspaceOAuthProvider(HttpClient http,IConfiguration config,TimeProvider clock) : MetaWorkspaceOAuthProvider(http,config,clock)
{ public override SocialPlatform Platform => SocialPlatform.Instagram; }

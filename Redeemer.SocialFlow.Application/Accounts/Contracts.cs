namespace Redeemer.SocialFlow.Application.Accounts;

public sealed record AccountProfile(string Id, string Email);
public interface IAccountEmailSender
{
    Task SendAsync(string email, string purpose, string link, CancellationToken token);
}
public interface IAccountService
{
    Task RegisterAsync(string email, string password, CancellationToken token);
    Task<bool> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<AccountProfile?> CurrentAsync();
    Task SendRecoveryAsync(string email, bool confirmation, CancellationToken token);
    Task<bool> ConfirmAsync(string email, string code);
    Task<bool> ResetAsync(string email, string code, string password);
}

// No implicit default workspace. Phase 2 must resolve and authorize membership explicitly.
public interface IWorkspaceAccessContext
{
    Guid? WorkspaceId { get; }
}
public sealed class UnresolvedWorkspaceAccessContext : IWorkspaceAccessContext
{
    public Guid? WorkspaceId => null;
}

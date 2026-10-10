using System.Security.Cryptography;
using System.Text.Json;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Exceptions;

namespace Redeemer.SocialFlow.Application.Visuals;

public sealed record IdentityInput(string IdentityId, string Name, string Style, string[] Colors,
    string? Slogan = null, string? Instructions = null, bool IsActive = true);
public sealed record LibraryIdentityDto(Guid Id, string IdentityId, string Name, string Style, string[] Colors,
    string? Slogan, string? Instructions, bool IsActive, bool IsDefault, long Revision, Guid? LogoId);
public interface IVisualIdentityRepository
{
    Task<IReadOnlyList<StoredVisualIdentity>> ListAsync(CancellationToken token);
    Task<StoredVisualIdentity> GetAsync(Guid id, CancellationToken token);
    Task<StoredVisualIdentity> CreateAsync(StoredVisualIdentity identity, CancellationToken token);
    Task<StoredVisualIdentity> UpdateAsync(Guid id, long revision, IdentityInput input, CancellationToken token);
    Task<StoredVisualIdentity> DefaultAsync(Guid id, long revision, CancellationToken token);
    Task DeleteAsync(Guid id, long revision, CancellationToken token);
    Task ReserveLogoAsync(IdentityLogo logo, long revision, CancellationToken token);
    Task<StoredVisualIdentity> CompleteLogoAsync(Guid id, Guid logoId, long revision, CancellationToken token);
    Task<IdentityLogo> GetLogoAsync(Guid id, Guid logoId, CancellationToken token);
}
public sealed class IdentityNotFoundException : Exception { }

public sealed class VisualIdentityLibrary(IVisualIdentityRepository repository, IVisualStorage storage,
    IVisualValidator validator, VisualLimits limits)
{
    private static LibraryIdentityDto Dto(StoredVisualIdentity x) => new(x.Id, x.IdentityId, x.Name, x.Style,
        JsonSerializer.Deserialize<string[]>(x.ColorsJson)!, x.Slogan, x.Instructions, x.IsActive, x.IsDefault, x.Revision, x.LogoId);
    public async Task<IReadOnlyList<LibraryIdentityDto>> ListAsync(CancellationToken token = default) =>
        (await repository.ListAsync(token)).Select(Dto).ToArray();
    public async Task<LibraryIdentityDto> CreateAsync(IdentityInput input, CancellationToken token = default)
    {
        var entity = StoredVisualIdentity.Create(input.IdentityId, input.Name, input.Style, input.Colors,
            input.Slogan, input.Instructions, DateTimeOffset.UtcNow);
        if (!input.IsActive) entity.Update(input.IdentityId, input.Name, input.Style, input.Colors, input.Slogan, input.Instructions, false);
        return Dto(await repository.CreateAsync(entity, token));
    }
    public async Task<LibraryIdentityDto> UpdateAsync(Guid id, long revision, IdentityInput input, CancellationToken token = default) =>
        Dto(await repository.UpdateAsync(id, revision, input, token));
    public async Task<LibraryIdentityDto> DefaultAsync(Guid id, long revision, CancellationToken token = default) =>
        Dto(await repository.DefaultAsync(id, revision, token));
    public Task DeleteAsync(Guid id, long revision, CancellationToken token = default) => repository.DeleteAsync(id, revision, token);
    public async Task<VisualIdentity> ResolveAsync(Guid id, long revision, VisualIdentity effective, CancellationToken token = default)
    {
        var source = await repository.GetAsync(id, token);
        if (!source.IsActive || source.Revision != revision) throw new DomainException("Identity changed or is inactive; reload it.");
        return new(effective.Id, effective.Name, effective.Style, effective.Colors, effective.Slogan,
            effective.Instructions, source.Id, source.Revision, source.LogoId);
    }
    public async Task<LibraryIdentityDto> UploadLogoAsync(Guid id, long revision, Stream input, CancellationToken token = default)
    {
        var source = await repository.GetAsync(id, token);
        if (source.Revision != revision) throw new DomainException("Identity changed; reload it.");
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0)
        {
            if (memory.Length + count > limits.MaxBytes) throw new InvalidVisualException("Logo is too large.");
            await memory.WriteAsync(buffer.AsMemory(0, count), token);
        }
        var bytes = memory.ToArray();
        var image = await validator.ValidateAsync(bytes, token);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var logo = IdentityLogo.Reserve(id, image.ContentType, image.Width, image.Height, bytes.Length, hash);
        // Persist a reservation before writing. Interrupted uploads remain tracked and inaccessible.
        await repository.ReserveLogoAsync(logo, revision, token);
        await storage.WriteAsync(logo.StorageKey, bytes, token);
        return Dto(await repository.CompleteLogoAsync(id, logo.Id, revision, token));
    }
    public async Task<VisualContent> ReadLogoAsync(Guid id, Guid logoId, CancellationToken token = default)
    {
        var logo = await repository.GetLogoAsync(id, logoId, token);
        var bytes = await storage.ReadAsync(logo.StorageKey, token);
        if (bytes.LongLength != logo.ByteLength || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes), Convert.FromHexString(logo.Sha256)))
            throw new VisualStorageException();
        return new(bytes, logo.ContentType);
    }
}

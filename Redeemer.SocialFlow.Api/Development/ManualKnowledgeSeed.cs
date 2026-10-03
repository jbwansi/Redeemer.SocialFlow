using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Domain.Entities;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.Persistence;

namespace Redeemer.SocialFlow.Api.Development;

/// <summary>Opt-in local test data. Never invoked by ordinary API startup.</summary>
public static class ManualKnowledgeSeed
{
    public const string Argument = "--seed-knowledge-test=true";
    public const string Title = "TEST — Accompagnement des bénévoles";

    // Deliberately require a command-line argument, not a persistent configuration setting.
    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.Ordinal);

    public static async Task RunAsync(string[] args, IHostEnvironment environment,
        SocialFlowDbContext db, TimeProvider timeProvider, CancellationToken cancellationToken = default)
    {
        if (!IsRequested(args)) return;
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("Knowledge test seed is only available in Development.");

        cancellationToken.ThrowIfCancellationRequested();
        // Keep the reserved test title as the identity; do not overwrite local edits.
        // The transaction keeps the document and both chunks atomic.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.KnowledgeDocuments.AnyAsync(document => document.Title == Title, cancellationToken))
            return;

        const string theme = "accompagnement des bénévoles";
        var document = KnowledgeDocument.Create(Title, theme, [theme], [KnowledgeUsage.Generation],
            SourceType.Other, AuthorityLevel.Low, "fr", timeProvider: timeProvider);
        document.ChangeStatus(KnowledgeDocumentStatus.Approved);
        db.KnowledgeDocuments.Add(document);
        db.KnowledgeChunks.AddRange(
            KnowledgeChunk.Create(document.Id,
                "Pour accompagner les bénévoles, clarifier leur rôle, écouter leurs attentes et identifier leurs besoins de soutien.",
                0, section: "Données fictives de test", timeProvider: timeProvider),
            KnowledgeChunk.Create(document.Id,
                "Prévoir des échanges réguliers avec les bénévoles, reconnaître leur contribution et adapter les responsabilités à leurs disponibilités.",
                1, section: "Données fictives de test", timeProvider: timeProvider));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

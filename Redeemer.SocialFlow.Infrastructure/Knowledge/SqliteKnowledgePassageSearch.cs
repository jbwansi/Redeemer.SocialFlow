using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Redeemer.SocialFlow.Application.Abstractions;
using Redeemer.SocialFlow.Application.Knowledge;
using Redeemer.SocialFlow.Domain.Enums;

namespace Redeemer.SocialFlow.Infrastructure.Knowledge;

/// <summary>
/// Text-only baseline: score is the number of distinct query words found in chunk content.
/// Words contain Unicode letters, combining marks or digits. Matching is ordinal ignoring
/// case, but preserves accents. Repetition does not increase the score; no title weighting.
/// Ties use document ID, chunk index, then chunk ID, all ascending.
/// </summary>
public sealed class SqliteKnowledgePassageSearch(IKnowledgeDbContext context) : IKnowledgePassageSearch
{
    private static readonly Regex Words = new(@"[\p{L}\p{M}\p{Nd}]+", RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<KnowledgePassage>> SearchForGenerationAsync(
        SearchKnowledgePassagesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Query) || request.Limit <= 0 ||
            request.Theme is not null && string.IsNullOrWhiteSpace(request.Theme) ||
            request.Language is not null && string.IsNullOrWhiteSpace(request.Language))
            throw new ArgumentException("Query must be non-blank, Limit positive, and supplied filters non-blank.", nameof(request));
        cancellationToken.ThrowIfCancellationRequested();
        var terms = Tokenize(request.Query, cancellationToken);
        if (terms.Count == 0) return Array.Empty<KnowledgePassage>();
        var theme = request.Theme?.Trim();
        var language = request.Language?.Trim();
        var documents = context.KnowledgeDocuments.AsNoTracking()
            .Where(document => document.Status == KnowledgeDocumentStatus.Approved && document.IsActive);
        if (language is not null)
            documents = documents.Where(document => document.Language == language);

        var candidates = from document in documents
                         join chunk in context.KnowledgeChunks.AsNoTracking()
                             on document.Id equals chunk.KnowledgeDocumentId
                         select new { Document = document, Chunk = chunk };
        var matches = new List<(int Score, KnowledgePassage Passage)>();
        await foreach (var candidate in candidates.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = candidate.Document;
            // Collections are stored through JSON value converters, not queryable collections.
            // Consult the Domain rule on current database values before scoring or limiting.
            if (!document.CanBeUsedForGeneration ||
                theme is not null && !document.Themes.Contains(theme, StringComparer.Ordinal)) continue;
            var chunk = candidate.Chunk;
            var words = Tokenize(chunk.Content, cancellationToken);
            var score = terms.Count(words.Contains);
            if (score == 0) continue;
            matches.Add((score, new KnowledgePassage(chunk.Id, document.Id, document.Title,
                document.Version, document.SourceType, document.AuthorityLevel, document.Language,
                chunk.Content, chunk.ChunkIndex, chunk.PageNumber, chunk.Section)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var results = matches.OrderByDescending(match => match.Score)
            .ThenBy(match => match.Passage.KnowledgeDocumentId)
            .ThenBy(match => match.Passage.ChunkIndex)
            .ThenBy(match => match.Passage.KnowledgeChunkId)
            .Take(request.Limit).Select(match => match.Passage).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static HashSet<string> Tokenize(string text, CancellationToken cancellationToken)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Words.Matches(text))
        {
            cancellationToken.ThrowIfCancellationRequested();
            words.Add(match.Value);
        }
        return words;
    }
}

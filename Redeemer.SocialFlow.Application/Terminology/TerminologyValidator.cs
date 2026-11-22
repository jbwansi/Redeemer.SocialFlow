using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Redeemer.SocialFlow.Application.Terminology;

/// <summary>Pure, deterministic spelling checks. No language/context inference, network, or fact certification.</summary>
public static class TerminologyValidator
{
    public static IReadOnlyList<TermRule> Applicable(IReadOnlyList<TermRule> rules, params string?[] texts) =>
        texts.SelectMany(t => Check(t, rules).Terms).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
    private const string Word = @"\p{L}\p{M}\p{N}_";
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    // References remain untouched by the caller. These spans protect literal material embedded in generated prose.
    private static readonly Regex Protected = new(
        "```[\\s\\S]*?(?:```|$)|~~~[\\s\\S]*?(?:~~~|$)|`[^`\\r\\n]*`|" +
        "(?:https?://|www\\.|mailto:)[^\\s<>]+|\\]\\([^\\r\\n)]*\\)|" +
        "\"(?:\\\\.|[^\"\\\\])*\"|«[^»]*»|“[^”]*”|(?<![\\p{L}\\p{N}])'[^'\\r\\n]+'(?![\\p{L}\\p{N}])|" +
        "<(?:pre|code)\\b[^>]*>[\\s\\S]*?</(?:pre|code)>|" +
        "(?m:^[ \\t]*>[^\\r\\n]*|^(?: {4}|\\t)[^\\r\\n]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);

    private sealed record Hit(int Start, int Length, TermRule Term);
    public static TerminologyCheck Check(string? input, IReadOnlyList<TermRule> rules, bool correct = false)
    {
        var text = input ?? "";
        if (text.Length == 0 || rules.Count == 0) return new(text, [], []);
        // Map NFC indices back to original text elements so protected/original prose is never normalized wholesale.
        var normalized = new StringBuilder(); var starts = new List<int>(); var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement(); var value = element.Normalize(NormalizationForm.FormC);
            normalized.Append(value);
            for (var i = 0; i < value.Length; i++) { starts.Add(elements.ElementIndex); ends.Add(elements.ElementIndex + element.Length); }
        }
        var searchable = normalized.ToString();
        var protectedSpans = Protected.Matches(text).Cast<Match>().ToArray();
        var hits = new List<Hit>();
        foreach (var rule in rules)
        foreach (var alias in rule.Aliases.Prepend(rule.Canonical).Distinct(StringComparer.Ordinal))
        {
            var escaped = Regex.Escape(alias.Normalize(NormalizationForm.FormC));
            var pattern = @"(?<![" + Word + "])" + escaped + @"(?![" + Word + "]|[®™])";
            foreach (Match match in Regex.Matches(searchable, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout))
                hits.Add(new(starts[match.Index], ends[match.Index + match.Length - 1] - starts[match.Index], rule));
        }
        var result = new StringBuilder(); var warnings = new List<string>(); var used = new Dictionary<Guid, TermRule>();
        var cursor = 0;
        var groups = hits.OrderBy(x => x.Start).ThenByDescending(x => x.Length).GroupBy(x => x.Start).ToArray();
        for (var groupIndex = 0; groupIndex < groups.Length; groupIndex++)
        {
            var group = groups[groupIndex];
            var longest = group.First();
            if (longest.Start < cursor) continue;
            var end = longest.Start + longest.Length;
            var overlapping = new List<TermRule>();
            for (var next = groupIndex + 1; next < groups.Length && groups[next].Key < end; next++)
            {
                var following = groups[next].First();
                if (following.Start + following.Length > end)
                { end = following.Start + following.Length; overlapping.AddRange(groups[next].Select(x => x.Term)); }
            }
            if (overlapping.Count > 0)
            {
                foreach (var item in overlapping.Concat(group.Select(x => x.Term))) used[item.Id] = item;
                warnings.Add("Variantes terminologiques chevauchantes : validation humaine requise ; aucun remplacement effectué.");
                result.Append(text.AsSpan(cursor, end - cursor)); cursor = end; continue;
            }
            var candidates = group.Where(x => x.Length == longest.Length).Select(x => x.Term).DistinctBy(x => x.Id).ToArray();
            foreach (var candidate in candidates) used[candidate.Id] = candidate;
            result.Append(text.AsSpan(cursor, longest.Start - cursor));
            var original = text.Substring(longest.Start, longest.Length);
            var protectedText = protectedSpans.Any(x => x.Index < longest.Start + longest.Length && x.Index + x.Length > longest.Start);
            var replacement = original;
            if (candidates.Length != 1)
                warnings.Add("Terminologie ambiguë : plusieurs appellations correspondent à une variante. Validation humaine requise ; aucun remplacement effectué.");
            else if (!string.Equals(original, candidates[0].Canonical, StringComparison.Ordinal))
            {
                var term = candidates[0];
                if (protectedText)
                    warnings.Add($"Graphie à vérifier dans une citation, URL ou zone littérale : {term.Canonical}. Zone conservée sans modification.");
                else if (!string.IsNullOrWhiteSpace(term.UsageContext))
                    warnings.Add($"Contexte d’utilisation à vérifier pour {term.Canonical}. Validation humaine requise ; aucun remplacement effectué.");
                else
                {
                    if (correct) replacement = term.Canonical;
                    warnings.Add(correct ? $"Graphie normalisée selon le référentiel du workspace : {term.Canonical}. Relisez le résultat."
                        : $"Graphie recommandée par le référentiel du workspace : {term.Canonical}. Validation humaine requise.");
                }
            }
            result.Append(replacement); cursor = longest.Start + longest.Length;
        }
        result.Append(text.AsSpan(cursor));
        return new(result.ToString(), warnings.Distinct(StringComparer.Ordinal).ToArray(), used.Values.OrderBy(x => x.Id).ToArray());
    }
}

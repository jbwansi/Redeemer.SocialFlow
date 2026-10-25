using System.Text.Json;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed partial class FacebookTokenExpiryTests
{
    [Theory]
    [InlineData("[\"9007199254740993\"]", "9007199254740993", "true", "true")]
    [InlineData("[9007199254740993]", "9007199254740993", "true", "true")]
    [InlineData("[\"9007199254740992\"]", "9007199254740993", "true", "false")]
    [InlineData("[\"09007199254740993\"]", "9007199254740993", "true", "false")]
    [InlineData("[\"9007199254740993\"]", " 9007199254740993", "true", "unknown")]
    [InlineData("[\"9007199254740993\"]", null, "true", "unknown")]
    [InlineData("[\"9007199254740993\"]", "", "true", "unknown")]
    [InlineData("[]", "9007199254740993", "false", "false")]
    [InlineData("[9.007199254740993e15]", "9007199254740993", "unknown", "unknown")]
    [InlineData("[9007199254740993.0]", "9007199254740993", "unknown", "unknown")]
    [InlineData("[\" 9007199254740993\"]", "9007199254740993", "unknown", "unknown")]
    [InlineData("[null]", "9007199254740993", "unknown", "unknown")]
    [InlineData("[{}]", "9007199254740993", "unknown", "unknown")]
    [InlineData("[true]", "9007199254740993", "unknown", "unknown")]
    [InlineData("null", "9007199254740993", "unknown", "unknown")]
    public async Task ComparisonsPreserveExactDigitsAndNeverExposeValues(string targets, string? expected, string common, string match)
    {
        var entries = FacebookOptions.RequiredScopes.Select(p => "{\"scope\":" + JsonSerializer.Serialize(p) + ",\"target_ids\":" + targets + "}");
        await CheckComparisons("[" + string.Join(',', entries) + "]", expected, common, [match, match, match]);
    }

    [Theory]
    [InlineData("[\"810001\",\"810002\"]", "[\"810002\",\"810003\"]", "[\"810001\",\"810003\"]", "false", "true", "false", "true")]
    [InlineData("[\"810001\",\"810002\"]", "[\"810002\"]", "[\"810002\",\"810001\"]", "true", "true", "false", "true")]
    [InlineData("[\"810001\"]", "[]", "[\"810001\"]", "false", "true", "false", "true")]
    [InlineData("[\"810001\"]", "null", "[\"810001\"]", "unknown", "true", "unknown", "true")]
    public async Task RequiresIntersectionOfAllThreePermissions(string first, string second, string third, string common, string a, string b, string c)
    {
        var entries = FacebookOptions.RequiredScopes.Zip(new[] { first, second, third }, (p, ids) => "{\"scope\":" + JsonSerializer.Serialize(p) + ",\"target_ids\":" + ids + "}");
        await CheckComparisons("[" + string.Join(',', entries) + "]", "810001", common, [a, b, c]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("[{}]")]
    [InlineData("[{\"scope\":\"pages_show_list\"}]")]
    [InlineData("[{\"scope\":\"pages_show_list_suffix\",\"target_ids\":[\"810001\"]}]")]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[],\"target_ids\":[\"810001\"]}]")]
    [InlineData("[{\"scope\":\"pages_show_list\",\"target_ids\":[]},{\"scope\":\"pages_show_list\",\"target_ids\":[]}]")]
    public async Task MissingOrAmbiguousTargetsRemainUnknown(string granular) =>
        await CheckComparisons(granular, "810001", "unknown", ["unknown", "unknown", "unknown"]);

    private static async Task CheckComparisons(string granular, string? expected, string common, string[] matches)
    {
        using var harness = new Harness(diagnostics: true, expectedPage: expected);
        harness.Handler.Extension["expires_in"] = 3600;
        harness.Handler.RawDebug = "{\"data\":{\"granular_scopes\":" + granular + "}}";
        harness.Handler.RawPages = "{\"data\":[]}";
        var connection = await harness.Graph.ExchangeAsync("private-code", default);
        Assert.Empty(connection.Pages);
        Assert.Equal(2, harness.Handler.ExchangeCalls);
        Assert.Equal(1, harness.Handler.PermissionCalls);
        Assert.Equal(1, harness.Handler.PageCalls);
        Assert.Equal(1, harness.Handler.DebugCalls);
        var commonRow = Assert.Single(harness.Log.States, s => s.ContainsKey("CommonTargetPresent"));
        Assert.Equal(common, commonRow["CommonTargetPresent"]);
        Assert.Equal(2, commonRow.Count);
        var rows = harness.Log.States.Where(s => s.ContainsKey("ExpectedPageMatched")).ToArray();
        Assert.Equal(3, rows.Length);
        for (var i = 0; i < rows.Length; i++)
        {
            Assert.Equal(FacebookOptions.RequiredScopes[i], rows[i]["Permission"]);
            Assert.Equal(matches[i], rows[i]["ExpectedPageMatched"]);
            Assert.Equal(3, rows[i].Count);
        }
        AssertSafe(harness.Log);
        var logged = string.Join('\n', harness.Log.Messages) + JsonSerializer.Serialize(harness.Log.States);
        foreach (var sensitive in new[] { expected, "9007199254740993", "9007199254740992", "810001", "810002", "810003" })
            if (!string.IsNullOrEmpty(sensitive)) Assert.DoesNotContain(sensitive, logged);
    }
}

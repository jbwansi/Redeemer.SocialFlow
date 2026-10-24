using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redeemer.SocialFlow.Infrastructure.Publishing.Facebook;
using Xunit;
namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed class FacebookOAuthDiagnosticTests
{
    [Theory]
    [InlineData(1, "code_exchange")]
    [InlineData(2, "token_extension")]
    [InlineData(3, "permissions")]
    [InlineData(4, "pages")]
    public async Task RecordsStageAndNumericMetaErrorWithoutSecrets(int failAt, string stage)
    {
        var log = new CapturedLogger();
        using var handler = new Handler(failAt);
        using var http = new HttpClient(handler);
        var graph = new FacebookGraphClient(http, Options.Create(new FacebookOptions { AppId = "42", AppSecret = "private-app" }), TimeProvider.System, log);
        await Assert.ThrowsAsync<FacebookAuthorizationException>(() => graph.ExchangeAsync("private-code", default));
        var output = string.Join("\n", log.Messages);
        Assert.Contains("Stage=" + stage, output);
        Assert.Contains("HttpStatus=400", output);
        Assert.Contains("MetaCode=190", output);
        Assert.Contains("MetaSubcode=460", output);
        Assert.DoesNotContain("private-", output);
        Assert.Equal(failAt, handler.Calls);
    }
    [Theory]
    [InlineData("network", "network_or_tls")]
    [InlineData("timeout", "timeout")]
    [InlineData("json", "invalid_response")]
    public async Task ClassifiesTransportAndMalformedResponsesWithoutExceptionMessages(string failure, string expected)
    {
        var log = new CapturedLogger();
        using var handler = new Handler(1, failure);
        using var http = new HttpClient(handler);
        var graph = new FacebookGraphClient(http, Options.Create(new FacebookOptions { AppId = "42", AppSecret = "private-app" }), TimeProvider.System, log);
        await Assert.ThrowsAnyAsync<Exception>(() => graph.ExchangeAsync("private-code", default));
        var output = string.Join("\n", log.Messages);
        Assert.Contains("Category=" + expected, output);
        Assert.Contains("Stage=code_exchange", output);
        Assert.DoesNotContain("private-", output);
    }
    private sealed class Handler(int failAt, string failure = "meta") : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Calls == failAt)
            {
                if (failure == "network") throw new HttpRequestException("private-network-message");
                if (failure == "timeout") throw new TaskCanceledException("private-timeout-message");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(failure == "json" ? "private-html" : "{\"error\":{\"code\":190,\"error_subcode\":460,\"message\":\"private-token private-app private-code\"}}") });
            }
            var body = Calls <= 2 ? "{\"access_token\":\"private-token\",\"expires_in\":3600}" : "{\"data\":[{\"permission\":\"pages_show_list\",\"status\":\"granted\"},{\"permission\":\"pages_read_engagement\",\"status\":\"granted\"},{\"permission\":\"pages_manage_posts\",\"status\":\"granted\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    private sealed class CapturedLogger : ILogger<FacebookGraphClient>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Assert.Null(exception);
            Messages.Add(formatter(state, exception));
        }
    }
}

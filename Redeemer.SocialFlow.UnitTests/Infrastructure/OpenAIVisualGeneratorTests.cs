using System.Buffers.Binary;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Images;
using Redeemer.SocialFlow.Application.AI;
using Redeemer.SocialFlow.Domain.Enums;
using Redeemer.SocialFlow.Infrastructure.AI;
using Xunit;

namespace Redeemer.SocialFlow.UnitTests.Infrastructure;

public sealed class OpenAIVisualGeneratorTests
{
    private const string Model = "gpt-image-1";
    private static VisualIdentity Identity => new("brand-a", "Brand A", "Clean and human", ["#123456"], "Together");
    private static GenerateVisualRequest Request(SocialPlatform platform = SocialPlatform.LinkedIn,
        VisualLayout? layout = null) => new("A team working together", platform, Identity, layout);

    [Theory]
    [InlineData(SocialPlatform.LinkedIn, null, 1024, 1024)]
    [InlineData(SocialPlatform.Facebook, null, 1536, 1024)]
    [InlineData(SocialPlatform.Instagram, null, 1024, 1024)]
    [InlineData(SocialPlatform.LinkedIn, VisualLayout.Portrait, 1024, 1536)]
    public async Task GeneratesOnePngWithExpectedRequestAndMetadata(SocialPlatform platform, VisualLayout? layout, int width, int height)
    {
        using var harness = new Harness(Png(width, height));
        var result = await harness.Generator.GenerateAsync(Request(platform, layout));
        Assert.Equal(harness.Image, result.Bytes.ToArray());
        Assert.Equal((width, height), (result.Metadata.Width, result.Metadata.Height));
        Assert.Equal("image/png", result.Metadata.ContentType);
        Assert.Equal(result.Bytes.Length, result.Metadata.ByteLength);
        Assert.Equal(Model, result.Metadata.Model);
        Assert.Equal("OpenAI", result.Metadata.Provider);
        Assert.Equal("brand-a", result.Metadata.IdentityId);
        Assert.Equal(platform, result.Metadata.Platform);
        Assert.Equal(Request().VisualBrief, result.Metadata.VisualBrief);
        Assert.Single(harness.Handler.Requests);
        var sent = harness.Handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal("https://api.openai.com/v1/images/generations", sent.Uri.ToString());
        using var json = JsonDocument.Parse(sent.Body);
        var root = json.RootElement;
        Assert.Equal(Model, root.GetProperty("model").GetString());
        Assert.Equal($"{width}x{height}", root.GetProperty("size").GetString());
        Assert.Equal("png", root.GetProperty("output_format").GetString());
        Assert.Equal("medium", root.GetProperty("quality").GetString());
        Assert.False(root.TryGetProperty("response_format", out _));
        if (root.TryGetProperty("n", out var count)) Assert.Equal(1, count.GetInt32());
        var prompt = root.GetProperty("prompt").GetString()!;
        Assert.Contains(Request().VisualBrief, prompt);
        Assert.Contains("#123456", prompt);
        Assert.Contains("Never create, recreate, imitate or invent a logo", prompt);
        Assert.DoesNotContain("Redeemer Holding", prompt);
    }

    [Fact]
    public async Task IdentityIsPerRequestWithoutCrossBrandState()
    {
        using var harness = new Harness(Png());
        await harness.Generator.GenerateAsync(Request());
        var second = new GenerateVisualRequest("A garden", SocialPlatform.Instagram,
            new VisualIdentity("brand-b", "Brand B", "Natural", ["#00FF00"]));
        var result = await harness.Generator.GenerateAsync(second);
        using var body = JsonDocument.Parse(harness.Handler.Requests[1].Body);
        var prompt = body.RootElement.GetProperty("prompt").GetString()!;
        Assert.Contains("Brand B", prompt);
        Assert.DoesNotContain("Brand A", prompt);
        Assert.DoesNotContain("#123456", prompt);
        Assert.Equal("brand-b", result.Metadata.IdentityId);
    }

    [Theory]
    [InlineData(400, VisualGenerationFailure.Rejected)]
    [InlineData(401, VisualGenerationFailure.Authentication)]
    [InlineData(403, VisualGenerationFailure.Authentication)]
    [InlineData(429, VisualGenerationFailure.RateLimited)]
    [InlineData(500, VisualGenerationFailure.ProviderUnavailable)]
    [InlineData(503, VisualGenerationFailure.ProviderUnavailable)]
    public async Task ProviderErrorsAreSafeAndNeverRetried(int status, VisualGenerationFailure expected)
    {
        using var harness = new Harness(Png());
        harness.Handler.Status = (HttpStatusCode)status;
        harness.Handler.Body = """{"error":{"message":"secret-provider-payload","type":"api_error","code":"error"}}""";
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(expected, error.Failure);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("secret-provider-payload", error.ToString());
        Assert.Single(harness.Handler.Requests);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"created\":1,\"data\":[]}")]
    [InlineData("{\"created\":1,\"data\":[{\"b64_json\":\"!\"}]}")]
    [InlineData("{\"created\":1,\"data\":[{\"url\":\"https://example.invalid/image.png\"}]}")]
    [InlineData("{\"created\":1,\"data\":[{\"b64_json\":\"\"}]}")]
    public async Task MalformedOrUrlOnlyResponsesAreRejectedWithoutDownloads(string body)
    {
        using var harness = new Harness(Png());
        harness.Handler.Body = body;
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.InvalidResponse, error.Failure);
        Assert.Single(harness.Handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WrongFormatOrDimensionsAreRejected(bool wrongDimensions)
    {
        using var harness = new Harness(wrongDimensions ? Png(1, 1) : Encoding.UTF8.GetBytes("not a PNG"));
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task OutputLimitIsEnforced()
    {
        using var harness = new Harness(Png(), options => options.MaxImageBytes = 1024);
        harness.Handler.Body = Envelope(new byte[1025]);
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.OutputTooLarge, error.Failure);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task PreCancellationAndNullRequestDoNotCallProvider()
    {
        using var harness = new Harness(Png());
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Generator.GenerateAsync(Request(), cancelled.Token));
        await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Generator.GenerateAsync(null!));
        Assert.Empty(harness.Handler.Requests);
    }

    [Fact]
    public async Task InFlightCancellationReachesTransportAndReleasesSlot()
    {
        using var harness = new Harness(Png(), options => options.MaxConcurrentGenerations = 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Handler.BeforeResponse = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        using var cancellation = new CancellationTokenSource();
        var pending = harness.Generator.GenerateAsync(Request(), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        harness.Handler.BeforeResponse = null;
        await harness.Generator.GenerateAsync(Request());
        Assert.Equal(2, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task DeadlineIsReportedWithoutRetry()
    {
        using var harness = new Harness(Png(), options => options.TimeoutSeconds = 1);
        harness.Handler.BeforeResponse = token => Task.Delay(Timeout.Infinite, token);
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.Timeout, error.Failure);
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public async Task ConcurrencyLimitRejectsBeforeSendingAndReleasesAfterSuccess()
    {
        using var harness = new Harness(Png(), options => options.MaxConcurrentGenerations = 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Handler.BeforeResponse = async token => { entered.SetResult(); await release.Task.WaitAsync(token); };
        var pending = harness.Generator.GenerateAsync(Request());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
            Assert.Equal(VisualGenerationFailure.ConcurrencyLimit, error.Failure);
            Assert.Single(harness.Handler.Requests);
        }
        finally { release.TrySetResult(); }
        await pending;
        harness.Handler.BeforeResponse = null;
        await harness.Generator.GenerateAsync(Request());
        Assert.Equal(2, harness.Handler.Requests.Count);
    }

    [Fact]
    public async Task TransportFailureIsSanitizedAndSlotIsReleased()
    {
        using var harness = new Harness(Png(), options => options.MaxConcurrentGenerations = 1);
        harness.Handler.BeforeResponse = _ => throw new HttpRequestException("secret transport payload");
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.ProviderUnavailable, error.Failure);
        Assert.DoesNotContain("secret", error.ToString());
        harness.Handler.BeforeResponse = null;
        await harness.Generator.GenerateAsync(Request());
    }

    [Theory]
    [InlineData("Model", "")]
    [InlineData("Model", "dall-e-3")]
    [InlineData("Quality", "invalid")]
    [InlineData("TimeoutSeconds", "0")]
    [InlineData("TimeoutSeconds", "601")]
    [InlineData("MaxConcurrentGenerations", "0")]
    [InlineData("MaxConcurrentGenerations", "17")]
    [InlineData("MaxImageBytes", "0")]
    [InlineData("MaxImageBytes", "52428801")]
    [InlineData("TimeoutSeconds", "secret-invalid-number")]
    public void InvalidConfigurationFailsLazilyAndSafely(string key, string value)
    {
        var values = Configuration();
        values["OpenAI:Images:" + key] = value;
        using var provider = new ServiceCollection().AddOpenAIVisualGeneration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IVisualGenerator>());
        Assert.DoesNotContain("secret", error.ToString());
    }

    [Fact]
    public void ConfigurationIsIndependentAndRegistersSingletonWithoutNetwork()
    {
        var values = Configuration();
        values["OpenAI:Model"] = "text-model";
        using var provider = new ServiceCollection().AddOpenAIContentGeneration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .AddOpenAIVisualGeneration(new ConfigurationBuilder().AddInMemoryCollection(values).Build())
            .BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<IVisualGenerator>(), provider.GetRequiredService<IVisualGenerator>());
        Assert.IsType<OpenAIVisualGenerator>(provider.GetRequiredService<IVisualGenerator>());
        Assert.Equal(Model, provider.GetRequiredService<IOptions<OpenAIVisualGeneratorOptions>>().Value.Model);
        Assert.Equal("text-model", provider.GetRequiredService<IOptions<OpenAIContentGeneratorOptions>>().Value.Model);
        Assert.Equal("unit-test-placeholder", provider.GetRequiredService<IOptions<OpenAIVisualGeneratorOptions>>().Value.ApiKey);
    }

    [Fact]
    public void MissingImagesConfigurationDoesNotPreventTextResolution()
    {
        var values = new Dictionary<string, string?> { ["OpenAI:ApiKey"] = "unit-test-placeholder", ["OpenAI:Model"] = "text-model" };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        using var provider = new ServiceCollection().AddOpenAIContentGeneration(config).AddOpenAIVisualGeneration(config).BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IContentGenerator>());
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IVisualGenerator>());
    }

    [Fact]
    public void ImageKeyCanOverrideSharedKey()
    {
        var values = Configuration();
        values["OpenAI:Images:ApiKey"] = "image-test-key";
        using var provider = new ServiceCollection().AddOpenAIVisualGeneration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        Assert.Equal("image-test-key", provider.GetRequiredService<IOptions<OpenAIVisualGeneratorOptions>>().Value.ApiKey);
    }

    [Fact]
    public async Task SdkTimeoutIsClassifiedWithoutExposingException()
    {
        using var harness = new Harness(Png());
        harness.Handler.BeforeResponse = _ => throw new TaskCanceledException("secret timeout");
        var error = await Assert.ThrowsAsync<VisualGenerationException>(() => harness.Generator.GenerateAsync(Request()));
        Assert.Equal(VisualGenerationFailure.Timeout, error.Failure);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Single(harness.Handler.Requests);
    }

    [Fact]
    public void MissingKeyIsRejectedWithoutCreatingClient()
    {
        var values = Configuration();
        values.Remove("OpenAI:ApiKey");
        using var provider = new ServiceCollection().AddOpenAIVisualGeneration(
            new ConfigurationBuilder().AddInMemoryCollection(values).Build()).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IVisualGenerator>());
    }

    private static Dictionary<string, string?> Configuration() => new()
    {
        ["OpenAI:ApiKey"] = "unit-test-placeholder", ["OpenAI:Images:Model"] = Model
    };

    private static string Envelope(byte[] image) => JsonSerializer.Serialize(new
    {
        created = 1790000000, data = new[] { new { b64_json = Convert.ToBase64String(image) } }
    });

    // Real PNG fixture generated locally using BCL only; no external image service.
    private static byte[] Png(int width = 1024, int height = 1024)
    {
        using var result = new MemoryStream();
        result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            var length = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            result.Write(length);
            var payload = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            result.Write(payload);
            uint crc = uint.MaxValue;
            foreach (var item in payload)
            {
                crc ^= item;
                for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 1 ? 0xedb88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(length, ~crc);
            result.Write(length);
        }
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // grayscale
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true))
            zlib.Write(new byte[(width + 1) * height]);
        Chunk("IDAT", compressed.ToArray());
        Chunk("IEND", []);
        return result.ToArray();
    }

    private sealed class Harness : IDisposable
    {
        public byte[] Image { get; }
        public Handler Handler { get; }
        public OpenAIVisualGenerator Generator { get; }
        private readonly HttpClient _http;
        public Harness(byte[] image, Action<OpenAIVisualGeneratorOptions>? configure = null)
        {
            Image = image;
            Handler = new Handler { Body = Envelope(image) };
            _http = new HttpClient(Handler);
            var settings = new OpenAIVisualGeneratorOptions { ApiKey = "unit-test-placeholder", Model = Model };
            configure?.Invoke(settings);
            var client = new ImageClient(Model, new ApiKeyCredential(settings.ApiKey), new OpenAIClientOptions
            {
                Transport = new HttpClientPipelineTransport(_http), RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            });
            Generator = new(client, Options.Create(settings));
        }
        public void Dispose() { Generator.Dispose(); _http.Dispose(); }
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; set; } = "";
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public List<(HttpMethod Method, Uri Uri, string Body)> Requests { get; } = [];
        public Func<CancellationToken, Task>? BeforeResponse { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!, await request.Content!.ReadAsStringAsync(cancellationToken)));
            if (BeforeResponse is not null) await BeforeResponse(cancellationToken);
            return new(Status) { Content = new StringContent(Body, Encoding.UTF8, "application/json") };
        }
    }
}

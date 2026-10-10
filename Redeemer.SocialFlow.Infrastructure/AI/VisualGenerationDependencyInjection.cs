using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Images;
using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Infrastructure.AI;

public static class VisualGenerationDependencyInjection
{
    public static IServiceCollection AddOpenAIVisualGeneration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenAIVisualGeneratorOptions>()
            .Configure(options =>
            {
                const string prefix = OpenAIVisualGeneratorOptions.SectionName + ":";
                options.ApiKey = configuration[prefix + "ApiKey"] ?? configuration["OpenAI:ApiKey"] ?? string.Empty;
                options.Model = configuration[prefix + "Model"] ?? string.Empty;
                options.Quality = configuration[prefix + "Quality"] ?? options.Quality;
                options.TimeoutSeconds = ReadInteger(configuration[prefix + "TimeoutSeconds"], options.TimeoutSeconds);
                options.MaxConcurrentGenerations = ReadInteger(configuration[prefix + "MaxConcurrentGenerations"], options.MaxConcurrentGenerations);
                options.MaxImageBytes = ReadInteger(configuration[prefix + "MaxImageBytes"], options.MaxImageBytes);
            })
            .Validate(options => options.IsValid(), "Invalid OpenAI image configuration. Check key, GPT image model, quality and limits.");
        // Lazy resolution: startup and text-only workflows require no image configuration.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<OpenAIVisualGeneratorOptions>>().Value;
            return new ImageClient(options.Model, new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions
            {
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
                NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
            });
        });
        services.AddSingleton<IVisualGenerator, OpenAIVisualGenerator>();
        return services;
    }
    private static int ReadInteger(string? value, int fallback) => value is null ? fallback :
        int.TryParse(value, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;
}

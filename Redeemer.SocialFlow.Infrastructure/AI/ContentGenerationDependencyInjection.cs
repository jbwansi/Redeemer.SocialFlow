#pragma warning disable OPENAI001 // Official SDK Responses surface.

using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using Redeemer.SocialFlow.Application.AI;

namespace Redeemer.SocialFlow.Infrastructure.AI;

public static class ContentGenerationDependencyInjection
{
    public static IServiceCollection AddOpenAIContentGeneration(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OpenAIContentGeneratorOptions>()
            .Configure(options =>
            {
                options.ApiKey = configuration[$"{OpenAIContentGeneratorOptions.SectionName}:ApiKey"] ?? string.Empty;
                options.Model = configuration[$"{OpenAIContentGeneratorOptions.SectionName}:Model"] ?? string.Empty;
            })
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "OpenAI:ApiKey is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Model), "OpenAI:Model is required.");

        // Resolve lazily so unrelated API operations work without OpenAI configuration.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<OpenAIContentGeneratorOptions>>().Value;
            return new ResponsesClient(new ApiKeyCredential(options.ApiKey), new ResponsesClientOptions
            {
                // Keep the generation limit predictable: no hidden transport retries.
                RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
            });
        });
        services.AddSingleton<IContentGenerator, OpenAIContentGenerator>();
        return services;
    }
}

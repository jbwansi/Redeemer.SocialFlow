using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing.LinkedIn;
using Xunit;

namespace Redeemer.SocialFlow.IntegrationTests.Publishing;

public sealed class LinkedInRegistrationTests
{
    [Theory]
    [InlineData("Development", null, false)]
    [InlineData("Development", "false", false)]
    [InlineData("Development", "true", true)]
    [InlineData("Production", "true", false)]
    [InlineData("Staging", "true", false)]
    public void PublicationRequiresExplicitDevelopmentOptIn(string environment, string? enabled, bool expected)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var host = new Host { EnvironmentName = environment };
        services.AddSingleton<IHostEnvironment>(host);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["LinkedIn:PublicationEnabled"] = enabled }).Build();
        services.AddLocalLinkedIn(configuration, host);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetService<IPostPublisher>();
        if (expected)
        {
            Assert.IsType<LinkedInPostPublisher>(publisher);
            Assert.IsType<ProtectedLinkedInCredentialProvider>(scope.ServiceProvider.GetRequiredService<ILinkedInCredentialProvider>());
            Assert.Contains(services, item => item.ServiceType == typeof(IPublishScheduledPost));
        }
        else Assert.Null(publisher);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(LinkedInPublicationWorker));
    }

    [Theory]
    [InlineData("Development", true, true, true)]
    [InlineData("Development", true, false, false)]
    [InlineData("Development", false, true, false)]
    [InlineData("Production", true, true, false)]
    public void WorkerRequiresBothFlagsInDevelopment(string environment, bool publication, bool worker, bool expected)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["LinkedIn:PublicationEnabled"] = publication.ToString(), ["LinkedIn:WorkerEnabled"] = worker.ToString() }).Build();
        services.AddLocalLinkedIn(configuration, new Host { EnvironmentName = environment });
        Assert.Equal(expected, services.Any(item => item.ServiceType == typeof(IHostedService) && item.ImplementationType == typeof(LinkedInPublicationWorker)));
    }

    private sealed class Host : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Redeemer.SocialFlow.Api.Development.Facebook;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Application.Publishing;
using Redeemer.SocialFlow.Infrastructure.Publishing;
using Xunit;
namespace Redeemer.SocialFlow.IntegrationTests.Publishing;
public sealed class FacebookRegistrationTests
{
    [Theory] [InlineData(true,true,1)] [InlineData(true,false,1)] [InlineData(false,true,1)] [InlineData(false,false,0)]
    public void BothPlatformsShareExactlyOneWorker(bool linkedin,bool facebook,int expected)
    {
        var services=new ServiceCollection();services.AddLogging();var host=new Host();services.AddSingleton<IHostEnvironment>(host);
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["LinkedIn:PublicationEnabled"]="true",["LinkedIn:WorkerEnabled"]=linkedin.ToString(),
            ["Facebook:Enabled"]="true",["Facebook:PublicationEnabled"]="true",["Facebook:WorkerEnabled"]=facebook.ToString() }).Build();
        services.AddLocalLinkedIn(configuration,host);services.AddLocalFacebook(configuration,host);
        Assert.Equal(expected,services.Count(s=>s.ServiceType==typeof(IHostedService)&&s.ImplementationType==typeof(LinkedInPublicationWorker)));
        Assert.Contains(services,s=>s.ServiceType==typeof(FacebookPublicationService));
    }
    [Theory] [InlineData("Production",true)] [InlineData("Development",false)]
    public void FacebookOffDoesNotReplaceLinkedIn(string environment,bool enabled)
    {
        var services=new ServiceCollection();var host=new Host {EnvironmentName=environment};
        services.AddLocalFacebook(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Facebook:Enabled"]=enabled.ToString(),["Facebook:PublicationEnabled"]="true",["Facebook:WorkerEnabled"]="true" }).Build(),host);
        Assert.DoesNotContain(services,s=>s.ServiceType==typeof(IPostPublisher)||s.ServiceType==typeof(IHostedService));
    }
    private sealed class Host:IHostEnvironment {
        public string EnvironmentName {get;set;}="Development";public string ApplicationName {get;set;}="Tests";
        public string ContentRootPath {get;set;}="";public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
    }
}

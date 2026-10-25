using Redeemer.SocialFlow.Api.Development.Visuals;
using Redeemer.SocialFlow.Api.Development.Facebook;
using Redeemer.SocialFlow.Infrastructure;
using Redeemer.SocialFlow.Infrastructure.AI;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Api.Errors;
using Redeemer.SocialFlow.Api.Controllers;
using Scalar.AspNetCore;
using Redeemer.SocialFlow.Api.Development;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Api.Development.LinkedIn;
using Redeemer.SocialFlow.Api.Security;

var builder = WebApplication.CreateBuilder(args);
Redeemer.SocialFlow.Infrastructure.Social.SocialDependencyInjection.AddWorkspaceSocialConnections(builder.Services);
builder.Services.AddHttpClient("WorkspaceLinkedInPublication", client => client.Timeout = TimeSpan.FromSeconds(30))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    .RemoveAllLoggers();
// Incoming OAuth codes must not enter framework request/endpoint diagnostics, including Production.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
// Authentication phase never starts historical automatic publishers, even with inherited flags.
builder.Configuration["LinkedIn:WorkerEnabled"] = "false";
builder.Configuration["Facebook:WorkerEnabled"] = "false";
builder.Services.AddAccountSecurity(builder.Configuration);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<LocalOAuthTickets>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddLocalLinkedIn(builder.Configuration, builder.Environment);
    builder.Services.AddLocalFacebook(builder.Configuration, builder.Environment);
    // Hosting/MVC informational and trace logs may include OAuth callback query values.
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
}

var connectionString = builder.Configuration.GetConnectionString("SocialFlow")
    ?? throw new InvalidOperationException("Connection string 'SocialFlow' is required.");
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();
builder.Services.AddOpenAIContentGeneration(builder.Configuration);
builder.Services.AddOpenAIVisualGeneration(builder.Configuration);
builder.Services.AddLocalVisuals(builder.Configuration, builder.Environment);
builder.Services.AddControllers(options =>
{
    options.Filters.AddService<AccountCsrfFilter>();
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    options.Conventions.Add(new FacebookEndpointsConvention(builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Facebook:Enabled")));
    options.Conventions.Add(new VisualEndpointsConvention(builder.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Visuals:Enabled")));
    if (!builder.Environment.IsDevelopment())
        options.Conventions.Add(new DevelopmentOnlyControllerConvention());
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

await using var app = builder.Build();
app.Use(async (context,next) => {
    if(context.Request.Path.StartsWithSegments("/api/social-oauth")) {
        context.Items["social-oauth-query"]=context.Request.Query;
        context.Request.QueryString=QueryString.Empty;
        context.Response.Headers["Cache-Control"]="no-store";
        context.Response.Headers["Referrer-Policy"]="no-referrer";
    }
    await next(context);
});
var historicalOwner = args.SingleOrDefault(a => a.StartsWith(HistoricalWorkspaceOwner.Prefix, StringComparison.Ordinal));
if (historicalOwner is not null)
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Historical owner assignment is a local Development operation.");
    await using var scope = app.Services.CreateAsyncScope();
    await HistoricalWorkspaceOwner.AssignAsync(historicalOwner[HistoricalWorkspaceOwner.Prefix.Length..],
        scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>(),
        scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Redeemer.SocialFlow.Infrastructure.Accounts.ApplicationUser>>());
    Console.WriteLine("Historical workspace ownership verified. No social connection was assigned or changed.");
    return;
}
if (ManualKnowledgeSeed.IsRequested(args))
{
    await using var scope = app.Services.CreateAsyncScope();
    var workspaceArgument = args.SingleOrDefault(a => a.StartsWith("--workspace-id=", StringComparison.Ordinal));
    if (!Guid.TryParse(workspaceArgument?["--workspace-id=".Length..], out var seedWorkspace))
        throw new InvalidOperationException("Knowledge maintenance requires an explicit --workspace-id.");
    scope.ServiceProvider.GetRequiredService<Redeemer.SocialFlow.Application.Accounts.WorkspaceAccessContext>().Bind(seedWorkspace);
    await ManualKnowledgeSeed.RunAsync(args, app.Environment,
        scope.ServiceProvider.GetRequiredService<SocialFlowDbContext>(), TimeProvider.System,
        app.Lifetime.ApplicationStopping);
    Console.WriteLine("Knowledge test seed completed (existing document left unchanged).");
    return;
}
app.UseExceptionHandler();
app.UseStatusCodePages();
// The built SPA shares the API's HTTPS origin. No Vite proxy is required for account cookies.
var spaRoot = app.Environment.IsDevelopment()
    ? Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "Redeemer.SocialFlow.Web", "dist"))
    : app.Environment.WebRootPath;
if (spaRoot is not null && Directory.Exists(spaRoot))
{
    var files = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(spaRoot);
    app.Lifetime.ApplicationStopped.Register(files.Dispose);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
}
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    if (context.Request.Path.StartsWithSegments("/api/auth") && !context.Request.IsHttps)
    {
        await AccountSecurity.WriteProblem(context.Response, 400, "HTTPS requis.");
        return;
    }
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.Run();

public partial class Program { }

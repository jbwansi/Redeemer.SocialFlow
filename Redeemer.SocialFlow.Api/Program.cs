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
if (ManualKnowledgeSeed.IsRequested(args))
{
    await using var scope = app.Services.CreateAsyncScope();
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

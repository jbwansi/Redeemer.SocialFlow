using Redeemer.SocialFlow.Infrastructure;
using Redeemer.SocialFlow.Infrastructure.AI;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Api.Errors;
using Redeemer.SocialFlow.Api.Controllers;
using Scalar.AspNetCore;
using Redeemer.SocialFlow.Api.Development;
using Redeemer.SocialFlow.Infrastructure.Persistence;
using Redeemer.SocialFlow.Api.Development.LinkedIn;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddLocalLinkedIn(builder.Configuration, builder.Environment);
    // Hosting/MVC informational and trace logs may include OAuth callback query values.
    builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
}

var connectionString = builder.Configuration.GetConnectionString("SocialFlow")
    ?? throw new InvalidOperationException("Connection string 'SocialFlow' is required.");
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();
builder.Services.AddOpenAIContentGeneration(builder.Configuration);
builder.Services.AddControllers(options =>
{
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
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
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
app.Run();

public partial class Program { }

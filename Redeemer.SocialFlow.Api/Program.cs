using Redeemer.SocialFlow.Infrastructure;
using Redeemer.SocialFlow.Application;
using Redeemer.SocialFlow.Api.Errors;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("SocialFlow")
    ?? throw new InvalidOperationException("Connection string 'SocialFlow' is required.");
builder.Services.AddPersistence(connectionString);
builder.Services.AddApplication();
builder.Services.AddControllers(options =>
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
    context.ProblemDetails.Extensions.TryAdd("traceId", context.HttpContext.TraceIdentifier);
});
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();
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

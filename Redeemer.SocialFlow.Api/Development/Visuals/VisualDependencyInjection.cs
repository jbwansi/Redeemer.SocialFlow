using Redeemer.SocialFlow.Application.Visuals;
using Redeemer.SocialFlow.Infrastructure.Visuals;

namespace Redeemer.SocialFlow.Api.Development.Visuals;

public static class VisualDependencyInjection
{
    public static IServiceCollection AddLocalVisuals(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var enabled = environment.IsDevelopment() && configuration.GetValue<bool>("Visuals:Enabled");
        var access = new LocalVisualAccess(enabled, configuration.GetValue<bool>("Visuals:AllowHttpLoopback"),
            Environment.GetEnvironmentVariable("SOCIALFLOW_VISUAL_ADMIN_SECRET"),
            configuration["OpenAI:ApiKey"], configuration["OpenAI:Images:ApiKey"]);
        services.AddSingleton(access);
        services.AddScoped<LocalVisualAccessFilter>();
        services.AddSingleton(_ =>
        {
            var limits = new VisualLimits();
            configuration.GetSection("Visuals:Limits").Bind(limits);
            if (limits.MaxBytes is < 1024 or > 20 * 1024 * 1024 ||
                limits.MaxDimension is < 1 or > 8192 || limits.MaxPixels is < 1 or > 36_000_000)
                throw new InvalidOperationException("Invalid visual limits.");
            return limits;
        });
        services.AddSingleton(_ => new LocalVisualStorageOptions
        {
            RootPath = configuration["Visuals:Storage:RootPath"] ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Redeemer.SocialFlow", "Visuals"),
            ForbiddenRoots = new[] { environment.ContentRootPath, environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"),
                Path.Combine(Directory.GetParent(environment.ContentRootPath)!.FullName, "Redeemer.SocialFlow.Web") }
        });
        services.AddSingleton<IVisualStorage, LocalVisualStorage>();
        services.AddSingleton<IVisualValidator, SkiaVisualValidator>();
        services.AddScoped<IPostVisualStore, SqlitePostVisualStore>();
        services.AddScoped<PostVisualService>();
        services.AddScoped<IVisualIdentityRepository, SqliteVisualIdentityRepository>();
        services.AddScoped<VisualIdentityLibrary>();
        return services;
    }
}

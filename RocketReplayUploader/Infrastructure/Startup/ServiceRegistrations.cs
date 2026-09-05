using Microsoft.Extensions.DependencyInjection;
using RocketReplayUploader.Application.Services;
using RocketReplayUploader.Infrastructure.Config;

namespace RocketReplayUploader.Infrastructure.Startup;

public static class ServiceRegistrations
{
    public static IServiceCollection AddReplayServices(this IServiceCollection services, AppConfig config)
    {
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<BallchasingClient>();
        services.AddSingleton<BallchasingService>();
        services.AddSingleton<IBallchasingService>(sp => sp.GetRequiredService<BallchasingService>());
        services.AddSingleton<FileRenamerService>();
        services.AddSingleton<UploadQueueService>();
        services.AddSingleton<ReplayProcessor>();
        services.AddSingleton<ReplayWatcher>();
        return services;
    }
}

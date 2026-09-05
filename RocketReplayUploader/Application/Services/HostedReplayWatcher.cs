using Microsoft.Extensions.Hosting;

namespace RocketReplayUploader.Application.Services;

// Envuelve el ReplayWatcher como BackgroundService para el modo servicio
// (Windows Service / --service), donde no hay interfaz que lo apague.
public class HostedReplayWatcher : BackgroundService
{
    private readonly ReplayWatcher _watcher;

    public HostedReplayWatcher(ReplayWatcher watcher)
    {
        _watcher = watcher;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _watcher.Start();
        return Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher.Stop();
        await base.StopAsync(cancellationToken);
    }
}

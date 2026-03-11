using BCTC.App.IService;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace BCTC.App.Services.MappingCache
{
    /// <summary>
    /// Background service chạy định kỳ để dọn dẹp cache expired
    /// Chạy mỗi ngày lúc 2:00 AM
    /// </summary>
    public class CacheCleanupBackgroundService : BackgroundService
    {
        private readonly ILogger<CacheCleanupBackgroundService> _logger;
        private readonly IFileMappingCacheService _cacheService;
        private readonly TimeSpan _checkInterval;

        public CacheCleanupBackgroundService(
            ILogger<CacheCleanupBackgroundService> logger,
            IFileMappingCacheService cacheService,
            TimeSpan? checkInterval = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
            _checkInterval = checkInterval ?? TimeSpan.FromHours(24); // Default: 24 hours
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[CacheCleanup] Background service started. Check interval: {Interval}",
                _checkInterval);

            // Wait until target time (2 AM)
            await WaitUntilTargetTimeAsync(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("[CacheCleanup] Running scheduled cleanup...");

                    var startTime = DateTime.UtcNow;
                    await _cacheService.CleanExpiredCacheAsync();
                    var duration = DateTime.UtcNow - startTime;

                    _logger.LogInformation("[CacheCleanup] Cleanup completed in {Duration:F2}s",
                        duration.TotalSeconds);

                    // Wait for next check
                    await Task.Delay(_checkInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[CacheCleanup] Service is stopping");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[CacheCleanup] Error during cleanup. Will retry in 1 hour.");

                    // Wait 1 hour before retry
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("[CacheCleanup] Background service stopped");
        }

        private async Task WaitUntilTargetTimeAsync(CancellationToken ct)
        {
            var now = DateTime.Now;
            var targetTime = DateTime.Today.AddHours(2); // 2 AM today

            // If past 2 AM today, schedule for tomorrow
            if (now > targetTime)
            {
                targetTime = targetTime.AddDays(1);
            }

            var delay = targetTime - now;

            if (delay.TotalSeconds > 0)
            {
                _logger.LogInformation("[CacheCleanup] Next cleanup scheduled at: {Time} ({Delay:F1}h from now)",
                    targetTime, delay.TotalHours);

                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("[CacheCleanup] Startup delay cancelled");
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[CacheCleanup] Stopping background service...");
            await base.StopAsync(cancellationToken);
        }
    }
}
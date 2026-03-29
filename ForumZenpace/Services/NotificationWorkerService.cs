namespace ForumZenpace.Services
{
    public class NotificationWorkerService : BackgroundService
    {
        private readonly IBackgroundTaskQueue _taskQueue;
        private readonly ILogger<NotificationWorkerService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;

        public NotificationWorkerService(IBackgroundTaskQueue taskQueue, ILogger<NotificationWorkerService> logger, IServiceScopeFactory serviceScopeFactory)
        {
            _taskQueue = taskQueue;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("NotificationWorkerService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        await workItem(scope.ServiceProvider, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ignore when cancellation is requested
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred executing background work item.");
                }
            }

            _logger.LogInformation("NotificationWorkerService is stopping.");
        }
    }
}

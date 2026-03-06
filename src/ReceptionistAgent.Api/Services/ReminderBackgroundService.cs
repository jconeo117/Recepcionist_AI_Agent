using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceptionistAgent.Core.Services;

namespace ReceptionistAgent.Api.Services;

/// <summary>
/// Background service que procesa recordatorios pendientes cada 5 minutos.
/// Consulta reminders con ScheduledFor <= now y los envía por Twilio.
/// </summary>
public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ReminderBackgroundService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public ReminderBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<ReminderBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ReminderBackgroundService started. Checking every {Interval} minutes.", _checkInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing reminders.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingRemindersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var reminderService = scope.ServiceProvider.GetRequiredService<IReminderService>();
        var messageSender = scope.ServiceProvider.GetRequiredService<IMessageSender>();

        var pendingReminders = await reminderService.GetPendingRemindersAsync(DateTime.UtcNow);

        if (!pendingReminders.Any())
            return;

        _logger.LogInformation("Processing {Count} pending reminders.", pendingReminders.Count);

        foreach (var reminder in pendingReminders)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var message = reminder.MessageContent ?? "Recordatorio: Tiene una cita próximamente.";
                var success = await messageSender.SendAsync(reminder.RecipientPhone, message);

                if (success)
                {
                    await reminderService.MarkAsSentAsync(reminder.Id);
                    _logger.LogInformation("Reminder {Id} sent to {Phone}.", reminder.Id, reminder.RecipientPhone);
                }
                else
                {
                    await reminderService.MarkAsFailedAsync(reminder.Id, "Message delivery failed.");
                    _logger.LogWarning("Reminder {Id} failed to send to {Phone}.", reminder.Id, reminder.RecipientPhone);
                }
            }
            catch (Exception ex)
            {
                await reminderService.MarkAsFailedAsync(reminder.Id, ex.Message);
                _logger.LogError(ex, "Error sending reminder {Id}.", reminder.Id);
            }
        }
    }
}

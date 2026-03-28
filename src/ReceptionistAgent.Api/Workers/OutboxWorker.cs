using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ReceptionistAgent.Core.Models;
using ReceptionistAgent.Core.Services;
using ReceptionistAgent.Core.Adapters;
using ReceptionistAgent.Core.Tenant;
using ReceptionistAgent.Connectors.Adapters;

namespace ReceptionistAgent.Api.Workers;

public class OutboxWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWebhookSenderService _webhookSender;
    private readonly ILogger<OutboxWorker> _logger;

    public OutboxWorker(IServiceProvider serviceProvider, IWebhookSenderService webhookSender, ILogger<OutboxWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _webhookSender = webhookSender;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxWorker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessOutboxEventsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OutboxWorker");
            }

            await Task.Delay(5000, stoppingToken); // Poll every 5 seconds
        }
    }

    private async Task ProcessOutboxEventsAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var tenantResolver = scope.ServiceProvider.GetRequiredService<ITenantResolver>();
        var backupService = scope.ServiceProvider.GetRequiredService<IBookingBackupService>();
        var adapterFactory = scope.ServiceProvider.GetRequiredService<ClientDataAdapterFactory>();

        var tenants = await tenantResolver.GetAllTenantsAsync();

        foreach (var tenant in tenants)
        {
            if (stoppingToken.IsCancellationRequested) break;

            try
            {
                var adapter = adapterFactory.CreateAdapter(tenant.ConnectionString, tenant.DbType);
                var events = await adapter.GetUnprocessedOutboxEventsAsync(20);

                foreach (var @event in events)
                {
                    try
                    {
                        await ProcessEventAsync(@event, backupService);
                        
                        // Enviar Webhook si el tenant tiene uno configurado
                        if (!string.IsNullOrWhiteSpace(tenant.WebhookUrl))
                        {
                            await _webhookSender.SendEventAsync(tenant.WebhookUrl, @event.EventType, @event.PayloadJson);
                        }

                        await adapter.UpdateOutboxStatusAsync(@event.Id, true);
                        _logger.LogInformation("Processed outbox event {Id} for tenant {TenantId}", @event.Id, tenant.TenantId);
                    }
                    catch (Exception ex)
                    {
                        await adapter.UpdateOutboxStatusAsync(@event.Id, false, ex.Message);
                        _logger.LogError(ex, "Failed to process outbox event {Id}", @event.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not process outbox for tenant {TenantId}: {Message}", tenant.TenantId, ex.Message);
            }
        }
    }

    private async Task ProcessEventAsync(OutboxEvent @event, IBookingBackupService backupService)
    {
        switch (@event.EventType)
        {
            case "BookingCreated":
                var booking = JsonSerializer.Deserialize<BookingRecord>(@event.PayloadJson);
                if (booking != null)
                {
                    await backupService.BackupAsync(booking, @event.TenantId);
                }
                break;
            case "BookingCancelled":
                var cancelledBooking = JsonSerializer.Deserialize<BookingRecord>(@event.PayloadJson);
                if (cancelledBooking != null)
                {
                    await backupService.UpdateStatusBackupAsync(cancelledBooking.Id, @event.TenantId, BookingStatus.Cancelled);
                }
                break;
        }
    }
}

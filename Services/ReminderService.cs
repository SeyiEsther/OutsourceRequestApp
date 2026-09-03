using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// Background service that runs every hour and sends reminder emails to
    /// approvers who have not acted on pending requests within the configured
    /// reminder interval (ReminderHours setting in Admin).
    /// </summary>
    public class ReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReminderService> _logger;

        public ReminderService(IServiceScopeFactory scopeFactory, ILogger<ReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Reminder service started.");

            // Stagger initial delay by 5 minutes so the app is fully warmed up
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                await CheckAndSendRemindersAsync();
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task CheckAndSendRemindersAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db    = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var email = scope.ServiceProvider.GetRequiredService<EmailService>();

                // Read the reminder interval from settings (default 24 hours)
                var setting = db.AppSettings
                    .FirstOrDefault(s => s.SettingKey == "ReminderHours")?.SettingValue ?? "24";

                if (!int.TryParse(setting, out int reminderHours) || reminderHours <= 0)
                    reminderHours = 24;

                var cutoff = DateTime.Now.AddHours(-reminderHours);

                // Requests that are pending AND haven't had a reminder within the interval
                var pendingStatuses = new[]
                {
                    RequestStatus.Submitted,
                    RequestStatus.ProductionPending,
                    RequestStatus.CostCompactPending,
                    RequestStatus.SourcingPending,
                    RequestStatus.MdPending
                };

                var pending = db.OutsourceRequests
                    .Where(r => pendingStatuses.Contains(r.Status)
                             && r.CreatedAt <= cutoff
                             && (r.LastReminderSentAt == null || r.LastReminderSentAt <= cutoff))
                    .ToList();

                if (!pending.Any())
                {
                    _logger.LogDebug("Reminder check: no overdue requests found.");
                    return;
                }

                _logger.LogInformation("Reminder check: {Count} overdue request(s) found.", pending.Count);

                foreach (var request in pending)
                {
                    var roleKey = RequestStatus.RoleKeyForPendingStatus(request.Status);
                    if (roleKey == null) continue;

                    var approver = db.ApproverRoles.FirstOrDefault(r => r.RoleKey == roleKey);
                    if (approver == null || string.IsNullOrEmpty(approver.Email)) continue;

                    email.SendReminder(request, approver);

                    _logger.LogInformation(
                        "Reminder sent for OSR-{Id} to {Email} ({Role})",
                        request.RequestId, approver.Email, roleKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in ReminderService.");
            }
        }
    }
}

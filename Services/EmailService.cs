using System.Net;
using System.Net.Mail;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Services
{
    public class EmailService
    {
        private readonly AppDbContext _db;
        private readonly ILogger<EmailService> _logger;

        public EmailService(AppDbContext db, ILogger<EmailService> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        private record SmtpConfig(string Host, int Port, string From, string FromName);

        /// <summary>
        /// Reads SMTP settings from the database. Returns null if SMTP is not configured.
        /// </summary>
        private SmtpConfig? GetConfig()
        {
            var settings = _db.AppSettings.ToList();

            string Get(string key, string fallback = "") =>
                settings.FirstOrDefault(s => s.SettingKey == key)?.SettingValue ?? fallback;

            var host = Get("SmtpHost");
            if (string.IsNullOrEmpty(host))
            {
                _logger.LogWarning("SMTP host not configured — email skipped.");
                return null;
            }

            int.TryParse(Get("SmtpPort", "25"), out int port);

            return new SmtpConfig(
                host,
                port,
                Get("SmtpFrom", "outsource-portal@company.com"),
                Get("SmtpFromName", "Outsource Portal"));
        }

        /// <summary>
        /// Creates, uses, and disposes an SmtpClient for a single send operation.
        /// </summary>
        private void Send(SmtpConfig cfg, MailMessage msg)
        {
            using var client = new SmtpClient(cfg.Host, cfg.Port)
            {
                DeliveryMethod      = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = true,
                EnableSsl           = false
            };
            client.Send(msg);
        }

        private string RequesterEmail(OutsourceRequest req)
        {
            var raw = req.CreatedByUsername ?? "";

            // Under Windows Authentication the app resolves the caller's AD e-mail,
            // so CreatedByUsername is normally already a full e-mail address.
            if (raw.Contains('@')) return raw;

            // Fallback for legacy rows stored as DOMAIN\user (or a bare username):
            // build an address from the configured company e-mail domain.
            var domain = _db.AppSettings
                .FirstOrDefault(s => s.SettingKey == "CompanyEmailDomain")?.SettingValue
                ?? "company.com";

            var username = raw.Contains('\\') ? raw.Split('\\')[1] : raw;
            return $"{username}@{domain}";
        }

        private static string AppUrl()
        {
            // Adjust this if your server address changes
            return "http://csm-srv-16:5200";
        }

        // ----------------------------------------------------------------
        // Public send methods
        // ----------------------------------------------------------------

        /// <summary>Notifies an approver that a request is awaiting their action.</summary>
        public void SendToApprover(OutsourceRequest req, ApproverRole approver)
        {
            try
            {
                var cfg = GetConfig();
                if (cfg == null || string.IsNullOrEmpty(approver.Email)) return;

                var msg = new MailMessage
                {
                    From    = new MailAddress(cfg.From, cfg.FromName),
                    Subject = $"Action required — OSR-{req.RequestId:000000} awaiting your approval",
                    Body    = $@"Hi {approver.FullName},

An outsource request requires your approval.

  Request:   OSR-{req.RequestId:000000}
  Part:      {req.PartNumber} — {req.SapDescription}
  Quantity:  {req.Quantity}
  Submitted: {req.CreatedAt:dd MMM yyyy HH:mm} by {req.CreatedByUsername}
  Reason:    {req.Reason}

Please log in to the portal to review this request:
{AppUrl()}/Outsource/Track/{req.RequestId}

This is an automated message — please do not reply.
",
                    IsBodyHtml = false
                };
                msg.To.Add(new MailAddress(approver.Email, approver.FullName));

                Send(cfg, msg);
                _logger.LogInformation("Approval email sent to {Email} for OSR-{Id}", approver.Email, req.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send approval email for OSR-{Id}", req.RequestId);
            }
        }

        /// <summary>
        /// Confirms to the original requester that their request has been submitted
        /// and is awaiting Supply Chain review.
        /// </summary>
        public void SendSubmissionConfirmation(OutsourceRequest req)
        {
            try
            {
                var cfg = GetConfig();
                if (cfg == null) return;

                var to = RequesterEmail(req);

                var msg = new MailMessage
                {
                    From    = new MailAddress(cfg.From, cfg.FromName),
                    Subject = $"Request received — OSR-{req.RequestId:000000}",
                    Body    = $@"Hi,

Your outsource request has been submitted and is now awaiting Supply Chain review.

  Request:   OSR-{req.RequestId:000000}
  Part:      {req.PartNumber} — {req.SapDescription}
  Quantity:  {req.Quantity}
  Submitted: {req.CreatedAt:dd MMM yyyy HH:mm}

You can track the progress of your request here:
{AppUrl()}/Outsource/Track/{req.RequestId}

This is an automated message — please do not reply.
",
                    IsBodyHtml = false
                };
                msg.To.Add(to);

                Send(cfg, msg);
                _logger.LogInformation("Submission confirmation sent to {Email} for OSR-{Id}", to, req.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send submission confirmation for OSR-{Id}", req.RequestId);
            }
        }

        /// <summary>Tells the requester the outcome at a given approval stage.</summary>
        public void SendConfirmationToRequester(OutsourceRequest req, string stage, bool approved, string comments)
        {
            try
            {
                var cfg = GetConfig();
                if (cfg == null) return;

                var to     = RequesterEmail(req);
                var status = approved ? "APPROVED" : "REJECTED";

                var msg = new MailMessage
                {
                    From    = new MailAddress(cfg.From, cfg.FromName),
                    Subject = $"OSR-{req.RequestId:000000} — {stage} {status.ToLower()}",
                    Body    = $@"Hi,

Your outsource request has been {status.ToLower()} at the {stage} stage.

  Request:   OSR-{req.RequestId:000000}
  Part:      {req.PartNumber} — {req.SapDescription}
  Stage:     {stage}
  Decision:  {status}
{(string.IsNullOrEmpty(comments) ? "" : $"  Comments:  {comments}\n")}
Track the full approval history here:
{AppUrl()}/Outsource/Track/{req.RequestId}

This is an automated message — please do not reply.
",
                    IsBodyHtml = false
                };
                msg.To.Add(to);

                Send(cfg, msg);
                _logger.LogInformation("Confirmation email sent to {Email} for OSR-{Id} ({Stage} {Status})",
                    to, req.RequestId, stage, status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for OSR-{Id}", req.RequestId);
            }
        }

        /// <summary>
        /// Notifies the SC approver that a request has been withdrawn by the requester.
        /// </summary>
        public void SendCancellationNotice(OutsourceRequest req, ApproverRole scApprover)
        {
            try
            {
                var cfg = GetConfig();
                if (cfg == null || string.IsNullOrEmpty(scApprover.Email)) return;

                var msg = new MailMessage
                {
                    From    = new MailAddress(cfg.From, cfg.FromName),
                    Subject = $"Withdrawn — OSR-{req.RequestId:000000} has been cancelled",
                    Body    = $@"Hi {scApprover.FullName},

The following outsource request has been withdrawn by the requester and no further action is needed.

  Request:   OSR-{req.RequestId:000000}
  Part:      {req.PartNumber} — {req.SapDescription}
  Submitted: {req.CreatedAt:dd MMM yyyy HH:mm} by {req.CreatedByUsername}

This is an automated message — please do not reply.
",
                    IsBodyHtml = false
                };
                msg.To.Add(new MailAddress(scApprover.Email, scApprover.FullName));

                Send(cfg, msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send cancellation notice for OSR-{Id}", req.RequestId);
            }
        }

        /// <summary>Chase email sent by the background reminder service.</summary>
        public void SendReminder(OutsourceRequest req, ApproverRole approver)
        {
            try
            {
                var cfg = GetConfig();
                if (cfg == null || string.IsNullOrEmpty(approver.Email)) return;

                var waiting = DateTime.Now - req.CreatedAt;
                var waitStr = waiting.TotalDays >= 1
                    ? $"{(int)waiting.TotalDays}d {waiting.Hours}h"
                    : $"{waiting.Hours}h {waiting.Minutes}m";

                var msg = new MailMessage
                {
                    From    = new MailAddress(cfg.From, cfg.FromName),
                    Subject = $"Reminder — OSR-{req.RequestId:000000} is still awaiting your approval",
                    Body    = $@"Hi {approver.FullName},

This is a reminder that the following outsource request is still waiting for your action.

  Request:   OSR-{req.RequestId:000000}
  Part:      {req.PartNumber} — {req.SapDescription}
  Submitted: {req.CreatedAt:dd MMM yyyy HH:mm}
  Waiting:   {waitStr}

Please log in to the portal to take action:
{AppUrl()}/Outsource/Track/{req.RequestId}

This is an automated message — please do not reply.
",
                    IsBodyHtml = false
                };
                msg.To.Add(new MailAddress(approver.Email, approver.FullName));

                Send(cfg, msg);

                req.LastReminderSentAt = DateTime.Now;
                _db.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send reminder email for OSR-{Id}", req.RequestId);
            }
        }
    }
}

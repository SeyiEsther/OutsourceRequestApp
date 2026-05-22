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

        private SmtpClient? BuildClient(out string fromAddress, out string fromName)
        {
            var settings = _db.AppSettings.ToList();

            string Get(string key, string fallback = "") =>
                settings.FirstOrDefault(s => s.SettingKey == key)?.SettingValue ?? fallback;

            var host = Get("SmtpHost");
            fromAddress = Get("SmtpFrom", "outsource-portal@company.com");
            fromName = Get("SmtpFromName", "Outsource Portal");

            if (string.IsNullOrEmpty(host))
            {
                _logger.LogWarning("SMTP host not configured — email not sent.");
                return null;
            }

            int.TryParse(Get("SmtpPort", "25"), out int port);

            return new SmtpClient(host, port)
            {
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = true,
                EnableSsl = false
            };
        }

        public void SendToApprover(OutsourceRequest req, ApproverRole approver)
        {
            try
            {
                var client = BuildClient(out var from, out var fromName);
                if (client == null || string.IsNullOrEmpty(approver.Email)) return;

                var subject = $"Action required — Outsource Request OSR-{req.RequestId:000000}";
                var body = $@"
Hi {approver.FullName},

An outsource request requires your approval.

Request:    OSR-{req.RequestId:000000}
Part:       {req.PartNumber} — {req.SapDescription}
Quantity:   {req.Quantity}
Submitted:  {req.CreatedAt:dd MMM yyyy HH:mm}
Reason:     {req.Reason}

Please log in to the Outsource Portal to review and approve or reject this request.

This is an automated message from the Outsource Request Portal.
";
                var msg = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };
                msg.To.Add(new MailAddress(approver.Email, approver.FullName));

                client.Send(msg);
                _logger.LogInformation("Approval email sent to {Email} for OSR-{Id}", approver.Email, req.RequestId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send approval email for OSR-{Id}", req.RequestId);
            }
        }

        public void SendConfirmationToRequester(OutsourceRequest req, string stage, bool approved, string comments)
        {
            try
            {
                var client = BuildClient(out var from, out var fromName);
                if (client == null) return;

                // Try to derive requester email from username (DOMAIN\username -> username@company.com)
                // You can adjust this logic to match your company's email format
                var settings = _db.AppSettings.ToList();
                var emailDomain = settings.FirstOrDefault(s => s.SettingKey == "CompanyEmailDomain")?.SettingValue
                                  ?? "company.com";

                var usernameOnly = req.CreatedByUsername.Contains('\\')
                    ? req.CreatedByUsername.Split('\\')[1]
                    : req.CreatedByUsername;

                var requesterEmail = $"{usernameOnly}@{emailDomain}";
                var status = approved ? "approved" : "rejected";

                var subject = $"Outsource Request OSR-{req.RequestId:000000} — {stage} {status}";
                var body = $@"
Hi,

Your outsource request has been {status} at the {stage} stage.

Request:    OSR-{req.RequestId:000000}
Part:       {req.PartNumber} — {req.SapDescription}
Stage:      {stage}
Decision:   {status.ToUpper()}
{(string.IsNullOrEmpty(comments) ? "" : $"Comments:   {comments}")}

Please log in to the Outsource Portal to view the full status.

This is an automated message from the Outsource Request Portal.
";
                var msg = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };
                msg.To.Add(requesterEmail);

                client.Send(msg);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email for OSR-{Id}", req.RequestId);
            }
        }

        public void SendReminder(OutsourceRequest req, ApproverRole approver)
        {
            try
            {
                var client = BuildClient(out var from, out var fromName);
                if (client == null || string.IsNullOrEmpty(approver.Email)) return;

                var subject = $"Reminder — Outsource Request OSR-{req.RequestId:000000} awaiting your approval";
                var body = $@"
Hi {approver.FullName},

This is a reminder that the following outsource request is still awaiting your approval.

Request:    OSR-{req.RequestId:000000}
Part:       {req.PartNumber} — {req.SapDescription}
Submitted:  {req.CreatedAt:dd MMM yyyy HH:mm}
Waiting:    {(DateTime.Now - req.CreatedAt).Days}d {(DateTime.Now - req.CreatedAt).Hours}h

Please log in to the Outsource Portal to take action.

This is an automated message from the Outsource Request Portal.
";
                var msg = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };
                msg.To.Add(new MailAddress(approver.Email, approver.FullName));

                client.Send(msg);

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

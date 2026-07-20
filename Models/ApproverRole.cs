using System.ComponentModel.DataAnnotations;

namespace OutsourceRequestApp.Models
{
    // Stores who is assigned to each approval role
    public class ApproverRole
    {
        [Key]
        public int Id { get; set; }

        // "WP", "PROD", "BUYER", "SOURCING", "MD"
        [Required]
        public string RoleKey { get; set; } = string.Empty;

        public string RoleDisplayName { get; set; } = string.Empty;

        // Windows username e.g. DOMAIN\sjones
        public string Username { get; set; } = string.Empty;

        // Display name e.g. Sarah Jones
        public string FullName { get; set; } = string.Empty;

        // Email address for notifications
        public string Email { get; set; } = string.Empty;
    }

    // Key-value store for app settings (SMTP, reminder hours, admin list)
    public class AppSetting
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string SettingKey { get; set; } = string.Empty;

        public string SettingValue { get; set; } = string.Empty;
    }
}

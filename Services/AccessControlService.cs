using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Models;

namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// Single source of truth for "who is signed in and what are they allowed
    /// to do" — mirrors the sibling TL Portal's AdminService/PortalAccessService
    /// split, which keeps this kind of check in one place instead of duplicated
    /// inline (this app previously repeated the admin check in three
    /// controllers/components, and the roleKey-to-status mapping in four).
    ///
    /// Matching is e-mail first (the primary key admins configure in the Admin
    /// panel), with a name-based fallback via <see cref="PortalNameMatcher"/>
    /// against the AD display name resolved for the current Windows account —
    /// so a stale/missing AD `mail` attribute doesn't lock out an approver whose
    /// full name is already configured correctly.
    /// </summary>
    public class AccessControlService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly ActiveDirectoryLookup _ad;

        public AccessControlService(AppDbContext db, IHttpContextAccessor http, ActiveDirectoryLookup ad)
        {
            _db   = db;
            _http = http;
            _ad   = ad;
        }

        /// <summary>The current request's identity name — an e-mail address once resolved by
        /// <see cref="WindowsIdentityEmailMiddleware"/>, or the raw Windows account if resolution failed.
        /// This is an internal matching/notification key, not something to show a person — e-mail
        /// only exists in this app to send them mail. Use <see cref="CurrentDisplayNameOrRaw"/>
        /// for anything a user sees on screen.</summary>
        public string CurrentUserName =>
            _http.HttpContext?.User?.Identity?.Name ?? "";

        /// <summary>AD display name for the current user, when it could be resolved. Used as a
        /// fallback match against an approver/admin's configured full name.</summary>
        public string? CurrentDisplayName =>
            _http.HttpContext?.User?.FindFirst(WindowsIdentityEmailMiddleware.DisplayNameClaimType)?.Value;

        /// <summary>The name to show the signed-in user for themselves (e.g. a "Created by" field,
        /// the topbar chip) — prefers the DisplayName claim the auth middleware already resolved
        /// (no extra AD round-trip), falls back to an on-demand AD lookup, and finally to the raw
        /// identity string if AD can't resolve it either.</summary>
        public string CurrentDisplayNameOrRaw =>
            !string.IsNullOrEmpty(CurrentDisplayName) ? CurrentDisplayName! : _ad.DisplayNameOrRaw(CurrentUserName);

        public async Task<bool> IsAdminAsync()
        {
            var setting = await _db.AppSettings.FirstOrDefaultAsync(s => s.SettingKey == "AdminUsers");
            var admins  = (setting?.SettingValue ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim());

            var current     = CurrentUserName;
            var displayName = CurrentDisplayName;

            return admins.Any(a =>
                a.Equals(current, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(displayName) && PortalNameMatcher.Matches(a, displayName)));
        }

        /// <summary>Returns the approver role assigned to the current user, or null if they aren't one.</summary>
        public async Task<ApproverRole?> GetMyApproverRoleAsync()
        {
            var roles       = await _db.ApproverRoles.ToListAsync();
            var current     = CurrentUserName;
            var displayName = CurrentDisplayName;

            return roles.FirstOrDefault(r => Matches(r, current, displayName));
        }

        public async Task<bool> IsAssignedApproverAsync(string roleKey)
        {
            var role = await _db.ApproverRoles.FirstOrDefaultAsync(r => r.RoleKey == roleKey);
            return role != null && Matches(role, CurrentUserName, CurrentDisplayName);
        }

        private static bool Matches(ApproverRole role, string currentUser, string? displayName)
        {
            if (!string.IsNullOrEmpty(role.Email) &&
                role.Email.Equals(currentUser, StringComparison.OrdinalIgnoreCase))
                return true;

            return !string.IsNullOrEmpty(displayName) &&
                   !string.IsNullOrEmpty(role.FullName) &&
                   PortalNameMatcher.Matches(role.FullName, displayName);
        }
    }
}

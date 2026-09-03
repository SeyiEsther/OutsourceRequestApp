using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Microsoft.Extensions.Caching.Memory;

namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// Centralised Active Directory lookups (display name, e-mail), cached the
    /// same way as the sibling TL Portal's UserService: an IMemoryCache entry
    /// per account with a bounded TTL, so a change in AD (or a lookup that
    /// failed transiently) converges within the hour rather than requiring an
    /// app restart — unlike a permanently-cached static dictionary.
    ///
    /// Registered as a singleton: it holds no per-request state, just a
    /// process-wide cache of username -> AD attribute.
    /// </summary>
    public class ActiveDirectoryLookup
    {
        private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

        private readonly IMemoryCache _cache;
        private readonly ILogger<ActiveDirectoryLookup> _logger;

        public ActiveDirectoryLookup(IMemoryCache cache, ILogger<ActiveDirectoryLookup> logger)
        {
            _cache  = cache;
            _logger = logger;
        }

        /// <summary>Strips a DOMAIN\ prefix or an @domain suffix down to the bare account name.</summary>
        public static string NormalizeAccountName(string identityName)
        {
            if (identityName.Contains('\\', StringComparison.Ordinal))
                return identityName.Split('\\').Last();
            if (identityName.Contains('@', StringComparison.Ordinal))
                return identityName.Split('@').First();
            return identityName;
        }

        public string? ResolveDisplayName(string identityName)
        {
            var username = NormalizeAccountName(identityName);
            if (string.IsNullOrWhiteSpace(username)) return null;

            return _cache.GetOrCreate($"ad-display-name::{username}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return OperatingSystem.IsWindows() ? ResolveDisplayNameWindows(username) : null;
            });
        }

        /// <summary>
        /// Resolves a display name for whatever is actually stored in the
        /// database for "who did this" (CreatedByUsername, JFSignedBy, etc).
        /// Under Windows Authentication that's normally an e-mail address (see
        /// WindowsIdentityEmailMiddleware) — NormalizeAccountName's "strip to
        /// before the @" trick does NOT recover a usable SamAccountName from an
        /// e-mail (mail local-parts routinely differ from SAM, e.g. j.smith vs
        /// jsmith), so an e-mail is looked up in AD by mail/UPN attribute
        /// instead of by username.
        /// </summary>
        public string? ResolveDisplayNameForStoredIdentity(string identityName)
        {
            if (string.IsNullOrWhiteSpace(identityName)) return null;

            if (identityName.Contains('@', StringComparison.Ordinal))
            {
                return _cache.GetOrCreate($"ad-display-by-email::{identityName}", entry =>
                {
                    entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                    return OperatingSystem.IsWindows() ? ResolveDisplayNameByEmailWindows(identityName) : null;
                });
            }

            return ResolveDisplayName(identityName);
        }

        /// <summary>Same as <see cref="ResolveDisplayNameForStoredIdentity"/> but
        /// falls back to the raw stored value (rather than null) when AD can't
        /// resolve it — for direct use in views so a lookup miss never blanks
        /// out a name that was previously visible as an e-mail.</summary>
        public string DisplayNameOrRaw(string? identityName)
        {
            if (string.IsNullOrWhiteSpace(identityName)) return "Unknown";
            return ResolveDisplayNameForStoredIdentity(identityName) is { Length: > 0 } name ? name : identityName;
        }

        public string? ResolveEmail(string identityName)
        {
            // Already e-mail shaped (dev impersonation, or a legacy stored row) — nothing to resolve.
            if (identityName.Contains('@', StringComparison.Ordinal)) return identityName;

            var username = NormalizeAccountName(identityName);
            if (string.IsNullOrWhiteSpace(username)) return null;

            return _cache.GetOrCreate($"ad-email::{username}", entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheDuration;
                return OperatingSystem.IsWindows() ? ResolveEmailWindows(username) : null;
            });
        }

        [SupportedOSPlatform("windows")]
        private string? ResolveDisplayNameWindows(string username)
        {
            try
            {
                using var ctx  = new PrincipalContext(ContextType.Domain);
                using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, username);
                return user?.DisplayName ?? user?.GivenName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get AD display name for {User}.", username);
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private string? ResolveDisplayNameByEmailWindows(string email)
        {
            try
            {
                using var ctx = new PrincipalContext(ContextType.Domain);

                // Try the fast, indexed exact-match lookup first (works when the
                // org's UPN is the e-mail address, which is the common case).
                using var byUpn = UserPrincipal.FindByIdentity(ctx, IdentityType.UserPrincipalName, email);
                if (!string.IsNullOrEmpty(byUpn?.DisplayName)) return byUpn.DisplayName;

                // Fall back to a search on the mail attribute for orgs where UPN != mail.
                using var searcher = new PrincipalSearcher(new UserPrincipal(ctx) { EmailAddress = email });
                using var byMail = searcher.FindOne() as UserPrincipal;
                return byMail?.DisplayName;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get AD display name for e-mail {Email}.", email);
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private string? ResolveEmailWindows(string username)
        {
            try
            {
                using var ctx  = new PrincipalContext(ContextType.Domain);
                using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, username);
                return user?.EmailAddress;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not get AD e-mail for {User}.", username);
                return null;
            }
        }
    }
}

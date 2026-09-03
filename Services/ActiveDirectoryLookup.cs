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

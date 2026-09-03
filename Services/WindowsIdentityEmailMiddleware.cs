using System.Collections.Concurrent;
using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using System.Security.Claims;

namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// The app identifies requesters, approvers and admins by their e-mail
    /// address, but IIS Windows Authentication only gives us a
    /// <c>DOMAIN\username</c> identity. This middleware looks the account up in
    /// Active Directory once per user, caches the result, and republishes the
    /// principal so that <c>User.Identity.Name</c> is the e-mail address
    /// everywhere in the app.
    ///
    /// It is a no-op for identities that are already e-mail based (e.g. the
    /// dev-impersonation principal used when running locally without AD) and for
    /// anonymous requests.
    /// </summary>
    public class WindowsIdentityEmailMiddleware
    {
        // DOMAIN\user -> email. Cached for the lifetime of the process so we only
        // hit AD once per user rather than on every request.
        private static readonly ConcurrentDictionary<string, string?> _emailCache =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly RequestDelegate _next;
        private readonly ILogger<WindowsIdentityEmailMiddleware> _logger;

        public WindowsIdentityEmailMiddleware(RequestDelegate next,
                                              ILogger<WindowsIdentityEmailMiddleware> logger)
        {
            _next   = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.User.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
            {
                var name = identity.Name ?? "";

                // Only act on a raw Windows domain identity. Anything already
                // e-mail based (dev impersonation, or an earlier pass) is left alone.
                if (!name.Contains('@') && name.Contains('\\'))
                {
                    var email = _emailCache.GetOrAdd(name, ResolveEmailFromAd);
                    if (string.IsNullOrEmpty(email))
                    {
                        _logger.LogWarning(
                            "Could not resolve an e-mail address for Windows account {Account}. " +
                            "This user will not match any approver or admin (both keyed by e-mail).",
                            name);
                    }
                    else
                    {
                        var emailIdentity = new ClaimsIdentity(identity.AuthenticationType);
                        emailIdentity.AddClaim(new Claim(ClaimTypes.Name, email));
                        emailIdentity.AddClaim(new Claim(ClaimTypes.Email, email));

                        var principal = new ClaimsPrincipal();
                        principal.AddIdentity(emailIdentity); // primary — drives User.Identity.Name
                        principal.AddIdentity(identity);      // keep the original Windows identity
                        context.User = principal;
                    }
                }
            }

            await _next(context);
        }

        private string? ResolveEmailFromAd(string domainUser)
        {
            if (!OperatingSystem.IsWindows()) return null;
            return ResolveEmailFromAdWindows(domainUser);
        }

        [SupportedOSPlatform("windows")]
        private string? ResolveEmailFromAdWindows(string domainUser)
        {
            try
            {
                var parts  = domainUser.Split('\\', 2);
                var domain = parts.Length == 2 ? parts[0] : null;
                var sam    = parts.Length == 2 ? parts[1] : parts[0];

                using var ctx = domain is null
                    ? new PrincipalContext(ContextType.Domain)
                    : new PrincipalContext(ContextType.Domain, domain);

                using var user = UserPrincipal.FindByIdentity(ctx, IdentityType.SamAccountName, sam);
                return user?.EmailAddress;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Active Directory lookup failed for {Account}.", domainUser);
                return null;
            }
        }
    }
}

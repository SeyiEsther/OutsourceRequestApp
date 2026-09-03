using System.Security.Claims;

namespace OutsourceRequestApp.Services
{
    /// <summary>
    /// The app identifies requesters, approvers and admins primarily by
    /// e-mail, but IIS Windows Authentication only gives us a
    /// <c>DOMAIN\username</c> identity. This middleware resolves the AD e-mail
    /// (and display name, published as a fallback-matching claim — see
    /// <see cref="AccessControlService"/>) for the Windows account and
    /// republishes the principal so that <c>User.Identity.Name</c> is the
    /// e-mail address everywhere in the app.
    ///
    /// It is a no-op for identities that are already e-mail based (e.g. the
    /// dev-impersonation principal used when running locally without AD) and
    /// for anonymous requests.
    /// </summary>
    public class WindowsIdentityEmailMiddleware
    {
        public const string DisplayNameClaimType = "OutsourcePortal:DisplayName";

        private readonly RequestDelegate _next;
        private readonly ActiveDirectoryLookup _ad;
        private readonly ILogger<WindowsIdentityEmailMiddleware> _logger;

        public WindowsIdentityEmailMiddleware(RequestDelegate next, ActiveDirectoryLookup ad,
                                              ILogger<WindowsIdentityEmailMiddleware> logger)
        {
            _next   = next;
            _ad     = ad;
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
                    var email       = _ad.ResolveEmail(name);
                    var displayName = _ad.ResolveDisplayName(name);

                    if (string.IsNullOrEmpty(email))
                    {
                        _logger.LogWarning(
                            "Could not resolve an e-mail address for Windows account {Account}. " +
                            "Falling back to display-name matching for approver/admin access where configured.",
                            name);
                    }

                    // Republish the principal. Name becomes the e-mail wherever it
                    // resolved; otherwise keep the raw Windows name so the account
                    // is still visible in the UI and DisplayName-based matching
                    // (AccessControlService) still has a chance to work.
                    var newIdentity = new ClaimsIdentity(identity.AuthenticationType);
                    newIdentity.AddClaim(new Claim(ClaimTypes.Name, email ?? name));
                    if (!string.IsNullOrEmpty(email))
                        newIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
                    if (!string.IsNullOrEmpty(displayName))
                        newIdentity.AddClaim(new Claim(DisplayNameClaimType, displayName));

                    var principal = new ClaimsPrincipal();
                    principal.AddIdentity(newIdentity); // primary — drives User.Identity.Name
                    principal.AddIdentity(identity);     // keep the original Windows identity
                    context.User = principal;
                }
            }

            await _next(context);
        }
    }
}

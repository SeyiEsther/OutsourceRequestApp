using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// Services
// ----------------------------------------------------

builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();

// Persist Data Protection keys outside the app folder so an app-pool recycle
// or a redeploy (which overwrites the binaries and restarts the app) can't
// regenerate the keys. Without this, every restart invalidates in-flight
// antiforgery tokens, so a form open before the restart fails to POST with a
// 400 — e.g. an approver mid-review whose tab was open since before a deploy.
// (Ported from the sibling TL Portal, which hit exactly this in production.)
// Try several candidate folders and pick the first one we can actually WRITE to
// (create + write + delete a probe file) — a folder that exists but isn't
// writable by the app-pool account would otherwise fail silently.
static string? ResolveWritableKeyDir(params string?[] candidates)
{
    foreach (var c in candidates)
    {
        if (string.IsNullOrWhiteSpace(c)) continue;
        try
        {
            Directory.CreateDirectory(c);
            var probe = Path.Combine(c, ".writetest");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return c;
        }
        catch { /* try the next candidate */ }
    }
    return null;
}

var keyDir = ResolveWritableKeyDir(
    builder.Configuration["DataProtection:KeyPath"],
    Path.Combine(builder.Environment.ContentRootPath, "..", "OutsourcePortal-dataprotection-keys"),
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "OutsourcePortal", "dataprotection-keys"));

var keysPersisted = keyDir != null;
if (keysPersisted)
{
    builder.Services.AddDataProtection()
        .SetApplicationName("OutsourcePortal")
        .PersistKeysToFileSystem(new DirectoryInfo(keyDir!));
}

builder.Services.AddScoped<EmailService>();

// AD lookups (display name, e-mail) are process-wide and cached — no per-request
// state — so this is registered as a singleton, unlike the scoped services below.
builder.Services.AddSingleton<ActiveDirectoryLookup>();

// Single source of truth for admin/approver matching (see AccessControlService).
builder.Services.AddScoped<AccessControlService>();

// Main application database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("OutsourceConnection")));

// SAP / Data Warehouse database (read-only part lookup)
builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DataWarehouseConnection")));

// Background service: sends reminder emails to overdue approvers
builder.Services.AddHostedService<ReminderService>();

// ----------------------------------------------------
// Build
// ----------------------------------------------------

var app = builder.Build();

// Make the Data Protection state obvious in the logs — this is what decides
// whether a deploy/recycle silently logs everyone out of open forms.
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
if (keysPersisted)
    startupLogger.LogInformation(
        "Data Protection keys persisted to {Path} — antiforgery tokens survive restarts and deploys.", keyDir);
else
    startupLogger.LogError(
        "Data Protection keys are NOT being persisted (no writable folder found). " +
        "Every app restart/deploy will invalidate open forms — users get HTTP 400 on submit. " +
        "Set DataProtection:KeyPath in appsettings to a folder the app-pool account can write to.");

// ----------------------------------------------------
// Middleware pipeline
// ----------------------------------------------------

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Windows Authentication is enforced by IIS at the site level (enable Windows
// Authentication and disable Anonymous Authentication on the IIS app), which
// populates HttpContext.User with the caller's DOMAIN\user identity when hosted
// in-process. The app matches requesters/approvers/admins primarily by e-mail
// (with a display-name fallback via AccessControlService), so this middleware
// resolves the AD e-mail/display name for the Windows account and republishes
// the principal — leaving User.Identity.Name as the e-mail address throughout
// the app. No-op in Development (the impersonation principal below is already
// e-mail based).
app.UseMiddleware<WindowsIdentityEmailMiddleware>();

// Dev-only: no real authentication provider runs locally, so User.Identity.Name
// would otherwise always be empty. Set DevImpersonateUser in
// appsettings.Development.json to test as a specific approver/admin email.
if (app.Environment.IsDevelopment())
{
    var devUser = app.Configuration["DevImpersonateUser"];
    if (!string.IsNullOrWhiteSpace(devUser))
    {
        app.Use(async (context, next) =>
        {
            var identity = new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, devUser) },
                "DevImpersonation");
            context.User = new System.Security.Claims.ClaimsPrincipal(identity);
            await next();
        });
    }
}

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// Services
// ----------------------------------------------------

builder.Services.AddControllersWithViews();

builder.Services.AddHttpContextAccessor();

builder.Services.AddScoped<EmailService>();

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
// in-process. The app matches requesters/approvers/admins
// by e-mail, so this middleware resolves the AD e-mail for the Windows account
// and republishes the principal — leaving User.Identity.Name as the e-mail address
// throughout the app. No-op in Development (the impersonation principal below is
// already e-mail based).
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

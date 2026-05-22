using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;
using OutsourceRequestApp.Services;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// Services
// ----------------------------------------------------

builder.Services.AddControllersWithViews();

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

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

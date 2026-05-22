using Microsoft.EntityFrameworkCore;
using OutsourceRequestApp.Data;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// Add services to the container
// ----------------------------------------------------

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<OutsourceRequestApp.Services.EmailService>();

// Main application database (Outsource Requests)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("OutsourceConnection")
    ));

// SAP / Data Warehouse database (Part lookup)
builder.Services.AddDbContext<WarehouseDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DataWarehouseConnection")
    ));



// ----------------------------------------------------
// Build the app
// ----------------------------------------------------

var app = builder.Build();


// ----------------------------------------------------
// Configure the HTTP request pipeline
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


// Default routing
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
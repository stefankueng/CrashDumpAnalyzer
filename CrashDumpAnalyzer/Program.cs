using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Services;
using CrashDumpAnalyzer.Utilities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using System.Globalization;


DateTime test;
var today = DateTime.Now.ToString("ddd MMM dd HH:mm:ss.fff yyyy (UTC z:00)",new CultureInfo("en-us"));
try
{
    test = DateTime.ParseExact("Mon Oct 31 02:58:29.000 2022 (UTC +1:00)", "ddd MMM d HH:mm:ss.fff yyyy (UTC z:00)", new CultureInfo("en-us"));
    test = DateTime.ParseExact("Thu Feb 2 05:14:12.000 2023 (UTC +1:00)", "ddd MMM d HH:mm:ss.fff yyyy (UTC z:00)", new CultureInfo("en-us"));
    test = DateTime.ParseExact("Sat Sep 3 00:30:01.000 2022 (UTC +1:00)", "ddd MMM d HH:mm:ss.fff yyyy (UTC z:00)", new CultureInfo("en-us"));

}
catch (Exception ex)
{
    var ee = ex.ToString();
}
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options =>
{
    options.FormFieldName = "AntiforgeryFieldname";
    options.HeaderName = "X-CSRF-TOKEN-HEADERNAME";
    options.SuppressXFrameOptionsHeader = false;
});
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddHostedService<QueuedHostedService>();

var app = builder.Build();

using (var serviceScope = app.Services.GetService<IServiceScopeFactory>()?.CreateScope())
{
    var context = serviceScope?.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context?.Database.Migrate();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

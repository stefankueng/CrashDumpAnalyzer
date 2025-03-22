using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.IssueTrackers;
using CrashDumpAnalyzer.IssueTrackers.Interfaces;
using CrashDumpAnalyzer.Services;
using CrashDumpAnalyzer.Utilities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.OpenApi;

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
builder.Services.AddWindowsService();
builder.Services.AddHostedService<QueuedHostedService>();
builder.Logging.AddLog4Net();
builder.Services.AddOpenApi();

builder.Services.Configure<KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = long.MaxValue; // if don't set default value is: 30 MB
});
builder.Services.Configure<FormOptions>(options =>
{
    options.ValueLengthLimit = int.MaxValue;
    options.MultipartBodyLengthLimit = long.MaxValue; // if don't set default value is: 128 MB
    options.MultipartHeadersLengthLimit = int.MaxValue;
});
builder.Services.AddHttpClient();
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

var issueTracker = IssueTrackerFactory.GetIssueTracker(builder.Configuration);
if (issueTracker != null)
    builder.Services.AddSingleton(issueTracker);

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
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "v1");
    });
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseResponseCompression();
builder.Configuration.GetSection("StaticFolders").GetChildren().ToList().ForEach(folder =>
{
    if (folder.Value != null)
    {
        app.UseStaticFiles(new StaticFileOptions()
        {
            ServeUnknownFileTypes = true,
            FileProvider = new PhysicalFileProvider(
            Path.Combine(Directory.GetCurrentDirectory(), folder.Value)
        ),
            RequestPath = new PathString($"/{folder.Key}")
        });
    }
});
BuildTypes.Initialize(app.Logger, builder.Configuration);

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

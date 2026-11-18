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
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration.Json;

var builder = WebApplication.CreateBuilder(args);
// Settings include startup-only services and middleware; apply base-file changes together on restart.
for (var i = 0; i < builder.Configuration.Sources.Count; i++)
{
    if (builder.Configuration.Sources[i] is JsonConfigurationSource source &&
        string.Equals(source.Path, "appsettings.json", StringComparison.OrdinalIgnoreCase))
    {
        source.ReloadOnChange = false;
        builder.Configuration.Sources[i] = source;
    }
}
builder.Services.AddWindowsService();
builder.Services.AddSingleton<SetupService>();
builder.Services.AddSingleton<SettingsService>();

// Bootstrap without opening a database or starting analysis workers.
if (SetupService.IsRequired(builder.Configuration))
{
    builder.Services.AddControllersWithViews();
    var setupApp = builder.Build();
    setupApp.Use(async (context, next) =>
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
        if (!SetupService.IsLocalRequest(context))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!context.Request.Path.StartsWithSegments("/Setup")
            && !context.Request.Path.StartsWithSegments("/css")
            && !context.Request.Path.StartsWithSegments("/lib"))
        {
            context.Response.Redirect(context.Request.PathBase.Add("/Setup"));
            return;
        }
        await next(context);
    });
    setupApp.UseStaticFiles();
    setupApp.MapControllers();
    setupApp.Lifetime.ApplicationStarted.Register(() => SetupBrowser.Open(setupApp.Urls, setupApp.Logger));
    setupApp.Run();
    return;
}

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=./webapplication.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddControllersWithViews(options =>
{
    if (builder.Configuration.GetValue<bool>("UserAuthEnabled"))
        options.Filters.Add(new Microsoft.AspNetCore.Mvc.Authorization.AuthorizeFilter());
});

IConfigurationSection googleAuthNSection =
builder.Configuration.GetSection("Authentication:Google");
if (googleAuthNSection["ClientId"] != null)
{
    builder.Services.AddAuthentication()
        .AddGoogleOpenIdConnect(options =>
        {
            options.ClientId = googleAuthNSection["ClientId"] ?? "";
            options.ClientSecret = googleAuthNSection["ClientSecret"] ?? "";
        }).AddCookie(options =>
        {
            options.Events.OnSigningIn = ctx =>
            {
                ctx.Properties.IsPersistent = true;
                return Task.CompletedTask;
            };
        });
}
IConfigurationSection FBAuthNSection =
builder.Configuration.GetSection("Authentication:Facebook");
if (FBAuthNSection["ClientId"] != null)
{
    builder.Services.AddAuthentication()
        .AddFacebook(options =>
        {
            options.ClientId = FBAuthNSection["ClientId"] ?? "";
            options.ClientSecret = FBAuthNSection["ClientSecret"] ?? "";
        }).AddCookie(options =>
        {
            options.Events.OnSigningIn = ctx =>
            {
                ctx.Properties.IsPersistent = true;
                return Task.CompletedTask;
            };
        });
}
IConfiguration MSAuthNSection = builder.Configuration.GetSection("Authentication:Microsoft");
if (MSAuthNSection["ClientId"] != null)
{
    builder.Services.AddAuthentication()
        .AddMicrosoftAccount(options =>
        {
            options.ClientId = MSAuthNSection["ClientId"] ?? "";
            options.ClientSecret = MSAuthNSection["ClientSecret"] ?? "";
        }).AddCookie(options =>
        {
            options.Events.OnSigningIn = ctx =>
            {
                ctx.Properties.IsPersistent = true;
                return Task.CompletedTask;
            };
        });
}
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(360);
    options.ReturnUrlParameter = CookieAuthenticationDefaults.ReturnUrlParameter;
    options.SlidingExpiration = true;
});
builder.Services.Configure<DataProtectionTokenProviderOptions>(o =>
       o.TokenLifespan = TimeSpan.FromDays(360));

builder.Services.AddTransient<IEmailSender, EmailSender>();

builder.Services.AddAntiforgery(options =>
{
    options.FormFieldName = "AntiforgeryFieldname";
    options.HeaderName = "X-CSRF-TOKEN-HEADERNAME";
    options.SuppressXFrameOptionsHeader = false;
});
builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
builder.Services.AddSingleton<DumpAnalysisQueue>();
builder.Services.AddScoped<DumpFileService>();
builder.Services.AddScoped<CallstackService>();
builder.Services.AddScoped<DumpCleanupService>();
builder.Services.AddScoped<AnalysisResultService>();
builder.Services.AddScoped<CallstackQueryService>();
builder.Services.AddScoped<HomePageService>();
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
builder.Services.AddHealthChecks()
    .AddDbContextCheck<ApplicationDbContext>();

var app = builder.Build();
app.MapHealthChecks("/health");

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
MinVersions.Initialize(app.Logger, builder.Configuration);
Constants.TicketBaseUrl = builder.Configuration.GetValue<string>("TicketBaseUrl") ?? string.Empty;

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();

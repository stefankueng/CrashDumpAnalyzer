using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CrashDumpAnalyzer.Services
{
    public sealed class SetupService(IConfiguration configuration, IWebHostEnvironment environment)
    {
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private volatile bool _completed;
        public bool Required { get; } = IsRequired(configuration);
        public bool Completed => _completed;

        public static bool IsRequired(IConfiguration configuration)
        {
            return configuration.GetValue<bool?>("Setup:Completed") is bool completed
                ? !completed
                : string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection"));
        }

        public static bool IsLocalRequest(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            if (address is null || !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address))
                return false;

            var host = context.Request.Host.Host.Trim('[', ']');
            var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host, out var hostAddress) && IPAddress.IsLoopback(hostAddress));
            // Do not expose bootstrap through a local reverse proxy or a rebound DNS name.
            return localHost && !context.Request.Headers.Keys.Any(header =>
                header.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("X-Forwarded-", StringComparison.OrdinalIgnoreCase)
                || header.StartsWith("X-Original-", StringComparison.OrdinalIgnoreCase));
        }

        public SetupModel GetDefaults() => new()
        {
            DefaultConnection = configuration.GetConnectionString("DefaultConnection") ?? "Data Source=C:\\CrashDumpAnalyzer\\WebApplication.db",
            UserAuthEnabled = configuration.GetValue<bool?>("UserAuthEnabled") ?? true,
            SmtpHost = configuration["Smtp:Host"],
            SmtpPort = int.TryParse(configuration["Smtp:Port"], out var port) && port > 0 ? port : 587,
            SmtpUsername = configuration["Smtp:Username"],
            SmtpEnableSsl = bool.TryParse(configuration["Smtp:EnableSsl"], out var ssl) ? ssl : true,
            SmtpFrom = configuration["Smtp:From"]
        };

        public async Task SaveAsync(SetupModel model)
        {
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, new ValidationContext(model), errors, true))
                throw new ValidationException(string.Join(" ", errors.Select(e => e.ErrorMessage)));

            await _saveLock.WaitAsync();
            try
            {
                if (!Required || Completed)
                    throw new ValidationException("Setup has already completed. Restart CrashDumpAnalyzer to continue.");

                var connection = GetConnectionString(model.DefaultConnection);
                var path = Path.Combine(environment.ContentRootPath, "appsettings.json");
                var nodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
                var settings = File.Exists(path)
                    ? JsonNode.Parse(await File.ReadAllTextAsync(path), nodeOptions, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    }) as JsonObject ?? throw new JsonException("appsettings.json must contain a JSON object.")
                    : new JsonObject(nodeOptions);
                GetSection(settings, "ConnectionStrings")["DefaultConnection"] = connection;
                settings["UserAuthEnabled"] = model.UserAuthEnabled;
                var smtp = GetSection(settings, "Smtp");
                smtp["Host"] = model.SmtpHost;
                smtp["Port"] = model.SmtpPort;
                smtp["Username"] = model.SmtpUsername;
                smtp["Password"] = model.SmtpPassword ?? configuration["Smtp:Password"] ?? "";
                smtp["EnableSsl"] = model.SmtpEnableSsl;
                smtp["From"] = model.SmtpFrom;
                GetSection(settings, "Setup")["Completed"] = true;

                var services = new ServiceCollection();
                services.AddLogging();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddIdentityCore<IdentityUser>().AddRoles<IdentityRole>()
                    .AddEntityFrameworkStores<ApplicationDbContext>();
                await using var provider = services.BuildServiceProvider();
                await using var scope = provider.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await db.Database.MigrateAsync();
                await using var transaction = await db.Database.BeginTransactionAsync();
                if (model.UserAuthEnabled)
                {
                    if (await db.Users.AnyAsync())
                        throw new ValidationException("The selected database already contains users. Configure an existing installation manually instead of creating another first administrator.");
                    var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    if (!await roles.RoleExistsAsync("Admin"))
                        Check(await roles.CreateAsync(new IdentityRole("Admin")));
                    var admin = new IdentityUser { UserName = model.AdminEmail, Email = model.AdminEmail, EmailConfirmed = true };
                    Check(await users.CreateAsync(admin, model.AdminPassword!));
                    Check(await users.AddToRoleAsync(admin, "Admin"));
                }

                var temporaryPath = Path.Combine(environment.ContentRootPath, $".setup-{Guid.NewGuid():N}.tmp");
                var backupPath = temporaryPath + ".bak";
                var existed = File.Exists(path);
                try
                {
                    await File.WriteAllTextAsync(temporaryPath, settings.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    if (existed)
                        File.Replace(temporaryPath, path, backupPath);
                    else
                        File.Move(temporaryPath, path);
                    try
                    {
                        await transaction.CommitAsync();
                    }
                    catch
                    {
                        // A failed database commit must not leave a completed setup file behind.
                        if (existed)
                            File.Move(backupPath, path, true);
                        else
                            File.Delete(path);
                        throw;
                    }
                    _completed = true;
                }
                finally
                {
                    File.Delete(temporaryPath);
                    if (_completed)
                        File.Delete(backupPath);
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private string GetConnectionString(string value)
        {
            try
            {
                var connection = new SqliteConnectionStringBuilder(value);
                if (string.IsNullOrWhiteSpace(connection.DataSource) || connection.DataSource == ":memory:"
                    || connection.Mode is SqliteOpenMode.Memory or SqliteOpenMode.ReadOnly)
                    throw new ValidationException("Use a writable, file-backed SQLite database.");
                connection.DataSource = Path.GetFullPath(connection.DataSource, environment.ContentRootPath);
                return connection.ToString();
            }
            catch (ArgumentException)
            {
                throw new ValidationException("Enter a valid SQLite connection string and database path.");
            }
        }

        private static JsonObject GetSection(JsonObject root, string name)
        {
            if (root[name] is null)
                root[name] = new JsonObject();
            return root[name] as JsonObject ?? throw new JsonException($"{name} must be a JSON object.");
        }

        private static void Check(IdentityResult result)
        {
            if (!result.Succeeded)
                throw new ValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}

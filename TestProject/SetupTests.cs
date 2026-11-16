using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json.Nodes;
using CrashDumpAnalyzer.Controllers;
using CrashDumpAnalyzer.Data;
using CrashDumpAnalyzer.Models;
using CrashDumpAnalyzer.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class SetupTests
    {
        [TestMethod]
        [DataRow("http://localhost:5000", "http://localhost:5000/Setup")]
        [DataRow("https://localhost:7443", "https://localhost:7443/Setup")]
        [DataRow("http://127.0.0.1:53127", "http://127.0.0.1:53127/Setup")]
        [DataRow("http://[::1]:5000", "http://[::1]:5000/Setup")]
        [DataRow("http://0.0.0.0:8080", "http://127.0.0.1:8080/Setup")]
        [DataRow("http://[::]:8080", "http://[::1]:8080/Setup")]
        [DataRow("http://*:8080", "http://localhost:8080/Setup")]
        [DataRow("http://+:8080", "http://localhost:8080/Setup")]
        [DataRow("http://server.example:8080", "http://localhost:8080/Setup")]
        [DataRow("http://localhost:5000/old?query=yes#fragment", "http://localhost:5000/Setup")]
        public void SetupBrowser_UsesLocalListenerAddressAndActualPort(string address, string expected)
        {
            Assert.AreEqual(new Uri(expected), SetupBrowser.GetSetupUrl([address]));
        }

        [TestMethod]
        [DataRow("http://192.0.2.1:5000")]
        [DataRow("http://[2001:db8::1]:5000")]
        [DataRow("ftp://localhost:5000")]
        [DataRow("http://localhost:0")]
        [DataRow("http://user:password@localhost:5000")]
        [DataRow("not an address")]
        public void SetupBrowser_DoesNotOpenUnusableOrNonLocalAddresses(string address)
        {
            Assert.IsNull(SetupBrowser.GetSetupUrl([address]));
        }

        [TestMethod]
        public void SetupBrowser_PrefersHttpsAndSkipsUnusableListeners()
        {
            Assert.AreEqual(new Uri("https://localhost:7443/Setup"), SetupBrowser.GetSetupUrl(
                ["http://localhost:5000", "https://192.0.2.1:443", "https://localhost:7443"]));
            Assert.AreEqual(new Uri("http://127.0.0.1:5000/Setup"), SetupBrowser.GetSetupUrl(
                ["https://192.0.2.1:443", "http://127.0.0.1:5000"]));
            Assert.IsNull(SetupBrowser.GetSetupUrl([]));
        }

        [TestMethod]
        [DataRow(null, null, true)]
        [DataRow("Data Source=existing.db", null, false)]
        [DataRow("Data Source=existing.db", "false", true)]
        [DataRow("Data Source=existing.db", "true", false)]
        public void Required_PreservesExistingInstallations(string? connection, string? completed, bool expected)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connection,
                ["Setup:Completed"] = completed
            }).Build();
            Assert.AreEqual(expected, SetupService.IsRequired(config));
        }

        [TestMethod]
        [DataRow("127.0.0.1", "localhost", true)]
        [DataRow("::1", "localhost:5000", true)]
        [DataRow("::ffff:127.0.0.1", "127.0.0.1", true)]
        [DataRow("::1", "[::1]:5000", true)]
        [DataRow("192.0.2.1", "localhost", false)]
        [DataRow("127.0.0.1", "attacker.example", false)]
        [DataRow(null, "localhost", false)]
        public void LocalAccess_RequiresLoopbackPeerAndHost(string? address, string host, bool expected)
        {
            var context = Request(address, host);
            Assert.AreEqual(expected, SetupService.IsLocalRequest(context));
        }

        [TestMethod]
        [DataRow("Forwarded")]
        [DataRow("X-Forwarded-For")]
        [DataRow("X-Original-For")]
        [DataRow("X-Forwarded-Host")]
        [DataRow("X-Forwarded-Proto")]
        public void LocalAccess_RejectsProxyHeaders(string header)
        {
            var context = Request("127.0.0.1", "localhost");
            context.Request.Headers[header] = "192.0.2.1";
            Assert.IsFalse(SetupService.IsLocalRequest(context));
        }

        [TestMethod]
        public async Task Save_PreservesSettingsAndCreatesConfirmedAdminWithHashedPassword()
        {
            using var fixture = new Fixture();
            await File.WriteAllTextAsync(fixture.SettingsPath,
                "{ // existing settings\n \"DumpPath\": \"keep-me\", \"ConnectionStrings\": { \"Other\": \"keep-other\" }, \"Smtp\": { \"Extra\": 42 }, }");
            var model = fixture.Model();

            await fixture.Setup.SaveAsync(model);

            var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsPath))!;
            Assert.AreEqual("keep-me", saved["DumpPath"]!.GetValue<string>());
            Assert.AreEqual("keep-other", saved["ConnectionStrings"]!["Other"]!.GetValue<string>());
            Assert.AreEqual(42, saved["Smtp"]!["Extra"]!.GetValue<int>());
            Assert.IsTrue(saved["Setup"]!["Completed"]!.GetValue<bool>());
            Assert.IsTrue(saved["UserAuthEnabled"]!.GetValue<bool>());
            Assert.AreEqual(model.SmtpHost, saved["Smtp"]!["Host"]!.GetValue<string>());
            Assert.AreEqual(587, saved["Smtp"]!["Port"]!.GetValue<int>());
            Assert.AreEqual(model.SmtpUsername, saved["Smtp"]!["Username"]!.GetValue<string>());
            Assert.AreEqual(model.SmtpPassword, saved["Smtp"]!["Password"]!.GetValue<string>());
            Assert.IsTrue(saved["Smtp"]!["EnableSsl"]!.GetValue<bool>());
            Assert.AreEqual(model.SmtpFrom, saved["Smtp"]!["From"]!.GetValue<string>());
            Assert.IsFalse((await File.ReadAllTextAsync(fixture.SettingsPath)).Contains(model.AdminPassword!, StringComparison.Ordinal));
            Assert.IsTrue(fixture.Setup.Completed);
            Assert.IsFalse(SetupService.IsRequired(new ConfigurationBuilder().AddJsonFile(fixture.SettingsPath).Build()));
            await using var db = fixture.Database();
            var admin = await db.Users.SingleAsync();
            Assert.AreEqual(model.AdminEmail, admin.Email);
            Assert.IsTrue(admin.EmailConfirmed);
            Assert.AreEqual(PasswordVerificationResult.Success,
                new PasswordHasher<IdentityUser>().VerifyHashedPassword(admin, admin.PasswordHash!, model.AdminPassword!));
            var role = await db.Roles.SingleAsync();
            Assert.AreEqual("Admin", role.Name);
            var membership = await db.UserRoles.SingleAsync();
            Assert.AreEqual(admin.Id, membership.UserId);
            Assert.AreEqual(role.Id, membership.RoleId);
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, ".setup-*").Length);
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Setup.SaveAsync(model));
        }

        [TestMethod]
        public async Task Save_UpdatesCaseInsensitiveSettingsWithoutDuplicateKeys()
        {
            using var fixture = new Fixture();
            await File.WriteAllTextAsync(fixture.SettingsPath,
                "{ \"connectionstrings\": { \"defaultconnection\": \"old\", \"other\": \"keep\" }, \"userauthenabled\": false, \"smtp\": { \"host\": \"old\" }, \"setup\": { \"completed\": false } }");

            await fixture.Setup.SaveAsync(fixture.Model());

            var saved = new ConfigurationBuilder().AddJsonFile(fixture.SettingsPath).Build();
            Assert.AreEqual("keep", saved.GetConnectionString("Other"));
            Assert.AreEqual("smtp.example.test", saved["Smtp:Host"]);
            Assert.IsTrue(saved.GetValue<bool>("UserAuthEnabled"));
            Assert.IsTrue(saved.GetValue<bool>("Setup:Completed"));
            var connection = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(saved.GetConnectionString("DefaultConnection"));
            Assert.AreEqual(Path.Combine(fixture.Root, "setup.db"), connection.DataSource);
        }

        [TestMethod]
        public async Task Save_AnonymousModeDoesNotRequireAdminOrSmtp()
        {
            using var fixture = new Fixture();
            await fixture.Setup.SaveAsync(new SetupModel { DefaultConnection = "Data Source=setup.db;Pooling=False", UserAuthEnabled = false });

            var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.SettingsPath))!;
            Assert.IsFalse(saved["UserAuthEnabled"]!.GetValue<bool>());
            await using var db = fixture.Database();
            Assert.AreEqual(0, await db.Users.CountAsync());
            Assert.IsTrue(await db.Database.CanConnectAsync());
        }

        [TestMethod]
        public async Task Save_InvalidPasswordRollsBackAndCanBeRetried()
        {
            using var fixture = new Fixture();
            var model = fixture.Model();
            model.AdminPassword = model.ConfirmPassword = "weak";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Setup.SaveAsync(model));
            Assert.IsFalse(File.Exists(fixture.SettingsPath));
            Assert.IsFalse(fixture.Setup.Completed);
            await using (var db = fixture.Database())
            {
                Assert.AreEqual(0, await db.Users.CountAsync());
                Assert.AreEqual(0, await db.Roles.CountAsync());
            }
            await fixture.Setup.SaveAsync(fixture.Model());
            Assert.IsTrue(fixture.Setup.Completed);
        }

        [TestMethod]
        public async Task Save_FileFailureRollsBackAdminAndAllowsRetry()
        {
            using var fixture = new Fixture();
            Directory.CreateDirectory(fixture.SettingsPath);
            await Assert.ThrowsAsync<IOException>(() => fixture.Setup.SaveAsync(fixture.Model()));
            Assert.IsFalse(fixture.Setup.Completed);
            await using (var db = fixture.Database())
            {
                Assert.AreEqual(0, await db.Users.CountAsync());
                Assert.AreEqual(0, await db.Roles.CountAsync());
            }
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, ".setup-*").Length);
            Directory.Delete(fixture.SettingsPath);
            await fixture.Setup.SaveAsync(fixture.Model());
            Assert.IsTrue(fixture.Setup.Completed);
        }

        [TestMethod]
        public async Task Save_RefusesToTakeOverExistingUsers()
        {
            using var fixture = new Fixture();
            await using (var db = fixture.Database())
            {
                await db.Database.MigrateAsync();
                db.Users.Add(new IdentityUser("existing") { Email = "existing@example.test" });
                await db.SaveChangesAsync();
            }
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Setup.SaveAsync(fixture.Model()));
            Assert.IsFalse(File.Exists(fixture.SettingsPath));
            await using var check = fixture.Database();
            Assert.AreEqual("existing", (await check.Users.SingleAsync()).UserName);
        }

        [TestMethod]
        [DataRow("Data Source=:memory:")]
        [DataRow("Data Source=test.db;Mode=Memory")]
        [DataRow("Data Source=test.db;Mode=ReadOnly")]
        [DataRow("not-a-connection-string")]
        public async Task Save_RejectsNonPersistentOrInvalidDatabase(string connection)
        {
            using var fixture = new Fixture();
            var model = fixture.Model();
            model.DefaultConnection = connection;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Setup.SaveAsync(model));
            Assert.IsFalse(File.Exists(fixture.SettingsPath));
            Assert.IsFalse(fixture.Setup.Completed);
        }

        [TestMethod]
        public async Task Save_ValidatesConfirmationAndSmtpBeforeWriting()
        {
            using var fixture = new Fixture();
            var model = fixture.Model();
            model.ConfirmPassword = "different";
            model.SmtpHost = null;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Setup.SaveAsync(model));
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root).Length);
        }

        [TestMethod]
        public async Task Controller_RejectsRemoteGetAndPostWithoutWriting()
        {
            using var fixture = new Fixture();
            var controller = new SetupController(fixture.Setup, NullLogger<SetupController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Request("192.0.2.1", "localhost") }
            };
            Assert.IsInstanceOfType<NotFoundResult>(controller.Index());
            Assert.IsInstanceOfType<NotFoundResult>(await controller.Index(fixture.Model()));
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root).Length);
        }

        [TestMethod]
        public async Task Save_ConcurrentSubmissionsCreateOnlyOneAdmin()
        {
            using var fixture = new Fixture();
            var first = fixture.Setup.SaveAsync(fixture.Model());
            var second = fixture.Setup.SaveAsync(fixture.Model());
            await first;
            await Assert.ThrowsExactlyAsync<ValidationException>(() => second);
            await using var db = fixture.Database();
            Assert.AreEqual(1, await db.Users.CountAsync());
            Assert.AreEqual(1, await db.UserRoles.CountAsync());
        }

        [TestMethod]
        public async Task Controller_PreservesPasswordErrorsButClearsSecrets()
        {
            using var fixture = new Fixture();
            var controller = new SetupController(fixture.Setup, NullLogger<SetupController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Request("127.0.0.1", "localhost") }
            };
            var model = fixture.Model();
            controller.ModelState.AddModelError(nameof(model.ConfirmPassword), "Passwords must match.");
            controller.ModelState.SetModelValue(nameof(model.AdminPassword), model.AdminPassword, model.AdminPassword);

            Assert.IsInstanceOfType<ViewResult>(await controller.Index(model));

            Assert.AreEqual("Passwords must match.", controller.ModelState[nameof(model.ConfirmPassword)]!.Errors.Single().ErrorMessage);
            Assert.IsNull(controller.ModelState[nameof(model.AdminPassword)]!.AttemptedValue);
            Assert.IsNull(model.AdminPassword);
            Assert.IsNull(model.ConfirmPassword);
            Assert.IsNull(model.SmtpPassword);
            Assert.IsFalse(File.Exists(fixture.SettingsPath));
        }

        [TestMethod]
        public async Task Controller_CompletedSetupShowsRestartPageAndRejectsPost()
        {
            using var fixture = new Fixture();
            await fixture.Setup.SaveAsync(fixture.Model());
            var controller = new SetupController(fixture.Setup, NullLogger<SetupController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = Request("127.0.0.1", "localhost") }
            };
            var result = controller.Index() as ViewResult;
            Assert.IsNotNull(result);
            Assert.AreEqual("Complete", result.ViewName);
            Assert.IsInstanceOfType<NotFoundResult>(await controller.Index(fixture.Model()));
        }

        private static DefaultHttpContext Request(string? address, string host)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = address is null ? null : IPAddress.Parse(address);
            context.Request.Host = new HostString(host);
            return context;
        }

        private sealed class Fixture : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "CrashDumpAnalyzer-Setup-" + Guid.NewGuid().ToString("N"));
            public string SettingsPath => Path.Combine(Root, "appsettings.json");
            public SetupService Setup { get; }

            public Fixture()
            {
                Directory.CreateDirectory(Root);
                var environment = new Mock<IWebHostEnvironment>();
                environment.SetupGet(e => e.ContentRootPath).Returns(Root);
                Setup = new SetupService(new ConfigurationBuilder().Build(), environment.Object);
            }

            public SetupModel Model() => new()
            {
                DefaultConnection = "Data Source=setup.db;Pooling=False",
                AdminEmail = "admin@example.test",
                AdminPassword = "First-admin-42!",
                ConfirmPassword = "First-admin-42!",
                SmtpHost = "smtp.example.test",
                SmtpUsername = "smtp-user",
                SmtpPassword = "smtp-secret",
                SmtpFrom = "cda@example.test"
            };

            public ApplicationDbContext Database() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={Path.Combine(Root, "setup.db")};Pooling=False").Options);

            public void Dispose()
            {
                Directory.Delete(Root, true);
            }
        }
    }
}

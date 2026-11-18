using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using CrashDumpAnalyzer.Controllers;
using CrashDumpAnalyzer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class SettingsTests
    {
        [TestMethod]
        [DataRow(false, false, false, false)]
        [DataRow(false, false, true, true)]
        [DataRow(false, true, false, false)]
        [DataRow(true, false, false, false)]
        [DataRow(true, true, false, true)]
        [DataRow(true, false, true, true)]
        public void Access_RequiresDirectLocalRequestOrAuthenticatedAdmin(bool authEnabled, bool admin, bool local, bool expected)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["UserAuthEnabled"] = authEnabled.ToString() }).Build();
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(local ? "127.0.0.1" : "192.0.2.1");
            context.Request.Host = new HostString(local ? "localhost" : "server.example");
            if (admin) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "test"));
            Assert.AreEqual(expected, SettingsController.CanAccess(context, config));
        }

        [TestMethod]
        public void Access_RejectsReboundAndProxiedLocalRequestsAndUnauthenticatedRole()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["UserAuthEnabled"] = "true" }).Build();
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Host = new HostString("attacker.example");
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")]));
            Assert.IsFalse(SettingsController.CanAccess(context, config));
            context.Request.Host = new HostString("localhost");
            context.Request.Headers["X-Forwarded-For"] = "192.0.2.1";
            Assert.IsFalse(SettingsController.CanAccess(context, config));
        }

        [TestMethod]
        public async Task Controller_RejectsRemoteAnonymousGetAndPost_AndRequiresAntiforgery()
        {
            using var fixture = new Fixture();
            var config = new ConfigurationBuilder().Build();
            var controller = new SettingsController(fixture.Service, config, NullLogger<SettingsController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
            };
            controller.HttpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
            controller.HttpContext.Request.Host = new HostString("localhost");
            Assert.IsInstanceOfType<NotFoundResult>(await controller.Index());
            Assert.IsInstanceOfType<NotFoundResult>(await controller.Index(new()));
            var post = typeof(SettingsController).GetMethod(nameof(SettingsController.Index), [typeof(CrashDumpAnalyzer.Models.SettingsModel)])!;
            Assert.IsTrue(Attribute.IsDefined(post, typeof(ValidateAntiForgeryTokenAttribute)));
            Assert.IsTrue(Attribute.IsDefined(typeof(SettingsController), typeof(AllowAnonymousAttribute)));
        }

        [TestMethod]
        public async Task Save_PreservesUnrelatedSettingsAndSecretsAndPersistsEdits()
        {
            using var fixture = new Fixture();
            await File.WriteAllTextAsync(fixture.Path, "{ // comment\n \"ConnectionStrings\": {\"DefaultConnection\": \"keep\"}, \"DumpPath\": \"old\", \"StaticFolders\": {\"old\": \"folder\"}, \"Smtp\": {\"Password\": \"mail-secret\", \"Extra\": \"keep\"}, \"IssueTracker\": {\"Type\": \"Jira\", \"Password\": \"secret-password\", \"BearerToken\": \"secret-token\", \"Extra\": \"keep\"} }");
            var model = await fixture.Service.LoadAsync();
            Assert.IsFalse(model.IssueTracker.Contains("secret", StringComparison.Ordinal));
            Assert.IsFalse(model.Smtp.Contains("mail-secret", StringComparison.Ordinal));
            model.General = "{\"DumpPath\":\"new\",\"DumpCommentGroupingRegex\":\"(?<GroupKey>.+)\",\"MaxNumOfParallelOperations\":2}";
            model.StaticFolders = new JsonObject { ["symbols"] = fixture.Root }.ToJsonString();
            model.LogFileAnalyzer = "{\"FileExtensions\":\".log;.txt\",\"LogIssueTypes\":{\"Assertion\":{\"Regex\":\"(error)\"}}}";
            model.IssueTracker = "{\"Type\":\"GitHub\",\"Owner\":\"example\",\"Repository\":\"repo\"}";
            model.Smtp = "{\"Host\":\"smtp.example\",\"Port\":587,\"EnableSsl\":true}";
            model.BuildTypes = "{\"alpha\":0,\"\":1,\"emptybuildtype\":\"release\"}";
            model.MinVersions = "{\"App.*\":[\"1.*.*.*\",\"2.0.0.0\"]}";
            model.IssueToken = "new-token";
            model.ClearIssuePassword = true;
            await fixture.Service.SaveAsync(model);
            var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
            Assert.AreEqual("keep", saved["ConnectionStrings"]!["DefaultConnection"]!.ToString());
            Assert.AreEqual("new", saved["DumpPath"]!.ToString());
            Assert.IsNull(saved["StaticFolders"]!["old"]);
            Assert.AreEqual(fixture.Root, saved["StaticFolders"]!["symbols"]!.ToString());
            Assert.AreEqual("(error)", saved["LogFileAnalyzer"]!["LogIssueTypes"]!["Assertion"]!["Regex"]!.ToString());
            Assert.AreEqual("GitHub", saved["IssueTracker"]!["Type"]!.ToString());
            Assert.AreEqual("keep", saved["IssueTracker"]!["Extra"]!.ToString());
            Assert.IsNull(saved["IssueTracker"]!["Password"]);
            Assert.AreEqual("secret-token", saved["IssueTracker"]!["BearerToken"]!.ToString());
            Assert.AreEqual("new-token", saved["IssueTracker"]!["Token"]!.ToString());
            Assert.AreEqual("smtp.example", saved["Smtp"]!["Host"]!.ToString());
            Assert.AreEqual("mail-secret", saved["Smtp"]!["Password"]!.ToString());
            Assert.AreEqual("keep", saved["Smtp"]!["Extra"]!.ToString());
            Assert.AreEqual("release", saved["BuildTypes"]!["emptybuildtype"]!.ToString());
            Assert.AreEqual("1.*.*.*", saved["MinVersions"]!["App.*"]![0]!.ToString());
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, ".settings-*").Length);
            var reloaded = await fixture.Service.LoadAsync();
            Assert.IsFalse(reloaded.IssueTracker.Contains("new-token", StringComparison.Ordinal));
        }

        [TestMethod]
        public async Task Save_UnchangedFileDoesNotRewriteOrAddMissingSections()
        {
            using var fixture = new Fixture();
            const string original = "{ // keep comment\n \"DumpPath\": \"old\", \"IssueTracker\": {\"BearerToken\": \"secret\"} }";
            await File.WriteAllTextAsync(fixture.Path, original);
            var model = await fixture.Service.LoadAsync();

            await fixture.Service.SaveAsync(model);

            Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
            Assert.AreEqual(0, Directory.GetFiles(fixture.Root, ".settings-*").Length);
        }

        [TestMethod]
        public async Task Save_RepeatedEditsPreserveRegexValuesHiddenSettingsAndBlankCredentials()
        {
            using var fixture = new Fixture();
            const string buildRegex = @"\s*ProductVersion:\s+(\d+\.\d+)";
            const string groupingRegex = @".+\\(?<GroupKey>[^\\]+)\\SupportReport\\.*";
            const string logRegex = @"^(\d{4}/\d{2})";
            const string versionRegex = @"App\.\d+";
            var original = new JsonObject
            {
                ["BuildTypeRegex"] = buildRegex,
                ["DumpCommentGroupingRegex"] = groupingRegex,
                ["DumpPath"] = "old",
                ["Hidden"] = new JsonObject { ["Path"] = @"C:\hidden\path", ["Value"] = 42 },
                ["IssueTracker"] = new JsonObject { ["Password"] = "jira-password", ["BearerToken"] = "jira-bearer", ["Token"] = "github-token", ["Extra"] = "keep" },
                ["Smtp"] = new JsonObject { ["Password"] = "smtp-password", ["Extra"] = "keep" },
                ["LogFileAnalyzer"] = new JsonObject { ["DateTimeRegex"] = logRegex, ["LogIssueTypes"] = new JsonObject { ["Assertion"] = new JsonObject { ["Regex"] = logRegex } } },
                ["MinVersions"] = new JsonObject { [versionRegex] = new JsonArray("1.0") }
            };
            await File.WriteAllTextAsync(fixture.Path, original.ToJsonString());

            for (var i = 0; i < 3; i++)
            {
                var model = await fixture.Service.LoadAsync();
                var general = JsonNode.Parse(model.General)!.AsObject();
                general["DumpPath"] = $"changed-{i}";
                model.General = general.ToJsonString();
                await fixture.Service.SaveAsync(model);

                var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
                Assert.AreEqual(buildRegex, saved["BuildTypeRegex"]!.GetValue<string>());
                Assert.AreEqual(groupingRegex, saved["DumpCommentGroupingRegex"]!.GetValue<string>());
                Assert.AreEqual(logRegex, saved["LogFileAnalyzer"]!["DateTimeRegex"]!.GetValue<string>());
                Assert.AreEqual(logRegex, saved["LogFileAnalyzer"]!["LogIssueTypes"]!["Assertion"]!["Regex"]!.GetValue<string>());
                Assert.IsNotNull(saved["MinVersions"]![versionRegex]);
                Assert.IsTrue(JsonNode.DeepEquals(original["Hidden"], saved["Hidden"]));
                Assert.IsTrue(JsonNode.DeepEquals(original["IssueTracker"], saved["IssueTracker"]));
                Assert.IsTrue(JsonNode.DeepEquals(original["Smtp"], saved["Smtp"]));
                Assert.IsNull(saved["StaticFolders"]);
                Assert.IsNull(saved["BuildTypes"]);
            }
        }

        [TestMethod]
        public async Task Save_EditedRegexLoadsWithTheIntendedBackslashes()
        {
            using var fixture = new Fixture();
            await File.WriteAllTextAsync(fixture.Path, "{}");
            var model = await fixture.Service.LoadAsync();
            model.General = "{\"BuildTypeRegex\":\"\\\\d+\\\\.\\\\d+\"}";
            model.LogFileAnalyzer = "{\"DateTimeRegex\":\"\\\\d{4}/\\\\d{2}\"}";

            await fixture.Service.SaveAsync(model);

            var configuration = new ConfigurationBuilder().AddJsonFile(fixture.Path).Build();
            Assert.AreEqual(@"\d+\.\d+", configuration["BuildTypeRegex"]);
            Assert.AreEqual(@"\d{4}/\d{2}", configuration["LogFileAnalyzer:DateTimeRegex"]);
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch("release 12.34", configuration["BuildTypeRegex"]!));
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch("2026/11", configuration["LogFileAnalyzer:DateTimeRegex"]!));
        }

        [TestMethod]
        public async Task LoadAndSave_KeepJsonReadableAndPreserveSpecialCharacters()
        {
            using var fixture = new Fixture();
            const string regex = @"(?<GroupKey>\w+)<tag>&'";
            const string path = "</textarea><script>alert(\"test\")</script>\nC:\\folder";
            var initial = new JsonObject
            {
                ["DumpCommentGroupingRegex"] = regex,
                ["DumpPath"] = path,
                ["LogFileAnalyzer"] = new JsonObject { ["DateTimeRegex"] = @"\d+" },
                ["Hidden"] = "<hidden>&+"
            };
            await File.WriteAllTextAsync(fixture.Path, initial.ToJsonString());

            var model = await fixture.Service.LoadAsync();
            StringAssert.Contains(model.General, "(?<GroupKey>");
            StringAssert.Contains(model.General, "+)<tag>&'");
            StringAssert.Contains(model.General, @"\\w+");
            StringAssert.Contains(model.LogFileAnalyzer, @"\\d+");
            Assert.AreEqual(path, JsonNode.Parse(model.General)!["DumpPath"]!.GetValue<string>());
            var general = JsonNode.Parse(model.General)!.AsObject();
            general["TicketBaseUrl"] = "https://example.test/";
            model.General = general.ToJsonString();

            await fixture.Service.SaveAsync(model);

            var text = await File.ReadAllTextAsync(fixture.Path);
            StringAssert.Contains(text, "(?<GroupKey>");
            StringAssert.Contains(text, "+)<tag>&'");
            StringAssert.Contains(text, "<hidden>&+");
            StringAssert.Contains(text, @"\\w+");
            StringAssert.Contains(text, @"\nC:\\folder");
            var saved = JsonNode.Parse(text)!;
            Assert.AreEqual(regex, saved["DumpCommentGroupingRegex"]!.GetValue<string>());
            Assert.AreEqual(path, saved["DumpPath"]!.GetValue<string>());
        }

        [TestMethod]
        public async Task Save_UnchangedLegacyValuesDoNotBlockUnrelatedEdits()
        {
            using var fixture = new Fixture();
            var original = new JsonObject
            {
                ["DumpPath"] = "old",
                ["StaticFolders"] = new JsonObject { ["missing"] = System.IO.Path.Combine(fixture.Root, "not-present") },
                ["Smtp"] = new JsonObject { ["EnableSsl"] = "", ["Password"] = "keep" }
            };
            await File.WriteAllTextAsync(fixture.Path, original.ToJsonString());
            var model = await fixture.Service.LoadAsync();
            model.General = "{\"DumpPath\":\"new\"}";

            await fixture.Service.SaveAsync(model);

            var saved = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Path))!;
            Assert.AreEqual("new", saved["DumpPath"]!.GetValue<string>());
            Assert.IsTrue(JsonNode.DeepEquals(original["StaticFolders"], saved["StaticFolders"]));
            Assert.IsTrue(JsonNode.DeepEquals(original["Smtp"], saved["Smtp"]));
        }

        [TestMethod]
        public async Task Save_RejectsMissingFieldsWithoutWriting()
        {
            using var fixture = new Fixture();
            const string original = "{\"DumpPath\":\"keep\",\"IssueTracker\":{\"BearerToken\":\"secret\"}}";
            await File.WriteAllTextAsync(fixture.Path, original);
            var model = await fixture.Service.LoadAsync();
            model.General = string.Empty;

            var errors = new List<ValidationResult>();
            Assert.IsFalse(Validator.TryValidateObject(model, new ValidationContext(model), errors, true));
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            Assert.AreEqual(original, await File.ReadAllTextAsync(fixture.Path));
        }

        [TestMethod]
        public async Task Save_RejectsInvalidInputAndStaleRevisionWithoutWriting()
        {
            using var fixture = new Fixture();
            await File.WriteAllTextAsync(fixture.Path, "{\"DumpPath\":\"original\"}");
            var model = await fixture.Service.LoadAsync();
            model.LogFileAnalyzer = "{\"DateTimeRegex\":\"[\"}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            Assert.AreEqual("{\"DumpPath\":\"original\"}", await File.ReadAllTextAsync(fixture.Path));
            model.LogFileAnalyzer = "{}";
            model.IssueTracker = "{\"Token\":\"must-not-be-accepted\"}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.IssueTracker = "{\"Type\":\"GitHub\",\"Url\":\"\"}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.IssueTracker = "{}";
            model.Smtp = "{\"Port\":0}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.Smtp = "{\"Password\":\"not-accepted\"}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.Smtp = "{}";
            model.General = "{\"MaxNumOfParallelOperations\":0}";
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.General = "{}";
            model.StaticFolders = new JsonObject { ["symbols"] = System.IO.Path.Combine(fixture.Root, "missing") }.ToJsonString();
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            model.StaticFolders = "{}";
            await File.WriteAllTextAsync(fixture.Path, "{\"DumpPath\":\"other\"}");
            await Assert.ThrowsExactlyAsync<ValidationException>(() => fixture.Service.SaveAsync(model));
            Assert.AreEqual("{\"DumpPath\":\"other\"}", await File.ReadAllTextAsync(fixture.Path));
        }

        private sealed class Fixture : IDisposable
        {
            public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cda-settings-" + Guid.NewGuid().ToString("N"));
            public string Path => System.IO.Path.Combine(Root, "appsettings.json");
            public SettingsService Service { get; }
            public Fixture()
            {
                Directory.CreateDirectory(Root);
                var environment = new Mock<IWebHostEnvironment>();
                environment.SetupGet(e => e.ContentRootPath).Returns(Root);
                Service = new SettingsService(environment.Object);
            }
            public void Dispose() => Directory.Delete(Root, true);
        }
    }
}

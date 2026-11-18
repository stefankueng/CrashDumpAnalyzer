using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using CrashDumpAnalyzer.Models;

namespace CrashDumpAnalyzer.Services
{
    public sealed class SettingsService(IWebHostEnvironment environment)
    {
        private static readonly string[] GeneralKeys = ["DumpPath", "CdbExe", "AgestoreExe", "MaxNumOfParallelOperations", "MaxCacheSize", "CachePath", "TicketBaseUrl", "DeleteDumpsUploadedBeforeDays", "DeleteDumpsAfterFixedDays", "ShowEntriesForDaysBack", "ShowMaxFixedEntries", "LastUploadsDays", "LastUploadsItems", "BuildTypeRegex", "DumpCommentGroupingRegex", "SymbolPath"];
        private static readonly string[] IssueKeys = ["Type", "Url", "Username", "Owner", "Repository"];
        private static readonly string[] SmtpKeys = ["Host", "Port", "Username", "EnableSsl", "From"];
        private static readonly string[] SecretKeys = ["Password", "BearerToken", "Token"];
        private static readonly JsonNodeOptions NodeOptions = new() { PropertyNameCaseInsensitive = true };
        private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        // JSON is written to disk or rendered through Razor's HTML-encoded textarea, not embedded in a script.
        private static readonly JsonSerializerOptions OutputOptions = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private string Pathname => Path.Combine(environment.ContentRootPath, "appsettings.json");

        public async Task<SettingsModel> LoadAsync()
        {
            var text = await ReadAsync();
            var root = Parse(text, "appsettings.json");
            var issue = Section(root, "IssueTracker");
            return new SettingsModel
            {
                Revision = Revision(text),
                General = Select(root, GeneralKeys).ToJsonString(OutputOptions),
                StaticFolders = Section(root, "StaticFolders").ToJsonString(OutputOptions),
                LogFileAnalyzer = Section(root, "LogFileAnalyzer").ToJsonString(OutputOptions),
                IssueTracker = Select(issue, IssueKeys).ToJsonString(OutputOptions),
                Smtp = Select(Section(root, "Smtp"), SmtpKeys).ToJsonString(OutputOptions),
                BuildTypes = Section(root, "BuildTypes").ToJsonString(OutputOptions),
                MinVersions = Section(root, "MinVersions").ToJsonString(OutputOptions)
            };
        }

        public async Task SaveAsync(SettingsModel model)
        {
            var general = ParseInput(model.General, "General");
            var folders = ParseInput(model.StaticFolders, "Static folders");
            var log = ParseInput(model.LogFileAnalyzer, "Log file analyzer");
            var issue = ParseInput(model.IssueTracker, "Issue tracker");
            var smtp = ParseInput(model.Smtp, "SMTP");
            var builds = ParseInput(model.BuildTypes, "Build types");
            var versions = ParseInput(model.MinVersions, "Minimum versions");
            if ((model.ClearSmtpPassword && !string.IsNullOrEmpty(model.SmtpPassword)) ||
                (model.ClearIssuePassword && !string.IsNullOrEmpty(model.IssuePassword)) ||
                (model.ClearIssueBearerToken && !string.IsNullOrEmpty(model.IssueBearerToken)) ||
                (model.ClearIssueToken && !string.IsNullOrEmpty(model.IssueToken)))
                throw new ValidationException("Do not enter and clear the same credential simultaneously.");

            await _saveLock.WaitAsync();
            try
            {
                var text = await ReadAsync();
                if (!string.Equals(model.Revision, Revision(text), StringComparison.Ordinal))
                    throw new ValidationException("appsettings.json changed since this page was opened. Reload the page before saving.");
                var root = Parse(text, "appsettings.json");
                Validate(
                    Changed(Select(root, GeneralKeys), general),
                    Changed(Section(root, "StaticFolders"), folders),
                    Changed(Section(root, "LogFileAnalyzer"), log),
                    Changed(Select(Section(root, "IssueTracker"), IssueKeys), issue),
                    Changed(Section(root, "BuildTypes"), builds),
                    Changed(Section(root, "MinVersions"), versions));
                ValidateSmtp(Changed(Select(Section(root, "Smtp"), SmtpKeys), smtp));
                var original = root.DeepClone();
                ReplaceSelected(root, general, GeneralKeys);
                ReplaceSection(root, "StaticFolders", folders);
                ReplaceSection(root, "LogFileAnalyzer", log);
                ReplaceSection(root, "BuildTypes", builds);
                ReplaceSection(root, "MinVersions", versions);
                var tracker = Section(root, "IssueTracker");
                ReplaceSelected(tracker, issue, IssueKeys);
                SetSecret(tracker, SecretKeys[0], model.IssuePassword, model.ClearIssuePassword);
                SetSecret(tracker, SecretKeys[1], model.IssueBearerToken, model.ClearIssueBearerToken);
                SetSecret(tracker, SecretKeys[2], model.IssueToken, model.ClearIssueToken);
                ReplaceSection(root, "IssueTracker", tracker);
                var mail = Section(root, "Smtp");
                ReplaceSelected(mail, smtp, SmtpKeys);
                SetSecret(mail, "Password", model.SmtpPassword, model.ClearSmtpPassword);
                ReplaceSection(root, "Smtp", mail);

                if (JsonNode.DeepEquals(original, root))
                    return;

                var temporaryPath = Path.Combine(environment.ContentRootPath, $".settings-{Guid.NewGuid():N}.tmp");
                try
                {
                    await File.WriteAllTextAsync(temporaryPath, root.ToJsonString(OutputOptions));
                    if (File.Exists(Pathname))
                        File.Replace(temporaryPath, Pathname, null);
                    else
                        File.Move(temporaryPath, Pathname);
                }
                finally
                {
                    if (File.Exists(temporaryPath))
                        File.Delete(temporaryPath);
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        private async Task<string> ReadAsync() => File.Exists(Pathname) ? await File.ReadAllTextAsync(Pathname) : "{}";
        private static string Revision(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        private static JsonObject Parse(string text, string name) => JsonNode.Parse(text, NodeOptions, DocumentOptions) as JsonObject
            ?? throw new JsonException($"{name} must be a JSON object.");
        private static JsonObject ParseInput(string? text, string name)
        {
            try { return Parse(text ?? "", name); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
            { throw new ValidationException($"{name} must be a valid JSON object with unique property names."); }
        }
        private static JsonObject Section(JsonObject root, string name) => root[name] switch
        {
            null => new JsonObject(NodeOptions),
            JsonObject section => (JsonObject)section.DeepClone(),
            _ => throw new JsonException($"{name} must be a JSON object.")
        };
        private static JsonObject Select(JsonObject source, IEnumerable<string> keys)
        {
            var result = new JsonObject(NodeOptions);
            foreach (var key in keys)
                if (source.TryGetPropertyValue(key, out var value))
                    result[key] = value?.DeepClone();
            return result;
        }
        private static JsonObject Changed(JsonObject original, JsonObject submitted) =>
            JsonNode.DeepEquals(original, submitted) ? new JsonObject(NodeOptions) : submitted;
        private static void ReplaceSection(JsonObject root, string name, JsonObject section)
        {
            if (!JsonNode.DeepEquals(Section(root, name), section))
                root[name] = section;
        }
        private static void ReplaceSelected(JsonObject target, JsonObject source, IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                if (source.TryGetPropertyValue(key, out var value))
                {
                    if (!target.TryGetPropertyValue(key, out var existing) || !JsonNode.DeepEquals(existing, value))
                        target[key] = value?.DeepClone();
                }
                else
                    target.Remove(key);
            }
        }
        private static void SetSecret(JsonObject section, string key, string? value, bool clear)
        {
            if (clear) section.Remove(key);
            else if (!string.IsNullOrEmpty(value)) section[key] = value;
        }
        private static string StringValue(JsonNode? node, string name)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && text != null)
                return text;
            throw new ValidationException($"{name} must be a string.");
        }
        private static void ValidateSmtp(JsonObject smtp)
        {
            foreach (var (key, value) in smtp)
            {
                if (!SmtpKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new ValidationException($"Unknown or credential SMTP setting: {key}. Use the password field below.");
                if (key.Equals("Port", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(value?.ToString(), out var port) || port is < 1 or > 65535)
                        throw new ValidationException("SMTP Port must be between 1 and 65535.");
                }
                else if (key.Equals("EnableSsl", StringComparison.OrdinalIgnoreCase))
                {
                    if (!bool.TryParse(value?.ToString(), out _))
                        throw new ValidationException("SMTP EnableSsl must be true or false.");
                }
                else StringValue(value, $"Smtp:{key}");
            }
        }
        private static void RegexValue(string pattern, string name, bool groupKey = false)
        {
            try
            {
                var regex = new Regex(pattern);
                if (groupKey && !regex.GetGroupNames().Contains("GroupKey"))
                    throw new ValidationException($"{name} must contain a named GroupKey capture group.");
            }
            catch (ArgumentException) { throw new ValidationException($"{name} is not a valid regular expression."); }
        }
        private static void Validate(JsonObject general, JsonObject folders, JsonObject log, JsonObject issue, JsonObject builds, JsonObject versions)
        {
            foreach (var (key, value) in general)
            {
                if (!GeneralKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                    throw new ValidationException($"Unknown general setting: {key}.");
                if (key.Equals("MaxNumOfParallelOperations", StringComparison.OrdinalIgnoreCase) || key.Equals("MaxCacheSize", StringComparison.OrdinalIgnoreCase) || key.Equals("DeleteDumpsUploadedBeforeDays", StringComparison.OrdinalIgnoreCase) || key.Equals("DeleteDumpsAfterFixedDays", StringComparison.OrdinalIgnoreCase) || key.Equals("ShowEntriesForDaysBack", StringComparison.OrdinalIgnoreCase) || key.Equals("ShowMaxFixedEntries", StringComparison.OrdinalIgnoreCase) || key.Equals("LastUploadsDays", StringComparison.OrdinalIgnoreCase) || key.Equals("LastUploadsItems", StringComparison.OrdinalIgnoreCase))
                {
                    var number = value?.ToString();
                    if (!long.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 0 || (key.Equals("MaxNumOfParallelOperations", StringComparison.OrdinalIgnoreCase) && (n == 0 || n > ushort.MaxValue)) || (key.StartsWith("Show", StringComparison.OrdinalIgnoreCase) || key.StartsWith("LastUploads", StringComparison.OrdinalIgnoreCase)) && n > int.MaxValue)
                        throw new ValidationException($"{key} must be a valid non-negative integer within its supported range.");
                }
                else
                {
                    var text = StringValue(value, key);
                    if (key.Equals("BuildTypeRegex", StringComparison.OrdinalIgnoreCase) || key.Equals("DumpCommentGroupingRegex", StringComparison.OrdinalIgnoreCase) && text.Length > 0)
                        RegexValue(text, key, key.Equals("DumpCommentGroupingRegex", StringComparison.OrdinalIgnoreCase));
                }
            }
            foreach (var (key, value) in folders)
            {
                if (string.IsNullOrWhiteSpace(key) || key.Contains('/') || key.Contains('\\') || key.Contains(':') || key is "." or "..")
                    throw new ValidationException("Static folder names must be single URL path segments.");
                var path = StringValue(value, $"StaticFolders:{key}");
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), path)))
                    throw new ValidationException($"StaticFolders:{key} must refer to an existing folder accessible to the application.");
            }
            foreach (var (key, value) in log)
            {
                if (key.Equals("LogIssueTypes", StringComparison.OrdinalIgnoreCase))
                {
                    if (value is not JsonObject types) throw new ValidationException("LogIssueTypes must be an object.");
                    foreach (var (type, entry) in types)
                    {
                        if (entry is not JsonObject obj) throw new ValidationException($"Log issue type {type} must be an object.");
                        foreach (var (property, item) in obj)
                            if (property.Equals("Regex", StringComparison.OrdinalIgnoreCase)) RegexValue(StringValue(item, $"LogIssueTypes:{type}:Regex"), $"LogIssueTypes:{type}:Regex");
                    }
                }
                else
                {
                    var text = StringValue(value, $"LogFileAnalyzer:{key}");
                    if (key.EndsWith("Regex", StringComparison.OrdinalIgnoreCase)) RegexValue(text, $"LogFileAnalyzer:{key}");
                }
            }
            foreach (var (key, value) in issue)
            {
                if (!IssueKeys.Contains(key, StringComparer.OrdinalIgnoreCase)) throw new ValidationException($"Unknown or credential issue tracker setting: {key}. Use the credential fields below.");
                var text = StringValue(value, $"IssueTracker:{key}");
                if (key.Equals("Type", StringComparison.OrdinalIgnoreCase) && text is not ("" or "Jira" or "GitHub")) throw new ValidationException("Issue tracker Type must be Jira, GitHub, or empty.");
                if (key.Equals("Url", StringComparison.OrdinalIgnoreCase) && (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))) throw new ValidationException("Issue tracker Url must be an absolute HTTP or HTTPS URL; remove it to use the default.");
            }
            foreach (var (key, value) in builds)
            {
                if (key.Equals("emptybuildtype", StringComparison.OrdinalIgnoreCase)) StringValue(value, "BuildTypes:emptybuildtype");
                else if (!int.TryParse(value?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) throw new ValidationException($"BuildTypes:{key} must be an integer.");
            }
            foreach (var (key, value) in versions)
            {
                RegexValue(key, $"MinVersions:{key}");
                if (value is not JsonArray array) throw new ValidationException($"MinVersions:{key} must be an array.");
                foreach (var item in array)
                {
                    var version = StringValue(item, $"MinVersions:{key}");
                    if (version.Length == 0) continue;
                    var parts = (version.StartsWith('v') ? version[1..] : version).Split('.');
                    if (parts.Length is < 2 or > 4 || parts.Any(part => part != "*" && (!int.TryParse(part, out var n) || n < 0))) throw new ValidationException($"Invalid minimum version in {key}: {version}.");
                }
            }
        }
    }
}

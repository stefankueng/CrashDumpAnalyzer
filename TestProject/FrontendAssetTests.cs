using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Tests
{
    [TestClass]
    public class FrontendAssetTests
    {
        [TestMethod]
        public void VendorAssets_MatchManifestAndRetainLicenses()
        {
            var root = ProjectDirectory();
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "libman.json")));
            var selected = manifest.RootElement.GetProperty("libraries").EnumerateArray()
                .SelectMany(library => library.GetProperty("files").EnumerateArray()
                    .Select(file => Path.GetFullPath(Path.Combine(root,
                        library.GetProperty("destination").GetString()!, file.GetString()!))))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in selected)
                Assert.IsTrue(File.Exists(file), file);
            var vendorRoot = Path.Combine(root, "wwwroot", "lib");
            foreach (var file in Directory.EnumerateFiles(vendorRoot, "*", SearchOption.AllDirectories))
                Assert.IsTrue(selected.Contains(file) || Path.GetFileNameWithoutExtension(file).Equals("LICENSE", StringComparison.OrdinalIgnoreCase), file);
            foreach (var library in Directory.EnumerateDirectories(vendorRoot))
                Assert.IsTrue(Directory.EnumerateFiles(library, "LICENSE*").Any(), library);
        }

        [TestMethod]
        public void Views_UseExistingExternalScriptsAndOnlyDynamicInlineStyles()
        {
            var root = ProjectDirectory();
            foreach (var view in Directory.EnumerateFiles(Path.Combine(root, "Views"), "*.cshtml", SearchOption.AllDirectories))
            {
                var content = File.ReadAllText(view);
                foreach (Match script in Regex.Matches(content, "<script\\b[^>]*>", RegexOptions.IgnoreCase))
                {
                    var source = Regex.Match(script.Value, "src=\"~/([^\"]+)\"");
                    Assert.IsTrue(source.Success, "Inline script in " + view);
                    Assert.IsTrue(File.Exists(Path.Combine(root, "wwwroot", source.Groups[1].Value)), source.Value);
                    if (source.Groups[1].Value.StartsWith("js/", StringComparison.Ordinal))
                        StringAssert.Contains(script.Value, "asp-append-version=\"true\"");
                }
                foreach (Match style in Regex.Matches(content, "style=\"([^\"]*)\"", RegexOptions.IgnoreCase))
                    Assert.AreEqual("background-color: @issue.StateColor", style.Groups[1].Value, view);
            }
        }

        [TestMethod]
        public void PageScripts_LoadAfterSharedDependenciesAndOnlyOnTheirPages()
        {
            var root = ProjectDirectory();
            var layout = File.ReadAllText(Path.Combine(root, "Views", "Shared", "_Layout.cshtml"));
            Assert.IsTrue(layout.IndexOf("js/theme.js", StringComparison.Ordinal) < layout.IndexOf("</head>", StringComparison.Ordinal));
            Assert.IsTrue(layout.IndexOf("jquery.min.js", StringComparison.Ordinal) < layout.IndexOf("bootstrap.bundle.min.js", StringComparison.Ordinal));
            Assert.IsTrue(layout.IndexOf("bootstrap.bundle.min.js", StringComparison.Ordinal) < layout.IndexOf("js/site.js", StringComparison.Ordinal));
            Assert.IsTrue(layout.IndexOf("js/site.js", StringComparison.Ordinal) < layout.IndexOf("RenderSectionAsync(\"Scripts\"", StringComparison.Ordinal));
            foreach (var (page, script) in new[] { ("Index", "upload"), ("GroupedDumps", "grouped-dumps"), ("Statistics", "statistics") })
            {
                var content = File.ReadAllText(Path.Combine(root, "Views", "Home", page + ".cshtml"));
                StringAssert.Contains(content, "@section Scripts");
                StringAssert.Contains(content, "~/js/" + script + ".js");
                Assert.IsFalse(layout.Contains("~/js/" + script + ".js", StringComparison.Ordinal));
            }
        }

        [TestMethod]
        public void CrashList_HasSemanticHeadersAndFocusableScrollRegion()
        {
            var view = File.ReadAllText(Path.Combine(ProjectDirectory(), "Views", "Home", "Index.cshtml"));
            StringAssert.Contains(view, "class=\"table-responsive crash-list\" role=\"region\" aria-label=\"Crash callstacks\" tabindex=\"0\"");
            var header = Regex.Match(view, "<thead>([\\s\\S]*?)</thead>");
            Assert.IsTrue(header.Success);
            Assert.AreEqual(9, Regex.Matches(header.Value, "<th scope=\"col\">").Count);
            StringAssert.Contains(header.Value, "id=\"select-all\"");
            StringAssert.Contains(view, "<tbody>");
            StringAssert.Contains(view, "</tbody>");
        }

        private static string ProjectDirectory()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
            {
                var project = Path.Combine(directory.FullName, "CrashDumpAnalyzer");
                if (File.Exists(Path.Combine(project, "libman.json")))
                    return project;
            }
            throw new DirectoryNotFoundException("Cannot locate the CrashDumpAnalyzer frontend sources.");
        }
    }
}

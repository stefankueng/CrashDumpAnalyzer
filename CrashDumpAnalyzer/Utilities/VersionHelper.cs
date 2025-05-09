using System.Reflection;

namespace CrashDumpAnalyzer.Utilities
{
    public partial class VersionHelper
    {
        public static string GetAssemblyVersion()
        {
            var infoVersion = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false).FirstOrDefault() as AssemblyInformationalVersionAttribute;
            return infoVersion?.InformationalVersion ?? string.Empty;
        }

        public static string GetAssemblyVersionAsHtml(string repoUrl)
        {
            var version = GetAssemblyVersion();
            if (string.IsNullOrEmpty(version))
                return string.Empty;

            // Extract Git hash (assuming it's the last part of the version string after a '+')
            var match = GitHashRegex().Match(version);
            if (!match.Success)
                return version; // Return plain version if no Git hash is found

            var gitHash = match.Groups[1].Value;
            var link = $"{repoUrl}/tree/{gitHash}";
            // change the full git hash into a short hash
            gitHash = gitHash[..7];
            return $"{version[..version.LastIndexOf('+')]}+<a href=\"{link}\" target=\"_blank\">{gitHash}</a>";
        }

        [System.Text.RegularExpressions.GeneratedRegex(@"\+([a-fA-F0-9]{7,40})$")]
        private static partial System.Text.RegularExpressions.Regex GitHashRegex();
    }
}

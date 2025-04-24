using System.Reflection;

namespace CrashDumpAnalyzer.Utilities
{
    public class VersionHelper
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
            var match = System.Text.RegularExpressions.Regex.Match(version, @"\+([a-fA-F0-9]{7,40})$");
            if (!match.Success)
                return version; // Return plain version if no Git hash is found

            var gitHash = match.Groups[1].Value;
            var link = $"{repoUrl}/tree/{gitHash}";
            return $"<a href=\"{link}\" target=\"_blank\">{version}</a>";
        }
    }
}

using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public static class MinVersions
    {
        private static Dictionary<Regex, Dictionary<int, SemanticVersion>> _minVersions = [];

        public static void Initialize(ILogger logger, IConfiguration configuration)
        {
            _minVersions.Clear();

            configuration.GetSection("MinVersions").GetChildren().ToList().ForEach(appEntry =>
            {
                if (appEntry.Key != null)
                {
                    try
                    {
                        var regex = new Regex(appEntry.Key, RegexOptions.IgnoreCase);
                        _minVersions[regex] = [];

                        appEntry.GetChildren().ToList().ForEach(versionEntry =>
                        {
                            if (versionEntry.Value != null )
                            {
                                try
                                {
                                    var versionString = versionEntry.Value;
                                    versionString = versionString.Replace("*", "65535");
                                    if (string.IsNullOrEmpty(versionString))
                                        versionString = "65535.65535.65535.65535";

                                    var version = new SemanticVersion(versionString, -1);
                                    _minVersions[regex][version.Major] = version;
                                }
                                catch (Exception e)
                                {
                                    logger.LogError(e, "Error parsing min version for app regex '{AppPattern}' major version {MajorVersion}", appEntry.Key, versionEntry.Key);
                                }
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Error parsing min version for app regex '{AppPattern}'", appEntry.Key);
                    }
                }
            });
        }

        public static bool IsVersionSupported(string application, SemanticVersion version)
        {
            foreach (var kvp in _minVersions)
            {
                if (kvp.Key.IsMatch(application))
                {
                    if (kvp.Value.TryGetValue(version.Major, out var minVersion))
                    {
                        if (version < minVersion)
                            return false;
                    }
                }
            }
            return true;
        }
    }
}

using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public static class MinVersions
    {
        private static Dictionary<Regex,Dictionary<Regex, SemanticVersion>> _minVersions = new Dictionary<Regex, Dictionary<Regex, SemanticVersion>>();
        public static void Initialize(ILogger logger, IConfiguration configuration)
        {
            configuration.GetSection("MinVersions").GetChildren().ToList().ForEach(type =>
            {
                if (type.Key != null)
                {
                    try
                    {
                        var regex = new Regex(type.Key, RegexOptions.IgnoreCase);
                        _minVersions[regex] = new Dictionary<Regex, SemanticVersion>();
                        type.GetChildren().ToList().ForEach(subtype =>
                        {
                            if (subtype.Key != null && subtype.Value != null)
                            {
                                try
                                {
                                    var subregex = new Regex(subtype.Key, RegexOptions.IgnoreCase);
                                    var versionString = subtype.Value;
                                    versionString = versionString.Replace("*", "65535");
                                    if (string.IsNullOrEmpty(versionString))
                                        versionString = "65535.65535.65535.65535";

                                    var version = new SemanticVersion(versionString, -1);
                                    _minVersions[regex][subregex] = version;
                                }
                                catch (Exception e)
                                {
                                    logger.LogError(e, "Error parsing min version regex");
                                }
                            }
                        });
                    }
                    catch (Exception e)
                    {
                        logger.LogError(e, "Error parsing min version app regex");
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
                    foreach (var kvp2 in kvp.Value)
                    {
                        if (kvp2.Key.IsMatch(version.ToVersionString()))
                        {
                            if (version < kvp2.Value)
                            {
                                return false;
                            }
                        }
                    }
                }
            }
            return true;
        }
    }
}

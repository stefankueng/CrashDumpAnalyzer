﻿using System.Text.RegularExpressions;

namespace CrashDumpAnalyzer.Utilities
{
    public static class BuildTypes
    {
        private static Regex? _buildTypeRegex;
        private static readonly Dictionary<string, int> _buildTypes = new Dictionary<string, int>();
        private static readonly object _lock = new object(); // Lock object for thread safety
        private static string emptybuildtype = "release";

        public static void Initialize(ILogger logger, IConfiguration configuration)
        {
            lock (_lock) // Ensure thread-safe initialization
            {
                var buildTypeRegexString = configuration.GetValue<string>("BuildTypeRegex") ?? string.Empty;
                try
                {
                    _buildTypeRegex = new Regex(buildTypeRegexString, RegexOptions.IgnoreCase);
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Error parsing build type regex");
                }

                configuration.GetSection("BuildTypes").GetChildren().ToList().ForEach(type =>
                {
                    if (type.Value != null)
                    {
                        if (type.Key == "emptybuildtype")
                            emptybuildtype = type.Value;
                        else
                            _buildTypes[type.Key] = int.Parse(type.Value);
                    }
                });
            }
        }

        public static bool HasRegex()
        {
            lock (_lock)
            {
                return _buildTypeRegex != null;
            }
        }

        public static int ExtractBuildType(string versionResourceString)
        {
            lock (_lock)
            {
                if (_buildTypeRegex == null)
                {
                    return -1;
                }
                var match = _buildTypeRegex.Match(versionResourceString);
                if (match.Success)
                {
                    var buildTypeString = match.Groups[1].Value;
                    if (_buildTypes.TryGetValue(buildTypeString, out var type))
                        return type;
                }
                return -1;
            }
        }

        public static List<string> BuildTypeStrings()
        {
            lock (_lock)
            {
                // Create a list of _buildTypes.Keys but sorted according to the values
                var list = _buildTypes.OrderBy(kv => kv.Value).Select(kv => kv.Key).ToList();
                // Replace the empty string in the list with the emptybuildtype
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Length == 0)
                    {
                        list[i] = emptybuildtype;
                    }
                }
                return list;
            }
        }

        public static int ParseBuildType(string s)
        {
            lock (_lock)
            {
                if (_buildTypes.TryGetValue(s, out var type))
                    return type;
                if (s == emptybuildtype)
                    if (_buildTypes.TryGetValue("", out type))
                        return type;
                return -1;
            }
        }

        public static string BuildTypeString(int type)
        {
            lock (_lock) 
            {
                return _buildTypes.FirstOrDefault(kv => kv.Value == type).Key;
            }
        }

        public static Dictionary<string, int> Types
        {
            get
            {
                lock (_lock) 
                {
                    return new Dictionary<string, int>(_buildTypes); // Return a copy to avoid external modification
                }
            }
        }
    }
}

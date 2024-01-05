namespace CrashDumpAnalyzer.Utilities
{
    public class SemanticVersion
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;
        private readonly string _version;

        public SemanticVersion(int major, int minor, int patch)
        {
            this._major = major;
            this._minor = minor;
            this._patch = patch;
            this._version = $"{major}.{minor}.{patch}";
        }

        public SemanticVersion(string version)
        {
            this._version = version.StartsWith('v') ? version.Substring(1) : version;
            try
            {
                string[] parts = this._version.Split('.');
                if (parts.Length == 2)
                {
                    this._major = int.Parse(parts[0]);
                    this._minor = int.Parse(parts[1]);
                    this._patch = 0;
                }
                else if (parts.Length == 3)
                {
                    this._major = int.Parse(parts[0]);
                    this._minor = int.Parse(parts[1]);
                    this._patch = int.Parse(parts[2]);
                }
                else
                {
                    throw new ArgumentException("Invalid version string");
                }
            }
            catch
            {
                this._major = 0;
                this._minor = 0;
                this._patch = 0;
                //this.version = "0.0.0";
            }
        }

        public override bool Equals(object? obj)
        {
            if (obj is SemanticVersion other)
            {
                return this._major == other._major && this._minor == other._minor && this._patch == other._patch;
            }
            else
            {
                return false;
            }
        }

        public override int GetHashCode()
        {
            return this._major.GetHashCode() ^ this._minor.GetHashCode() ^ this._patch.GetHashCode();
        }

        /// <summary>
        /// Use ToVersionString(string? prefix) instead if possible.
        /// </summary>
        /// <returns>major.minor.patch</returns>
        public override string ToString()
        {
            return this._version;
        }

        public string ToVersionString(string? prefix = null)
        {
            if (prefix == null)
            {
                return this._version;
            }
            else
            {
                return $"{prefix}{this._version}";
            }
        }

        public static bool operator ==(SemanticVersion v1, SemanticVersion v2)
        { return v1.Equals(v2); }

        public static bool operator !=(SemanticVersion v1, SemanticVersion v2)
        { return !v1.Equals(v2); }

        public static bool operator >=(SemanticVersion v1, SemanticVersion v2)
        { return v1.GreaterEquals(v2); }

        public static bool operator <=(SemanticVersion v1, SemanticVersion v2)
        { return v1.LessEquals(v2); }

        #region Private

        private bool GreaterEquals(SemanticVersion other)
        {
            if (this._major < other._major)
            {
                return false;
            }
            else if (this._major > other._major)
            {
                return true;
            }
            else
            {
                if (this._minor < other._minor)
                {
                    return false;
                }
                else if (this._minor > other._minor)
                {
                    return true;
                }
                else
                {
                    if (this._patch < other._patch)
                    {
                        return false;
                    }
                    else if (this._patch > other._patch)
                    {
                        return true;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
        }

        private bool LessEquals(SemanticVersion other)
        {
            if (this._major < other._major)
            {
                return true;
            }
            else if (this._major > other._major)
            {
                return false;
            }
            else
            {
                if (this._minor < other._minor)
                {
                    return true;
                }
                else if (this._minor > other._minor)
                {
                    return false;
                }
                else
                {
                    if (this._patch < other._patch)
                    {
                        return true;
                    }
                    else if (this._patch > other._patch)
                    {
                        return false;
                    }
                    else
                    {
                        return true;
                    }
                }
            }
        }

        #endregion Private
    }
}

namespace CrashDumpAnalyzer.Utilities
{
    public class SemanticVersion
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _micro;
        private readonly int _patch;
        private readonly int _buildType; // alpha/beta/rc/release, higher number is more stable
        private readonly string _version;

        public SemanticVersion(int major, int minor, int micro, int patch, int buildType)
        {
            this._major = major;
            this._minor = minor;
            this._micro = micro;
            this._patch = patch;
            this._version = $"{major}.{minor}.{micro}.{patch}";
            _buildType = buildType;
        }

        public SemanticVersion(string version, int buildType)
        {
            this._version = version.StartsWith('v') ? version.Substring(1) : version;
            this._major = 0;
            this._minor = 0;
            this._micro = 0;
            this._patch = 0;
            this._buildType = buildType;
            try
            {
                string[] parts = this._version.Split('.');
                if (parts.Length == 2)
                {
                    this._major = int.Parse(parts[0]);
                    this._minor = int.Parse(parts[1]);
                }
                else if (parts.Length == 3)
                {
                    this._major = int.Parse(parts[0]);
                    this._minor = int.Parse(parts[1]);
                    this._micro = int.Parse(parts[2]);
                }
                else if (parts.Length == 4)
                {
                    this._major = int.Parse(parts[0]);
                    this._minor = int.Parse(parts[1]);
                    this._micro = int.Parse(parts[2]);
                    this._patch = int.Parse(parts[3]);
                }
                else if (version.Length > 0)
                {
                    throw new ArgumentException("Invalid version string");
                }
            }
            catch
            {
                this._major = 0;
                this._minor = 0;
                this._micro = 0;
                this._patch = 0;
                this._buildType = -1;
            }
        }
        public bool isValid()
        {
            return this._major != 0 || this._minor != 0 || this._micro != 0 || this._patch != 0 || this._buildType != -1;
        }
        public override bool Equals(object? obj)
        {
            if (obj is SemanticVersion other)
            {
                return this._major == other._major && this._minor == other._minor && this._micro == other._micro && this._patch == other._patch && this._buildType == other._buildType;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return this._major.GetHashCode() ^ this._minor.GetHashCode() ^ this._micro.GetHashCode() ^ this._patch.GetHashCode() ^ this._buildType.GetHashCode();
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
        { return v1.Greater(v2, true); }
        public static bool operator >(SemanticVersion v1, SemanticVersion v2)
        { return v1.Greater(v2, false); }

        public static bool operator <=(SemanticVersion v1, SemanticVersion v2)
        { return v1.Less(v2, true); }
        public static bool operator <(SemanticVersion v1, SemanticVersion v2)
        { return v1.Less(v2, false); }

        #region Private

        private bool Greater(SemanticVersion other, bool orEqual)
        {
            if (this._major < other._major)
                return false;
            if (this._major > other._major)
                return true;
            if (this._minor < other._minor)
                return false;
            if (this._minor > other._minor)
                return true;
            if (this._micro < other._micro)
                return false;
            if (this._micro > other._micro)
                return true;
            if (this._buildType < other._buildType)
                return false;
            if (this._buildType > other._buildType)
                return true;
            if (this._patch < other._patch)
                return false;
            if (this._patch > other._patch)
                return true;
            return orEqual;
        }

        private bool Less(SemanticVersion other, bool orEqual)
        {
            if (this._major < other._major)
                return true;
            if (this._major > other._major)
                return false;
            if (this._minor < other._minor)
                return true;
            if (this._minor > other._minor)
                return false;
            if (this._micro < other._micro)
                return true;
            if (this._micro > other._micro)
                return false;
            if (this._buildType < other._buildType)
                return true;
            if (this._buildType > other._buildType)
                return false;
            if (this._patch < other._patch)
                return true;
            if (this._patch > other._patch)
                return false;
            return orEqual;
        }

        #endregion Private
    }
}

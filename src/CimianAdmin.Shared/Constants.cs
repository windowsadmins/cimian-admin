namespace CimianAdmin.Shared;

/// <summary>
/// Application-wide constants.
/// </summary>
public static class Constants
{
    /// <summary>
    /// Application name.
    /// </summary>
    public const string AppName = "CimianAdmin";

    /// <summary>
    /// Application version.
    /// </summary>
    public const string AppVersion = "0.1.0";

    /// <summary>
    /// Repository directory names.
    /// </summary>
    public static class RepositoryDirectories
    {
        public const string Catalogs = "catalogs";
        public const string Manifests = "manifests";
        public const string PkgsInfo = "pkgsinfo";
        public const string Pkgs = "pkgs";
    }

    /// <summary>
    /// File extensions.
    /// </summary>
    public static class FileExtensions
    {
        public const string Yaml = ".yaml";
        public const string Yml = ".yml";
        public const string Msi = ".msi";
        public const string Exe = ".exe";
        public const string Ps1 = ".ps1";
        public const string Nupkg = ".nupkg";
    }

    /// <summary>
    /// Installer types.
    /// </summary>
    public static class InstallerTypes
    {
        public const string Msi = "msi";
        public const string Exe = "exe";
        public const string Ps1 = "ps1";
        public const string Nupkg = "nupkg";
    }

    /// <summary>
    /// Detection types.
    /// </summary>
    public static class DetectionTypes
    {
        public const string File = "file";
        public const string Registry = "registry";
        public const string ProductCode = "product_code";
        public const string UpgradeCode = "upgrade_code";
        public const string Script = "script";
    }

    /// <summary>
    /// Comparison operators.
    /// </summary>
    public static class Operators
    {
        public const string Equal = "==";
        public const string NotEqual = "!=";
        public const string GreaterThan = ">";
        public const string GreaterThanOrEqual = ">=";
        public const string LessThan = "<";
        public const string LessThanOrEqual = "<=";
        public const string Contains = "contains";
        public const string StartsWith = "startswith";
        public const string EndsWith = "endswith";
        public const string Matches = "matches";
    }

    /// <summary>
    /// Common system facts for conditional items.
    /// </summary>
    public static class SystemFacts
    {
        public const string Hostname = "hostname";
        public const string OsVersion = "os_version";
        public const string OsBuild = "os_build";
        public const string Architecture = "architecture";
        public const string Domain = "domain";
        public const string Username = "username";
        public const string IpAddress = "ip_address";
        public const string SerialNumber = "serial_number";
    }
}

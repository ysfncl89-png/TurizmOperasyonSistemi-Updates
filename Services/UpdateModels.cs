using System;

namespace TurizmOperasyonSistemi.Services
{
    /// <summary>
    /// DTO describing an available update from manifest
    /// </summary>
    public class UpdateInfo
    {
        public string Version { get; set; }
        public string Channel { get; set; }
        public string ReleaseType { get; set; }
        public bool Mandatory { get; set; }
        public int? MinimumDatabaseVersion { get; set; }
        // If the release package explicitly contains database migration steps that will be applied
        // by the installer, set this to true in the manifest. When present this flag is trusted
        // by the client to allow update even if the local DB version is lower than MinimumDatabaseVersion.
        public bool? ContainsMigrations { get; set; }
        public string MinimumClientVersion { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string ReleaseNotes { get; set; }
        public string DownloadUrl { get; set; }
        public string InstallerType { get; set; }
        public string Checksum { get; set; }
    }

    /// <summary>
    /// Result of a download operation
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string FilePath { get; set; }
        public string ErrorMessage { get; set; }
    }
}
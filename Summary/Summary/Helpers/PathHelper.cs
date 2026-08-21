using System;
using System.IO;

namespace Summary.Helpers
{
    internal static class PathHelper
    {
        private static readonly string BasePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Summary");
        public static string DatabasePath => Path.Combine(BasePath, "Data");
        public static string LogsPath => Path.Combine(BasePath, "Logs");
        public static string RecordingsPath { get; set; } = Path.Combine(BasePath, "Recordings");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DatabasePath);
            Directory.CreateDirectory(LogsPath);
            Directory.CreateDirectory(RecordingsPath);
        }
    }
}
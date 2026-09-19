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
        public static string WhisperLargePath { get; set; } = Path.Combine(BasePath, "Models", "whisper-large");
        public static string WhisperSmallPath { get; set; } = Path.Combine(BasePath, "Models", "whisper-small");
        public static string OpusMtEnEsPath { get; set; } = Path.Combine(BasePath, "Models", "opus-mt-en-es");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DatabasePath);
            Directory.CreateDirectory(LogsPath);
            Directory.CreateDirectory(RecordingsPath);
            Directory.CreateDirectory(WhisperLargePath);
            Directory.CreateDirectory(WhisperSmallPath);
            Directory.CreateDirectory(OpusMtEnEsPath);
        }
    }
}
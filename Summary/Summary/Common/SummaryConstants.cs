using System.Reflection;

namespace Summary.Common
{
    internal static class SummaryConstants
    {
        public static readonly string OLLAMA_URI = "OLLAMA_URI";
        public static readonly string OLLAMA_MODEL = "OLLAMA_MODEL";
        public static readonly string WHISPER_MODEL = "WHISPER_MODEL";
        public static readonly string APP_NAME = "Summary";
        public static readonly string APP_VERSION = Assembly.GetExecutingAssembly().GetName().Version!.ToString();
    }
}
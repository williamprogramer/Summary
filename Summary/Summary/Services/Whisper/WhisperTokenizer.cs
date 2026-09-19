using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Summary.Services.Whisper
{
    internal sealed class WhisperTokenizer
    {
        public const int SotToken = 50258;
        public const int EotToken = 50257;
        public const int TranscribeToken = 50359;
        public const int NoTimestampsToken = 50363;
        public const int MaxLength = 448;
        public const int SpecialTokenStart = 50257;

        private readonly Dictionary<int, string> _idToToken;
        private readonly Dictionary<char, byte> _unicodeToByte;
        private readonly Dictionary<string, int> _langToId;
        private readonly HashSet<int> _languageTokenIds;
        private readonly HashSet<int> _suppressTokens;

        public WhisperTokenizer(string modelDir)
        {
            _idToToken = LoadVocab(Path.Combine(modelDir, "vocab.json"));
            MergeAddedTokens(_idToToken, Path.Combine(modelDir, "added_tokens.json"));
            _unicodeToByte = BuildUnicodeToByte();
            _langToId = LoadLanguageMap(Path.Combine(modelDir, "generation_config.json"));
            _languageTokenIds = [.. _langToId.Values];
            _suppressTokens = LoadSuppressTokens(Path.Combine(modelDir, "generation_config.json"));
        }

        public IReadOnlySet<int> LanguageTokenIds => _languageTokenIds;
        public IReadOnlySet<int> SuppressTokens => _suppressTokens;

        public bool TryGetLanguageToken(string language, out int tokenId)
        {
            tokenId = 0;
            if (string.IsNullOrWhiteSpace(language))
                return false;

            if (_langToId.TryGetValue(language, out tokenId))
                return true;

            string wrapped = language.StartsWith("<|", StringComparison.Ordinal)
                ? language
                : $"<|{language}|>";
            return _langToId.TryGetValue(wrapped, out tokenId);
        }

        public string Decode(IReadOnlyList<int> tokenIds)
        {
            StringBuilder pieces = new();
            foreach (int id in tokenIds)
            {
                if (id >= SpecialTokenStart)
                    continue;
                if (_idToToken.TryGetValue(id, out string? token))
                    pieces.Append(token);
            }

            return ByteLevelDecode(pieces.ToString());
        }

        private string ByteLevelDecode(string tokenString)
        {
            List<byte> bytes = new(tokenString.Length);
            foreach (char ch in tokenString)
            {
                if (_unicodeToByte.TryGetValue(ch, out byte b))
                    bytes.Add(b);
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }

        private static Dictionary<int, string> LoadVocab(string path)
        {
            Dictionary<int, string> idToToken = new();
            using FileStream stream = File.OpenRead(path);
            Dictionary<string, int>? vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(stream);
            if (vocab is null)
                return idToToken;

            foreach ((string token, int id) in vocab)
                idToToken[id] = token;
            return idToToken;
        }

        private static void MergeAddedTokens(Dictionary<int, string> idToToken, string path)
        {
            if (!File.Exists(path))
                return;

            using FileStream stream = File.OpenRead(path);
            Dictionary<string, int>? added = JsonSerializer.Deserialize<Dictionary<string, int>>(stream);
            if (added is null)
                return;

            foreach ((string token, int id) in added)
                idToToken[id] = token;
        }

        private static Dictionary<string, int> LoadLanguageMap(string path)
        {
            Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("lang_to_id", out JsonElement element))
                return map;

            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.TryGetInt32(out int id))
                    map[property.Name] = id;
            }

            return map;
        }

        private static HashSet<int> LoadSuppressTokens(string path)
        {
            HashSet<int> ids = new();
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("suppress_tokens", out JsonElement list))
                return ids;

            foreach (JsonElement item in list.EnumerateArray())
            {
                if (item.TryGetInt32(out int id))
                    ids.Add(id);
            }

            return ids;
        }

        private static Dictionary<char, byte> BuildUnicodeToByte()
        {
            List<int> bs = new();
            for (int i = '!'; i <= '~'; i++)
                bs.Add(i);
            for (int i = '¡'; i <= '¬'; i++)
                bs.Add(i);
            for (int i = '®'; i <= 'ÿ'; i++)
                bs.Add(i);

            List<int> cs = new(bs);
            int n = 0;
            for (int b = 0; b < 256; b++)
            {
                if (!bs.Contains(b))
                {
                    bs.Add(b);
                    cs.Add(256 + n);
                    n++;
                }
            }

            Dictionary<char, byte> map = new();
            for (int i = 0; i < bs.Count; i++)
                map[(char)cs[i]] = (byte)bs[i];
            return map;
        }
    }
}

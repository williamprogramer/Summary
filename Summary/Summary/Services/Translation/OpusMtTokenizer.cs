using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Summary.Services.Translation
{
    internal sealed class OpusMtTokenizer
    {
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        private const float UnkPenalty = -20f;

        private readonly Dictionary<int, string> _idToToken;
        private readonly HashSet<int> _specialIds;
        private readonly TrieNode _root;
        private readonly int _unkId;
        private readonly int _eosId;

        private OpusMtTokenizer(
            Dictionary<int, string> idToToken,
            HashSet<int> specialIds,
            TrieNode root,
            int unkId,
            int eosId)
        {
            _idToToken = idToToken;
            _specialIds = specialIds;
            _root = root;
            _unkId = unkId;
            _eosId = eosId;
        }

        public static OpusMtTokenizer Load(string modelDir, int eosId, int unkId)
        {
            string path = Path.Combine(modelDir, "tokenizer.json");
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;

            Dictionary<int, string> idToToken = new();
            HashSet<int> specialIds = new();
            TrieNode trie = new();
            int resolvedUnk = unkId;

            if (root.TryGetProperty("added_tokens", out JsonElement added) && added.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement token in added.EnumerateArray())
                {
                    if (!token.TryGetProperty("id", out JsonElement idEl) || !idEl.TryGetInt32(out int id))
                        continue;
                    string content = token.TryGetProperty("content", out JsonElement contentEl)
                        ? contentEl.GetString() ?? string.Empty
                        : string.Empty;
                    idToToken[id] = content;
                    if (token.TryGetProperty("special", out JsonElement specialEl) && specialEl.ValueKind == JsonValueKind.True)
                        specialIds.Add(id);
                    if (content is "<unk>" or "[UNK]")
                        resolvedUnk = id;
                }
            }

            JsonElement vocab = root.GetProperty("model").GetProperty("vocab");
            int index = 0;
            foreach (JsonElement pair in vocab.EnumerateArray())
            {
                string piece = pair[0].GetString() ?? string.Empty;
                float score = pair[1].ValueKind == JsonValueKind.Number ? pair[1].GetSingle() : 0f;
                idToToken[index] = piece;
                if (piece is "<unk>" or "[UNK]")
                    resolvedUnk = index;

                bool isSpecial = specialIds.Contains(index) || piece is "</s>" or "<unk>" or "<pad>" or "[UNK]";
                if (!isSpecial)
                    Insert(trie, piece, index, score);

                index++;
            }

            return new OpusMtTokenizer(idToToken, specialIds, trie, resolvedUnk, eosId);
        }

        public int[] Encode(string text)
        {
            string normalized = Normalize(text);
            List<int> ids = [];
            foreach (string word in Whitespace.Split(normalized))
            {
                if (word.Length == 0)
                    continue;
                ids.AddRange(EncodePiece("\u2581" + word));
            }

            ids.Add(_eosId);
            return [.. ids];
        }

        public string Decode(IReadOnlyList<int> tokenIds)
        {
            StringBuilder pieces = new();
            foreach (int id in tokenIds)
            {
                if (id == _eosId || _specialIds.Contains(id) && id != _unkId)
                    continue;
                if (_idToToken.TryGetValue(id, out string? piece))
                    pieces.Append(piece);
            }

            string text = pieces.ToString().Replace('\u2581', ' ').Trim();
            return Regex.Replace(text, @"\s+", " ");
        }

        private static string Normalize(string text)
        {
            string replaced = text.Replace("``", "\"", StringComparison.Ordinal)
                .Replace("''", "\"", StringComparison.Ordinal);
            return replaced.Normalize(NormalizationForm.FormKC).ToLower(CultureInfo.InvariantCulture);
        }

        private List<int> EncodePiece(string text)
        {
            int n = text.Length;
            if (n == 0)
                return [];

            float[] best = new float[n + 1];
            int[] prev = new int[n + 1];
            int[] tokenAt = new int[n + 1];
            Array.Fill(best, float.NegativeInfinity);
            Array.Fill(prev, -1);
            Array.Fill(tokenAt, _unkId);
            best[0] = 0f;

            for (int i = 0; i < n; i++)
            {
                if (float.IsNegativeInfinity(best[i]))
                    continue;

                TrieNode node = _root;
                bool anyPiece = false;
                for (int j = i; j < n; j++)
                {
                    if (!node.Children.TryGetValue(text[j], out TrieNode? next))
                        break;
                    node = next;
                    if (node.TokenId < 0)
                        continue;

                    anyPiece = true;
                    float candidate = best[i] + node.Score;
                    if (candidate > best[j + 1])
                    {
                        best[j + 1] = candidate;
                        prev[j + 1] = i;
                        tokenAt[j + 1] = node.TokenId;
                    }
                }

                if (!anyPiece)
                {
                    float unkCandidate = best[i] + UnkPenalty;
                    if (unkCandidate > best[i + 1])
                    {
                        best[i + 1] = unkCandidate;
                        prev[i + 1] = i;
                        tokenAt[i + 1] = _unkId;
                    }
                }
            }

            if (float.IsNegativeInfinity(best[n]))
                return Enumerable.Repeat(_unkId, n).ToList();

            List<int> tokens = new();
            for (int i = n; i > 0; i = prev[i])
                tokens.Add(tokenAt[i]);
            tokens.Reverse();
            return tokens;
        }

        private static void Insert(TrieNode root, string piece, int id, float score)
        {
            TrieNode node = root;
            foreach (char ch in piece)
            {
                if (!node.Children.TryGetValue(ch, out TrieNode? next))
                {
                    next = new TrieNode();
                    node.Children[ch] = next;
                }

                node = next;
            }

            node.TokenId = id;
            node.Score = score;
        }

        private sealed class TrieNode
        {
            public Dictionary<char, TrieNode> Children { get; } = [];
            public int TokenId { get; set; } = -1;
            public float Score { get; set; }
        }
    }
}

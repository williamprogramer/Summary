using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Summary.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Summary.Services.Translation
{
    public sealed class OpusMtTranslator : IDisposable
    {
        private const int LiveMaxNewTokens = 96;

        private readonly ILogger<OpusMtTranslator> _logger;
        private readonly string _modelDir;
        private readonly object _gate = new();

        private InferenceSession? _encoder;
        private InferenceSession? _decoder;
        private OpusMtTokenizer? _tokenizer;
        private int _eosId = 0;
        private int _padId = 65000;
        private int _decoderStartId = 65000;
        private int _unkId = 1;
        private int _maxLength = 512;
        private HashSet<int> _badWords = [65000];
        private bool _disposed;

        public OpusMtTranslator(ILogger<OpusMtTranslator> logger)
        {
            _logger = logger;
            _modelDir = PathHelper.OpusMtEnEsPath;
        }

        public string Translate(string englishText)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (string.IsNullOrWhiteSpace(englishText))
                return string.Empty;

            EnsureLoaded();
            int[] inputIds = _tokenizer!.Encode(englishText.Trim());
            if (inputIds.Length == 0)
                return string.Empty;

            DenseTensor<long> encoderIds = ToInputIds(inputIds);
            DenseTensor<long> encoderMask = Ones(inputIds.Length);

            List<NamedOnnxValue> encoderInputs = BuildEncoderInputs(encoderIds, encoderMask);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderResults = _encoder!.Run(encoderInputs);
            DenseTensor<float> hidden = CopyTensor(FindOutput(encoderResults, "last_hidden_state", "hidden"));

            List<int> tokens = [_decoderStartId];
            int maxSteps = Math.Min(_maxLength, LiveMaxNewTokens);
            int repeatCount = 0;
            int lastToken = -1;
            for (int step = 0; step < maxSteps; step++)
            {
                DenseTensor<long> prefixIds = ToInputIds(tokens);
                (int nextToken, _) = RunDecoder(_decoder!, prefixIds, hidden, encoderMask, past: null, takeLast: true);
                if (nextToken == _eosId)
                    break;

                tokens.Add(nextToken);
                if (nextToken == lastToken)
                {
                    repeatCount++;
                    if (repeatCount >= 2)
                        break;
                }
                else
                {
                    repeatCount = 0;
                    lastToken = nextToken;
                }

                if (HasRepeatedNgram(tokens, 2) || HasRepeatedNgram(tokens, 3))
                    break;
            }

            return _tokenizer.Decode(tokens);
        }

        private static bool HasRepeatedNgram(IReadOnlyList<int> tokens, int n)
        {
            int generated = tokens.Count - 1;
            if (n <= 0 || generated < n * 2)
                return false;

            int end = tokens.Count;
            for (int i = 0; i < n; i++)
            {
                if (tokens[end - n + i] != tokens[end - (2 * n) + i])
                    return false;
            }

            return true;
        }

        private void EnsureLoaded()
        {
            if (_encoder is not null)
                return;

            lock (_gate)
            {
                if (_encoder is not null)
                    return;

                if (!Directory.Exists(_modelDir))
                    throw new DirectoryNotFoundException($"opus-mt model folder not found: {_modelDir}");

                LoadGenerationConfig();
                _tokenizer = OpusMtTokenizer.Load(_modelDir, _eosId, _unkId);

                SessionOptions options = new()
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                _encoder = new InferenceSession(Path.Combine(_modelDir, "encoder_model_quantized.onnx"), options);
                _decoder = new InferenceSession(Path.Combine(_modelDir, "decoder_model_quantized.onnx"), options);

                _logger.LogInformation(
                    "opus-mt ONNX sessions loaded. Encoder inputs: {EncoderIn}; Decoder inputs: {DecoderIn}",
                    string.Join(", ", _encoder.InputMetadata.Keys),
                    string.Join(", ", _decoder.InputMetadata.Keys));
            }
        }

        public void EnsureReady()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();
        }

        private void LoadGenerationConfig()
        {
            string path = Path.Combine(_modelDir, "generation_config.json");
            if (!File.Exists(path))
                path = Path.Combine(_modelDir, "config.json");
            if (!File.Exists(path))
                return;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("eos_token_id", out JsonElement eos) && eos.TryGetInt32(out int eosId))
                _eosId = eosId;
            if (root.TryGetProperty("pad_token_id", out JsonElement pad) && pad.TryGetInt32(out int padId))
                _padId = padId;
            if (root.TryGetProperty("decoder_start_token_id", out JsonElement start) && start.TryGetInt32(out int startId))
                _decoderStartId = startId;
            if (root.TryGetProperty("unk_token_id", out JsonElement unk) && unk.TryGetInt32(out int unkId))
                _unkId = unkId;
            if (root.TryGetProperty("max_length", out JsonElement max) && max.TryGetInt32(out int maxLength))
                _maxLength = maxLength;

            _badWords = [_padId];
            if (root.TryGetProperty("bad_words_ids", out JsonElement bad) && bad.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in bad.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0 && item[0].TryGetInt32(out int id))
                        _badWords.Add(id);
                    else if (item.TryGetInt32(out int scalar))
                        _badWords.Add(scalar);
                }
            }
        }

        private List<NamedOnnxValue> BuildEncoderInputs(DenseTensor<long> inputIds, DenseTensor<long> mask)
        {
            List<NamedOnnxValue> inputs = new();
            foreach (string name in _encoder!.InputMetadata.Keys)
            {
                if (name.Contains("input_ids", StringComparison.OrdinalIgnoreCase))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, inputIds));
                else if (name.Contains("mask", StringComparison.OrdinalIgnoreCase))
                    inputs.Add(CreateMask(name, _encoder.InputMetadata[name], inputIds, null, mask));
            }

            return inputs;
        }

        private (int NextToken, Dictionary<string, DenseTensor<float>> Past) RunDecoder(
            InferenceSession session,
            DenseTensor<long> inputIds,
            DenseTensor<float> encoderHidden,
            DenseTensor<long> encoderMask,
            Dictionary<string, DenseTensor<float>>? past,
            bool takeLast)
        {
            List<NamedOnnxValue> inputs = BuildDecoderInputs(session, inputIds, encoderHidden, encoderMask, past);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);
            Tensor<float> logits = FindOutput(results, "logits").AsTensor<float>();
            int next = ArgMaxLast(logits, takeLast);
            Dictionary<string, DenseTensor<float>> nextPast = CollectPast(results, past);
            return (next, nextPast);
        }

        private List<NamedOnnxValue> BuildDecoderInputs(
            InferenceSession session,
            DenseTensor<long> inputIds,
            DenseTensor<float> encoderHidden,
            DenseTensor<long> encoderMask,
            Dictionary<string, DenseTensor<float>>? past)
        {
            List<NamedOnnxValue> inputs = new();
            foreach (string name in session.InputMetadata.Keys)
            {
                if (name.Contains("input_ids", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, inputIds));
                    continue;
                }

                if (name.Contains("encoder_hidden", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("encoder_outputs", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, encoderHidden));
                    continue;
                }

                if (name.Contains("mask", StringComparison.OrdinalIgnoreCase))
                {
                    inputs.Add(CreateMask(name, session.InputMetadata[name], inputIds, encoderHidden, encoderMask));
                    continue;
                }

                if (past is null)
                    continue;

                if (past.TryGetValue(name, out DenseTensor<float>? tensor))
                    inputs.Add(NamedOnnxValue.CreateFromTensor(name, tensor));
            }

            return inputs;
        }

        private static NamedOnnxValue CreateMask(
            string name,
            NodeMetadata meta,
            DenseTensor<long> inputIds,
            DenseTensor<float>? encoderHidden,
            DenseTensor<long> encoderMask)
        {
            if (name.Contains("encoder", StringComparison.OrdinalIgnoreCase))
            {
                if (meta.ElementType == typeof(long) || meta.ElementType == typeof(int))
                    return NamedOnnxValue.CreateFromTensor(name, encoderMask);
            }

            int[] dims;
            if (meta.Dimensions.Length > 0 && meta.Dimensions.All(d => d > 0))
                dims = meta.Dimensions.ToArray();
            else if (name.Contains("encoder", StringComparison.OrdinalIgnoreCase) && encoderHidden is not null)
                dims = [1, encoderHidden.Dimensions[1]];
            else
                dims = [1, inputIds.Dimensions[1]];

            int count = dims.Aggregate(1, (a, b) => a * b);
            Type elementType = meta.ElementType;
            if (elementType == typeof(long))
            {
                long[] data = new long[count];
                Array.Fill(data, 1L);
                return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<long>(data, dims));
            }

            if (elementType == typeof(int))
            {
                int[] data = new int[count];
                Array.Fill(data, 1);
                return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<int>(data, dims));
            }

            float[] values = new float[count];
            Array.Fill(values, 1f);
            return NamedOnnxValue.CreateFromTensor(name, new DenseTensor<float>(values, dims));
        }

        private int ArgMaxLast(Tensor<float> logits, bool takeLast)
        {
            int rank = logits.Dimensions.Length;
            int vocab = logits.Dimensions[rank - 1];
            int seq = rank >= 2 ? logits.Dimensions[rank - 2] : 1;
            int tokenIndex = takeLast ? seq - 1 : 0;

            int best = _eosId;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < vocab; i++)
            {
                if (_badWords.Contains(i))
                    continue;

                float score = GetLogit(logits, rank, tokenIndex, i);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return bestScore == float.NegativeInfinity ? _eosId : best;
        }

        private Dictionary<string, DenseTensor<float>> CollectPast(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            Dictionary<string, DenseTensor<float>>? previous)
        {
            Dictionary<string, DenseTensor<float>> next = new(StringComparer.Ordinal);
            if (previous is not null)
            {
                foreach ((string key, DenseTensor<float> value) in previous)
                {
                    if (key.Contains("encoder", StringComparison.OrdinalIgnoreCase))
                        next[key] = value;
                }
            }

            foreach (DisposableNamedOnnxValue output in results)
            {
                if (output.Name.Contains("logits", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isEncoder = output.Name.Contains("encoder", StringComparison.OrdinalIgnoreCase);
                if (isEncoder && next.ContainsKey(output.Name))
                    continue;

                DenseTensor<float> copy = CopyTensor(output);
                StorePast(next, output.Name, copy);
            }

            return next;
        }

        private static void StorePast(Dictionary<string, DenseTensor<float>> past, string outputName, DenseTensor<float> tensor)
        {
            past[outputName] = tensor;
            if (outputName.StartsWith("present.", StringComparison.Ordinal))
                past["past_key_values." + outputName["present.".Length..]] = tensor;
            else if (outputName.StartsWith("present_key_values.", StringComparison.Ordinal))
                past["past_key_values." + outputName["present_key_values.".Length..]] = tensor;
        }

        private static float GetLogit(Tensor<float> logits, int rank, int tokenIndex, int vocabIndex)
        {
            if (rank >= 3)
                return logits[0, tokenIndex, vocabIndex];
            if (rank == 2)
                return logits[tokenIndex, vocabIndex];
            return logits[vocabIndex];
        }

        private static DenseTensor<long> ToInputIds(IReadOnlyList<int> tokens)
        {
            long[] data = new long[tokens.Count];
            for (int i = 0; i < tokens.Count; i++)
                data[i] = tokens[i];
            return new DenseTensor<long>(data, [1, tokens.Count]);
        }

        private static DenseTensor<long> Ones(int length)
        {
            long[] data = new long[length];
            Array.Fill(data, 1L);
            return new DenseTensor<long>(data, [1, length]);
        }

        private static DisposableNamedOnnxValue FindOutput(
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results,
            params string[] hints)
        {
            foreach (string hint in hints)
            {
                DisposableNamedOnnxValue? match = results.FirstOrDefault(r =>
                    r.Name.Equals(hint, StringComparison.OrdinalIgnoreCase) ||
                    r.Name.Contains(hint, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                    return match;
            }

            return results.First();
        }

        private static DenseTensor<float> CopyTensor(DisposableNamedOnnxValue value)
        {
            Tensor<float> tensor = value.AsTensor<float>();
            int[] dims = tensor.Dimensions.ToArray();
            return new DenseTensor<float>(tensor.ToArray(), dims);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _encoder?.Dispose();
            _decoder?.Dispose();
        }
    }
}

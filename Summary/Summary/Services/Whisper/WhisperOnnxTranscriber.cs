using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Summary.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Summary.Services.Whisper
{
    public sealed class WhisperOnnxTranscriber : IDisposable
    {
        private readonly ILogger<WhisperOnnxTranscriber> _logger;
        private readonly string _modelDir;
        private readonly object _gate = new();

        private InferenceSession? _encoder;
        private InferenceSession? _decoder;
        private InferenceSession? _decoderPast;
        private WhisperTokenizer? _tokenizer;
        private bool _disposed;

        public WhisperOnnxTranscriber(ILogger<WhisperOnnxTranscriber> logger)
        {
            _logger = logger;
            _modelDir = PathHelper.WhisperLargePath;
        }

        public string Transcribe(string wavPath)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            EnsureLoaded();

            float[] audio = WhisperFeatureExtractor.LoadMono16k(wavPath);
            if (audio.Length == 0)
                return string.Empty;

            StringBuilder text = new();
            const int chunk = WhisperFeatureExtractor.NSamples;
            for (int offset = 0; offset < audio.Length; offset += chunk)
            {
                int length = Math.Min(chunk, audio.Length - offset);
                if (offset > 0 && length < WhisperFeatureExtractor.SampleRate)
                    break;

                float[] slice = audio.AsSpan(offset, length).ToArray();
                string chunkText = TranscribeChunk(slice);
                if (chunkText.Length == 0)
                    continue;
                if (text.Length > 0 && !char.IsWhiteSpace(chunkText[0]))
                    text.Append(' ');
                text.Append(chunkText);
            }

            return text.ToString().Trim();
        }

        private string TranscribeChunk(float[] audio)
        {
            float[] mel = WhisperFeatureExtractor.ComputeLogMel(audio);
            DenseTensor<float> melTensor = new(mel, [1, WhisperFeatureExtractor.NMels, WhisperFeatureExtractor.NFrames]);

            string encoderInput = _encoder!.InputMetadata.Keys.First();
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderResults =
                _encoder.Run([NamedOnnxValue.CreateFromTensor(encoderInput, melTensor)]);

            DenseTensor<float> hidden = CopyTensor(FindOutput(encoderResults, "last_hidden_state", "hidden"));

            int languageToken = DetectLanguage(hidden);
            List<int> tokens =
            [
                WhisperTokenizer.SotToken,
                languageToken,
                WhisperTokenizer.TranscribeToken,
                WhisperTokenizer.NoTimestampsToken
            ];

            Dictionary<string, DenseTensor<float>>? past = null;
            DenseTensor<long> prefixIds = ToInputIds(tokens);
            (int nextToken, past) = RunDecoder(_decoder!, prefixIds, hidden, past, takeLast: true);

            HashSet<int> suppress = new(_tokenizer!.SuppressTokens);
            for (int step = 0; step < WhisperTokenizer.MaxLength - tokens.Count; step++)
            {
                if (nextToken == WhisperTokenizer.EotToken)
                    break;

                tokens.Add(nextToken);
                DenseTensor<long> stepIds = ToInputIds([nextToken]);
                (nextToken, past) = RunDecoder(_decoderPast!, stepIds, hidden, past, takeLast: true, suppress);
            }

            return _tokenizer.Decode(tokens);
        }

        private int DetectLanguage(DenseTensor<float> encoderHidden)
        {
            DenseTensor<long> sot = ToInputIds([WhisperTokenizer.SotToken]);
            (int predicted, _) = RunDecoder(_decoder!, sot, encoderHidden, past: null, takeLast: true, suppress: null, constrainToLanguage: true);
            if (_tokenizer!.LanguageTokenIds.Contains(predicted))
                return predicted;

            return _tokenizer.LanguageTokenIds.Contains(50262) ? 50262 : _tokenizer.LanguageTokenIds.First();
        }

        private (int NextToken, Dictionary<string, DenseTensor<float>> Past) RunDecoder(
            InferenceSession session,
            DenseTensor<long> inputIds,
            DenseTensor<float> encoderHidden,
            Dictionary<string, DenseTensor<float>>? past,
            bool takeLast,
            IReadOnlySet<int>? suppress = null,
            bool constrainToLanguage = false)
        {
            List<NamedOnnxValue> inputs = BuildDecoderInputs(session, inputIds, encoderHidden, past);
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = session.Run(inputs);

            Tensor<float> logits = FindOutput(results, "logits").AsTensor<float>();
            int next = ArgMaxLast(logits, takeLast, suppress, constrainToLanguage);
            Dictionary<string, DenseTensor<float>> nextPast = CollectPast(results, past);
            return (next, nextPast);
        }

        private List<NamedOnnxValue> BuildDecoderInputs(
            InferenceSession session,
            DenseTensor<long> inputIds,
            DenseTensor<float> encoderHidden,
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
                    inputs.Add(CreateMask(name, session.InputMetadata[name], inputIds, encoderHidden));
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
            DenseTensor<float> encoderHidden)
        {
            int[] dims;
            if (meta.Dimensions.Length > 0 && meta.Dimensions.All(d => d > 0))
                dims = meta.Dimensions.ToArray();
            else if (name.Contains("encoder", StringComparison.OrdinalIgnoreCase))
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

        private int ArgMaxLast(
            Tensor<float> logits,
            bool takeLast,
            IReadOnlySet<int>? suppress,
            bool constrainToLanguage)
        {
            int rank = logits.Dimensions.Length;
            int vocab = logits.Dimensions[rank - 1];
            int seq = rank >= 2 ? logits.Dimensions[rank - 2] : 1;
            int tokenIndex = takeLast ? seq - 1 : 0;

            int best = 0;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < vocab; i++)
            {
                if (constrainToLanguage && !_tokenizer!.LanguageTokenIds.Contains(i))
                    continue;
                if (!constrainToLanguage && suppress is not null && suppress.Contains(i))
                    continue;

                float score = GetLogit(logits, rank, tokenIndex, i);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (bestScore == float.NegativeInfinity)
                return WhisperTokenizer.EotToken;
            return best;
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

        private void EnsureLoaded()
        {
            if (_encoder is not null)
                return;

            lock (_gate)
            {
                if (_encoder is not null)
                    return;

                if (!Directory.Exists(_modelDir))
                    throw new DirectoryNotFoundException($"Whisper model folder not found: {_modelDir}");

                _tokenizer = new WhisperTokenizer(_modelDir);

                SessionOptions options = new()
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
                };

                _encoder = new InferenceSession(Path.Combine(_modelDir, "encoder_model_quantized.onnx"), options);
                _decoder = new InferenceSession(Path.Combine(_modelDir, "decoder_model_quantized.onnx"), options);
                _decoderPast = new InferenceSession(Path.Combine(_modelDir, "decoder_with_past_model_quantized.onnx"), options);

                _logger.LogInformation(
                    "Whisper ONNX sessions loaded. Encoder inputs: {EncoderIn}; Decoder inputs: {DecoderIn}; DecoderPast inputs: {PastIn}",
                    string.Join(", ", _encoder.InputMetadata.Keys),
                    string.Join(", ", _decoder.InputMetadata.Keys),
                    string.Join(", ", _decoderPast.InputMetadata.Keys));
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _encoder?.Dispose();
            _decoder?.Dispose();
            _decoderPast?.Dispose();
        }
    }
}

using Microsoft.Extensions.Logging;
using Summary.Helpers;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services
{
    public sealed class LiveModelDownloadService
    {
        private const string WhisperSmallBaseUrl = "https://huggingface.co/Xenova/whisper-small/resolve/main/";
        private const string OpusMtBaseUrl = "https://huggingface.co/Xenova/opus-mt-en-es/resolve/main/";

        internal static readonly (string UrlPath, string FileName)[] WhisperSmallFiles =
        [
            ("vocab.json", "vocab.json"),
            ("tokenizer.json", "tokenizer.json"),
            ("tokenizer_config.json", "tokenizer_config.json"),
            ("special_tokens_map.json", "special_tokens_map.json"),
            ("added_tokens.json", "added_tokens.json"),
            ("merges.txt", "merges.txt"),
            ("config.json", "config.json"),
            ("generation_config.json", "generation_config.json"),
            ("preprocessor_config.json", "preprocessor_config.json"),
            ("onnx/encoder_model_quantized.onnx", "encoder_model_quantized.onnx"),
            ("onnx/decoder_model_quantized.onnx", "decoder_model_quantized.onnx"),
            ("onnx/decoder_with_past_model_quantized.onnx", "decoder_with_past_model_quantized.onnx")
        ];

        internal static readonly (string UrlPath, string FileName)[] OpusMtFiles =
        [
            ("source.spm", "source.spm"),
            ("target.spm", "target.spm"),
            ("tokenizer.json", "tokenizer.json"),
            ("tokenizer_config.json", "tokenizer_config.json"),
            ("vocab.json", "vocab.json"),
            ("special_tokens_map.json", "special_tokens_map.json"),
            ("config.json", "config.json"),
            ("generation_config.json", "generation_config.json"),
            ("onnx/encoder_model_quantized.onnx", "encoder_model_quantized.onnx"),
            ("onnx/decoder_model_quantized.onnx", "decoder_model_quantized.onnx"),
            ("onnx/decoder_with_past_model_quantized.onnx", "decoder_with_past_model_quantized.onnx")
        ];

        private readonly HuggingFaceModelDownloadService _downloader;
        private readonly ILogger<LiveModelDownloadService> _logger;

        public LiveModelDownloadService(
            HuggingFaceModelDownloadService downloader,
            ILogger<LiveModelDownloadService> logger)
        {
            _downloader = downloader;
            _logger = logger;
        }

        public bool AllFilesPresent() =>
            HuggingFaceModelDownloadService.AllFilesPresent(PathHelper.WhisperSmallPath, WhisperSmallFiles) &&
            HuggingFaceModelDownloadService.AllFilesPresent(PathHelper.OpusMtEnEsPath, OpusMtFiles);

        public Task EnsureModelsAsync(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Ensuring live translation model files.");
            return _downloader.EnsureFilesAsync(
            [
                new HuggingFaceModelSpec(WhisperSmallBaseUrl, PathHelper.WhisperSmallPath, WhisperSmallFiles, "Whisper small"),
                new HuggingFaceModelSpec(OpusMtBaseUrl, PathHelper.OpusMtEnEsPath, OpusMtFiles, "opus-mt-en-es")
            ], cancellationToken);
        }
    }
}

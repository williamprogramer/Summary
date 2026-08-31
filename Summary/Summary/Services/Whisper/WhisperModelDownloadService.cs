using Microsoft.Extensions.Logging;
using Summary.Helpers;
using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services.Whisper
{
    public sealed class WhisperModelDownloadService
    {
        public const string HttpClientName = "HuggingFace";
        private const string BaseUrl = "https://huggingface.co/Xenova/whisper-large/resolve/main/";
        private const string DownloadingMessage = "Downloading the model…";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BusyService _busy;
        private readonly ILogger<WhisperModelDownloadService> _logger;

        private static readonly (string UrlPath, string FileName)[] Files =
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

        public WhisperModelDownloadService(
            IHttpClientFactory httpClientFactory,
            BusyService busy,
            ILogger<WhisperModelDownloadService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _busy = busy;
            _logger = logger;
        }

        public async Task EnsureModelsAsync(CancellationToken cancellationToken = default)
        {
            if (AllFilesPresent())
            {
                _logger.LogInformation("Whisper large model files already present at {Path}.", PathHelper.WhisperLargePath);
                return;
            }

            await SetBusyAsync(true, DownloadingMessage);
            try
            {
                Directory.CreateDirectory(PathHelper.WhisperLargePath);
                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

                foreach ((string urlPath, string fileName) in Files)
                {
                    string destination = Path.Combine(PathHelper.WhisperLargePath, fileName);
                    if (IsComplete(destination))
                        continue;

                    await SetBusyAsync(true, $"{DownloadingMessage} {fileName}");
                    await DownloadFileAsync(client, urlPath, destination, cancellationToken);
                }

                _logger.LogInformation("Whisper large model download completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download Whisper large model files.");
                await SetBusyAsync(true, $"Error al descargar el modelo: {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken);
            }
            finally
            {
                await SetBusyAsync(false, string.Empty);
            }
        }

        private static bool AllFilesPresent()
        {
            foreach ((_, string fileName) in Files)
            {
                if (!IsComplete(Path.Combine(PathHelper.WhisperLargePath, fileName)))
                    return false;
            }
            return true;
        }

        private static bool IsComplete(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

        private static async Task DownloadFileAsync(HttpClient client, string urlPath, string destination, CancellationToken cancellationToken)
        {
            string url = BaseUrl + urlPath;
            string partial = destination + ".partial";
            if (File.Exists(partial))
                File.Delete(partial);
            if (File.Exists(destination) && new FileInfo(destination).Length == 0)
                File.Delete(destination);

            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (FileStream target = new(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            File.Move(partial, destination, overwrite: true);
        }

        private Task SetBusyAsync(bool isBusy, string message)
        {
            TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            bool queued = App.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    _busy.StatusMessage = message;
                    _busy.IsBusy = isBusy;
                    tcs.TrySetResult();
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            if (!queued)
                tcs.TrySetResult();

            return tcs.Task;
        }
    }
}

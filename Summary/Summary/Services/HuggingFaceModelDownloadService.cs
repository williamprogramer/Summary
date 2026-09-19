using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services
{
    public sealed class HuggingFaceModelDownloadService
    {
        public const string HttpClientName = "HuggingFace";
        public const string DownloadingMessage = "Descargando el modelo…";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly BusyService _busy;
        private readonly ILogger<HuggingFaceModelDownloadService> _logger;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public HuggingFaceModelDownloadService(
            IHttpClientFactory httpClientFactory,
            BusyService busy,
            ILogger<HuggingFaceModelDownloadService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _busy = busy;
            _logger = logger;
        }

        public static bool AllFilesPresent(string destinationDir, IReadOnlyList<(string UrlPath, string FileName)> files)
        {
            foreach ((_, string fileName) in files)
            {
                if (!IsComplete(Path.Combine(destinationDir, fileName)))
                    return false;
            }

            return true;
        }

        public Task EnsureFilesAsync(
            string baseUrl,
            string destinationDir,
            IReadOnlyList<(string UrlPath, string FileName)> files,
            string modelLabel,
            CancellationToken cancellationToken = default)
        {
            return EnsureFilesAsync(
            [
                new HuggingFaceModelSpec(baseUrl, destinationDir, files, modelLabel)
            ], cancellationToken);
        }

        public async Task EnsureFilesAsync(
            IReadOnlyList<HuggingFaceModelSpec> models,
            CancellationToken cancellationToken = default)
        {
            if (models.Count == 0)
                return;

            bool allPresent = true;
            foreach (HuggingFaceModelSpec model in models)
            {
                if (!AllFilesPresent(model.DestinationDir, model.Files))
                {
                    allPresent = false;
                    break;
                }
            }

            if (allPresent)
            {
                foreach (HuggingFaceModelSpec model in models)
                    _logger.LogInformation("{Model} files already present at {Path}.", model.Label, model.DestinationDir);
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await SetBusyAsync(true, DownloadingMessage);
                HttpClient client = _httpClientFactory.CreateClient(HttpClientName);

                foreach (HuggingFaceModelSpec model in models)
                {
                    if (AllFilesPresent(model.DestinationDir, model.Files))
                    {
                        _logger.LogInformation("{Model} files already present at {Path}.", model.Label, model.DestinationDir);
                        continue;
                    }

                    Directory.CreateDirectory(model.DestinationDir);
                    string baseUrl = model.BaseUrl.EndsWith('/') ? model.BaseUrl : model.BaseUrl + "/";

                    foreach ((string urlPath, string fileName) in model.Files)
                    {
                        string destination = Path.Combine(model.DestinationDir, fileName);
                        if (IsComplete(destination))
                            continue;

                        await SetBusyAsync(true, $"{DownloadingMessage} {fileName}");
                        await DownloadFileAsync(client, baseUrl + urlPath, destination, cancellationToken).ConfigureAwait(false);
                    }

                    _logger.LogInformation("{Model} download completed.", model.Label);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download Hugging Face model files.");
                await SetBusyAsync(true, $"Error al descargar el modelo: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Overlay still clears in finally.
                }

                throw;
            }
            finally
            {
                await SetBusyAsync(false, string.Empty);
                _gate.Release();
            }
        }

        public Task SetBusyAsync(bool isBusy, string message)
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

        private static bool IsComplete(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

        private static async Task DownloadFileAsync(HttpClient client, string url, string destination, CancellationToken cancellationToken)
        {
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
    }

    public sealed record HuggingFaceModelSpec(
        string BaseUrl,
        string DestinationDir,
        IReadOnlyList<(string UrlPath, string FileName)> Files,
        string Label);
}

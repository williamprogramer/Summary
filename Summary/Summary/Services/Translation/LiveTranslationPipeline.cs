using Microsoft.Extensions.Logging;
using Summary.Services.Whisper;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services.Translation
{
    public sealed class LiveTranslationSegment
    {
        public required string English { get; init; }
        public required string Spanish { get; init; }
    }

    public sealed class LiveTranslationPipeline : IDisposable
    {
        public const int WindowSamples = WhisperFeatureExtractor.SampleRate * 4;
        public const int OverlapSamples = WhisperFeatureExtractor.SampleRate / 2;

        private readonly WhisperOnnxTranscriber _whisper;
        private readonly OpusMtTranslator _translator;
        private readonly ILogger<LiveTranslationPipeline> _logger;
        private readonly object _gate = new();
        private readonly List<float> _buffer = [];

        private CancellationTokenSource? _cts;
        private int _session;
        private bool _running;
        private bool _processing;
        private bool _disposed;

        public event EventHandler<LiveTranslationSegment>? SegmentReady;
        public event EventHandler<Exception>? Failed;

        public LiveTranslationPipeline(
            WhisperOnnxTranscriber whisper,
            OpusMtTranslator translator,
            ILogger<LiveTranslationPipeline> logger)
        {
            _whisper = whisper;
            _translator = translator;
            _logger = logger;
        }

        public void Warmup()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _whisper.EnsureReady();
            _translator.EnsureReady();
        }

        public void Start()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lock (_gate)
            {
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _session++;
                _buffer.Clear();
                _running = true;
                _processing = false;
            }
        }

        public void Pause()
        {
            lock (_gate)
            {
                _running = false;
                _session++;
                _cts?.Cancel();
                _buffer.Clear();
                _processing = false;
            }
        }

        public void Stop() => Pause();

        public void AddSamples(float[] interleaved, int sampleRate, int channels)
        {
            if (!_running || interleaved.Length == 0)
                return;

            float[] mono = WhisperFeatureExtractor.ToMono16k(interleaved, sampleRate, channels);
            bool startWorker = false;
            int session = 0;
            CancellationToken token = CancellationToken.None;
            lock (_gate)
            {
                if (!_running)
                    return;

                _buffer.AddRange(mono);
                int cap = WindowSamples + OverlapSamples;
                if (_processing && _buffer.Count > cap)
                    _buffer.RemoveRange(0, _buffer.Count - cap);

                if (!_processing && _buffer.Count >= WindowSamples && _cts is not null)
                {
                    _processing = true;
                    startWorker = true;
                    session = _session;
                    token = _cts.Token;
                }
            }

            if (startWorker)
                _ = Task.Run(() => ProcessLoop(session, token));
        }

        private void ProcessLoop(int session, CancellationToken cancellationToken)
        {
            try
            {
                while (!IsCancelled(cancellationToken))
                {
                    float[]? chunk = null;
                    lock (_gate)
                    {
                        if (session != _session || !_running || _buffer.Count < WindowSamples)
                        {
                            if (session == _session)
                                _processing = false;
                            return;
                        }

                        chunk = _buffer.GetRange(0, WindowSamples).ToArray();
                        int keepFrom = WindowSamples - OverlapSamples;
                        _buffer.RemoveRange(0, keepFrom);
                    }

                    if (IsSilent(chunk))
                        continue;

                    string english = _whisper.Transcribe(chunk, "en");
                    if (string.IsNullOrWhiteSpace(english) || session != Volatile.Read(ref _session) || IsCancelled(cancellationToken))
                        continue;

                    string spanish = _translator.Translate(english);
                    if (session != Volatile.Read(ref _session) || IsCancelled(cancellationToken))
                        return;

                    SegmentReady?.Invoke(this, new LiveTranslationSegment
                    {
                        English = english.Trim(),
                        Spanish = string.IsNullOrWhiteSpace(spanish) ? english.Trim() : spanish.Trim()
                    });
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Live translation chunk failed.");
                Failed?.Invoke(this, ex);
            }
            finally
            {
                lock (_gate)
                {
                    if (session == _session)
                        _processing = false;
                }
            }
        }

        private static bool IsCancelled(CancellationToken cancellationToken)
        {
            try
            {
                return cancellationToken.IsCancellationRequested;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        private static bool IsSilent(float[] audio)
        {
            if (audio.Length == 0)
                return true;

            double sum = 0;
            for (int i = 0; i < audio.Length; i++)
                sum += audio[i] * audio[i];
            return Math.Sqrt(sum / audio.Length) < 0.008;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Pause();
            _cts?.Dispose();
        }
    }
}

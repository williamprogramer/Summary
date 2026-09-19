using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Summary.Services;
using Summary.Services.Translation;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Summary.ViewModels
{
    public partial class LiveTranslationViewModel : ObservableObject
    {
        private readonly DispatcherTimer _timer;
        private readonly Stopwatch _stopwatch;
        private readonly NAudioService _naudioService;
        private readonly LiveTranslationPipeline _pipeline;
        private readonly LiveModelDownloadService _downloader;
        private readonly string _defaultTime = "00:00:00";
        private bool _audioHooked;
        private bool _warmed;

        [ObservableProperty]
        public partial string Time { get; set; } = "00:00:00";

        [ObservableProperty]
        public partial bool IsRunning { get; set; }

        [ObservableProperty]
        public partial bool IsPaused { get; set; }

        [ObservableProperty]
        public partial bool IsBusy { get; set; }

        [ObservableProperty]
        public partial string EnglishText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string SpanishText { get; set; } = string.Empty;

        [ObservableProperty]
        public partial string ErrorMessage { get; set; } = string.Empty;

        public LiveTranslationViewModel()
        {
            _naudioService = App.ServiceProvider.GetRequiredService<NAudioService>();
            _pipeline = App.ServiceProvider.GetRequiredService<LiveTranslationPipeline>();
            _downloader = App.ServiceProvider.GetRequiredService<LiveModelDownloadService>();
            _stopwatch = new Stopwatch();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += Timer_Tick;
            _pipeline.SegmentReady += OnSegmentReady;
            _pipeline.Failed += OnPipelineFailed;
        }

        private bool CanPlay() => !IsBusy && !IsRunning;

        private bool CanPause() => !IsBusy && IsRunning;

        private bool CanStop() => !IsBusy && (IsRunning || IsPaused);

        public async Task InitializeAsync()
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                await EnsureModelsAndWarmupAsync();
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanPlay))]
        public async Task PlayAsync()
        {
            if (IsRunning)
                return;

            IsBusy = true;
            ErrorMessage = string.Empty;
            try
            {
                await EnsureModelsAndWarmupAsync();

                if (!IsPaused)
                {
                    EnglishText = string.Empty;
                    SpanishText = string.Empty;
                    _stopwatch.Reset();
                    Time = _defaultTime;
                }

                HookAudio(true);
                _pipeline.Start();
                _naudioService.StartLoopback();
                IsRunning = true;
                IsPaused = false;
                _stopwatch.Start();
                _timer.Start();
            }
            catch (Exception ex)
            {
                HookAudio(false);
                _pipeline.Stop();
                _naudioService.StopLoopback();
                IsRunning = false;
                IsPaused = false;
                ErrorMessage = ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanPause))]
        public void Pause()
        {
            if (!IsRunning)
                return;

            _naudioService.PauseLoopback();
            _pipeline.Pause();
            HookAudio(false);
            IsRunning = false;
            IsPaused = true;
            _stopwatch.Stop();
            _timer.Stop();
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        public void Stop()
        {
            _naudioService.StopLoopback();
            _pipeline.Stop();
            HookAudio(false);
            IsRunning = false;
            IsPaused = false;
            _stopwatch.Stop();
            _stopwatch.Reset();
            _timer.Stop();
            Time = _defaultTime;
        }

        public void Shutdown()
        {
            Stop();
            _pipeline.SegmentReady -= OnSegmentReady;
            _pipeline.Failed -= OnPipelineFailed;
            _timer.Tick -= Timer_Tick;
        }

        private async Task EnsureModelsAndWarmupAsync()
        {
            await _downloader.EnsureModelsAsync();
            if (_warmed)
                return;

            await Task.Run(() => _pipeline.Warmup());
            _warmed = true;
        }

        private void HookAudio(bool enable)
        {
            if (enable && !_audioHooked)
            {
                _naudioService.LoopbackDataAvailable += OnLoopbackData;
                _audioHooked = true;
            }
            else if (!enable && _audioHooked)
            {
                _naudioService.LoopbackDataAvailable -= OnLoopbackData;
                _audioHooked = false;
            }
        }

        private void OnLoopbackData(object? sender, LoopbackAudioEventArgs e)
        {
            _pipeline.AddSamples(e.Samples, e.SampleRate, e.Channels);
        }

        private void OnSegmentReady(object? sender, LiveTranslationSegment segment)
        {
            App.DispatcherQueue.TryEnqueue(() =>
            {
                EnglishText = Append(EnglishText, segment.English);
                SpanishText = Append(SpanishText, segment.Spanish);
            });
        }

        private void OnPipelineFailed(object? sender, Exception ex)
        {
            App.DispatcherQueue.TryEnqueue(() => ErrorMessage = ex.Message);
        }

        private void Timer_Tick(object? sender, object e)
        {
            if (IsRunning)
                Time = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        private static string Append(string current, string next)
        {
            if (string.IsNullOrWhiteSpace(next))
                return current;
            if (string.IsNullOrWhiteSpace(current))
                return next;
            return current + " " + next;
        }

        partial void OnIsRunningChanged(bool value)
        {
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsPausedChanged(bool value)
        {
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsBusyChanged(bool value)
        {
            PlayCommand.NotifyCanExecuteChanged();
            PauseCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
        }
    }
}

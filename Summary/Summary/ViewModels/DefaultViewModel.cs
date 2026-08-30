using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Summary.Helpers;
using Summary.Services;
using Summary.Services.Whisper;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Summary.ViewModels
{
    public partial class DefaultViewModel : ObservableObject
    {
        private readonly DispatcherTimer? _timer = default!;
        private readonly Stopwatch? _stopwatch = default!;
        private bool _isRecordingStopped = false;
        private readonly string _defaultTime = "00:00:00";
        private readonly NAudioService _naudioService = default!;
        private readonly WhisperOnnxTranscriber _transcriber = default!;
        private readonly BusyService _busy = default!;
        private string? _currentWavPath;

        [ObservableProperty]
        public partial string Time { get; set; } = string.Empty;
        [ObservableProperty]
        public partial bool IsRecording { get; set; } = false;
        [ObservableProperty]
        public partial bool IsBusy { get; set; } = false;
        [ObservableProperty]
        public partial string Transcription { get; set; } = string.Empty;

        public DefaultViewModel()
        {
            _naudioService = App.ServiceProvider.GetRequiredService<NAudioService>();
            _transcriber = App.ServiceProvider.GetRequiredService<WhisperOnnxTranscriber>();
            _busy = App.ServiceProvider.GetRequiredService<BusyService>();
            Time = _defaultTime;
            _stopwatch = new();
            _timer = new()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        private bool CanStart() => !IsRecording && !IsBusy;

        private bool CanStop() => IsRecording && !IsBusy;

        private void Timer_Tick(object? sender, object e)
        {
            if (_stopwatch != null && _timer != null && IsRecording && !_isRecordingStopped)
                Time = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        [RelayCommand(CanExecute = nameof(CanStart))]
        public void StartRecording()
        {
            if (!IsRecording && _stopwatch != null && _timer != null)
            {
                _currentWavPath = Path.Combine(PathHelper.RecordingsPath, $"{Guid.NewGuid()}.wav");
                _naudioService.Start(_currentWavPath);
                IsRecording = true;
                _isRecordingStopped = false;
                Transcription = string.Empty;
                _stopwatch.Start();
                _timer.Start();
            }
        }

        [RelayCommand(CanExecute = nameof(CanStop))]
        public async Task StopRecording()
        {
            if (!IsRecording || _stopwatch == null || _timer == null)
                return;

            _naudioService.Stop();
            IsBusy = true;
            _busy.IsBusy = true;
            IsRecording = false;
            _isRecordingStopped = true;
            _stopwatch.Stop();
            _stopwatch.Reset();
            Time = _defaultTime;
            _timer.Stop();

            string? wavPath = _currentWavPath;
            try
            {
                if (string.IsNullOrWhiteSpace(wavPath) || !File.Exists(wavPath))
                {
                    Transcription = "No se encontró el archivo de audio.";
                    return;
                }

                Transcription = await Task.Run(() => _transcriber.Transcribe(wavPath));
            }
            catch (Exception ex)
            {
                Transcription = ex.Message;
            }
            finally
            {
                IsBusy = false;
                _busy.IsBusy = false;
            }
        }

        partial void OnIsRecordingChanged(bool value)
        {
            StartRecordingCommand.NotifyCanExecuteChanged();
            StopRecordingCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsBusyChanged(bool value)
        {
            StartRecordingCommand.NotifyCanExecuteChanged();
            StopRecordingCommand.NotifyCanExecuteChanged();
        }
    }
}

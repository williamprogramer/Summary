using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Summary.Helpers;
using Summary.Services;
using System;
using System.Diagnostics;
using System.IO;

namespace Summary.ViewModels
{
    public partial class DefaultViewModel : ObservableObject
    {
        private readonly DispatcherTimer? _timer = default!;
        private readonly Stopwatch? _stopwatch = default!;
        private bool _isRecordingStopped = false;
        private readonly string _defaultTime = "00:00:00";
        private readonly NAudioService _naudioService = default!;

        [ObservableProperty]
        public partial string Time { get; set; } = string.Empty;
        [ObservableProperty]
        public partial bool IsRecording { get; set; } = false;
        
        public DefaultViewModel()
        {
            _naudioService = App.ServiceProvider.GetRequiredService<NAudioService>();
            Time = _defaultTime;
            _stopwatch = new();
            _timer = new()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }
        
        private void Timer_Tick(object? sender, object e)
        {
            if (_stopwatch != null && _timer != null && IsRecording && !_isRecordingStopped)
                Time = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
        }

        [RelayCommand]
        public void StartRecording()
        {
            if (!IsRecording && _stopwatch != null && _timer != null)
            {
                _naudioService.Start(Path.Combine(PathHelper.RecordingsPath, $"{Guid.NewGuid()}.wav"));
                IsRecording = true;
                _isRecordingStopped = false;
                _stopwatch.Start();
                _timer.Start();
            }
        }

        [RelayCommand]
        public void StopRecording()
        {
            if (IsRecording && _stopwatch != null && _timer != null)
            {
                _naudioService.Stop();
                IsRecording = false;
                _isRecordingStopped = true;
                _stopwatch.Stop();
                _stopwatch.Reset();
                Time = _defaultTime;
                _timer.Stop();
            }
        }
    }
}
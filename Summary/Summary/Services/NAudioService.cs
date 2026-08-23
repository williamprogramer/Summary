using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Extras;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Summary.Services
{
    public class NAudioService
    {
        private WasapiRecorder? _micRecorder;
        private WasapiRecorder? _systemRecorder;
        private RealtimeCaptureMixer? _mixer;
        private WaveFileWriter? _writer;
        private Task? _pumpTask;
        private bool _stop;
        private ILogger<NAudioService> _logger;

        /// <summary>
        /// Boost applied to the microphone before mixing. Increase if the mic sounds too quiet in the recording.
        /// </summary>
        public float MicGain { get; set; } = 4f;

        public NAudioService()
        {
            _logger = App.ServiceProvider.GetRequiredService<ILogger<NAudioService>>();
        }

        public void Start(string filename)
        {
            using MMDeviceEnumerator enumerator = new();
            MMDevice renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            MMDevice captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            (int systemSampleRate, int systemChannels) = GetNativeFormat(renderDevice);
            (int micSampleRate, int micChannels) = GetNativeFormat(captureDevice);
            int unifiedRate = Math.Max(systemSampleRate, micSampleRate);
            WaveFormat targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, 2);

            _mixer = new RealtimeCaptureMixer(targetFormat);

            _systemRecorder = new WasapiRecorderBuilder()
                .WithDevice(renderDevice)
                .WithLoopbackCapture()
                .WithPollingSync()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, systemChannels))
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            CaptureMixerInput systemInput = _mixer.AddInput(_systemRecorder.WaveFormat);
            _systemRecorder.DataAvailable += (data, flags, dev, qpc) => systemInput.AddSamples(data);

            _micRecorder = new WasapiRecorderBuilder()
                .WithDevice(captureDevice)
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, micChannels))
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            float micGain = MicGain;
            CaptureMixerInput micInput = _mixer.AddInput(_micRecorder.WaveFormat, provider => new VolumeSampleProvider(provider) { Volume = micGain });
            _micRecorder.DataAvailable += (data, flags, dev, qpc) => micInput.AddSamples(data);

            _writer = new WaveFileWriter(filename, _mixer.WaveFormat);

            float[] buffer = new float[_mixer.WaveFormat.SampleRate * _mixer.WaveFormat.Channels / 5];
            _stop = false;
            _pumpTask = Task.Run(() =>
            {
                while (!_stop)
                {
                    int read = _mixer.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                        _writer.WriteSamples(buffer, 0, read);
                    else
                        Thread.Sleep(5);
                }
            });

            _mixer.Start();
            _systemRecorder.StartRecording();
            _micRecorder.StartRecording();
        }

        public void Stop()
        {
            _stop = true;
            _pumpTask?.Wait();

            _systemRecorder?.StopRecording();
            _micRecorder?.StopRecording();

            _systemRecorder?.Dispose();
            _micRecorder?.Dispose();

            _writer?.Dispose();
            _writer = null;
        }

        private static (int SampleRate, int Channels) GetNativeFormat(MMDevice device)
        {
            try
            {
                using AudioClient audioClient = device.CreateAudioClient();
                WaveFormat mix = audioClient.MixFormat;
                return (mix.SampleRate, mix.Channels);
            }
            catch
            {
                return (48000, 2);
            }
        }
    }
}

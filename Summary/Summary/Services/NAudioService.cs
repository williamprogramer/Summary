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
        private readonly ILogger<NAudioService> _logger;
        private WasapiRecorder? _micRecorder;
        private WasapiRecorder? _systemRecorder;
        private RealtimeCaptureMixer? _mixer;
        private WaveFileWriter? _writer;
        private Task? _pumpTask;
        private bool _stop;

        /// <summary>
        /// Boost applied to the microphone before mixing. Increase if the mic sounds too quiet in the recording.
        /// </summary>
        public float MicGain { get; set; } = 4f;

        public NAudioService(ILogger<NAudioService> logger)
        {
            _logger = logger;
        }

        public void Start(string filename)
        {
            using var enumerator = new MMDeviceEnumerator();
            var renderDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var captureDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);

            var systemNative = GetNativeFormat(renderDevice);
            var micNative = GetNativeFormat(captureDevice);
            var unifiedRate = Math.Max(systemNative.SampleRate, micNative.SampleRate);
            var targetFormat = WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, 2);

            _logger.LogInformation(
                "Recording: system={SystemDevice} ({SystemRate} Hz, {SystemChannels} ch), mic={MicDevice} ({MicRate} Hz, {MicChannels} ch), mix={MixRate} Hz stereo, micGain={MicGain}",
                renderDevice.FriendlyName,
                systemNative.SampleRate,
                systemNative.Channels,
                captureDevice.FriendlyName,
                micNative.SampleRate,
                micNative.Channels,
                unifiedRate,
                MicGain);

            _mixer = new RealtimeCaptureMixer(targetFormat);

            _systemRecorder = new WasapiRecorderBuilder()
                .WithDevice(renderDevice)
                .WithLoopbackCapture()
                .WithPollingSync()
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, systemNative.Channels))
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            var systemInput = _mixer.AddInput(_systemRecorder.WaveFormat);
            _systemRecorder.DataAvailable += (data, flags, dev, qpc) => systemInput.AddSamples(data);

            _micRecorder = new WasapiRecorderBuilder()
                .WithDevice(captureDevice)
                .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(unifiedRate, micNative.Channels))
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            var micGain = MicGain;
            var micInput = _mixer.AddInput(_micRecorder.WaveFormat, provider =>
                new VolumeSampleProvider(provider) { Volume = micGain });
            _micRecorder.DataAvailable += (data, flags, dev, qpc) => micInput.AddSamples(data);

            _writer = new WaveFileWriter(filename, _mixer.WaveFormat);

            var buffer = new float[_mixer.WaveFormat.SampleRate * _mixer.WaveFormat.Channels / 5];
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
                using var audioClient = device.CreateAudioClient();
                var mix = audioClient.MixFormat;
                return (mix.SampleRate, mix.Channels);
            }
            catch
            {
                return (48000, 2);
            }
        }
    }
}

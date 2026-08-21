using NAudio.Extras;
using NAudio.Wave;
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

        public void Start(string filename)
        {
            _mixer = new RealtimeCaptureMixer(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1));

            _systemRecorder = new WasapiRecorderBuilder()
                .WithLoopbackCapture()
                .WithPollingSync()
                .Build();

            var systemInput = _mixer.AddInput(_systemRecorder.WaveFormat);
            _systemRecorder.DataAvailable += (data, flags, dev, qpc) => systemInput.AddSamples(data);

            _micRecorder = new WasapiRecorderBuilder().WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(48000, 1)).Build();
            var micInput = _mixer.AddInput(_micRecorder.WaveFormat);
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
    }
}

using NAudio.Wave;
using System;
using System.Collections.Generic;

namespace Summary.Services.Whisper
{
    internal static class WhisperFeatureExtractor
    {
        public const int SampleRate = 16000;
        public const int NFft = 400;
        public const int HopLength = 160;
        public const int NMels = 80;
        public const int NSamples = 480000;
        public const int NFrames = 3000;

        private static readonly float[] HannWindow = CreateHannWindow();
        private static readonly float[] CosTable;
        private static readonly float[] SinTable;
        private static readonly float[,] MelFilters = CreateMelFilters();

        private const int NFreqs = NFft / 2 + 1;

        static WhisperFeatureExtractor()
        {
            CosTable = new float[NFreqs * NFft];
            SinTable = new float[NFreqs * NFft];
            for (int k = 0; k < NFreqs; k++)
            {
                for (int n = 0; n < NFft; n++)
                {
                    double angle = 2.0 * Math.PI * k * n / NFft;
                    CosTable[k * NFft + n] = (float)Math.Cos(angle);
                    SinTable[k * NFft + n] = (float)Math.Sin(angle);
                }
            }
        }

        public static float[] LoadMono16k(string wavPath)
        {
            using AudioFileReader reader = new(wavPath);
            ISampleProvider provider = reader;
            int channels = Math.Max(1, reader.WaveFormat.Channels);
            int sourceRate = reader.WaveFormat.SampleRate;

            List<float> interleaved = new(sourceRate * channels * 8);
            float[] buffer = new float[sourceRate * channels];
            int read;
            while ((read = provider.Read(buffer.AsSpan())) > 0)
            {
                for (int i = 0; i < read; i++)
                    interleaved.Add(buffer[i]);
            }

            int frames = interleaved.Count / channels;
            float[] mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0f;
                int baseIndex = i * channels;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[baseIndex + c];
                mono[i] = sum / channels;
            }

            if (sourceRate == SampleRate)
                return mono;

            return Resample(mono, sourceRate, SampleRate);
        }

        public static float[] ComputeLogMel(float[] audio)
        {
            float[] padded = new float[NSamples];
            int copy = Math.Min(audio.Length, NSamples);
            if (copy > 0)
                Array.Copy(audio, padded, copy);

            float[] centered = ReflectPad(padded, NFft / 2);
            int totalFrames = 1 + (centered.Length - NFft) / HopLength;
            int useFrames = Math.Min(NFrames, totalFrames > 0 ? totalFrames - 1 : 0);

            float[] frame = new float[NFft];
            float[] power = new float[NFreqs];
            float[] mel = new float[NMels * NFrames];

            for (int t = 0; t < useFrames; t++)
            {
                int start = t * HopLength;
                for (int n = 0; n < NFft; n++)
                    frame[n] = centered[start + n] * HannWindow[n];

                for (int k = 0; k < NFreqs; k++)
                {
                    float re = 0f;
                    float im = 0f;
                    int tableOffset = k * NFft;
                    for (int n = 0; n < NFft; n++)
                    {
                        float sample = frame[n];
                        re += sample * CosTable[tableOffset + n];
                        im -= sample * SinTable[tableOffset + n];
                    }
                    power[k] = re * re + im * im;
                }

                for (int m = 0; m < NMels; m++)
                {
                    float sum = 0f;
                    for (int k = 0; k < NFreqs; k++)
                        sum += MelFilters[m, k] * power[k];
                    mel[m * NFrames + t] = MathF.Max(sum, 1e-10f);
                }
            }

            float logMax = float.NegativeInfinity;
            for (int i = 0; i < mel.Length; i++)
            {
                float log = MathF.Log10(mel[i] <= 0f ? 1e-10f : mel[i]);
                mel[i] = log;
                if (log > logMax)
                    logMax = log;
            }

            float clamp = logMax - 8f;
            for (int i = 0; i < mel.Length; i++)
            {
                float v = mel[i] < clamp ? clamp : mel[i];
                mel[i] = (v + 4f) / 4f;
            }

            return mel;
        }

        private static float[] Resample(float[] mono, int sourceRate, int targetRate)
        {
            if (mono.Length == 0)
                return mono;

            double ratio = (double)sourceRate / targetRate;
            int outLength = Math.Max(1, (int)(mono.Length / ratio));
            float[] output = new float[outLength];
            int last = mono.Length - 1;
            for (int i = 0; i < outLength; i++)
            {
                double src = i * ratio;
                int i0 = Math.Min((int)src, last);
                int i1 = Math.Min(i0 + 1, last);
                float frac = (float)(src - i0);
                output[i] = mono[i0] + (mono[i1] - mono[i0]) * frac;
            }
            return output;
        }

        private static float[] ReflectPad(float[] audio, int pad)
        {
            float[] result = new float[audio.Length + 2 * pad];
            for (int i = 0; i < pad; i++)
                result[i] = audio[pad - i];
            Array.Copy(audio, 0, result, pad, audio.Length);
            int n = audio.Length;
            for (int i = 0; i < pad; i++)
                result[pad + n + i] = audio[n - 2 - i];
            return result;
        }

        private static float[] CreateHannWindow()
        {
            float[] window = new float[NFft];
            for (int i = 0; i < NFft; i++)
                window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / NFft));
            return window;
        }

        private static float[,] CreateMelFilters()
        {
            const float fMin = 0f;
            const float fMax = SampleRate / 2f;
            float[] fftFreqs = new float[NFreqs];
            for (int i = 0; i < NFreqs; i++)
                fftFreqs[i] = fMax * i / (NFreqs - 1);

            float[] melPoints = new float[NMels + 2];
            float minMel = HzToMel(fMin);
            float maxMel = HzToMel(fMax);
            for (int i = 0; i < melPoints.Length; i++)
                melPoints[i] = MelToHz(minMel + (maxMel - minMel) * i / (NMels + 1));

            float[] fDiff = new float[melPoints.Length - 1];
            for (int i = 0; i < fDiff.Length; i++)
                fDiff[i] = melPoints[i + 1] - melPoints[i];

            float[,] weights = new float[NMels, NFreqs];
            for (int i = 0; i < NMels; i++)
            {
                for (int k = 0; k < NFreqs; k++)
                {
                    float lower = (fftFreqs[k] - melPoints[i]) / fDiff[i];
                    float upper = (melPoints[i + 2] - fftFreqs[k]) / fDiff[i + 1];
                    weights[i, k] = MathF.Max(0f, MathF.Min(lower, upper));
                }

                float enorm = 2f / (melPoints[i + 2] - melPoints[i]);
                for (int k = 0; k < NFreqs; k++)
                    weights[i, k] *= enorm;
            }

            return weights;
        }

        private static float HzToMel(float hz)
        {
            const float fSp = 200f / 3f;
            const float minLogHz = 1000f;
            const float minLogMel = minLogHz / fSp;
            float logstep = MathF.Log(6.4f) / 27f;
            if (hz >= minLogHz)
                return minLogMel + MathF.Log(hz / minLogHz) / logstep;
            return hz / fSp;
        }

        private static float MelToHz(float mel)
        {
            const float fSp = 200f / 3f;
            const float minLogHz = 1000f;
            const float minLogMel = minLogHz / fSp;
            float logstep = MathF.Log(6.4f) / 27f;
            if (mel >= minLogMel)
                return minLogHz * MathF.Exp(logstep * (mel - minLogMel));
            return mel * fSp;
        }
    }
}

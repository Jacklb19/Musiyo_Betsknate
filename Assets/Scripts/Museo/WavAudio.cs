using System;
using System.IO;
using System.Text;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// The WAV files of the guide's voice: a recording leaves as PCM16 mono at 16 kHz, as the transcription contract asks;
    /// a spoken answer arrives as PCM16 mono at the rate written in its own header (16 000, 22 050 or 24 000 Hz).
    /// </summary>
    public static class WavAudio
    {
        public const int RecordingRate = 16000;
        /// <summary>
        /// Below this peak (about −40 dBFS) a recording holds no voice at all: a muted, held-back or missing microphone.
        /// Speech recognition turns such silence into a stock phrase («Gracias por ver el video»), so it is never sent.
        /// </summary>
        public const float SilentPeak = .01f;
        /// <summary>
        /// Below this loudness (about −48 dBFS) a recording holds no voice either, even with a click or a knock in it: a
        /// muted microphone gave peak 0.014 and rms 0.0009 for three seconds (26/10/2026).
        /// </summary>
        public const float SilentRms = .004f;

        /// <summary>Peak and loudness (RMS) of a PCM16 WAV, in [0, 1], and its length in seconds; false for anything else.</summary>
        public static bool Measure(byte[] wav, out float peak, out float rms, out float seconds)
        {
            peak = rms = seconds = 0;
            if (!TryDecodePcm16(wav, out var samples, out int rate) || samples.Length == 0) return false;
            double energy = 0;
            foreach (var sample in samples)
            {
                float level = Math.Abs(sample);
                if (level > peak) peak = level;
                energy += sample * sample;
            }
            rms = (float)Math.Sqrt(energy / samples.Length);
            seconds = samples.Length / (float)rate;
            return true;
        }

        /// <summary>A recording that holds no voice; its length and levels go to the log either way, never its sound.</summary>
        public static bool Silent(byte[] wav, string who)
        {
            if (!Measure(wav, out float peak, out float rms, out float seconds)) return false;
            UnityEngine.Debug.Log("[" + who + "] take " + wav.Length + " bytes, " + seconds.ToString("F2") + " s, peak " + peak.ToString("F4")
                + ", rms " + rms.ToString("F4") + (peak < SilentPeak || rms < SilentRms ? ": no voice, not sent" : ""));
            return peak < SilentPeak || rms < SilentRms;
        }

        /// <summary>Encodes samples in [-1, 1] as PCM16 mono WAV, resampling linearly to <paramref name="targetRate"/>.</summary>
        public static byte[] EncodePcm16Mono(float[] samples, int sourceRate, int targetRate = RecordingRate)
        {
            if (samples == null) throw new ArgumentNullException(nameof(samples));
            if (sourceRate <= 0 || targetRate <= 0) throw new ArgumentOutOfRangeException(nameof(sourceRate));
            int frames = sourceRate == targetRate ? samples.Length : (int)((long)samples.Length * targetRate / sourceRate);
            var data = new byte[frames * 2];
            double step = (double)sourceRate / targetRate;
            for (int frame = 0; frame < frames; frame++)
            {
                float value;
                if (sourceRate == targetRate) value = samples[frame];
                else
                {
                    double position = frame * step;
                    int index = (int)position;
                    float next = index + 1 < samples.Length ? samples[index + 1] : samples[index];
                    value = (float)(samples[index] + (next - samples[index]) * (position - index));
                }
                short pcm = (short)Math.Round(Math.Max(-1f, Math.Min(1f, value)) * short.MaxValue);
                data[frame * 2] = (byte)pcm;
                data[frame * 2 + 1] = (byte)(pcm >> 8);
            }
            using var stream = new MemoryStream(44 + data.Length);
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + data.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(targetRate);
                writer.Write(targetRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(data.Length);
                writer.Write(data);
            }
            return stream.ToArray();
        }

        /// <summary>
        /// Reads a PCM16 WAV: its rate and channels from the "fmt " chunk and its samples from the "data" chunk, skipping
        /// any other chunk. Several channels are mixed down to one. Anything else is refused.
        /// </summary>
        public static bool TryDecodePcm16(byte[] wav, out float[] samples, out int sampleRate)
        {
            samples = null;
            sampleRate = 0;
            if (wav == null || wav.Length < 44) return false;
            try
            {
                using var reader = new BinaryReader(new MemoryStream(wav, false));
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF") return false;
                reader.ReadInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE") return false;
                int channels = 0, bits = 0;
                while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
                {
                    string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int size = reader.ReadInt32();
                    if (size < 0 || reader.BaseStream.Position + size > reader.BaseStream.Length) return false;
                    if (chunk == "fmt ")
                    {
                        if (size < 16) return false;
                        short format = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        sampleRate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bits = reader.ReadInt16();
                        reader.ReadBytes(size - 16);
                        if (format != 1 || bits != 16 || channels < 1 || channels > 2 || sampleRate < 8000 || sampleRate > 48000) return false;
                    }
                    else if (chunk == "data")
                    {
                        if (channels == 0) return false;
                        int frames = size / (2 * channels);
                        samples = new float[frames];
                        for (int frame = 0; frame < frames; frame++)
                        {
                            float sum = 0;
                            for (int channel = 0; channel < channels; channel++) sum += reader.ReadInt16() / 32768f;
                            samples[frame] = sum / channels;
                        }
                        return frames > 0;
                    }
                    else reader.ReadBytes(size);
                    if ((size & 1) == 1 && reader.BaseStream.Position < reader.BaseStream.Length) reader.ReadByte();
                }
                return false;
            }
            catch (EndOfStreamException) { return false; }
        }
    }
}

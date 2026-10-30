using System;
using System.Diagnostics;
using System.IO;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Writes one narration at 1x and at 1.5x with the stretch the museum uses, for the owner to hear that the voice keeps
    /// its pitch (owner, 28/10/2026). Explicit: run it by name with U1_NARRATION_MP3 set to a narration's MP3 and
    /// U1_EVIDENCE_DIR to the folder for the two WAV files and the time the stretch took.
    /// </summary>
    [Explicit, Category("Capture")]
    public sealed class NarrationSpeedSamples
    {
        private const string Folder = "Assets/NarrationSpeedSamples";

        [Test]
        public void WriteANarrationAtBothSpeeds()
        {
            string source = Environment.GetEnvironmentVariable("U1_NARRATION_MP3");
            string output = Environment.GetEnvironmentVariable("U1_EVIDENCE_DIR");
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output)) Assert.Ignore("Set U1_NARRATION_MP3 and U1_EVIDENCE_DIR.");
            try
            {
                Directory.CreateDirectory(Folder);
                string asset = Folder + "/narration.mp3";
                File.Copy(source, asset, true);
                AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
                // Decoded to PCM in the Editor, so the samples are the MP3's own and not a second compression of it.
                var importer = (AudioImporter)AssetImporter.GetAtPath(asset);
                var sampleSettings = importer.defaultSampleSettings;
                sampleSettings.loadType = AudioClipLoadType.DecompressOnLoad;
                sampleSettings.compressionFormat = AudioCompressionFormat.PCM;
                sampleSettings.preloadAudioData = true;
                importer.defaultSampleSettings = sampleSettings;
                importer.SaveAndReimport();
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(asset);
                clip.LoadAudioData();
                var samples = new float[clip.samples * clip.channels];
                Assert.That(clip.GetData(samples, 0), Is.True);
                var clock = Stopwatch.StartNew();
                var fast = NarrationTimeStretch.Stretch(samples, clip.channels, clip.frequency, MuseumNarrationSpeed.Fast);
                clock.Stop();
                float fastSeconds = fast.Length / (float)clip.channels / clip.frequency;
                Directory.CreateDirectory(output);
                Wav(Path.Combine(output, "narracion-1x.wav"), samples, clip.channels, clip.frequency);
                Wav(Path.Combine(output, "narracion-1_5x.wav"), fast, clip.channels, clip.frequency);
                File.WriteAllText(Path.Combine(output, "duracion-y-coste.txt"),
                    $"Original: {clip.length:0.00} s, {clip.channels} canal(es), {clip.frequency} Hz\n" +
                    $"A 1,5x: {fastSeconds:0.00} s\n" +
                    $"Estiramiento en el Editor (PC): {clock.ElapsedMilliseconds} ms, una vez por narración y fuera del hilo principal\n");
                UnityEngine.Debug.Log($"[NarrationSpeed] {clip.length:0.00} s -> {fastSeconds:0.00} s in {clock.ElapsedMilliseconds} ms");
                Assert.That(fastSeconds, Is.EqualTo(clip.length / 1.5f).Within(.05f));
            }
            finally { AssetDatabase.DeleteAsset(Folder); }
        }

        private static void Wav(string path, float[] samples, int channels, int frequency)
        {
            using var writer = new BinaryWriter(File.Create(path));
            int bytes = samples.Length * 2;
            writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
            writer.Write(36 + bytes);
            writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
            writer.Write(16); writer.Write((short)1); writer.Write((short)channels); writer.Write(frequency);
            writer.Write(frequency * channels * 2); writer.Write((short)(channels * 2)); writer.Write((short)16);
            writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
            writer.Write(bytes);
            foreach (var sample in samples) writer.Write((short)(Mathf.Clamp(sample, -1, 1) * 32767));
        }
    }
}

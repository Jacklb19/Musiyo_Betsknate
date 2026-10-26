using System;
using System.IO;
using MusiyoBetsknate.Museum;
using MusiyoBetsknate.Museo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Tests
{
    /// <summary>The guide's voice against the canonical examples of the shared contracts and synthetic audio.</summary>
    public sealed class GuideVoiceTests
    {
        private static string Example(string name) => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        private static float[] Tone(int rate, float seconds, float frequency = 440)
        {
            var samples = new float[(int)(rate * seconds)];
            for (int index = 0; index < samples.Length; index++) samples[index] = .5f * Mathf.Sin(2 * Mathf.PI * frequency * index / rate);
            return samples;
        }

        [Test]
        public void ARecordingWithoutVoiceIsToldApartFromSpeech()
        {
            Assert.That(WavAudio.Measure(WavAudio.EncodePcm16Mono(Tone(48000, 2), 48000), out var peak, out var rms, out var seconds), Is.True);
            Assert.That(peak, Is.EqualTo(.5f).Within(.01f));
            Assert.That(rms, Is.EqualTo(.5f / Mathf.Sqrt(2)).Within(.01f));
            Assert.That(seconds, Is.EqualTo(2).Within(.01f));
            Assert.That(WavAudio.Silent(WavAudio.EncodePcm16Mono(Tone(48000, 2), 48000), "test"), Is.False);
            Assert.That(WavAudio.Silent(WavAudio.EncodePcm16Mono(new float[48000], 48000), "test"), Is.True, "A muted microphone.");
            var hiss = new float[48000];
            for (int index = 0; index < hiss.Length; index++) hiss[index] = (index % 7 - 3) * .0005f;
            Assert.That(WavAudio.Silent(WavAudio.EncodePcm16Mono(hiss, 48000), "test"), Is.True, "Only the faintest hiss.");
            Assert.That(WavAudio.Measure(new byte[] { 1, 2, 3 }, out _, out _, out _), Is.False);
        }

        [Test]
        public void ARecordingLeavesAsPcm16MonoAtSixteenKilohertz()
        {
            var wav = WavAudio.EncodePcm16Mono(Tone(48000, 1), 48000);
            Assert.That(System.Text.Encoding.ASCII.GetString(wav, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(BitConverter.ToInt16(wav, 20), Is.EqualTo(1), "PCM");
            Assert.That(BitConverter.ToInt16(wav, 22), Is.EqualTo(1), "mono");
            Assert.That(BitConverter.ToInt32(wav, 24), Is.EqualTo(16000));
            Assert.That(BitConverter.ToInt16(wav, 34), Is.EqualTo(16), "16 bits");
            Assert.That(wav.Length, Is.EqualTo(44 + 16000 * 2));
            Assert.That(WavAudio.TryDecodePcm16(wav, out var samples, out var rate), Is.True);
            Assert.That(rate, Is.EqualTo(16000));
            Assert.That(samples.Length, Is.EqualTo(16000));
            Assert.That(samples[400], Is.EqualTo(.5f * Mathf.Sin(2 * Mathf.PI * 440 * 400 / 16000f)).Within(.01f), "Resampling keeps the wave.");
            Assert.That(WavAudio.EncodePcm16Mono(Tone(16000, 30), 16000).Length, Is.LessThan(5000000), "The longest question fits the limit.");
        }

        [TestCase(16000)]
        [TestCase(22050)]
        [TestCase(24000)]
        public void ASpokenAnswerIsPlayedAtTheRateItsHeaderSays(int answerRate)
        {
            var wav = WavAudio.EncodePcm16Mono(Tone(answerRate, .5f), answerRate, answerRate);
            Assert.That(WavAudio.TryDecodePcm16(wav, out var samples, out var rate), Is.True);
            Assert.That(rate, Is.EqualTo(answerRate));
            Assert.That(samples.Length, Is.EqualTo(answerRate / 2));
        }

        [Test]
        public void AnythingButPcm16WavIsRefused()
        {
            var wav = WavAudio.EncodePcm16Mono(Tone(16000, .2f), 16000);
            var eightBit = (byte[])wav.Clone();
            BitConverter.GetBytes((short)8).CopyTo(eightBit, 34);
            Assert.That(WavAudio.TryDecodePcm16(eightBit, out _, out _), Is.False);
            Assert.That(WavAudio.TryDecodePcm16(new byte[60], out _, out _), Is.False);
            Assert.That(WavAudio.TryDecodePcm16(null, out _, out _), Is.False);
        }

        [Test]
        public void CapabilitiesSayWhetherAndWhereTheVoiceIsProcessed()
        {
            // As the online service answered on 23/10/2026, when the headset could not talk to the companion.
            const string online = "{\"schema_version\":1,\"transcription\":\"configured\",\"synthesis\":\"configured\",\"processing\":\"external_providers\","
                + "\"providers\":[\"ElevenLabs\",\"Groq\"],\"retention\":\"request_only\",\"input_mime\":\"audio/wav\",\"max_input_bytes\":5000000,"
                + "\"max_input_seconds\":30,\"request_timeout_seconds\":45}";
            Assert.That(VoiceCapabilitiesContractV1.TryParse(online, out var live), Is.True);
            Assert.That(live.Listens && live.Speaks && live.External, Is.True);
            Assert.That(VoiceCapabilitiesContractV1.TryParse(Example("voice_capabilities_disabled.json"), out var disabled), Is.True);
            Assert.That(disabled.Listens || disabled.Speaks, Is.False, "Both disabled: no voice controls.");
            Assert.That(VoiceCapabilitiesContractV1.TryParse(Example("voice_capabilities_external.json"), out var external), Is.True);
            Assert.That(external.Listens && external.Speaks && external.External, Is.True);
            Assert.That(external.providers, Is.EqualTo(new[] { "ElevenLabs", "Groq" }));
            Assert.That(external.max_input_seconds, Is.EqualTo(30));
            Assert.That(VoiceCapabilitiesContractV1.TryParse("{\"transcription\": \"maybe\"}", out _), Is.False);
        }

        [Test]
        public void ATranscriptionIsTextToConfirmAndEachFailureHasItsOwnState()
        {
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.Success, 200, Example("guide_transcription.json"), out var text),
                Is.EqualTo(GuideTranscriptionState.Transcribed));
            Assert.That(text, Is.EqualTo("Pregunta sintética de prueba"));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.ProtocolError, 503, "", out _), Is.EqualTo(GuideTranscriptionState.NotUnderstood));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.ProtocolError, 422, "", out _), Is.EqualTo(GuideTranscriptionState.Rejected));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.ProtocolError, 413, "", out _), Is.EqualTo(GuideTranscriptionState.Rejected));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.ProtocolError, 429, "", out _), Is.EqualTo(GuideTranscriptionState.RateLimited));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.ConnectionError, 0, "", out _), Is.EqualTo(GuideTranscriptionState.Failed));
            Assert.That(GuideVoiceClient.ClassifyTranscription(UnityWebRequest.Result.Success, 200, "{\"text\": \"\"}", out _), Is.EqualTo(GuideTranscriptionState.Incompatible));
        }

        [Test]
        public void ASpokenAnswerKeepsItsTextAndCitationsWithOrWithoutSpeech()
        {
            Assert.That(GuideQuestionClient.ClassifySpoken(UnityWebRequest.Result.Success, 200, null, Example("guide_voice_disabled.json"),
                out var response, out var status, out var speech), Is.EqualTo(GuideQuestionState.Answered));
            Assert.That(response.answer, Is.EqualTo("Respuesta sintética, sin contenido cultural."));
            Assert.That(response.citations.Length, Is.EqualTo(1));
            Assert.That(status, Is.EqualTo("disabled"));
            Assert.That(speech, Is.Null);
            Assert.That(GuideQuestionClient.ClassifySpoken(UnityWebRequest.Result.Success, 200, null, Example("guide_voice_unavailable.json"),
                out response, out status, out speech), Is.EqualTo(GuideQuestionState.ProviderUnavailable));
            Assert.That(status, Is.EqualTo("not_applicable"));
            Assert.That(speech, Is.Null);
            var wav = WavAudio.EncodePcm16Mono(Tone(22050, .3f), 22050, 22050);
            var ready = Example("guide_voice_disabled.json").Replace("\"speech_status\": \"disabled\"", "\"speech_status\": \"ready\"")
                .Replace("\"audio\": null", "\"audio\": {\"mime\": \"audio/wav\", \"data_base64\": \"" + Convert.ToBase64String(wav) + "\"}");
            Assert.That(GuideQuestionClient.ClassifySpoken(UnityWebRequest.Result.Success, 200, null, ready, out response, out status, out speech),
                Is.EqualTo(GuideQuestionState.Answered));
            Assert.That(status, Is.EqualTo("ready"));
            Assert.That(WavAudio.TryDecodePcm16(speech, out _, out var rate) && rate == 22050, Is.True);
            Assert.That(GuideQuestionClient.ClassifySpoken(UnityWebRequest.Result.ProtocolError, 429, null, "", out _, out _, out _),
                Is.EqualTo(GuideQuestionState.RateLimited));
        }
    }
}

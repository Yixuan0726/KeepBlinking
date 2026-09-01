using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace KeepBlinking.Tests
{
  public sealed class CareRoutineMusicAssetTests
  {
    private const string MusicPath =
      "Assets/KeepBlinking/Resources/CareStation/Audio/Music/LongNight_Aventure.mp3";
    private const string MixerPath =
      "Assets/KeepBlinking/Resources/CareStation/Audio/Music/CareRoutineMixer.mixer";
    private const string LicensePath =
      "Assets/KeepBlinking/Resources/CareStation/Audio/Music/LongNight_Aventure_LICENSE.md";
    private const string ExpectedSha256 =
      "BC6AE6565C68AF9A43E221B60E5E03C53AC5602AE5D214D40F92BCA41B55624E";

    [Test]
    public void SuppliedTrackWasCopiedExactlyAndHasProvenanceRecord()
    {
      Assert.That(File.Exists(MusicPath), Is.True);
      using var sha = SHA256.Create();
      var hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(MusicPath)))
        .Replace("-", string.Empty);
      Assert.That(hash, Is.EqualTo(ExpectedSha256));
      var record = File.ReadAllText(LicensePath);
      Assert.That(record, Does.Contain("Long Night"));
      Assert.That(record, Does.Contain("Aventure"));
      Assert.That(record, Does.Contain("https://www.bensound.com/royalty-free-music/track/long-night-calm-warm"));
      Assert.That(record, Does.Contain(ExpectedSha256));
      Assert.That(record, Does.Contain("did not include a separate Bensound certificate"));
    }

    [Test]
    public void MusicUsesMobileStreamingVorbisImportSettings()
    {
      var importer = AssetImporter.GetAtPath(MusicPath) as AudioImporter;
      Assert.That(importer, Is.Not.Null);
      Assert.That(importer.forceToMono, Is.False);
      Assert.That(importer.ambisonic, Is.False);
      Assert.That(importer.loadInBackground, Is.True);
      AssertSettings(importer.defaultSampleSettings);
      AssertSettings(importer.GetOverrideSampleSettings("Android"));
      AssertSettings(importer.GetOverrideSampleSettings("iOS"));
      var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicPath);
      Assert.That(clip, Is.Not.Null);
      Assert.That(clip.length, Is.GreaterThan(150f));
    }

    [Test]
    public void MixerContainsIndependentMusicGroupAndRuntimeResourcePath()
    {
      var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
      Assert.That(mixer, Is.Not.Null);
      var groups = mixer.FindMatchingGroups("Music");
      Assert.That(groups, Has.Length.EqualTo(1));
      Assert.That(groups[0].name, Is.EqualTo("Music"));
      Assert.That(Resources.Load<AudioMixer>("CareStation/Audio/Music/CareRoutineMixer"), Is.SameAs(mixer));
      Assert.That(Resources.Load<AudioClip>("CareStation/Audio/Music/LongNight_Aventure"), Is.Not.Null);
    }

    private static void AssertSettings(AudioImporterSampleSettings settings)
    {
      Assert.That(settings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
      Assert.That(settings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis));
      Assert.That(settings.quality, Is.EqualTo(0.72f).Within(0.001f));
      Assert.That(settings.sampleRateSetting, Is.EqualTo(AudioSampleRateSetting.OptimizeSampleRate));
      Assert.That(settings.preloadAudioData, Is.False);
    }
  }
}

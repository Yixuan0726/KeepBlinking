using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareAmbienceAssetTests
  {
    private const string Folder = "Assets/KeepBlinking/Resources/CareStation/Audio/Ambience";
    private static readonly string[] Names =
    {
      "Focus_Ambience", "Pilot_Ambience", "Guided_Ambience", "Rest_Ambience",
    };
    private static readonly IReadOnlyDictionary<string, string> ExpectedSha256 =
      new Dictionary<string, string>
      {
        ["Focus_Ambience"] = "EF0E9D058E8367FE27B19DCBE3D0C205F7960C37D52B90D8DC01E07D945F3826",
        ["Pilot_Ambience"] = "8E1401E60A7542A8E3F63850D97EFC7DF3B20F3817A9B36213706359D3CDBAEC",
        ["Guided_Ambience"] = "3B8227A36AE22C1148DE1312A0248940BE6AC0943A387D95304CD8ACDEF2CD34",
        ["Rest_Ambience"] = "3FB0D2C618B4DC586CE875E105E55E781FD2D4646F317981CEC2FDCE1D204334",
      };

    [Test]
    public void FourCareAmbienceWavesAreDistinctDeterministicPcmAssets()
    {
      var hashes = new HashSet<string>();
      foreach (var name in Names)
      {
        var path = $"{Folder}/{name}.wav";
        Assert.That(File.Exists(path), Is.True, path);
        var bytes = File.ReadAllBytes(path);
        Assert.That(bytes.Length, Is.GreaterThan(44), path);
        using var sha = SHA256.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
        hashes.Add(hash);
        Assert.That(hash, Is.EqualTo(ExpectedSha256[name]), path + " was not generated deterministically.");

        var wave = ReadWave(bytes);
        Assert.That(wave.SampleRate, Is.EqualTo(48000), path);
        Assert.That(wave.Channels, Is.EqualTo(2), path);
        Assert.That(wave.BitsPerSample, Is.EqualTo(16), path);
        Assert.That(wave.Samples.Length, Is.EqualTo(48000 * 12 * 2), path);
        Assert.That(wave.Peak, Is.InRange(0.12f, 0.32f), path);
        Assert.That(wave.Rms, Is.InRange(0.008f, 0.16f), path);
        Assert.That(Math.Abs(wave.DcOffset), Is.LessThan(0.002f), path);
      }
      Assert.That(hashes.Count, Is.EqualTo(Names.Length), "Ambience assets must not be copied waveforms.");
    }

    [Test]
    public void CareAmbienceLoopBoundariesRemainBelowOrdinarySignalMotion()
    {
      foreach (var name in Names)
      {
        var wave = ReadWave(File.ReadAllBytes($"{Folder}/{name}.wav"));
        for (var channel = 0; channel < wave.Channels; channel++)
        {
          var first = wave.Samples[channel];
          var last = wave.Samples[wave.Samples.Length - wave.Channels + channel];
          var boundaryJump = Math.Abs(first - last);
          Assert.That(boundaryJump, Is.LessThanOrEqualTo(1f / short.MaxValue), name);

          double meanAdjacentDelta = 0d;
          var frames = wave.Samples.Length / wave.Channels;
          for (var frame = 1; frame < frames; frame += 47)
          {
            var current = wave.Samples[frame * wave.Channels + channel];
            var previous = wave.Samples[(frame - 1) * wave.Channels + channel];
            meanAdjacentDelta += Math.Abs(current - previous);
          }
          meanAdjacentDelta /= Math.Ceiling((frames - 1) / 47d);
          Assert.That(meanAdjacentDelta, Is.LessThan(0.02), name + " contains excessive high-frequency motion.");
        }
      }
    }

    [Test]
    public void CareAmbiencePairsHaveDifferentWaveShapes()
    {
      var waves = Names.Select(name => ReadWave(File.ReadAllBytes($"{Folder}/{name}.wav"))).ToArray();
      for (var a = 0; a < waves.Length; a++)
      for (var b = a + 1; b < waves.Length; b++)
      {
        var correlation = AbsoluteCorrelation(waves[a].Samples, waves[b].Samples, 89);
        Assert.That(correlation, Is.LessThan(0.90), $"{Names[a]} and {Names[b]} are too similar.");
      }
    }

    [Test]
    public void CareAmbienceUsesMobileFriendlyLoopImportSettings()
    {
      foreach (var name in Names)
      {
        var path = $"{Folder}/{name}.wav";
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        Assert.That(importer, Is.Not.Null, path);
        Assert.That(importer.forceToMono, Is.False, path);
        Assert.That(importer.ambisonic, Is.False, path);
        Assert.That(importer.loadInBackground, Is.True, path);
        var serializedImporter = new SerializedObject(importer);
        var normalize = serializedImporter.FindProperty("m_Normalize");
        if (normalize != null) Assert.That(normalize.boolValue, Is.False, path);
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        Assert.That(clip, Is.Not.Null, path);
        Assert.That(clip.frequency, Is.EqualTo(48000), path);
        Assert.That(clip.channels, Is.EqualTo(2), path);
        Assert.That(clip.length, Is.EqualTo(12f).Within(0.01f), path);
        var settings = importer.defaultSampleSettings;
        Assert.That(settings.loadType, Is.EqualTo(AudioClipLoadType.CompressedInMemory), path);
        Assert.That(settings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis), path);
        Assert.That(settings.quality, Is.EqualTo(0.72f).Within(0.001f), path);
        Assert.That(settings.sampleRateSetting, Is.EqualTo(AudioSampleRateSetting.PreserveSampleRate), path);
        var preloadField = typeof(AudioImporterSampleSettings).GetField("preloadAudioData");
        Assert.That(preloadField, Is.Not.Null,
          "Unity's per-platform AudioImporterSampleSettings must expose preloadAudioData.");
        Assert.That((bool)preloadField.GetValue(settings), Is.True, path);
      }
    }

    [Test]
    public void FourCareAmbiencesResolveFromTheRuntimeResourcesPathsAsDistinctClips()
    {
      var clips = Names.Select(name =>
        Resources.Load<AudioClip>($"CareStation/Audio/Ambience/{name}")).ToArray();

      Assert.That(clips, Has.All.Not.Null,
        "Each authored ambience must be loadable through the exact Resources path used at runtime.");
      Assert.That(clips.Distinct().Count(), Is.EqualTo(Names.Length),
        "Focus, Pilot, Guided and Rest must resolve to four different AudioClip assets.");
      CollectionAssert.AreEquivalent(Names, clips.Select(clip => clip.name));
    }

    private static double AbsoluteCorrelation(float[] a, float[] b, int stride)
    {
      double sumA = 0d, sumB = 0d, sumAA = 0d, sumBB = 0d, sumAB = 0d;
      var count = 0;
      var length = Math.Min(a.Length, b.Length);
      for (var i = 0; i < length; i += stride)
      {
        var av = a[i];
        var bv = b[i];
        sumA += av;
        sumB += bv;
        sumAA += av * av;
        sumBB += bv * bv;
        sumAB += av * bv;
        count++;
      }
      var covariance = sumAB - sumA * sumB / count;
      var varianceA = sumAA - sumA * sumA / count;
      var varianceB = sumBB - sumB * sumB / count;
      return Math.Abs(covariance / Math.Sqrt(Math.Max(1e-20, varianceA * varianceB)));
    }

    private static WaveData ReadWave(byte[] bytes)
    {
      using var stream = new MemoryStream(bytes, false);
      using var reader = new BinaryReader(stream);
      Assert.That(new string(reader.ReadChars(4)), Is.EqualTo("RIFF"));
      reader.ReadInt32();
      Assert.That(new string(reader.ReadChars(4)), Is.EqualTo("WAVE"));
      Assert.That(new string(reader.ReadChars(4)), Is.EqualTo("fmt "));
      Assert.That(reader.ReadInt32(), Is.EqualTo(16));
      Assert.That(reader.ReadInt16(), Is.EqualTo(1));
      var channels = reader.ReadInt16();
      var sampleRate = reader.ReadInt32();
      reader.ReadInt32();
      reader.ReadInt16();
      var bits = reader.ReadInt16();
      Assert.That(new string(reader.ReadChars(4)), Is.EqualTo("data"));
      var dataBytes = reader.ReadInt32();
      var sampleCount = dataBytes / sizeof(short);
      var samples = new float[sampleCount];
      double squareSum = 0d;
      double sum = 0d;
      var peak = 0f;
      for (var i = 0; i < samples.Length; i++)
      {
        samples[i] = reader.ReadInt16() / (float)short.MaxValue;
        peak = Math.Max(peak, Math.Abs(samples[i]));
        sum += samples[i];
        squareSum += samples[i] * samples[i];
      }
      return new WaveData
      {
        Channels = channels,
        SampleRate = sampleRate,
        BitsPerSample = bits,
        Samples = samples,
        Peak = peak,
        Rms = (float)Math.Sqrt(squareSum / samples.Length),
        DcOffset = (float)(sum / samples.Length),
      };
    }

    private sealed class WaveData
    {
      public int Channels;
      public int SampleRate;
      public int BitsPerSample;
      public float[] Samples;
      public float Peak;
      public float Rms;
      public float DcOffset;
    }
  }
}

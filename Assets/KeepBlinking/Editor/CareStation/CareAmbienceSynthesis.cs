using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KeepBlinking.EditorTools
{
  /// <summary>
  /// Deterministic, offline synthesis for the four low-stimulation care beds.
  /// This class deliberately has no Unity dependency so the exact production
  /// algorithm can also be invoked by build tooling without running the game.
  /// </summary>
  public static class CareAmbienceSynthesis
  {
    public const int SampleRate = 48000;
    public const int Channels = 2;
    public const int DurationSeconds = 12;
    public const int FrameCount = SampleRate * DurationSeconds;

    public const string AssetFolder = "Assets/KeepBlinking/Resources/CareStation/Audio/Ambience";
    public const string ParameterPath = "Tools/AudioSources/CareStation/CareAmbienceParameters.json";

    public sealed class Profile
    {
      public readonly string Name;
      public readonly int Seed;
      public readonly string Character;
      public readonly float TargetPeak;
      public readonly int[] SmoothingRadii;
      public readonly float[] SmoothingWeights;

      public Profile(string name, int seed, string character, float targetPeak,
        int[] smoothingRadii, float[] smoothingWeights)
      {
        Name = name;
        Seed = seed;
        Character = character;
        TargetPeak = targetPeak;
        SmoothingRadii = smoothingRadii;
        SmoothingWeights = smoothingWeights;
      }
    }

    private static readonly Profile[] ProfileSet =
    {
      new Profile(
        "Focus_Ambience", 0x1564A11,
        "soft air with a slow low-frequency breathing pulse",
        0.275f, new[] { 8, 37, 181 }, new[] { 0.52f, 0.31f, 0.17f }),
      new Profile(
        "Pilot_Ambience", 0x2471B29,
        "filtered pink wind with a very light wooden navigation pulse",
        0.255f, new[] { 5, 23, 97, 389 }, new[] { 0.34f, 0.29f, 0.23f, 0.14f }),
      new Profile(
        "Guided_Ambience", 0x35C2D37,
        "rounded pink air with a slow stereo orbit and sustained soft partials",
        0.265f, new[] { 11, 53, 257 }, new[] { 0.42f, 0.34f, 0.24f }),
      new Profile(
        "Rest_Ambience", 0x46D3E43,
        "very soft brown air with a quiet low chord",
        0.140f, new[] { 31, 151, 751, 1801 }, new[] { 0.24f, 0.31f, 0.29f, 0.16f }),
    };

    public static IReadOnlyList<Profile> Profiles => ProfileSet;

    public static void GenerateAll(string projectRoot)
    {
      if (string.IsNullOrWhiteSpace(projectRoot))
        throw new ArgumentException("A project root is required.", nameof(projectRoot));

      var assetDirectory = Path.Combine(projectRoot, AssetFolder.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(assetDirectory);
      foreach (var profile in ProfileSet)
      {
        var samples = GenerateInterleavedSamples(profile);
        WritePcm16WaveAtomic(Path.Combine(assetDirectory, profile.Name + ".wav"), samples);
      }

      var parameterFile = Path.Combine(projectRoot, ParameterPath.Replace('/', Path.DirectorySeparatorChar));
      Directory.CreateDirectory(Path.GetDirectoryName(parameterFile) ?? projectRoot);
      WriteUtf8Atomic(parameterFile, BuildParameterManifest());
    }

    public static float[] GenerateInterleavedSamples(Profile profile)
    {
      if (profile == null) throw new ArgumentNullException(nameof(profile));
      var left = BuildNoiseBed(profile, profile.Seed);
      var right = BuildNoiseBed(profile, unchecked(profile.Seed * 1664525 + 1013904223));
      var output = new float[FrameCount * Channels];

      for (var frame = 0; frame < FrameCount; frame++)
      {
        var phase = frame / (double)FrameCount;
        AddSignature(profile.Name, frame, phase, ref left[frame], ref right[frame]);
        output[frame * 2] = left[frame];
        output[frame * 2 + 1] = right[frame];
      }

      RemoveDcAndNormalize(output, profile.TargetPeak);
      // The synthesis is cyclic throughout. Matching the final PCM frame to
      // the first removes the final quantisation-step discontinuity as well.
      output[output.Length - 2] = output[0];
      output[output.Length - 1] = output[1];
      return output;
    }

    private static float[] BuildNoiseBed(Profile profile, int seed)
    {
      var white = new float[FrameCount];
      var random = new DeterministicRandom(seed);
      for (var i = 0; i < white.Length; i++) white[i] = random.NextSigned();

      var mixed = new float[FrameCount];
      for (var layer = 0; layer < profile.SmoothingRadii.Length; layer++)
      {
        var smoothed = CircularMovingAverage(white, profile.SmoothingRadii[layer]);
        var weight = profile.SmoothingWeights[layer];
        for (var i = 0; i < mixed.Length; i++) mixed[i] += smoothed[i] * weight;
      }
      return mixed;
    }

    private static float[] CircularMovingAverage(float[] source, int radius)
    {
      var result = new float[source.Length];
      var width = radius * 2 + 1;
      double sum = 0d;
      for (var offset = -radius; offset <= radius; offset++)
        sum += source[Wrap(offset, source.Length)];

      for (var i = 0; i < source.Length; i++)
      {
        result[i] = (float)(sum / width);
        sum -= source[Wrap(i - radius, source.Length)];
        sum += source[Wrap(i + radius + 1, source.Length)];
      }
      return result;
    }

    private static void AddSignature(string profileName, int frame, double phase, ref float left, ref float right)
    {
      var seconds = frame / (double)SampleRate;
      switch (profileName)
      {
        case "Focus_Ambience":
        {
          var breath = 0.54 + 0.18 * Math.Sin(Tau * 3d * phase - 0.4);
          var low = Math.Sin(Tau * 72d * seconds) * breath;
          var softFifth = Math.Sin(Tau * 108d * seconds + 0.7);
          left = left * 0.105f + (float)(low * 0.030 + softFifth * 0.010);
          right = right * 0.105f + (float)(low * 0.029 + softFifth * 0.011);
          break;
        }
        case "Pilot_Ambience":
        {
          var pan = Math.Sin(Tau * phase);
          var woodPulse = SoftPulse(phase, 6) * Math.Sin(Tau * 168d * seconds);
          left = left * 0.115f + (float)(woodPulse * (0.018 - 0.004 * pan));
          right = right * 0.115f + (float)(woodPulse * (0.018 + 0.004 * pan));
          break;
        }
        case "Guided_Ambience":
        {
          var orbit = Math.Sin(Tau * phase);
          var toneA = Math.Sin(Tau * 174d * seconds + 0.15);
          var toneB = Math.Sin(Tau * 261d * seconds + 1.1);
          left = left * 0.10f + (float)(toneA * (0.017 + 0.005 * orbit) + toneB * 0.008);
          right = right * 0.10f + (float)(toneA * (0.017 - 0.005 * orbit) + toneB * 0.008);
          break;
        }
        case "Rest_Ambience":
        {
          var slow = 0.72 + 0.08 * Math.Sin(Tau * 2d * phase);
          var chord = Math.Sin(Tau * 110d * seconds) * 0.017 +
                      Math.Sin(Tau * 165d * seconds + 0.8) * 0.011 +
                      Math.Sin(Tau * 220d * seconds + 1.6) * 0.006;
          left = left * 0.09f + (float)(chord * slow);
          right = right * 0.09f + (float)(chord * (slow * 0.97));
          break;
        }
        default:
          throw new InvalidOperationException("Unknown care ambience profile: " + profileName);
      }
    }

    private static double SoftPulse(double phase, int pulseCount)
    {
      var local = phase * pulseCount;
      local -= Math.Floor(local);
      if (local >= 0.16) return 0d;
      var envelope = Math.Sin(Math.PI * local / 0.16);
      return envelope * envelope;
    }

    private static void RemoveDcAndNormalize(float[] interleaved, float targetPeak)
    {
      double leftMean = 0d;
      double rightMean = 0d;
      for (var i = 0; i < interleaved.Length; i += 2)
      {
        leftMean += interleaved[i];
        rightMean += interleaved[i + 1];
      }
      leftMean /= FrameCount;
      rightMean /= FrameCount;

      var peak = 0f;
      for (var i = 0; i < interleaved.Length; i += 2)
      {
        interleaved[i] -= (float)leftMean;
        interleaved[i + 1] -= (float)rightMean;
        peak = Math.Max(peak, Math.Abs(interleaved[i]));
        peak = Math.Max(peak, Math.Abs(interleaved[i + 1]));
      }
      if (peak <= 1e-7f) throw new InvalidOperationException("Generated ambience was silent.");

      var gain = targetPeak / peak;
      for (var i = 0; i < interleaved.Length; i++) interleaved[i] *= gain;
    }

    private static void WritePcm16WaveAtomic(string path, float[] interleaved)
    {
      var tempPath = path + ".tmp";
      using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
      using (var writer = new BinaryWriter(stream, Encoding.ASCII, false))
      {
        var dataBytes = interleaved.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)Channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * Channels * sizeof(short));
        writer.Write((short)(Channels * sizeof(short)));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (var i = 0; i < interleaved.Length; i++)
        {
          var sample = Math.Max(-1f, Math.Min(1f, interleaved[i]));
          writer.Write((short)Math.Round(sample * short.MaxValue));
        }
      }
      ReplaceFile(tempPath, path);
    }

    private static string BuildParameterManifest()
    {
      var builder = new StringBuilder();
      builder.AppendLine("{");
      builder.AppendLine("  \"generator\": \"CareAmbienceSynthesis\",");
      builder.AppendLine("  \"version\": 1,");
      builder.AppendLine("  \"sampleRate\": " + SampleRate + ",");
      builder.AppendLine("  \"channels\": " + Channels + ",");
      builder.AppendLine("  \"durationSeconds\": " + DurationSeconds + ",");
      builder.AppendLine("  \"format\": \"PCM16 WAV\",");
      builder.AppendLine("  \"loopMethod\": \"cyclic FIR noise plus integer-cycle tonal components\",");
      builder.AppendLine("  \"profiles\": [");
      for (var i = 0; i < ProfileSet.Length; i++)
      {
        var profile = ProfileSet[i];
        builder.AppendLine("    {");
        builder.AppendLine("      \"name\": \"" + profile.Name + "\",");
        builder.AppendLine("      \"seed\": " + profile.Seed + ",");
        builder.AppendLine("      \"character\": \"" + profile.Character + "\",");
        builder.AppendLine("      \"targetPeak\": " + profile.TargetPeak.ToString("0.000", CultureInfo.InvariantCulture) + ",");
        builder.AppendLine("      \"smoothingRadii\": [" + string.Join(", ", profile.SmoothingRadii) + "],");
        var weights = Array.ConvertAll(profile.SmoothingWeights,
          value => value.ToString("0.00", CultureInfo.InvariantCulture));
        builder.AppendLine("      \"smoothingWeights\": [" + string.Join(", ", weights) + "]");
        builder.Append("    }");
        builder.AppendLine(i + 1 == ProfileSet.Length ? string.Empty : ",");
      }
      builder.AppendLine("  ]");
      builder.AppendLine("}");
      return builder.ToString();
    }

    private static void WriteUtf8Atomic(string path, string contents)
    {
      var tempPath = path + ".tmp";
      File.WriteAllText(tempPath, contents, new UTF8Encoding(false));
      ReplaceFile(tempPath, path);
    }

    private static void ReplaceFile(string tempPath, string finalPath)
    {
      if (File.Exists(finalPath)) File.Delete(finalPath);
      File.Move(tempPath, finalPath);
    }

    private static int Wrap(int value, int length)
    {
      value %= length;
      return value < 0 ? value + length : value;
    }

    private const double Tau = Math.PI * 2d;

    private struct DeterministicRandom
    {
      private uint _state;

      public DeterministicRandom(int seed)
      {
        _state = unchecked((uint)seed);
        if (_state == 0u) _state = 0x6D2B79F5u;
      }

      public float NextSigned()
      {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return ((_state >> 8) / 8388607.5f) - 1f;
      }
    }
  }
}

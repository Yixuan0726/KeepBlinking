using System;
using UnityEngine;

namespace KeepBlinking.Input
{
  public enum GazeProviderMode
  {
    Current,
    L2CS,
    Compare,
  }

  public readonly struct OffScreenGazeDirectionSample
  {
    public OffScreenGazeDirectionSample(
      double timestampSeconds,
      Vector2 rawDirectionDegrees,
      Vector2 centeredDirectionDegrees,
      bool trackingValid,
      float inferenceLatencyMilliseconds)
    {
      TimestampSeconds = timestampSeconds;
      RawDirectionDegrees = rawDirectionDegrees;
      CenteredDirectionDegrees = centeredDirectionDegrees;
      TrackingValid = trackingValid;
      InferenceLatencyMilliseconds = inferenceLatencyMilliseconds;
    }

    public double TimestampSeconds { get; }
    public Vector2 RawDirectionDegrees { get; }
    public Vector2 CenteredDirectionDegrees { get; }
    public bool TrackingValid { get; }
    public float InferenceLatencyMilliseconds { get; }
  }

  public readonly struct GazeProviderSample
  {
    public GazeProviderSample(
      string providerName,
      double timestampSeconds,
      bool trackingValid,
      Vector2 rawValue,
      bool hasScreenPosition,
      Vector2 normalizedScreenPosition,
      Vector2 directionDegrees,
      float inferenceLatencyMilliseconds)
    {
      ProviderName = providerName;
      TimestampSeconds = timestampSeconds;
      TrackingValid = trackingValid;
      RawValue = rawValue;
      HasScreenPosition = hasScreenPosition;
      NormalizedScreenPosition = normalizedScreenPosition;
      DirectionDegrees = directionDegrees;
      InferenceLatencyMilliseconds = inferenceLatencyMilliseconds;
    }

    public string ProviderName { get; }
    public double TimestampSeconds { get; }
    public bool TrackingValid { get; }
    public Vector2 RawValue { get; }
    public bool HasScreenPosition { get; }
    public Vector2 NormalizedScreenPosition { get; }
    public Vector2 DirectionDegrees { get; }
    public float InferenceLatencyMilliseconds { get; }

    public Vector2 ScreenPosition => new Vector2(
      NormalizedScreenPosition.x * Screen.width,
      NormalizedScreenPosition.y * Screen.height);
  }

  public interface IGazePositionProvider : IDisposable
  {
    string ProviderName { get; }
    bool IsAvailable { get; }
    string FailureReason { get; }
    CalibratedScreenGazeMapper Mapper { get; }
    void Tick();
    bool TryGetLatest(out GazeProviderSample sample);
  }
}

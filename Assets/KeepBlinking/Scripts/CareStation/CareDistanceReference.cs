using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// Captures one immutable gesture origin from fresh camera samples. The
  /// median reference is never fed by the later smoothed gesture signal.
  /// </summary>
  public sealed class CareDistanceReferenceSampler
  {
    private readonly List<float> _samples = new List<float>(24);
    private readonly float _captureSeconds;
    private readonly int _minimumSamples;
    private long _lastSequence = long.MinValue;
    private float _captureStartedAt = -1f;
    private float _lastSampleAt = -1f;

    public CareDistanceReferenceSampler(float captureSeconds = 0.5f, int minimumSamples = 5)
    {
      _captureSeconds = Mathf.Clamp(captureSeconds, 0.25f, 0.4f);
      _minimumSamples = Mathf.Max(3, minimumSamples);
    }

    public bool IsComplete { get; private set; }
    public float ReferenceScale { get; private set; }
    public int SampleCount => _samples.Count;

    public void Reset()
    {
      _samples.Clear();
      _lastSequence = long.MinValue;
      _captureStartedAt = -1f;
      _lastSampleAt = -1f;
      IsComplete = false;
      ReferenceScale = 0f;
    }

    public bool Restore(float referenceScale, bool valid)
    {
      Reset();
      if (!valid || !IsValidScale(referenceScale)) return false;
      ReferenceScale = referenceScale;
      IsComplete = true;
      return true;
    }

    public bool AddFreshSample(long sequence, float scale, float nowSeconds, bool trackingValid)
    {
      if (IsComplete) return true;
      if (!trackingValid || !IsValidScale(scale))
      {
        Reset();
        return false;
      }
      if (sequence == _lastSequence) return false;
      if (_lastSampleAt >= 0f && nowSeconds - _lastSampleAt > 0.35f)
      {
        _samples.Clear();
        _captureStartedAt = -1f;
      }
      _lastSequence = sequence;
      if (_captureStartedAt < 0f) _captureStartedAt = nowSeconds;
      _lastSampleAt = nowSeconds;
      _samples.Add(scale);
      if (Mathf.Max(0f, nowSeconds - _captureStartedAt) < _captureSeconds || _samples.Count < _minimumSamples) return false;

      _samples.Sort();
      var middle = _samples.Count / 2;
      ReferenceScale = (_samples.Count & 1) == 0
        ? (_samples[middle - 1] + _samples[middle]) * 0.5f
        : _samples[middle];
      IsComplete = IsValidScale(ReferenceScale);
      return IsComplete;
    }

    public static bool IsValidScale(float scale)
    {
      return scale > 0.000001f && !float.IsNaN(scale) && !float.IsInfinity(scale);
    }

    public static bool HasMeaningfulScaleUpdates(
      float minimum,
      float maximum,
      float referenceScale,
      float minimumRelativeChange = 0.0005f)
    {
      if (!IsValidScale(minimum) || !IsValidScale(maximum) || !IsValidScale(referenceScale)) return false;
      return (maximum - minimum) / referenceScale >= Mathf.Max(0.00001f, minimumRelativeChange);
    }
  }

  /// <summary>
  /// One low-latency relative distance step. It owns no global baseline: the
  /// caller supplies the immutable median reference captured for this step.
  /// </summary>
  public sealed class CareRelativeDistanceStep
  {
    private readonly float _deadZone;
    private readonly float _completeThreshold;
    private readonly float _holdSeconds;
    private readonly float _progressFallSeconds;
    private float _stableSeconds;

    public CareRelativeDistanceStep(
      CareDistanceDirection direction,
      float deadZone = 0.02f,
      float completeThreshold = 0.06f,
      float holdSeconds = 0.25f,
      float progressFallSeconds = 0.25f,
      float initialProgress = 0f,
      float initialStableSeconds = 0f)
    {
      Direction = direction;
      _deadZone = Mathf.Clamp(deadZone, 0f, 0.2f);
      _completeThreshold = Mathf.Max(_deadZone + 0.005f, completeThreshold);
      _holdSeconds = Mathf.Max(0.05f, holdSeconds);
      _progressFallSeconds = Mathf.Max(0.05f, progressFallSeconds);
      Progress = Mathf.Clamp01(initialProgress);
      _stableSeconds = Mathf.Max(0f, initialStableSeconds);
    }

    public CareDistanceDirection Direction { get; }
    public float DirectionDelta { get; private set; }
    public float Progress { get; private set; }
    public float StableSeconds => _stableSeconds;
    public bool Completed { get; private set; }

    public bool Advance(
      float currentScale,
      float referenceScale,
      float sampleDelta,
      bool trackingValid,
      bool sampleFresh)
    {
      if (Completed || !sampleFresh) return false;
      if (!trackingValid || !CareDistanceReferenceSampler.IsValidScale(currentScale) ||
          !CareDistanceReferenceSampler.IsValidScale(referenceScale))
      {
        _stableSeconds = 0f;
        return false;
      }

      var ratio = currentScale / referenceScale;
      if (!CareDistanceReferenceSampler.IsValidScale(ratio))
      {
        _stableSeconds = 0f;
        return false;
      }
      DirectionDelta = Direction == CareDistanceDirection.Closer ? ratio - 1f : 1f - ratio;
      var target = Mathf.Clamp01((DirectionDelta - _deadZone) /
                                 Mathf.Max(0.005f, _completeThreshold - _deadZone));
      var delta = Mathf.Clamp(sampleDelta, 0f, 0.25f);
      Progress = target >= Progress
        ? target
        : Mathf.MoveTowards(Progress, target, delta / _progressFallSeconds);
      _stableSeconds = DirectionDelta >= _completeThreshold
        ? _stableSeconds + delta
        : 0f;
      if (_stableSeconds < _holdSeconds) return false;
      Completed = true;
      Progress = 1f;
      _stableSeconds = 0f;
      return true;
    }

    public void FreezeForTrackingLoss()
    {
      // Keep visible progress and the immutable step reference, but require a
      // fresh stable hold after tracking returns.
      _stableSeconds = 0f;
    }
  }

  /// <summary>Relative Push Away/return gate for one collection phase.</summary>
  public sealed class CarePushAwayGesture
  {
    private readonly float _pushRatio;
    private readonly float _pushHoldSeconds;
    private readonly Vector2 _neutralRange;
    private readonly float _returnHoldSeconds;
    private float _pushHeld;
    private float _returnHeld;

    public CarePushAwayGesture(
      float pushRatio = 0.88f,
      float pushHoldSeconds = 0.5f,
      float neutralMin = 0.92f,
      float neutralMax = 1.08f,
      float returnHoldSeconds = 0.4f)
    {
      _pushRatio = Mathf.Clamp(pushRatio, 0.5f, 0.99f);
      _pushHoldSeconds = Mathf.Max(0.1f, pushHoldSeconds);
      _neutralRange = new Vector2(Mathf.Min(neutralMin, neutralMax), Mathf.Max(neutralMin, neutralMax));
      _returnHoldSeconds = Mathf.Max(0.1f, returnHoldSeconds);
    }

    public bool PushCompleted { get; private set; }
    public bool ReturnCompleted { get; private set; }
    public float PushStableSeconds => _pushHeld;
    public float ReturnStableSeconds => _returnHeld;
    public Vector2 ReturnRange => _neutralRange;

    public bool AdvancePush(float ratio, float delta, bool trackingValid)
    {
      if (PushCompleted) return false;
      if (!trackingValid || !CareDistanceReferenceSampler.IsValidScale(ratio))
      {
        _pushHeld = 0f;
        return false;
      }
      _pushHeld = ratio <= _pushRatio ? _pushHeld + Mathf.Max(0f, delta) : 0f;
      if (_pushHeld < _pushHoldSeconds) return false;
      PushCompleted = true;
      _pushHeld = 0f;
      return true;
    }

    public bool AdvanceReturn(float ratio, float delta, bool trackingValid)
    {
      if (ReturnCompleted) return false;
      if (!trackingValid || !CareDistanceReferenceSampler.IsValidScale(ratio))
      {
        _returnHeld = 0f;
        return false;
      }
      var neutral = ratio >= _neutralRange.x && ratio <= _neutralRange.y;
      _returnHeld = neutral ? _returnHeld + Mathf.Max(0f, delta) : 0f;
      if (_returnHeld < _returnHoldSeconds) return false;
      ReturnCompleted = true;
      _returnHeld = 0f;
      return true;
    }
  }
}

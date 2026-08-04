using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Input
{
  public sealed class CalibratedScreenGazeMapper
  {
    private Vector2 _scale = Vector2.one;
    private Vector2 _offset;

    public bool IsCalibrated { get; private set; }
    public string CalibrationVersion { get; private set; } = "uncalibrated";
    public Vector2 Scale => _scale;
    public Vector2 Offset => _offset;

    public bool SetCalibration(IReadOnlyList<Vector2> rawSamples, IReadOnlyList<Vector2> normalizedTargets)
    {
      if (rawSamples == null || normalizedTargets == null || rawSamples.Count < 2 || rawSamples.Count != normalizedTargets.Count)
      {
        Reset();
        return false;
      }

      if (!TryFitAxis(rawSamples, normalizedTargets, true, out var scaleX, out var offsetX) ||
          !TryFitAxis(rawSamples, normalizedTargets, false, out var scaleY, out var offsetY))
      {
        Reset();
        return false;
      }

      _scale = new Vector2(scaleX, scaleY);
      _offset = new Vector2(offsetX, offsetY);
      IsCalibrated = true;
      CalibrationVersion = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
      return true;
    }

    public bool SetCalibrationParameters(Vector2 scale, Vector2 offset, string calibrationVersion)
    {
      if (!IsFinite(scale.x) || !IsFinite(scale.y) || !IsFinite(offset.x) || !IsFinite(offset.y) ||
          Mathf.Abs(scale.x) <= 0.000001f || Mathf.Abs(scale.y) <= 0.000001f)
      {
        Reset();
        return false;
      }

      _scale = scale;
      _offset = offset;
      IsCalibrated = true;
      CalibrationVersion = string.IsNullOrWhiteSpace(calibrationVersion) ? "imported" : calibrationVersion;
      return true;
    }

    public bool TryMap(Vector2 rawValue, out Vector2 normalizedScreenPosition)
    {
      if (!IsCalibrated || !IsFinite(rawValue.x) || !IsFinite(rawValue.y))
      {
        normalizedScreenPosition = default;
        return false;
      }

      normalizedScreenPosition = new Vector2(
        Mathf.Clamp01(rawValue.x * _scale.x + _offset.x),
        Mathf.Clamp01(rawValue.y * _scale.y + _offset.y));
      return true;
    }

    public void Reset()
    {
      _scale = Vector2.one;
      _offset = Vector2.zero;
      IsCalibrated = false;
      CalibrationVersion = "uncalibrated";
    }

    private static bool TryFitAxis(
      IReadOnlyList<Vector2> rawSamples,
      IReadOnlyList<Vector2> normalizedTargets,
      bool horizontal,
      out float scale,
      out float offset)
    {
      var rawMean = 0f;
      var targetMean = 0f;
      for (var i = 0; i < rawSamples.Count; i++)
      {
        rawMean += horizontal ? rawSamples[i].x : rawSamples[i].y;
        targetMean += horizontal ? normalizedTargets[i].x : normalizedTargets[i].y;
      }

      rawMean /= rawSamples.Count;
      targetMean /= rawSamples.Count;

      var numerator = 0f;
      var denominator = 0f;
      for (var i = 0; i < rawSamples.Count; i++)
      {
        var raw = (horizontal ? rawSamples[i].x : rawSamples[i].y) - rawMean;
        var target = (horizontal ? normalizedTargets[i].x : normalizedTargets[i].y) - targetMean;
        numerator += raw * target;
        denominator += raw * raw;
      }

      if (denominator <= 0.000001f)
      {
        scale = 1f;
        offset = 0f;
        return false;
      }

      scale = numerator / denominator;
      offset = targetMean - scale * rawMean;
      return IsFinite(scale) && IsFinite(offset) && Mathf.Abs(scale) > 0.000001f;
    }

    private static bool IsFinite(float value)
    {
      return !float.IsNaN(value) && !float.IsInfinity(value);
    }
  }
}

using System;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Mediapipe.Tasks.Vision.FaceLandmarker;
using UnityEngine;

namespace KeepBlinking.Input
{
  public struct EyeInputDebugSnapshot
  {
    public bool FaceDetected;
    public int FaceCount;
    public int LandmarkCount;

    public UnityEngine.Rect FaceRect;
    public float FaceArea;
    public float SmoothedFaceArea;
    public float FaceAreaDelta;
    public bool FaceMovingAway;

    public bool HasGazeScreenPosition;
    public Vector2 GazeScreenPosition;

    public float LeftEyeOpen;
    public float RightEyeOpen;
    public float LeftBlinkScore;
    public float RightBlinkScore;
    public float LeftEyeAspectRatio;
    public float RightEyeAspectRatio;

    public bool IsBlinking;
    public bool BlinkStarted;
    public int BlinkCount;
    public float LastBlinkStartedSeconds;
    public bool IsHardSqueeze;
    public bool HasHeadPose;
    public float HeadYawDegrees;
    public float HeadPitchDegrees;
    public float LastUpdateSeconds;
  }

  public static class EyeInputDebugState
  {
    public const float BlinkOpenThreshold = 0.35f;
    public const float NaturalBlinkBlendshapeThreshold = 0.42f;
    public const float NaturalBlinkLandmarkOpenThreshold = 0.52f;
    public const float HardSqueezeBlinkThreshold = 0.82f;
    public const float MovingAwayDeltaThreshold = -0.0015f;

    private static readonly object _lock = new object();
    private static readonly DateTime _startTime = DateTime.UtcNow;

    private static EyeInputDebugSnapshot _latest;
    private static bool _lastBlinking;
    private static int _blinkCount;
    private static float _lastBlinkStartedSeconds = -1f;
    private static bool _hasSmoothedFaceArea;
    private static float _smoothedFaceArea;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private static float _nextDiagnosticLogSeconds;
#endif

    public static EyeInputDebugSnapshot Latest
    {
      get
      {
        lock (_lock)
        {
          return _latest;
        }
      }
    }

    public static void Clear()
    {
      var now = SecondsSinceStart();
      lock (_lock)
      {
        _latest = new EyeInputDebugSnapshot
        {
          BlinkCount = _blinkCount,
          LastBlinkStartedSeconds = _lastBlinkStartedSeconds,
          LastUpdateSeconds = now,
        };
        _lastBlinking = false;
        _hasSmoothedFaceArea = false;
        _smoothedFaceArea = 0f;
      }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (now >= _nextDiagnosticLogSeconds)
      {
        _nextDiagnosticLogSeconds = now + 1f;
        Debug.Log($"Eye input sample: face=false blinkCount={_blinkCount}");
      }
#endif
    }

    public static void UpdateFrom(FaceLandmarkerResult result)
    {
      var faceLandmarks = result.faceLandmarks;
      if (faceLandmarks == null || faceLandmarks.Count == 0 || faceLandmarks[0].landmarks == null || faceLandmarks[0].landmarks.Count == 0)
      {
        Clear();
        return;
      }

      var landmarks = faceLandmarks[0].landmarks;
      var faceRect = CalculateFaceRect(landmarks);
      var faceArea = faceRect.width * faceRect.height;

      var leftEar = CalculateEyeAspectRatio(landmarks, 33, 133, 159, 145, 158, 153);
      var rightEar = CalculateEyeAspectRatio(landmarks, 362, 263, 386, 374, 385, 380);
      var gazeScreenPosition = EstimateGazeScreenPosition(landmarks, faceRect);
      var leftOpenByLandmarks = NormalizeEar(leftEar);
      var rightOpenByLandmarks = NormalizeEar(rightEar);

      var leftBlinkScore = 1f - leftOpenByLandmarks;
      var rightBlinkScore = 1f - rightOpenByLandmarks;
      var leftSquintScore = 0f;
      var rightSquintScore = 0f;
      var hasBlendshapes = TryReadBlendshapes(result, ref leftBlinkScore, ref rightBlinkScore, ref leftSquintScore, ref rightSquintScore);

      var leftEyeOpen = Mathf.Clamp01(1f - leftBlinkScore);
      var rightEyeOpen = Mathf.Clamp01(1f - rightBlinkScore);
      // Face blendshapes are more stable for natural blinks, while the landmark
      // fallback uses a wider threshold because a short blink may only be sampled
      // once at webcam frame rates. Both eyes must agree, preventing winks and
      // single-landmark noise from becoming gameplay blinks.
      var isBlinking = hasBlendshapes
        ? leftBlinkScore >= NaturalBlinkBlendshapeThreshold && rightBlinkScore >= NaturalBlinkBlendshapeThreshold
        : leftOpenByLandmarks <= NaturalBlinkLandmarkOpenThreshold && rightOpenByLandmarks <= NaturalBlinkLandmarkOpenThreshold;
      var averageBlink = (leftBlinkScore + rightBlinkScore) * 0.5f;
      var averageSquint = (leftSquintScore + rightSquintScore) * 0.5f;
      var hasHeadPose = TryGetHeadPose(result, out var headYawDegrees, out var headPitchDegrees);

      var blinkStartedForDiagnostics = false;
      var shouldLogDiagnostics = false;
      EyeInputDebugSnapshot diagnosticSnapshot = default;
      lock (_lock)
      {
        var now = SecondsSinceStart();
        var blinkStarted = isBlinking && !_lastBlinking;
        if (blinkStarted)
        {
          _blinkCount++;
          _lastBlinkStartedSeconds = now;
        }

        var previousSmoothedArea = _smoothedFaceArea;
        if (_hasSmoothedFaceArea)
        {
          _smoothedFaceArea = Mathf.Lerp(_smoothedFaceArea, faceArea, 0.25f);
        }
        else
        {
          _smoothedFaceArea = faceArea;
          previousSmoothedArea = faceArea;
          _hasSmoothedFaceArea = true;
        }

        var areaDelta = _smoothedFaceArea - previousSmoothedArea;
        _latest = new EyeInputDebugSnapshot
        {
          FaceDetected = true,
          FaceCount = faceLandmarks.Count,
          LandmarkCount = landmarks.Count,
          FaceRect = faceRect,
          FaceArea = faceArea,
          SmoothedFaceArea = _smoothedFaceArea,
          FaceAreaDelta = areaDelta,
          FaceMovingAway = areaDelta <= MovingAwayDeltaThreshold,
          HasGazeScreenPosition = true,
          GazeScreenPosition = gazeScreenPosition,
          LeftEyeOpen = hasBlendshapes ? leftEyeOpen : leftOpenByLandmarks,
          RightEyeOpen = hasBlendshapes ? rightEyeOpen : rightOpenByLandmarks,
          LeftBlinkScore = leftBlinkScore,
          RightBlinkScore = rightBlinkScore,
          LeftEyeAspectRatio = leftEar,
          RightEyeAspectRatio = rightEar,
          IsBlinking = isBlinking,
          BlinkStarted = blinkStarted,
          BlinkCount = _blinkCount,
          LastBlinkStartedSeconds = _lastBlinkStartedSeconds,
          IsHardSqueeze = averageBlink >= HardSqueezeBlinkThreshold && averageSquint >= 0.2f,
          HasHeadPose = hasHeadPose,
          HeadYawDegrees = headYawDegrees,
          HeadPitchDegrees = headPitchDegrees,
          LastUpdateSeconds = now,
        };
        _lastBlinking = isBlinking;
        blinkStartedForDiagnostics = blinkStarted;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        shouldLogDiagnostics = now >= _nextDiagnosticLogSeconds;
        if (shouldLogDiagnostics)
        {
          _nextDiagnosticLogSeconds = now + 1f;
        }
#endif
        diagnosticSnapshot = _latest;
      }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (blinkStartedForDiagnostics || shouldLogDiagnostics)
      {
        var eventName = blinkStartedForDiagnostics ? "BLINK STARTED" : "sample";
        Debug.Log(
          $"Eye input {eventName}: face=true blendshapes={hasBlendshapes} " +
          $"L={diagnosticSnapshot.LeftEyeOpen:F3} R={diagnosticSnapshot.RightEyeOpen:F3} " +
          $"blinkL={diagnosticSnapshot.LeftBlinkScore:F3} blinkR={diagnosticSnapshot.RightBlinkScore:F3} " +
          $"earL={diagnosticSnapshot.LeftEyeAspectRatio:F3} earR={diagnosticSnapshot.RightEyeAspectRatio:F3} " +
          $"blinking={diagnosticSnapshot.IsBlinking} blinkCount={diagnosticSnapshot.BlinkCount}");
      }
#endif
    }

    private static UnityEngine.Rect CalculateFaceRect(IReadOnlyList<NormalizedLandmark> landmarks)
    {
      var minX = float.PositiveInfinity;
      var minY = float.PositiveInfinity;
      var maxX = float.NegativeInfinity;
      var maxY = float.NegativeInfinity;

      for (var i = 0; i < landmarks.Count; i++)
      {
        var landmark = landmarks[i];
        minX = Mathf.Min(minX, landmark.x);
        minY = Mathf.Min(minY, landmark.y);
        maxX = Mathf.Max(maxX, landmark.x);
        maxY = Mathf.Max(maxY, landmark.y);
      }

      return new UnityEngine.Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static float CalculateEyeAspectRatio(
      IReadOnlyList<NormalizedLandmark> landmarks,
      int outerCorner,
      int innerCorner,
      int upperA,
      int lowerA,
      int upperB,
      int lowerB)
    {
      var highestIndex = Mathf.Max(Mathf.Max(outerCorner, innerCorner), Mathf.Max(Mathf.Max(upperA, lowerA), Mathf.Max(upperB, lowerB)));
      if (landmarks.Count <= highestIndex)
      {
        return 0f;
      }

      var eyeWidth = Distance2D(landmarks[outerCorner], landmarks[innerCorner]);
      if (eyeWidth <= 0.0001f)
      {
        return 0f;
      }

      var openA = Distance2D(landmarks[upperA], landmarks[lowerA]);
      var openB = Distance2D(landmarks[upperB], landmarks[lowerB]);
      return ((openA + openB) * 0.5f) / eyeWidth;
    }

    private static Vector2 EstimateGazeScreenPosition(IReadOnlyList<NormalizedLandmark> landmarks, UnityEngine.Rect faceRect)
    {
      if (TryEstimateGazeFromIris(landmarks, out var normalizedGaze))
      {
        return NormalizedToScreenPosition(normalizedGaze);
      }

      return NormalizedToScreenPosition(new Vector2(faceRect.center.x, faceRect.center.y));
    }

    private static bool TryEstimateGazeFromIris(IReadOnlyList<NormalizedLandmark> landmarks, out Vector2 normalizedGaze)
    {
      normalizedGaze = default;
      if (landmarks.Count <= 477)
      {
        return false;
      }

      var leftEyeCenter = AverageLandmarks2D(landmarks, 33, 133, 159, 145);
      var rightEyeCenter = AverageLandmarks2D(landmarks, 362, 263, 386, 374);
      var leftIrisCenter = AverageLandmarks2D(landmarks, 468, 469, 470, 471, 472);
      var rightIrisCenter = AverageLandmarks2D(landmarks, 473, 474, 475, 476, 477);

      var eyeCenter = (leftEyeCenter + rightEyeCenter) * 0.5f;
      var irisCenter = (leftIrisCenter + rightIrisCenter) * 0.5f;
      var eyeSpan = Mathf.Max(0.001f, Mathf.Abs(landmarks[263].x - landmarks[33].x));
      var verticalSpan = Mathf.Max(0.001f, Mathf.Abs(landmarks[10].y - landmarks[152].y));
      var irisOffset = irisCenter - eyeCenter;

      normalizedGaze = new Vector2(
        Mathf.Clamp01(0.5f + irisOffset.x / eyeSpan * 1.8f),
        Mathf.Clamp01(0.5f + irisOffset.y / verticalSpan * 3.2f));
      return true;
    }

    private static Vector2 AverageLandmarks2D(IReadOnlyList<NormalizedLandmark> landmarks, params int[] indices)
    {
      var sum = Vector2.zero;
      var count = 0;
      for (var i = 0; i < indices.Length; i++)
      {
        var index = indices[i];
        if (index < 0 || index >= landmarks.Count)
        {
          continue;
        }

        sum += new Vector2(landmarks[index].x, landmarks[index].y);
        count++;
      }

      return count == 0 ? Vector2.zero : sum / count;
    }

    private static Vector2 NormalizedToScreenPosition(Vector2 normalized)
    {
      return new Vector2(
        Mathf.Clamp01(normalized.x) * Screen.width,
        (1f - Mathf.Clamp01(normalized.y)) * Screen.height);
    }

    private static float NormalizeEar(float ear)
    {
      return Mathf.Clamp01(Mathf.InverseLerp(0.05f, 0.25f, ear));
    }

    private static float Distance2D(NormalizedLandmark a, NormalizedLandmark b)
    {
      var dx = a.x - b.x;
      var dy = a.y - b.y;
      return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private static bool TryReadBlendshapes(
      FaceLandmarkerResult result,
      ref float leftBlinkScore,
      ref float rightBlinkScore,
      ref float leftSquintScore,
      ref float rightSquintScore)
    {
      if (result.faceBlendshapes == null || result.faceBlendshapes.Count == 0 || result.faceBlendshapes[0].categories == null)
      {
        return false;
      }

      var categories = result.faceBlendshapes[0].categories;
      var foundLeftBlink = TryGetCategoryScore(categories, "eyeBlinkLeft", out leftBlinkScore);
      var foundRightBlink = TryGetCategoryScore(categories, "eyeBlinkRight", out rightBlinkScore);
      TryGetCategoryScore(categories, "eyeSquintLeft", out leftSquintScore);
      TryGetCategoryScore(categories, "eyeSquintRight", out rightSquintScore);
      return foundLeftBlink && foundRightBlink;
    }

    private static bool TryGetHeadPose(FaceLandmarkerResult result, out float yawDegrees, out float pitchDegrees)
    {
      yawDegrees = 0f;
      pitchDegrees = 0f;
      var matrices = result.facialTransformationMatrixes;
      if (matrices == null || matrices.Count == 0)
      {
        return false;
      }

      var matrix = matrices[0];
      var forward = new Vector3(matrix.m02, matrix.m12, matrix.m22);
      var up = new Vector3(matrix.m01, matrix.m11, matrix.m21);
      if (forward.sqrMagnitude <= 0.0001f || up.sqrMagnitude <= 0.0001f ||
          Vector3.Cross(forward, up).sqrMagnitude <= 0.0001f)
      {
        return false;
      }

      var euler = Quaternion.LookRotation(forward.normalized, up.normalized).eulerAngles;
      pitchDegrees = NormalizeSignedAngle(euler.x);
      yawDegrees = NormalizeSignedAngle(euler.y);
      return IsFinite(pitchDegrees) && IsFinite(yawDegrees);
    }

    private static float NormalizeSignedAngle(float angle)
    {
      return angle > 180f ? angle - 360f : angle;
    }

    private static bool IsFinite(float value)
    {
      return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool TryGetCategoryScore(IReadOnlyList<Category> categories, string categoryName, out float score)
    {
      for (var i = 0; i < categories.Count; i++)
      {
        var category = categories[i];
        if (category.categoryName == categoryName)
        {
          score = Mathf.Clamp01(category.score);
          return true;
        }
      }

      score = 0f;
      return false;
    }

    private static float SecondsSinceStart()
    {
      return (float)(DateTime.UtcNow - _startTime).TotalSeconds;
    }
  }
}

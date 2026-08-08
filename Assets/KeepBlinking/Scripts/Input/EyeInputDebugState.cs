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
    public long SampleSequence;
    public int FaceCount;
    public int LandmarkCount;

    public UnityEngine.Rect FaceRect;
    public bool HasFaceCenter;
    public Vector2 FaceCenterNormalized;
    public float FaceCenterConfidence;
    public float FaceArea;
    public float RobustFaceScale;
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
    public bool CameraMirrored;
    public int CameraRotationDegrees;
    public ScreenOrientation ScreenOrientation;
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
    private static long _sampleSequence;
    private static bool _cameraMirrored;
    private static int _cameraRotationDegrees;
    private static ScreenOrientation _screenOrientation;
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
        _sampleSequence++;
        _latest = new EyeInputDebugSnapshot
        {
          SampleSequence = _sampleSequence,
          BlinkCount = _blinkCount,
          LastBlinkStartedSeconds = _lastBlinkStartedSeconds,
          LastUpdateSeconds = now,
          CameraMirrored = _cameraMirrored,
          CameraRotationDegrees = _cameraRotationDegrees,
          ScreenOrientation = _screenOrientation,
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

    /// <summary>
    /// Records how the camera frame was normalized before MediaPipe received it.
    /// Gameplay consumes the resulting landmarks as-is and must not mirror X again.
    /// </summary>
    public static void SetFrameTransformMetadata(bool cameraMirrored, int rotationDegrees, ScreenOrientation orientation)
    {
      lock (_lock)
      {
        _cameraMirrored = cameraMirrored;
        _cameraRotationDegrees = ((rotationDegrees % 360) + 360) % 360;
        _screenOrientation = orientation;
      }
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
      var faceCenter = CalculateStableFaceCenter(landmarks, faceRect, out var faceCenterConfidence);
      var faceArea = CalculateRobustFaceScale(landmarks, faceRect);

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
        _sampleSequence++;
        if (_latest.FaceDetected && _latest.HasFaceCenter)
        {
          var jump = Vector2.Distance(_latest.FaceCenterNormalized, faceCenter);
          if (jump > 0.12f)
          {
            // Reject a one-frame landmark jump without freezing the input. The
            // directional One Euro filter receives this bounded recovery point.
            faceCenter = Vector2.Lerp(_latest.FaceCenterNormalized, faceCenter, 0.15f);
            faceCenterConfidence *= 0.35f;
          }
        }
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
          SampleSequence = _sampleSequence,
          FaceCount = faceLandmarks.Count,
          LandmarkCount = landmarks.Count,
          FaceRect = faceRect,
          HasFaceCenter = true,
          FaceCenterNormalized = faceCenter,
          FaceCenterConfidence = faceCenterConfidence,
          FaceArea = faceArea,
          RobustFaceScale = faceArea,
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
          CameraMirrored = _cameraMirrored,
          CameraRotationDegrees = _cameraRotationDegrees,
          ScreenOrientation = _screenOrientation,
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

    private static Vector2 CalculateStableFaceCenter(
      IReadOnlyList<NormalizedLandmark> landmarks,
      UnityEngine.Rect faceRect,
      out float confidence)
    {
      // All points are already in the rotation/mirror-normalized MediaPipe
      // coordinate system. Combine several symmetric, expression-resistant
      // centers here and never mirror X again in gameplay code.
      var weighted = Vector2.zero;
      var totalWeight = 0f;
      AddSymmetricCenter(landmarks, 33, 263, 0.24f, faceRect, ref weighted, ref totalWeight);   // outer eyes
      AddSymmetricCenter(landmarks, 133, 362, 0.24f, faceRect, ref weighted, ref totalWeight); // inner eyes
      AddSymmetricCenter(landmarks, 159, 386, 0.18f, faceRect, ref weighted, ref totalWeight); // upper eye centers
      AddSingleCenter(landmarks, 168, 0.22f, faceRect, ref weighted, ref totalWeight);          // upper nose bridge
      AddSymmetricCenter(landmarks, 234, 454, 0.12f, faceRect, ref weighted, ref totalWeight); // cheeks
      confidence = Mathf.Clamp01(totalWeight);
      if (totalWeight > 0.35f)
      {
        return weighted / totalWeight;
      }

      if (landmarks.Count > 168)
      {
        confidence = 0.25f;
        return new Vector2(landmarks[168].x, landmarks[168].y);
      }

      confidence = 0.1f;
      return faceRect.center;
    }

    private static void AddSymmetricCenter(
      IReadOnlyList<NormalizedLandmark> landmarks,
      int leftIndex,
      int rightIndex,
      float weight,
      UnityEngine.Rect faceRect,
      ref Vector2 weighted,
      ref float totalWeight)
    {
      if (!TryGetLandmark(landmarks, leftIndex, out var left) ||
          !TryGetLandmark(landmarks, rightIndex, out var right)) return;
      AddCandidate((left + right) * 0.5f, weight, faceRect, ref weighted, ref totalWeight);
    }

    private static void AddSingleCenter(
      IReadOnlyList<NormalizedLandmark> landmarks,
      int index,
      float weight,
      UnityEngine.Rect faceRect,
      ref Vector2 weighted,
      ref float totalWeight)
    {
      if (!TryGetLandmark(landmarks, index, out var point)) return;
      AddCandidate(point, weight, faceRect, ref weighted, ref totalWeight);
    }

    private static void AddCandidate(
      Vector2 point,
      float weight,
      UnityEngine.Rect faceRect,
      ref Vector2 weighted,
      ref float totalWeight)
    {
      if (!IsFinite(point.x) || !IsFinite(point.y) ||
          point.x < faceRect.xMin - faceRect.width * 0.15f ||
          point.x > faceRect.xMax + faceRect.width * 0.15f ||
          point.y < faceRect.yMin - faceRect.height * 0.15f ||
          point.y > faceRect.yMax + faceRect.height * 0.15f) return;
      weighted += point * weight;
      totalWeight += weight;
    }

    private static bool TryGetLandmark(
      IReadOnlyList<NormalizedLandmark> landmarks,
      int index,
      out Vector2 point)
    {
      point = default;
      if (landmarks == null || index < 0 || index >= landmarks.Count) return false;
      var landmark = landmarks[index];
      if (!IsFinite(landmark.x) || !IsFinite(landmark.y) ||
          landmark.x < -0.15f || landmark.x > 1.15f ||
          landmark.y < -0.15f || landmark.y > 1.15f) return false;
      point = new Vector2(landmark.x, landmark.y);
      return true;
    }

    private static float CalculateRobustFaceScale(
      IReadOnlyList<NormalizedLandmark> landmarks,
      UnityEngine.Rect faceRect)
    {
      var eyeWidth = NormalizedSpan(landmarks, 33, 263, 0.48f);
      var cheekWidth = NormalizedSpan(landmarks, 234, 454, 0.90f);
      var ovalWidth = NormalizedSpan(landmarks, 127, 356, 0.82f);
      var width = MedianPositive(eyeWidth, cheekWidth, ovalWidth, Mathf.Max(0.0001f, faceRect.width));
      // Squaring a stable linear span preserves the existing face-area ratio
      // semantics used by fixed distance, Too Close and Push Away thresholds.
      return Mathf.Max(0.000001f, width * width);
    }

    private static float NormalizedSpan(
      IReadOnlyList<NormalizedLandmark> landmarks,
      int first,
      int second,
      float expectedFraction)
    {
      if (!TryGetLandmark(landmarks, first, out var a) ||
          !TryGetLandmark(landmarks, second, out var b)) return -1f;
      return Vector2.Distance(a, b) / Mathf.Max(0.01f, expectedFraction);
    }

    private static float MedianPositive(float a, float b, float c, float fallback)
    {
      var count = 0;
      var first = 0f;
      var second = 0f;
      var third = 0f;
      if (a > 0f) { first = a; count++; }
      if (b > 0f) { if (count == 0) first = b; else second = b; count++; }
      if (c > 0f) { if (count == 0) first = c; else if (count == 1) second = c; else third = c; count++; }
      if (count == 0) return fallback;
      if (count == 1) return first;
      if (count == 2) return (first + second) * 0.5f;
      if (first > second) Swap(ref first, ref second);
      if (second > third) Swap(ref second, ref third);
      if (first > second) Swap(ref first, ref second);
      return second;
    }

    private static void Swap(ref float a, ref float b)
    {
      var temporary = a;
      a = b;
      b = temporary;
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

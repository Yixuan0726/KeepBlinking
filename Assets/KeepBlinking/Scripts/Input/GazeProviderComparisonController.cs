using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KeepBlinking.Gameplay;
using UnityEngine;

namespace KeepBlinking.Input
{
  public sealed class GazeProviderComparisonController : MonoBehaviour
  {
    private const float CalibrationPointSeconds = 1.5f;
    private const float EvaluationPointSeconds = 2f;
    private const float SettleSeconds = 0.5f;
    private const float FreeLookSmoothingSpeed = 9f;
    private const int EvaluationRounds = 3;
    private const string DiagnosticsFolderName = "KeepBlinking/Diagnostics";
    private const string CalibrationFileName = "gaze_provider_calibrations.json";

    private static readonly Vector2[] CalibrationTargets =
    {
      new Vector2(0.5f, 0.5f),
      new Vector2(0.14f, 0.84f),
      new Vector2(0.86f, 0.84f),
      new Vector2(0.86f, 0.16f),
      new Vector2(0.14f, 0.16f),
    };

    private static readonly Vector2[] EvaluationTargets =
    {
      new Vector2(0.24f, 0.78f), new Vector2(0.50f, 0.78f), new Vector2(0.76f, 0.78f),
      new Vector2(0.24f, 0.48f), new Vector2(0.50f, 0.48f), new Vector2(0.76f, 0.48f),
      new Vector2(0.24f, 0.22f), new Vector2(0.50f, 0.22f), new Vector2(0.76f, 0.22f),
    };

    public static GazeProviderComparisonController Instance { get; private set; }

    [SerializeField] private GazeProviderMode _mode = GazeProviderMode.Current;
    [SerializeField] private bool _showDevelopmentOverlay;

    private readonly CurrentGazeProvider _current = new CurrentGazeProvider();
    private readonly L2CSGazeProvider _l2cs = new L2CSGazeProvider();
    private readonly List<Vector2> _pointSamples = new List<Vector2>(180);
    private readonly List<Vector2> _currentCalibrationSamples = new List<Vector2>(CalibrationTargets.Length);
    private readonly List<Vector2> _l2csCalibrationSamples = new List<Vector2>(CalibrationTargets.Length);
    private readonly List<DiagnosticRow> _rows = new List<DiagnosticRow>(12000);

    private TestPhase _phase;
    private int _pointIndex;
    private int _roundIndex;
    private double _phaseStartedAt;
    private string _status = "Choose FREE LOOK or MEASURED TEST.";
    private Vector2 _targetRegionHalfSizeNormalized;
    private float _smoothedFrameMilliseconds;
    private Rect _windowRect = new Rect(8f, 8f, 374f, 370f);
    private MarkerState _currentMarker;
    private MarkerState _l2csMarker;
    private Texture2D _markerDiscTexture;
    private Texture2D _markerRingTexture;
    private bool _showRaw;
    private bool _freeMovement;
    private bool _autoAdvanceReference = true;
    private int _freeLookReferenceIndex = 4;
    private double _nextReferenceSwitchAt;
    private float _latestL2CSLatencyMilliseconds = -1f;
    private bool _offScreenSamplingRequested;

    public GazeProviderMode Mode => _mode;
    public bool IsTestRunning => _phase != TestPhase.Idle && _phase != TestPhase.Complete;
    public string L2CSStatus => _l2cs.IsAvailable ? "READY" : _l2cs.FailureReason;

    public static GazeProviderComparisonController EnsureExists()
    {
      if (Instance != null)
      {
        return Instance;
      }

      Instance = FindFirstObjectByType<GazeProviderComparisonController>();
      if (Instance != null)
      {
        return Instance;
      }

      var owner = new GameObject("Gaze Provider Comparison Controller");
      DontDestroyOnLoad(owner);
      return owner.AddComponent<GazeProviderComparisonController>();
    }

    public void BindFrameSource(
      Func<Texture> getCurrentTexture,
      Func<bool> isFrameSourceReady,
      Func<bool> isFlippedHorizontally,
      Func<bool> isFlippedVertically,
      Func<int> getRotationQuarterTurns)
    {
      _l2cs.BindFrameSource(
        getCurrentTexture,
        isFrameSourceReady,
        isFlippedHorizontally,
        isFlippedVertically,
        getRotationQuarterTurns);
    }

    public static bool TryGetGameplayGazeScreenPosition(out Vector2 screenPosition)
    {
      screenPosition = default;
      if (Instance == null || Instance.IsTestRunning ||
          Instance._mode == GazeProviderMode.Current || Instance._mode == GazeProviderMode.Compare)
      {
        return false;
      }

      if (!Instance._l2cs.TryGetLatest(out var sample) || !sample.TrackingValid || !sample.HasScreenPosition)
      {
        return false;
      }

      screenPosition = sample.ScreenPosition;
      return true;
    }

    public static void SetOffScreenSamplingRequested(bool requested)
    {
      if (requested)
      {
        EnsureExists()._offScreenSamplingRequested = true;
      }
      else if (Instance != null)
      {
        Instance._offScreenSamplingRequested = false;
      }
    }

    public static bool TryGetOffScreenDirection(out OffScreenGazeDirectionSample direction, out string failureReason)
    {
      direction = default;
      failureReason = string.Empty;
      if (Instance == null)
      {
        failureReason = "L2CS INITIALIZING";
        return false;
      }

      if (!Instance._l2cs.IsAvailable)
      {
        failureReason = string.IsNullOrWhiteSpace(Instance._l2cs.FailureReason)
          ? "L2CS GAZE UNAVAILABLE"
          : Instance._l2cs.FailureReason.ToUpperInvariant();
        return false;
      }

      var mapper = Instance._l2cs.Mapper;
      if (!mapper.IsCalibrated || Mathf.Abs(mapper.Scale.x) <= 0.0001f || Mathf.Abs(mapper.Scale.y) <= 0.0001f)
      {
        failureReason = "L2CS CALIBRATION REQUIRED";
        return false;
      }

      if (!Instance._l2cs.TryGetLatest(out var sample) || !sample.TrackingValid)
      {
        failureReason = "L2CS TRACKING LOST";
        return false;
      }

      var ageSeconds = Time.unscaledTimeAsDouble - sample.TimestampSeconds;
      if (ageSeconds < -0.05d || ageSeconds > 0.5d)
      {
        failureReason = "L2CS GAZE STALE";
        return false;
      }

      var rawCenter = new Vector2(
        (0.5f - mapper.Offset.x) / mapper.Scale.x,
        (0.5f - mapper.Offset.y) / mapper.Scale.y);
      var centered = new Vector2(
        (sample.RawValue.x - rawCenter.x) * Mathf.Sign(mapper.Scale.x),
        (sample.RawValue.y - rawCenter.y) * Mathf.Sign(mapper.Scale.y));
      if (!IsFinite(sample.RawValue.x) || !IsFinite(sample.RawValue.y) ||
          !IsFinite(centered.x) || !IsFinite(centered.y))
      {
        failureReason = "L2CS GAZE INVALID";
        return false;
      }

      direction = new OffScreenGazeDirectionSample(
        sample.TimestampSeconds,
        sample.RawValue,
        centered,
        true,
        sample.InferenceLatencyMilliseconds);
      return true;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }

      Instance = this;
      DontDestroyOnLoad(gameObject);
      LoadCalibrations();
      CreateMarkerTextures();
    }

    private void Update()
    {
      _current.Tick();
      var shouldRunL2CS = _mode != GazeProviderMode.Current || _showDevelopmentOverlay || IsTestRunning || _offScreenSamplingRequested;
      if (shouldRunL2CS)
      {
        _l2cs.Tick();
        if (_l2cs.TryGetLatest(out var l2csPerformanceSample))
        {
          _latestL2CSLatencyMilliseconds = l2csPerformanceSample.InferenceLatencyMilliseconds;
        }
      }

      var frameMs = Time.unscaledDeltaTime * 1000f;
      _smoothedFrameMilliseconds = Mathf.Lerp(_smoothedFrameMilliseconds, frameMs, 0.08f);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (UnityEngine.Input.GetKeyDown(KeyCode.F8))
      {
        _showDevelopmentOverlay = !_showDevelopmentOverlay;
      }

      if (UnityEngine.Input.GetKeyDown(KeyCode.F9) && !IsTestRunning)
      {
        TryStartTest();
      }

      if (IsTestRunning && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
      {
        ExitActiveTest("Test closed.");
      }

      AdvanceTest(Time.unscaledTimeAsDouble);
      if (_phase == TestPhase.FreeLook)
      {
        UpdateFreeLookInput();
        UpdateFreeLookMarkers(Time.unscaledTimeAsDouble);
      }
#endif
    }

    private void OnDestroy()
    {
      if (Instance == this)
      {
        Instance = null;
      }

      _current.Dispose();
      _l2cs.Dispose();
      if (_markerDiscTexture != null) Destroy(_markerDiscTexture);
      if (_markerRingTexture != null) Destroy(_markerRingTexture);
    }

    private static bool IsFinite(float value)
    {
      return !float.IsNaN(value) && !float.IsInfinity(value);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
      if (_phase == TestPhase.FreeLook)
      {
        DrawFreeLookTest();
        return;
      }

      if (IsTestRunning)
      {
        DrawRunningStatus();
        DrawTarget(GetActiveTarget());
        return;
      }

      if (!_showDevelopmentOverlay && !IsTestRunning)
      {
        GUI.Label(new Rect(8f, 8f, Mathf.Max(160f, Screen.width - 16f), 24f), "F8: GAZE PROVIDER TEST");
        return;
      }

      FitWindowToScreen();
      _windowRect = GUI.Window(GetInstanceID(), _windowRect, DrawWindow, "GAZE PROVIDER TEST");
    }

    private void DrawRunningStatus()
    {
      var width = Mathf.Max(220f, Mathf.Min(520f, Screen.width - 16f));
      var area = new Rect(8f, 8f, width, 62f);
      GUI.Box(area, string.Empty);
      var wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.UpperCenter };
      GUI.Label(new Rect(area.x + 6f, area.y + 5f, area.width - 12f, 34f), _status, wrappedLabel);
      GUI.Label(new Rect(area.x + 6f, area.y + 39f, area.width - 12f, 20f), "ESC TO CANCEL", wrappedLabel);
    }

    private void DrawWindow(int id)
    {
      var wrappedLabel = new GUIStyle(GUI.skin.label) { wordWrap = true };
      var snapshot = EyeInputDebugState.Latest;
      GUILayout.BeginHorizontal();
      GUILayout.Label($"CURRENT: {(snapshot.FaceDetected ? "LIVE" : "LOST")}");
      GUILayout.Label($"L2CS: {(_l2cs.IsAvailable ? "READY" : "UNAVAILABLE")}");
      GUILayout.EndHorizontal();
      GUILayout.BeginHorizontal();
      GUILayout.Label($"GPU FRAME: {(_latestL2CSLatencyMilliseconds >= 0f ? _latestL2CSLatencyMilliseconds.ToString("F1", CultureInfo.InvariantCulture) : "--")} ms");
      GUILayout.Label($"TRACKING: {(snapshot.FaceDetected ? "OK" : "LOST")}");
      GUILayout.EndHorizontal();
      GUILayout.Label($"MODE: {_mode.ToString().ToUpperInvariant()}", wrappedLabel);
      if (GUILayout.Button("CURRENT", GUILayout.Height(27f))) _mode = GazeProviderMode.Current;
      if (GUILayout.Button("L2CS EXPERIMENTAL", GUILayout.Height(27f))) _mode = GazeProviderMode.L2CS;
      if (GUILayout.Button("COMPARE", GUILayout.Height(27f))) _mode = GazeProviderMode.Compare;
      _showRaw = GUILayout.Toggle(_showRaw, "SHOW RAW");
      _autoAdvanceReference = GUILayout.Toggle(_autoAdvanceReference, "AUTO TARGET (4 SEC)");
      _freeMovement = GUILayout.Toggle(_freeMovement, "FREE MOVEMENT");
      GUILayout.Label($"L2CS DETAIL: {L2CSStatus}", wrappedLabel);
      GUILayout.Label($"TEST: {_status}", wrappedLabel);
      GUILayout.Label($"FRAME: {_smoothedFrameMilliseconds:F1} ms");

      GUI.enabled = !IsTestRunning;
      if (GUILayout.Button("START FREE LOOK"))
      {
        StartFreeLookTest();
      }
      if (GUILayout.Button("START MEASURED TEST (F9)"))
      {
        TryStartTest();
      }
      GUI.enabled = true;

      GUILayout.Label("Tests save numbers only. No camera images or video are saved.", wrappedLabel);
      GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    private void FitWindowToScreen()
    {
      const float margin = 8f;
      _windowRect.width = Mathf.Max(220f, Mathf.Min(520f, Screen.width - margin * 2f));
      _windowRect.height = Mathf.Max(300f, Mathf.Min(390f, Screen.height - margin * 2f));
      _windowRect.x = Mathf.Clamp(_windowRect.x, margin, Mathf.Max(margin, Screen.width - _windowRect.width - margin));
      _windowRect.y = Mathf.Clamp(_windowRect.y, margin, Mathf.Max(margin, Screen.height - _windowRect.height - margin));
    }

    private void DrawTarget(Vector2 normalized)
    {
      var center = new Vector2(normalized.x * Screen.width, (1f - normalized.y) * Screen.height);
      const float outerSize = 72f;
      const float innerSize = 12f;
      var old = GUI.color;
      GUI.color = Color.white;
      GUI.DrawTexture(new Rect(center.x - outerSize * 0.5f, center.y - outerSize * 0.5f, outerSize, outerSize), _markerRingTexture);
      GUI.color = new Color(0.1f, 0.95f, 0.95f, 1f);
      GUI.DrawTexture(new Rect(center.x - innerSize * 0.5f, center.y - innerSize * 0.5f, innerSize, innerSize), _markerDiscTexture);
      GUI.color = old;
    }

    private void DrawFreeLookTest()
    {
      var oldColor = GUI.color;
      GUI.color = new Color(0.015f, 0.035f, 0.04f, 0.94f);
      GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);

      if (!_freeMovement)
      {
        DrawFreeLookReferences();
      }

      var showCurrent = _mode == GazeProviderMode.Current || _mode == GazeProviderMode.Compare;
      var showL2CS = _mode == GazeProviderMode.L2CS || _mode == GazeProviderMode.Compare;
      if (_mode == GazeProviderMode.Compare && _currentMarker.Alpha > 0.04f && _l2csMarker.Alpha > 0.04f)
      {
        DrawLine(
          ToGuiPosition(_currentMarker.SmoothedNormalized),
          ToGuiPosition(_l2csMarker.SmoothedNormalized),
          new Color(0.55f, 1f, 0.82f, 0.18f),
          1f);
      }

      if (showCurrent)
      {
        DrawProviderMarker(_currentMarker, "CURRENT", new Color(1f, 0.94f, 0.84f, 1f), false);
      }

      if (showL2CS)
      {
        DrawProviderMarker(_l2csMarker, "L2CS", new Color(0.43f, 1f, 0.75f, 1f), true);
      }

      DrawFreeLookLegend();
      GUI.color = oldColor;
    }

    private void DrawFreeLookReferences()
    {
      for (var index = 0; index < EvaluationTargets.Length; index++)
      {
        var position = ToGuiPosition(EvaluationTargets[index]);
        var active = index == _freeLookReferenceIndex;
        var size = active ? 62f : 52f;
        GUI.color = active
          ? new Color(0.88f, 0.94f, 1f, 0.92f)
          : new Color(0.72f, 0.82f, 0.86f, 0.24f);
        GUI.DrawTexture(new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size), _markerRingTexture);
      }
    }

    private void DrawProviderMarker(MarkerState marker, string label, Color color, bool drawCenterDot)
    {
      if (!marker.HasPosition || marker.Alpha <= 0.02f)
      {
        return;
      }

      var position = ToGuiPosition(marker.SmoothedNormalized);
      color.a *= marker.Alpha;
      GUI.color = color;
      GUI.DrawTexture(new Rect(position.x - 15f, position.y - 15f, 30f, 30f), _markerRingTexture);
      if (drawCenterDot)
      {
        GUI.DrawTexture(new Rect(position.x - 5f, position.y - 5f, 10f, 10f), _markerDiscTexture);
      }

      if (_showRaw && marker.HasRawPosition)
      {
        var raw = ToGuiPosition(marker.RawNormalized);
        DrawLine(raw + new Vector2(-5f, 0f), raw + new Vector2(5f, 0f), color, 1f);
        DrawLine(raw + new Vector2(0f, -5f), raw + new Vector2(0f, 5f), color, 1f);
      }

      var labelStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 9,
      };
      labelStyle.normal.textColor = color;
      GUI.Label(new Rect(position.x + 17f, position.y - 9f, 68f, 18f), label, labelStyle);
    }

    private void DrawFreeLookLegend()
    {
      var width = Mathf.Min(205f, Screen.width - 16f);
      var area = new Rect(Screen.width - width - 8f, 8f, width, 148f);
      GUI.color = new Color(0.03f, 0.055f, 0.06f, 0.88f);
      GUI.Box(area, string.Empty);

      var style = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
      var currentState = _currentMarker.IsTracking ? "TRACKING" : "TRACKING LOST";
      var l2csState = _l2csMarker.IsTracking ? "TRACKING" : "TRACKING LOST";
      GUI.color = Color.white;
      GUI.Label(new Rect(area.x + 8f, area.y + 3f, area.width - 16f, 16f), "FREE LOOK TEST", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 19f, area.width - 16f, 16f), $"CURRENT: {currentState}", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 35f, area.width - 16f, 16f), $"L2CS: {l2csState}", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 51f, area.width - 16f, 16f), $"GPU FRAME: {(_latestL2CSLatencyMilliseconds >= 0f ? _latestL2CSLatencyMilliseconds.ToString("F1", CultureInfo.InvariantCulture) : "--")} ms", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 67f, area.width - 16f, 16f), $"TRACKING: {(EyeInputDebugState.Latest.FaceDetected ? "OK" : "LOST")}", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 85f, area.width - 16f, 32f), "1 CURRENT  2 L2CS  3 COMPARE\nR RAW  A AUTO  M FREE", style);
      GUI.Label(new Rect(area.x + 8f, area.y + 119f, area.width - 16f, 22f), "ARROWS: TARGET   ESC: EXIT", style);
    }

    private static Vector2 ToGuiPosition(Vector2 normalized)
    {
      return new Vector2(normalized.x * Screen.width, (1f - normalized.y) * Screen.height);
    }

    private static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
    {
      var delta = end - start;
      if (delta.sqrMagnitude < 0.01f)
      {
        return;
      }

      var oldColor = GUI.color;
      var oldMatrix = GUI.matrix;
      GUI.color = color;
      var pivot = (start + end) * 0.5f;
      GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, pivot);
      GUI.DrawTexture(new Rect(pivot.x - delta.magnitude * 0.5f, pivot.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
      GUI.matrix = oldMatrix;
      GUI.color = oldColor;
    }
#endif

    private void CreateMarkerTextures()
    {
      _markerDiscTexture = CreateCircularTexture(false);
      _markerRingTexture = CreateCircularTexture(true);
    }

    private static Texture2D CreateCircularTexture(bool ring)
    {
      const int size = 64;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
      {
        name = ring ? "Gaze Marker Ring" : "Gaze Marker Disc",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var pixels = new Color32[size * size];
      var center = (size - 1) * 0.5f;
      var outerRadius = size * 0.46f;
      var innerRadius = ring ? size * 0.35f : 0f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
          var visible = distance <= outerRadius && (!ring || distance >= innerRadius);
          pixels[y * size + x] = visible ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
        }
      }

      texture.SetPixels32(pixels);
      texture.Apply(false, true);
      return texture;
    }

    private void UpdateFreeLookInput()
    {
      if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad1))
        _mode = GazeProviderMode.Current;
      if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad2))
        _mode = GazeProviderMode.L2CS;
      if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3) || UnityEngine.Input.GetKeyDown(KeyCode.Keypad3))
        _mode = GazeProviderMode.Compare;
      if (UnityEngine.Input.GetKeyDown(KeyCode.R)) _showRaw = !_showRaw;
      if (UnityEngine.Input.GetKeyDown(KeyCode.A))
      {
        _autoAdvanceReference = !_autoAdvanceReference;
        _nextReferenceSwitchAt = Time.unscaledTimeAsDouble + 4.0;
      }
      if (UnityEngine.Input.GetKeyDown(KeyCode.M)) _freeMovement = !_freeMovement;

      var direction = 0;
      if (UnityEngine.Input.GetKeyDown(KeyCode.LeftArrow)) direction = -1;
      if (UnityEngine.Input.GetKeyDown(KeyCode.RightArrow)) direction = 1;
      if (direction != 0)
      {
        _freeLookReferenceIndex = (_freeLookReferenceIndex + direction + EvaluationTargets.Length) % EvaluationTargets.Length;
        _nextReferenceSwitchAt = Time.unscaledTimeAsDouble + 4.0;
      }

      if (_autoAdvanceReference && !_freeMovement && Time.unscaledTimeAsDouble >= _nextReferenceSwitchAt)
      {
        _freeLookReferenceIndex = (_freeLookReferenceIndex + 1) % EvaluationTargets.Length;
        _nextReferenceSwitchAt = Time.unscaledTimeAsDouble + 4.0;
      }
    }

    private void UpdateFreeLookMarkers(double now)
    {
      UpdateFreeLookMarker(_current, false, ref _currentMarker, now);
      UpdateFreeLookMarker(_l2cs, true, ref _l2csMarker, now);
    }

    private static void UpdateFreeLookMarker(
      IGazePositionProvider provider,
      bool requiresCalibration,
      ref MarkerState marker,
      double now)
    {
      var valid = provider.TryGetLatest(out var sample) && sample.TrackingValid;
      var rawPosition = Vector2.zero;
      var providerPosition = Vector2.zero;
      if (valid)
      {
        if (requiresCalibration)
        {
          valid = sample.HasScreenPosition && provider.Mapper.TryMap(sample.RawValue, out rawPosition);
          providerPosition = sample.NormalizedScreenPosition;
        }
        else
        {
          rawPosition = sample.NormalizedScreenPosition;
          if (provider.Mapper.IsCalibrated && provider.Mapper.TryMap(sample.RawValue, out var calibratedCurrent))
          {
            rawPosition = calibratedCurrent;
          }
          providerPosition = rawPosition;
        }
      }

      valid = valid && IsFiniteNormalized(rawPosition) && IsFiniteNormalized(providerPosition);
      if (!valid)
      {
        marker.IsTracking = false;
        marker.HasRawPosition = false;
        marker.Alpha = Mathf.MoveTowards(marker.Alpha, 0f, Time.unscaledDeltaTime * 4f);
        return;
      }

      rawPosition = ClampNormalized(rawPosition);
      providerPosition = ClampNormalized(providerPosition);
      if (!marker.HasPosition || marker.Alpha <= 0.02f)
      {
        marker.SmoothedNormalized = providerPosition;
      }
      else
      {
        var smoothing = 1f - Mathf.Exp(-FreeLookSmoothingSpeed * Time.unscaledDeltaTime);
        marker.SmoothedNormalized = Vector2.Lerp(marker.SmoothedNormalized, providerPosition, smoothing);
      }

      marker.RawNormalized = rawPosition;
      marker.HasPosition = true;
      marker.HasRawPosition = true;
      marker.IsTracking = true;
      marker.Alpha = Mathf.MoveTowards(marker.Alpha, 1f, Time.unscaledDeltaTime * 8f);
      marker.LastValidTimestampSeconds = now;
    }

    private static bool IsFiniteNormalized(Vector2 value)
    {
      return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
             !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }

    private static Vector2 ClampNormalized(Vector2 value)
    {
      return new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));
    }

    private void TryStartTest()
    {
      var gameplay = FindFirstObjectByType<EdgeOrbitHarvestMvp>();
      if (gameplay == null || !gameplay.TryGetDevelopmentTargetRegionHalfSizeNormalized(out _targetRegionHalfSizeNormalized))
      {
        _status = "WAITING FOR AN ACTIVE GAME TARGET SIZE";
        _showDevelopmentOverlay = true;
        return;
      }

      _rows.Clear();
      _currentCalibrationSamples.Clear();
      _l2csCalibrationSamples.Clear();
      _current.Mapper.Reset();
      _l2cs.Mapper.Reset();
      _pointIndex = 0;
      _roundIndex = 0;
      _phase = TestPhase.CalibrateCurrent;
      _phaseStartedAt = Time.unscaledTimeAsDouble;
      _status = "CALIBRATING CURRENT: LOOK AT THE TARGET";
      _showDevelopmentOverlay = true;
      _pointSamples.Clear();
    }

    private void StartFreeLookTest()
    {
      _pointIndex = 0;
      _pointSamples.Clear();
      _currentMarker = default;
      _l2csMarker = default;
      _freeLookReferenceIndex = 4;
      _nextReferenceSwitchAt = Time.unscaledTimeAsDouble + 4.0;
      _showDevelopmentOverlay = true;

      if (!_l2cs.Mapper.IsCalibrated)
      {
        _l2csCalibrationSamples.Clear();
        _phase = TestPhase.FreeLookCalibrateL2CS;
        _phaseStartedAt = Time.unscaledTimeAsDouble;
        _status = "L2CS CALIBRATION: LOOK AT THE LARGE TARGET 1/5";
        return;
      }

      EnterFreeLook();
    }

    private void EnterFreeLook()
    {
      _phase = TestPhase.FreeLook;
      _phaseStartedAt = Time.unscaledTimeAsDouble;
      _nextReferenceSwitchAt = Time.unscaledTimeAsDouble + 4.0;
      _status = "FREE LOOK TEST";
    }

    private void AdvanceTest(double now)
    {
      if (!IsTestRunning)
      {
        return;
      }

      var elapsed = now - _phaseStartedAt;
      switch (_phase)
      {
        case TestPhase.CalibrateCurrent:
          CollectCalibrationSample(_current, elapsed);
          if (elapsed >= CalibrationPointSeconds) CompleteCalibrationPoint(_current, _currentCalibrationSamples, TestPhase.CalibrateL2CS, now);
          break;
        case TestPhase.CalibrateL2CS:
          CollectCalibrationSample(_l2cs, elapsed);
          if (elapsed >= CalibrationPointSeconds) CompleteCalibrationPoint(_l2cs, _l2csCalibrationSamples, TestPhase.Evaluate, now);
          break;
        case TestPhase.Evaluate:
          CollectEvaluationRows(now, elapsed);
          if (elapsed >= EvaluationPointSeconds) CompleteEvaluationPoint(now);
          break;
        case TestPhase.FreeLookCalibrateL2CS:
          CollectCalibrationSample(_l2cs, elapsed);
          if (elapsed >= CalibrationPointSeconds) CompleteFreeLookCalibrationPoint(now);
          break;
      }
    }

    private void CompleteFreeLookCalibrationPoint(double now)
    {
      if (_pointSamples.Count < 3)
      {
        _status = "L2CS TRACKING LOST. HOLD STILL AND LOOK AT THE LARGE TARGET.";
        _phaseStartedAt = now;
        _pointSamples.Clear();
        return;
      }

      _l2csCalibrationSamples.Add(MedianVector(_pointSamples));
      _pointSamples.Clear();
      _pointIndex++;
      if (_pointIndex < CalibrationTargets.Length)
      {
        _phaseStartedAt = now;
        _status = $"L2CS CALIBRATION: LOOK AT THE LARGE TARGET {_pointIndex + 1}/5";
        return;
      }

      if (!_l2cs.Mapper.SetCalibration(_l2csCalibrationSamples, CalibrationTargets))
      {
        ExitActiveTest("L2CS CALIBRATION FAILED.");
        return;
      }

      SaveCalibrations();
      _pointIndex = 0;
      EnterFreeLook();
    }

    private void CollectCalibrationSample(IGazePositionProvider provider, double elapsed)
    {
      if (elapsed < SettleSeconds || !provider.TryGetLatest(out var sample) || !sample.TrackingValid)
      {
        return;
      }

      _pointSamples.Add(sample.RawValue);
    }

    private void CompleteCalibrationPoint(
      IGazePositionProvider provider,
      List<Vector2> providerSamples,
      TestPhase nextPhase,
      double now)
    {
      if (_pointSamples.Count < 3)
      {
        _status = $"{provider.ProviderName.ToUpperInvariant()} TRACKING LOST. HOLD STILL AND LOOK AT THE TARGET.";
        _phaseStartedAt = now;
        _pointSamples.Clear();
        return;
      }

      providerSamples.Add(MedianVector(_pointSamples));
      _pointSamples.Clear();
      _pointIndex++;
      if (_pointIndex < CalibrationTargets.Length)
      {
        _phaseStartedAt = now;
        _status = $"CALIBRATING {provider.ProviderName.ToUpperInvariant()}: POINT {_pointIndex + 1}/{CalibrationTargets.Length}";
        return;
      }

      if (!provider.Mapper.SetCalibration(providerSamples, CalibrationTargets))
      {
        ResetTest($"{provider.ProviderName.ToUpperInvariant()} CALIBRATION FAILED.");
        return;
      }

      SaveCalibrations();

      _pointIndex = 0;
      _phase = nextPhase;
      _phaseStartedAt = now;
      if (nextPhase == TestPhase.CalibrateL2CS)
      {
        _status = "CALIBRATING L2CS: LOOK AT THE TARGET";
      }
      else
      {
        _status = "EVALUATION ROUND 1/3";
      }
    }

    private void CollectEvaluationRows(double now, double elapsed)
    {
      if (elapsed < SettleSeconds)
      {
        return;
      }

      AppendRow(_current, now);
      AppendRow(_l2cs, now);
    }

    private void AppendRow(IGazePositionProvider provider, double now)
    {
      var hasSample = provider.TryGetLatest(out var sample);
      var trackingValid = hasSample && sample.TrackingValid;
      var predicted = Vector2.zero;
      var hasPrediction = trackingValid && provider.Mapper.TryMap(sample.RawValue, out predicted);
      _rows.Add(new DiagnosticRow
      {
        provider = provider.ProviderName,
        unityTimestampSeconds = now,
        providerTimestampSeconds = hasSample ? sample.TimestampSeconds : 0.0,
        targetX = EvaluationTargets[_pointIndex].x,
        targetY = EvaluationTargets[_pointIndex].y,
        predictionX = hasPrediction ? predicted.x : -1f,
        predictionY = hasPrediction ? predicted.y : -1f,
        trackingValid = trackingValid,
        inferenceLatencyMilliseconds = hasSample ? sample.InferenceLatencyMilliseconds : -1f,
        unityFps = Time.unscaledDeltaTime > 0f ? 1f / Time.unscaledDeltaTime : 0f,
        frameMilliseconds = Time.unscaledDeltaTime * 1000f,
        headPosePitchDegrees = float.NaN,
        headPoseYawDegrees = float.NaN,
        calibrationVersion = provider.Mapper.CalibrationVersion,
        evaluationRound = _roundIndex + 1,
        targetIndex = _pointIndex + 1,
      });
    }

    private void CompleteEvaluationPoint(double now)
    {
      _pointIndex++;
      if (_pointIndex >= EvaluationTargets.Length)
      {
        _pointIndex = 0;
        _roundIndex++;
      }

      if (_roundIndex >= EvaluationRounds)
      {
        CompleteTest();
        return;
      }

      _phaseStartedAt = now;
      _status = $"EVALUATION ROUND {_roundIndex + 1}/{EvaluationRounds}, POINT {_pointIndex + 1}/{EvaluationTargets.Length}";
    }

    private void CompleteTest()
    {
      try
      {
        var folder = Path.Combine(Application.persistentDataPath, DiagnosticsFolderName);
        Directory.CreateDirectory(folder);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var csvPath = Path.Combine(folder, $"gaze_provider_ab_{stamp}.csv");
        var jsonPath = Path.Combine(folder, $"gaze_provider_ab_{stamp}_summary.json");
        File.WriteAllText(csvPath, BuildCsv(), Encoding.UTF8);
        var report = BuildReport(csvPath);
        File.WriteAllText(jsonPath, JsonUtility.ToJson(report, true), Encoding.UTF8);
        _phase = TestPhase.Complete;
        _mode = GazeProviderMode.Current;
        _status = $"COMPLETE: {Path.GetFileName(jsonPath)}";
        Debug.Log($"Gaze provider A/B diagnostics saved to {folder}");
      }
      catch (Exception exception)
      {
        ResetTest($"SAVE FAILED: {exception.Message}");
      }
    }

    private string BuildCsv()
    {
      var builder = new StringBuilder(1024 * 32);
      builder.AppendLine("ProviderName,UnityTimestampSeconds,ProviderTimestampSeconds,TargetNormalizedX,TargetNormalizedY,PredictionNormalizedX,PredictionNormalizedY,TrackingValid,InferenceLatencyMilliseconds,UnityFPS,FrameMilliseconds,HeadPosePitchDegrees,HeadPoseYawDegrees,CalibrationVersion,EvaluationRound,TargetIndex");
      foreach (var row in _rows)
      {
        builder.Append(row.provider).Append(',')
          .Append(F(row.unityTimestampSeconds)).Append(',').Append(F(row.providerTimestampSeconds)).Append(',')
          .Append(F(row.targetX)).Append(',').Append(F(row.targetY)).Append(',')
          .Append(F(row.predictionX)).Append(',').Append(F(row.predictionY)).Append(',')
          .Append(row.trackingValid ? "true" : "false").Append(',')
          .Append(F(row.inferenceLatencyMilliseconds)).Append(',').Append(F(row.unityFps)).Append(',')
          .Append(F(row.frameMilliseconds)).Append(',').Append(F(row.headPosePitchDegrees)).Append(',')
          .Append(F(row.headPoseYawDegrees)).Append(',').Append(row.calibrationVersion).Append(',')
          .Append(row.evaluationRound).Append(',').Append(row.targetIndex).AppendLine();
      }
      return builder.ToString();
    }

    private ComparisonReport BuildReport(string csvPath)
    {
      return new ComparisonReport
      {
        createdUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        csvPath = csvPath,
        targetRegionHalfWidthNormalized = _targetRegionHalfSizeNormalized.x,
        targetRegionHalfHeightNormalized = _targetRegionHalfSizeNormalized.y,
        current = BuildMetrics("Current"),
        l2cs = BuildMetrics("L2CS"),
        decision = "HUMAN A/B DATA RECORDED; REVIEW THRESHOLDS BEFORE CHANGING THE DEFAULT PROVIDER",
      };
    }

    private void LoadCalibrations()
    {
      try
      {
        var path = Path.Combine(Application.persistentDataPath, DiagnosticsFolderName, CalibrationFileName);
        if (!File.Exists(path))
        {
          return;
        }

        var store = JsonUtility.FromJson<CalibrationStore>(File.ReadAllText(path));
        if (store == null)
        {
          return;
        }

        if (store.currentCalibrated)
        {
          _current.Mapper.SetCalibrationParameters(
            new Vector2(store.currentScaleX, store.currentScaleY),
            new Vector2(store.currentOffsetX, store.currentOffsetY),
            store.currentVersion);
        }

        if (store.l2csCalibrated)
        {
          _l2cs.Mapper.SetCalibrationParameters(
            new Vector2(store.l2csScaleX, store.l2csScaleY),
            new Vector2(store.l2csOffsetX, store.l2csOffsetY),
            store.l2csVersion);
        }
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"Gaze provider calibration load failed: {exception.Message}");
      }
    }

    private void SaveCalibrations()
    {
      try
      {
        var folder = Path.Combine(Application.persistentDataPath, DiagnosticsFolderName);
        Directory.CreateDirectory(folder);
        var store = new CalibrationStore
        {
          currentCalibrated = _current.Mapper.IsCalibrated,
          currentScaleX = _current.Mapper.Scale.x,
          currentScaleY = _current.Mapper.Scale.y,
          currentOffsetX = _current.Mapper.Offset.x,
          currentOffsetY = _current.Mapper.Offset.y,
          currentVersion = _current.Mapper.CalibrationVersion,
          l2csCalibrated = _l2cs.Mapper.IsCalibrated,
          l2csScaleX = _l2cs.Mapper.Scale.x,
          l2csScaleY = _l2cs.Mapper.Scale.y,
          l2csOffsetX = _l2cs.Mapper.Offset.x,
          l2csOffsetY = _l2cs.Mapper.Offset.y,
          l2csVersion = _l2cs.Mapper.CalibrationVersion,
        };
        File.WriteAllText(Path.Combine(folder, CalibrationFileName), JsonUtility.ToJson(store, true));
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"Gaze provider calibration save failed: {exception.Message}");
      }
    }

    private ProviderMetrics BuildMetrics(string provider)
    {
      var all = _rows.Where(row => row.provider == provider).ToList();
      var valid = all.Where(row => row.trackingValid && row.predictionX >= 0f && row.predictionY >= 0f).ToList();
      var errors = valid.Select(ScreenDiagonalError).OrderBy(value => value).ToList();
      var jitters = new List<float>();
      var stableTimes = new List<float>();
      var regionHits = 0;
      var wrongLocks = 0;

      foreach (var row in valid)
      {
        var prediction = new Vector2(row.predictionX, row.predictionY);
        var target = new Vector2(row.targetX, row.targetY);
        if (IsInsideRegion(prediction, target)) regionHits++;
        if (!IsInsideRegion(prediction, target) && EvaluationTargets.Any(other => other != target && IsInsideRegion(prediction, other))) wrongLocks++;
      }

      foreach (var group in valid.GroupBy(row => new { row.evaluationRound, row.targetIndex }))
      {
        var points = group.Select(row => new Vector2(row.predictionX, row.predictionY)).ToList();
        if (points.Count > 1)
        {
          var center = points.Aggregate(Vector2.zero, (sum, point) => sum + point) / points.Count;
          jitters.Add(Median(points.Select(point => Vector2.Distance(point, center)).OrderBy(value => value).ToList()));
        }

        var ordered = group.OrderBy(row => row.unityTimestampSeconds).ToList();
        double? stableRunStartedAt = null;
        for (var index = 0; index < ordered.Count; index++)
        {
          var row = ordered[index];
          var isInside = IsInsideRegion(
            new Vector2(row.predictionX, row.predictionY),
            new Vector2(row.targetX, row.targetY));
          if (!isInside)
          {
            stableRunStartedAt = null;
            continue;
          }

          stableRunStartedAt ??= row.unityTimestampSeconds;
          if (row.unityTimestampSeconds - stableRunStartedAt.Value >= 0.25)
          {
            stableTimes.Add((float)(stableRunStartedAt.Value - ordered[0].unityTimestampSeconds));
            break;
          }
        }
      }

      var latencies = valid.Select(row => row.inferenceLatencyMilliseconds).Where(value => value >= 0f).OrderBy(value => value).ToList();
      var fps = all.Select(row => row.unityFps).Where(value => value > 0f).ToList();
      var frameTimes = all.Select(row => row.frameMilliseconds).Where(value => value > 0f).OrderBy(value => value).ToList();
      return new ProviderMetrics
      {
        provider = provider,
        sampleCount = all.Count,
        validSampleCount = valid.Count,
        medianScreenDiagonalError = Percentile(errors, 0.5f),
        p90ScreenDiagonalError = Percentile(errors, 0.9f),
        medianScreenErrorPixels = Percentile(errors, 0.5f) * Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height),
        p90ScreenErrorPixels = Percentile(errors, 0.9f) * Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height),
        medianStationaryJitterNormalized = Median(jitters.OrderBy(value => value).ToList()),
        trackingLossRatio = all.Count > 0 ? 1f - (float)valid.Count / all.Count : 1f,
        regionHitRate = valid.Count > 0 ? (float)regionHits / valid.Count : 0f,
        wrongLockRate = valid.Count > 0 ? (float)wrongLocks / valid.Count : 0f,
        medianStableLockSeconds = Median(stableTimes.OrderBy(value => value).ToList()),
        averageInferenceLatencyMilliseconds = latencies.Count > 0 ? latencies.Average() : -1f,
        p95InferenceLatencyMilliseconds = Percentile(latencies, 0.95f),
        averageUnityFps = fps.Count > 0 ? fps.Average() : 0f,
        p95FrameMilliseconds = Percentile(frameTimes, 0.95f),
        maximumFrameMilliseconds = frameTimes.Count > 0 ? frameTimes[frameTimes.Count - 1] : 0f,
        stutterFrameCountOver50Milliseconds = frameTimes.Count(value => value > 50f),
      };
    }

    private bool IsInsideRegion(Vector2 point, Vector2 target)
    {
      return Mathf.Abs(point.x - target.x) <= _targetRegionHalfSizeNormalized.x &&
             Mathf.Abs(point.y - target.y) <= _targetRegionHalfSizeNormalized.y;
    }

    private static float ScreenDiagonalError(DiagnosticRow row)
    {
      var dx = (row.predictionX - row.targetX) * Screen.width;
      var dy = (row.predictionY - row.targetY) * Screen.height;
      var diagonal = Mathf.Sqrt(Screen.width * Screen.width + Screen.height * Screen.height);
      return diagonal > 0f ? Mathf.Sqrt(dx * dx + dy * dy) / diagonal : 0f;
    }

    private Vector2 GetActiveTarget()
    {
      return _phase == TestPhase.Evaluate ? EvaluationTargets[_pointIndex] : CalibrationTargets[_pointIndex];
    }

    private void ResetTest(string status)
    {
      _phase = TestPhase.Idle;
      _mode = GazeProviderMode.Current;
      _pointIndex = 0;
      _roundIndex = 0;
      _pointSamples.Clear();
      _currentMarker = default;
      _l2csMarker = default;
      _status = status;
    }

    private void ExitActiveTest(string status)
    {
      ResetTest(status);
      _showDevelopmentOverlay = true;
    }

    private static Vector2 MedianVector(IReadOnlyList<Vector2> values)
    {
      var xs = values.Select(value => value.x).OrderBy(value => value).ToList();
      var ys = values.Select(value => value.y).OrderBy(value => value).ToList();
      return new Vector2(Median(xs), Median(ys));
    }

    private static float Median(IReadOnlyList<float> values)
    {
      return Percentile(values, 0.5f);
    }

    private static float Percentile(IReadOnlyList<float> sortedValues, float percentile)
    {
      if (sortedValues == null || sortedValues.Count == 0) return 0f;
      var position = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
      var lower = Mathf.FloorToInt(position);
      var upper = Mathf.CeilToInt(position);
      return Mathf.Lerp(sortedValues[lower], sortedValues[upper], position - lower);
    }

    private static string F(double value)
    {
      return double.IsNaN(value) ? string.Empty : value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private enum TestPhase
    {
      Idle,
      CalibrateCurrent,
      CalibrateL2CS,
      Evaluate,
      Complete,
      FreeLookCalibrateL2CS,
      FreeLook,
    }

    private struct MarkerState
    {
      public Vector2 RawNormalized;
      public Vector2 SmoothedNormalized;
      public bool HasRawPosition;
      public bool HasPosition;
      public bool IsTracking;
      public float Alpha;
      public double LastValidTimestampSeconds;
    }

    [Serializable]
    private sealed class DiagnosticRow
    {
      public string provider;
      public double unityTimestampSeconds;
      public double providerTimestampSeconds;
      public float targetX;
      public float targetY;
      public float predictionX;
      public float predictionY;
      public bool trackingValid;
      public float inferenceLatencyMilliseconds;
      public float unityFps;
      public float frameMilliseconds;
      public float headPosePitchDegrees;
      public float headPoseYawDegrees;
      public string calibrationVersion;
      public int evaluationRound;
      public int targetIndex;
    }

    [Serializable]
    private sealed class CalibrationStore
    {
      public bool currentCalibrated;
      public float currentScaleX;
      public float currentScaleY;
      public float currentOffsetX;
      public float currentOffsetY;
      public string currentVersion;
      public bool l2csCalibrated;
      public float l2csScaleX;
      public float l2csScaleY;
      public float l2csOffsetX;
      public float l2csOffsetY;
      public string l2csVersion;
    }

    [Serializable]
    private sealed class ComparisonReport
    {
      public string createdUtc;
      public string csvPath;
      public float targetRegionHalfWidthNormalized;
      public float targetRegionHalfHeightNormalized;
      public ProviderMetrics current;
      public ProviderMetrics l2cs;
      public string decision;
    }

    [Serializable]
    private sealed class ProviderMetrics
    {
      public string provider;
      public int sampleCount;
      public int validSampleCount;
      public float medianScreenErrorPixels;
      public float p90ScreenErrorPixels;
      public float medianScreenDiagonalError;
      public float p90ScreenDiagonalError;
      public float medianStationaryJitterNormalized;
      public float trackingLossRatio;
      public float regionHitRate;
      public float wrongLockRate;
      public float medianStableLockSeconds;
      public float averageInferenceLatencyMilliseconds;
      public float p95InferenceLatencyMilliseconds;
      public float averageUnityFps;
      public float p95FrameMilliseconds;
      public float maximumFrameMilliseconds;
      public int stutterFrameCountOver50Milliseconds;
    }
  }
}

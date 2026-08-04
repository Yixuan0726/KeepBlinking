using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public enum SoftFocusGazeState
  {
    Comfort,
    Peripheral,
    TrackingLost,
    Paused,
  }

  public sealed class SoftFocusFieldController : MonoBehaviour
  {
    [Header("Field Geometry")]
    [SerializeField, Range(0.3f, 0.9f)] private float _baseWidthNormalized = 0.55f;
    [SerializeField, Range(0.2f, 0.8f)] private float _baseHeightNormalized = 0.40f;
    [SerializeField, Range(-0.2f, 0.2f)] private float _verticalOffsetNormalized = 0.05f;
    [SerializeField, Range(0.05f, 0.6f)] private float _fieldVisualAlpha = 0.38f;
    [SerializeField, Min(0.05f)] private float _fieldResponseSeconds = 0.4f;

    [Header("Comfort Gaze Hysteresis")]
    [SerializeField, Range(0.1f, 0.5f)] private float _comfortEnterHalfWidth = 0.28f;
    [SerializeField, Range(0.1f, 0.5f)] private float _comfortEnterHalfHeight = 0.28f;
    [SerializeField, Range(0.1f, 0.5f)] private float _comfortExitHalfWidth = 0.33f;
    [SerializeField, Range(0.1f, 0.5f)] private float _comfortExitHalfHeight = 0.33f;
    [SerializeField, Min(0f)] private float _focusHoldSeconds = 0.4f;

    [Header("Purification")]
    [SerializeField, Min(0.2f)] private float _purificationSeconds = 1.2f;
    [SerializeField, Min(1)] private int _baseConcurrentCapacity = 2;

    [Header("Blink Health")]
    [SerializeField, Min(0f)] private float _drynessStartSeconds = 5f;
    [SerializeField, Min(0f)] private float _drynessMediumSeconds = 8f;
    [SerializeField, Min(0f)] private float _drynessSevereSeconds = 12f;
    [SerializeField, Range(0.2f, 1f)] private float _mediumFieldScale = 0.85f;
    [SerializeField, Range(0.2f, 1f)] private float _severeFieldScale = 0.60f;
    [SerializeField, Range(0.1f, 1f)] private float _criticalFieldScale = 0.35f;
    [SerializeField, Range(0.1f, 1f)] private float _severePurificationSpeed = 0.70f;
    [SerializeField, Min(0.1f)] private float _blinkRecoverySeconds = 0.4f;
    [SerializeField, Min(0.1f)] private float _criticalShrinkSeconds = 1f;

    [Header("Distance and Temporary Focus")]
    [SerializeField, Range(0.2f, 1f)] private float _maximumTooCloseFieldScale = 0.55f;
    [SerializeField, Range(1f, 1.4f)] private float _distanceShrinkStartRatio = 1.10f;
    [SerializeField, Range(1f, 1.5f)] private float _distanceShrinkMaximumRatio = 1.30f;
    [SerializeField, Range(1f, 1.5f)] private float _wideFocusScale = 1.15f;
    [SerializeField, Range(1f, 1.5f)] private float _freshFocusScale = 1.15f;
    [SerializeField, Min(0.1f)] private float _freshFocusSeconds = 8f;

    private EdgeOrbitHarvestMvp _gameplay;
    private CanvasGroup _canvasGroup;
    private RectTransform _fieldRect;
    private SoftFocusFieldGraphic _graphic;
    private bool _comfortGaze = true;
    private bool _eyeBreakPaused;
    private float _comfortHoldUntil = -1f;
    private float _secondsSinceLastBlink;
    private float _blinkGraceRemaining;
    private float _freshFocusUntil = -1f;
    private float _quietFieldUntil = -1f;
    private float _currentScale = 1f;
    private float _currentAlpha;
    private float _recoveryRipple;

    public static SoftFocusFieldController Instance { get; private set; }

    public SoftFocusGazeState GazeState { get; private set; } = SoftFocusGazeState.Comfort;
    public bool IsBlinkHealthPaused => _eyeBreakPaused;
    public float SecondsSinceLastBlink => _secondsSinceLastBlink;
    public float FieldScale => _currentScale;
    public float DrynessAmount => CalculateDrynessAmount(_secondsSinceLastBlink);
    public float PurificationSeconds => Mathf.Max(0.2f, _purificationSeconds);
    public float PurificationSpeedMultiplier => _secondsSinceLastBlink < _drynessMediumSeconds
      ? 1f
      : _severePurificationSpeed;
    public bool CanAccumulate => !_eyeBreakPaused &&
                                 _gameplay != null &&
                                 _gameplay.IsSoftFocusNormalGameplayActive &&
                                 GazeState == SoftFocusGazeState.Comfort;
    public bool CanComplete => CanAccumulate &&
                               _secondsSinceLastBlink < _drynessSevereSeconds &&
                               !_gameplay.IsTooClose;
    public int ConcurrentCapacity
    {
      get
      {
        var capacity = Mathf.Max(1, _baseConcurrentCapacity);
        if (_gameplay != null && _gameplay.HasFirstLevelModule(FirstLevelModuleId.ChainBlink)) capacity++;
        if (_gameplay != null && _gameplay.HasFirstLevelModule(FirstLevelModuleId.WideChain)) capacity++;
        return capacity;
      }
    }

    public int BaseConcurrentCapacity => Mathf.Max(1, _baseConcurrentCapacity);
    public Vector2 CenterViewport => new Vector2(0.5f, 0.5f + _verticalOffsetNormalized);
    public Vector2 SizeViewport => new Vector2(_baseWidthNormalized, _baseHeightNormalized) * _currentScale;

    public static SoftFocusFieldController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null)
      {
        Instance = FindFirstObjectByType<SoftFocusFieldController>();
      }

      if (Instance == null)
      {
        var owner = new GameObject("Soft Focus Field Controller");
        Instance = owner.AddComponent<SoftFocusFieldController>();
      }

      Instance.Bind(gameplay);
      return Instance;
    }

    public bool ContainsViewportPoint(Vector2 viewportPoint)
    {
      return SoftFocusFieldLogic.IsInsideEllipse(viewportPoint, CenterViewport, SizeViewport);
    }

    public void SetEyeBreakPaused(bool paused)
    {
      if (_eyeBreakPaused == paused)
      {
        return;
      }

      _eyeBreakPaused = paused;
      if (!paused)
      {
        _blinkGraceRemaining = Mathf.Max(_blinkGraceRemaining, 2f);
        _secondsSinceLastBlink = 0f;
        _recoveryRipple = 1f;
      }
    }

    public void GrantFreshFocus()
    {
      _freshFocusUntil = Time.unscaledTime + Mathf.Max(0.1f, _freshFocusSeconds);
      _recoveryRipple = 1f;
    }

    public void GrantQuietField(float seconds)
    {
      _quietFieldUntil = Mathf.Max(_quietFieldUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
      _recoveryRipple = 1f;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }

      Instance = this;
      CreateVisual();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay)
      {
        return;
      }

      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted -= HandleNaturalBlink;
      }

      _gameplay = gameplay;
      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted += HandleNaturalBlink;
      }
    }

    private void HandleNaturalBlink()
    {
      _secondsSinceLastBlink = 0f;
      _recoveryRipple = 1f;
    }

    private void Update()
    {
      if (_gameplay == null)
      {
        return;
      }

      var deltaTime = Time.unscaledDeltaTime;
      UpdateBlinkHealth(deltaTime);
      UpdateGazeState();

      var visible = _gameplay.ShouldShowSoftFocusField;
      var targetAlpha = visible && !_eyeBreakPaused && !_gameplay.IsExperienceCollectionInProgress
        ? _fieldVisualAlpha
        : 0f;
      var targetScale = CalculateTargetScale();
      var response = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.05f, _fieldResponseSeconds));
      _currentScale = Mathf.Lerp(_currentScale, _eyeBreakPaused ? 0.12f : targetScale, response);
      _currentAlpha = Mathf.Lerp(_currentAlpha, targetAlpha, response);
      _recoveryRipple = Mathf.MoveTowards(_recoveryRipple, 0f, deltaTime / Mathf.Max(0.1f, _blinkRecoverySeconds));

      UpdateVisual();
    }

    private void UpdateBlinkHealth(float deltaTime)
    {
      if (_eyeBreakPaused || !_gameplay.IsSoftFocusBlinkHealthActive || !_gameplay.IsTrackingAvailable)
      {
        return;
      }

      if (_blinkGraceRemaining > 0f)
      {
        _blinkGraceRemaining = Mathf.Max(0f, _blinkGraceRemaining - deltaTime);
        return;
      }

      _secondsSinceLastBlink += Mathf.Max(0f, deltaTime);
    }

    private void UpdateGazeState()
    {
      if (_eyeBreakPaused || !_gameplay.IsSoftFocusNormalGameplayActive)
      {
        GazeState = SoftFocusGazeState.Paused;
        return;
      }

      if (!_gameplay.IsTrackingAvailable || !_gameplay.HasCurrentGazeInput || _gameplay.AreEyesClosed)
      {
        _comfortGaze = false;
        GazeState = SoftFocusGazeState.TrackingLost;
        return;
      }

      var gaze = _gameplay.CurrentGazeScreenPosition;
      var normalized = new Vector2(
        Screen.width > 0 ? gaze.x / Screen.width : 0.5f,
        Screen.height > 0 ? gaze.y / Screen.height : 0.5f);
      var offset = normalized - new Vector2(0.5f, 0.5f);
      if (_comfortGaze)
      {
        if (Mathf.Abs(offset.x) > _comfortExitHalfWidth || Mathf.Abs(offset.y) > _comfortExitHalfHeight)
        {
          _comfortGaze = false;
          if (_gameplay.HasFirstLevelModule(FirstLevelModuleId.LockHold))
          {
            _comfortHoldUntil = Time.unscaledTime + Mathf.Max(0f, _focusHoldSeconds);
            _gameplay.NotifySoftFocusModuleActivated(FirstLevelModuleId.LockHold);
          }
        }
      }
      else if (Mathf.Abs(offset.x) <= _comfortEnterHalfWidth && Mathf.Abs(offset.y) <= _comfortEnterHalfHeight)
      {
        _comfortGaze = true;
      }

      GazeState = _comfortGaze || Time.unscaledTime < _comfortHoldUntil
        ? SoftFocusGazeState.Comfort
        : SoftFocusGazeState.Peripheral;
    }

    private float CalculateTargetScale()
    {
      var drynessScale = 1f;
      if (_secondsSinceLastBlink >= _drynessSevereSeconds)
      {
        drynessScale = Mathf.Lerp(
          _severeFieldScale,
          _criticalFieldScale,
          Mathf.InverseLerp(
            _drynessSevereSeconds,
            _drynessSevereSeconds + Mathf.Max(0.1f, _criticalShrinkSeconds),
            _secondsSinceLastBlink));
      }
      else if (_secondsSinceLastBlink >= _drynessMediumSeconds)
      {
        drynessScale = Mathf.Lerp(
          _mediumFieldScale,
          _severeFieldScale,
          Mathf.InverseLerp(_drynessMediumSeconds, _drynessSevereSeconds, _secondsSinceLastBlink));
      }
      else if (_secondsSinceLastBlink >= _drynessStartSeconds)
      {
        drynessScale = Mathf.Lerp(
          1f,
          _mediumFieldScale,
          Mathf.InverseLerp(_drynessStartSeconds, _drynessMediumSeconds, _secondsSinceLastBlink));
      }

      var distanceAmount = _gameplay.IsTrackingAvailable
        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
          _distanceShrinkStartRatio,
          Mathf.Max(_distanceShrinkStartRatio + 0.01f, _distanceShrinkMaximumRatio),
          _gameplay.DistanceRatio))
        : 0f;
      var distanceScale = Mathf.Lerp(1f, _maximumTooCloseFieldScale, distanceAmount);
      var moduleScale = _gameplay.HasFirstLevelModule(FirstLevelModuleId.WideBlink) ? _wideFocusScale : 1f;
      var temporaryScale = Time.unscaledTime < _freshFocusUntil || Time.unscaledTime < _quietFieldUntil
        ? _freshFocusScale
        : 1f;
      return Mathf.Clamp(drynessScale * distanceScale * moduleScale * temporaryScale, 0.12f, 1.55f);
    }

    private float CalculateDrynessAmount(float seconds)
    {
      return Mathf.Clamp01(Mathf.InverseLerp(_drynessStartSeconds, _drynessSevereSeconds, seconds));
    }

    private void CreateVisual()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Soft Focus Field Canvas", -100, out _, out _canvasGroup);
      var fieldObject = FirstLevelUiFactory.CreateObject("Soft Focus Field", safe);
      _fieldRect = fieldObject.GetComponent<RectTransform>();
      _fieldRect.anchorMin = _fieldRect.anchorMax = new Vector2(0.5f, 0.5f + _verticalOffsetNormalized);
      _fieldRect.pivot = new Vector2(0.5f, 0.5f);
      _fieldRect.anchoredPosition = Vector2.zero;
      _graphic = fieldObject.AddComponent<SoftFocusFieldGraphic>();
      _graphic.raycastTarget = false;
      _graphic.color = KeepBlinkingTheme.AccentPrimary;
      _canvasGroup.alpha = 0f;
      UpdateVisual();
    }

    private void UpdateVisual()
    {
      if (_fieldRect == null || _graphic == null || _canvasGroup == null)
      {
        return;
      }

      var parent = _fieldRect.parent as RectTransform;
      if (parent != null)
      {
        _fieldRect.sizeDelta = new Vector2(
          parent.rect.width * _baseWidthNormalized,
          parent.rect.height * _baseHeightNormalized);
      }
      _fieldRect.localScale = Vector3.one * _currentScale;
      _canvasGroup.alpha = _currentAlpha;
      _graphic.DrynessAmount = DrynessAmount;
      _graphic.RecoveryRipple = _recoveryRipple;
      _graphic.ComfortActive = GazeState == SoftFocusGazeState.Comfort;
      _graphic.SetVerticesDirty();
    }

    private void OnDestroy()
    {
      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted -= HandleNaturalBlink;
      }
      if (Instance == this) Instance = null;
    }
  }

  internal sealed class SoftFocusFieldGraphic : MaskableGraphic
  {
    private float _drynessAmount;
    private float _recoveryRipple;
    private bool _comfortActive;

    internal float DrynessAmount
    {
      get => _drynessAmount;
      set
      {
        if (Mathf.Approximately(_drynessAmount, value)) return;
        _drynessAmount = value;
        SetVerticesDirty();
      }
    }

    internal float RecoveryRipple
    {
      get => _recoveryRipple;
      set
      {
        if (Mathf.Approximately(_recoveryRipple, value)) return;
        _recoveryRipple = value;
        SetVerticesDirty();
      }
    }

    internal bool ComfortActive
    {
      get => _comfortActive;
      set
      {
        if (_comfortActive == value) return;
        _comfortActive = value;
        SetVerticesDirty();
      }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      var rect = GetPixelAdjustedRect();
      var center = rect.center;
      var radii = rect.size * 0.5f;
      var baseColor = color;
      AddEllipseFill(vh, center, radii, new Color(baseColor.r, baseColor.g, baseColor.b, _comfortActive ? 0.08f : 0.035f));

      var breath = 0.84f + (Mathf.Sin(Time.unscaledTime * 1.25f) * 0.5f + 0.5f) * 0.16f;
      for (var quadrant = 0; quadrant < 4; quadrant++)
      {
        var start = quadrant * 90f + 12f;
        var end = quadrant * 90f + 72f;
        AddEllipseArc(vh, center, radii * breath, start, end, 10, 3f,
          new Color(baseColor.r, baseColor.g, baseColor.b, _comfortActive ? 0.27f : 0.12f));
      }

      if (_drynessAmount > 0.01f)
      {
        AddDrynessCracks(vh, center, radii, _drynessAmount, new Color(0.86f, 0.70f, 0.39f, 0.22f + _drynessAmount * 0.38f));
        var dryEyeCenter = center + new Vector2(0f, radii.y * 0.68f);
        var dryEyeRadii = new Vector2(Mathf.Min(34f, radii.x * 0.12f), Mathf.Min(15f, radii.y * 0.08f));
        var dryEyeColor = new Color(0.96f, 0.93f, 0.84f, 0.24f + _drynessAmount * 0.42f);
        AddEllipseArc(vh, dryEyeCenter, dryEyeRadii, 0f, 360f, 24, 2f, dryEyeColor);
        AddEllipseFill(vh, dryEyeCenter, Vector2.one * Mathf.Lerp(2.5f, 4.5f, _drynessAmount),
          new Color(0.86f, 0.70f, 0.39f, dryEyeColor.a));
      }

      if (_recoveryRipple > 0.01f)
      {
        var rippleRadii = radii * Mathf.Lerp(0.65f, 1.18f, 1f - _recoveryRipple);
        AddEllipseArc(vh, center, rippleRadii, 0f, 360f, 48, 3f,
          new Color(baseColor.r, baseColor.g, baseColor.b, _recoveryRipple * 0.8f));
      }
    }

    private static void AddEllipseFill(VertexHelper vh, Vector2 center, Vector2 radii, Color fillColor)
    {
      const int segments = 48;
      var centerIndex = vh.currentVertCount;
      vh.AddVert(center, fillColor, new Vector2(0.5f, 0.5f));
      for (var index = 0; index <= segments; index++)
      {
        var angle = index / (float)segments * Mathf.PI * 2f;
        var point = center + new Vector2(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y);
        vh.AddVert(point, fillColor, Vector2.zero);
        if (index > 0)
        {
          vh.AddTriangle(centerIndex, centerIndex + index, centerIndex + index + 1);
        }
      }
    }

    private static void AddEllipseArc(VertexHelper vh, Vector2 center, Vector2 radii, float startDegrees, float endDegrees, int segments, float width, Color lineColor)
    {
      var previous = EllipsePoint(center, radii, startDegrees);
      for (var index = 1; index <= segments; index++)
      {
        var angle = Mathf.Lerp(startDegrees, endDegrees, index / (float)segments);
        var next = EllipsePoint(center, radii, angle);
        AddLine(vh, previous, next, width, lineColor);
        previous = next;
      }
    }

    private static void AddDrynessCracks(VertexHelper vh, Vector2 center, Vector2 radii, float amount, Color crackColor)
    {
      const int crackCount = 12;
      var visibleCount = Mathf.CeilToInt(crackCount * amount);
      for (var index = 0; index < visibleCount; index++)
      {
        var angle = index * 137.5f + 18f;
        var edge = EllipsePoint(center, radii, angle);
        var towardCenter = (center - edge).normalized;
        var length = Mathf.Lerp(9f, 34f, amount) * (0.72f + (index % 3) * 0.14f);
        var mid = edge + towardCenter * length * 0.55f + new Vector2(-towardCenter.y, towardCenter.x) * ((index % 2 == 0 ? 1f : -1f) * 4f);
        var end = edge + towardCenter * length;
        AddLine(vh, edge, mid, 1.8f, crackColor);
        AddLine(vh, mid, end, 1.4f, crackColor);
      }
    }

    private static Vector2 EllipsePoint(Vector2 center, Vector2 radii, float degrees)
    {
      var radians = degrees * Mathf.Deg2Rad;
      return center + new Vector2(Mathf.Cos(radians) * radii.x, Mathf.Sin(radians) * radii.y);
    }

    private static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float width, Color lineColor)
    {
      var direction = (end - start).normalized;
      var normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
      var first = vh.currentVertCount;
      vh.AddVert(start - normal, lineColor, Vector2.zero);
      vh.AddVert(start + normal, lineColor, Vector2.up);
      vh.AddVert(end + normal, lineColor, Vector2.one);
      vh.AddVert(end - normal, lineColor, Vector2.right);
      vh.AddTriangle(first, first + 1, first + 2);
      vh.AddTriangle(first, first + 2, first + 3);
    }
  }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// Presentation-only FILTER state. It deliberately carries no inventory or
  /// production authority; the station view supplies state and progress.
  /// </summary>
  public enum FilterProductionVisualState
  {
    Idle = 0,
    Filtering = 1,
    BottleComplete = 2,
  }

  /// <summary>
  /// Presentation-only controller for the authored FILTER sprite layers.
  /// It never changes production, inventory, upgrades, or save data.
  /// </summary>
  public sealed class CareStationFilterArtView : MonoBehaviour
  {
    [Serializable]
    public sealed class LevelVisual
    {
      public RectTransform root;
      public RectTransform contentRoot;
      public CanvasGroup group;
      public Image baseImage;
      public Image flowImage;
      public Image crankImage;
      public Image badgeImage;
      public Image brushImage;
      public Image gaugeNeedleImage;
      public Image rawLiquidImage;
      public Image rawParticlesImage;
      public Image filterCartridgeImage;
      public Image funnelAndPipeImage;
      public Image bottleImage;
      public Image bottleFillImage;
      public Sprite[] flowFrames = Array.Empty<Sprite>();
      public Vector2 crankPivot;
      public Vector2 gaugePivot;
      public Vector2 displayScale = Vector2.one;
      public Rect normalizedHitBounds = new Rect(0f, 0f, 1f, 1f);
    }

    [SerializeField] private LevelVisual[] _levels = Array.Empty<LevelVisual>();
    [SerializeField, Range(6f, 8f)] private float _flowFramesPerSecond = 7f;
    [SerializeField, Range(0.2f, 0.4f)] private float _levelFadeSeconds = 0.28f;
    [SerializeField] private Image _hitRect;

    private int _level = 1;
    private int _transitionFrom = -1;
    private int _transitionTo = -1;
    private float _transitionStartedAt;
    private bool _running;
    private bool _pipelineHighlighted;
    private FilterProductionVisualState _productionVisualState = FilterProductionVisualState.Idle;
    private float _productionVisualProgress;
    private float _productionStateEnteredAt;

    public int Level => _level;
    public bool Running => _running;
    public bool PipelineHighlighted => _pipelineHighlighted;
    public FilterProductionVisualState ProductionVisualState => _productionVisualState;
    public float ProductionVisualProgress => _productionVisualProgress;
    public Image HitRect => _hitRect;
    public LevelVisual[] Levels => _levels;

    public static CareStationFilterArtView Create(Transform parent, CareStationFilterArtCatalog catalog)
    {
      if (catalog == null || catalog.Levels == null || catalog.Levels.Length == 0) return null;
      var root = CreateRect("CareStationFilterArt");
      root.SetParent(parent, false);
      root.sizeDelta = new Vector2(1024f, 1024f);

      var hitRect = AddImage("Filter Hit Rect", root, null);
      Stretch(hitRect.rectTransform);
      hitRect.color = Color.clear;
      hitRect.raycastTarget = false;

      var levels = new LevelVisual[catalog.Levels.Length];
      for (var i = 0; i < catalog.Levels.Length; i++)
      {
        var source = catalog.Levels[i];
        var levelRoot = CreateRect($"Filter L{source.level}");
        levelRoot.SetParent(root, false);
        Stretch(levelRoot);
        var group = levelRoot.gameObject.AddComponent<CanvasGroup>();
        var contentRoot = CreateRect("Artwork");
        contentRoot.SetParent(levelRoot, false);
        Stretch(contentRoot);
        // Authored equipment shares a bottom-center pivot. Runtime artwork is
        // sized by its RectTransform; keeping this transform at unit scale
        // avoids a second, fractional resampling pass through the Canvas.
        contentRoot.pivot = new Vector2(0.5f, 0f);
        var displayScale = SanitizeDisplayScale(source.displayScale);
        contentRoot.localScale = Vector3.one;
        var visual = new LevelVisual
        {
          root = levelRoot,
          contentRoot = contentRoot,
          group = group,
          rawLiquidImage = AddOptionalFullCanvasLayer("Raw Liquid", contentRoot, source.rawLiquidSprite),
          rawParticlesImage = AddOptionalFullCanvasLayer("Raw Particles", contentRoot, source.rawParticlesSprite),
          bottleFillImage = AddOptionalFullCanvasLayer("Bottle Fill", contentRoot, source.bottleFillSprite),
          baseImage = AddFullCanvasLayer("Base", contentRoot, source.baseSprite),
          filterCartridgeImage = AddOptionalFullCanvasLayer("Filter Cartridge", contentRoot, source.filterCartridgeSprite),
          funnelAndPipeImage = AddOptionalFullCanvasLayer("Funnel And Pipe", contentRoot, source.funnelAndPipeSprite),
          flowImage = AddFullCanvasLayer("Flow", contentRoot,
            source.flowFrames != null && source.flowFrames.Length > 0 ? source.flowFrames[0] : null),
          bottleImage = AddOptionalFullCanvasLayer("Bottle", contentRoot, source.bottleSprite),
          badgeImage = AddFullCanvasLayer("Badge", contentRoot, source.badgeSprite),
          flowFrames = source.flowFrames ?? Array.Empty<Sprite>(),
          crankPivot = source.crankPivot,
          gaugePivot = source.gaugePivot,
          displayScale = displayScale,
          normalizedHitBounds = SanitizeNormalizedBounds(source.normalizedHitBounds),
        };
        // The approved Level 1 design has no crank. Keeping this guard here
        // also prevents a stale pre-approval catalog reference from rendering.
        if (source.level != 1 && source.crankSprite != null)
          visual.crankImage = AddPivotedLayer("Crank", contentRoot, source.crankSprite, source.crankPivot);
        if (source.brushSprite != null)
          visual.brushImage = AddFullCanvasLayer("Automatic Brush", contentRoot, source.brushSprite);
        if (source.gaugeNeedleSprite != null)
          visual.gaugeNeedleImage = AddPivotedLayer("Gauge Needle", contentRoot, source.gaugeNeedleSprite, source.gaugePivot);
        ConfigureVerticalFill(visual.rawLiquidImage, 1f);
        ConfigureVerticalFill(visual.bottleFillImage, 0f);
        levels[i] = visual;
      }

      var view = root.gameObject.AddComponent<CareStationFilterArtView>();
      view.EditorConfigure(levels, hitRect);
      return view;
    }

    private void Awake()
    {
      ApplyImmediate(_level);
      SetRunning(false);
    }

    public void SetLevel(int level, bool animate)
    {
      level = Mathf.Clamp(level, 1, Mathf.Max(1, _levels.Length));
      if (level == _level && _transitionTo < 0)
      {
        ApplyImmediate(level);
        return;
      }

      var previous = Mathf.Clamp(_level - 1, 0, Mathf.Max(0, _levels.Length - 1));
      _level = level;
      if (!animate || !isActiveAndEnabled || _levels.Length == 0)
      {
        ApplyImmediate(level);
        return;
      }

      _transitionFrom = previous;
      _transitionTo = level - 1;
      _transitionStartedAt = Time.unscaledTime;
      for (var i = 0; i < _levels.Length; i++)
      {
        var active = i == _transitionFrom || i == _transitionTo;
        if (_levels[i]?.root != null) _levels[i].root.gameObject.SetActive(active);
      }
      if (_levels[_transitionFrom]?.group != null) _levels[_transitionFrom].group.alpha = 1f;
      if (_levels[_transitionTo]?.group != null) _levels[_transitionTo].group.alpha = 0f;
      if (_levels[_transitionTo]?.root != null) _levels[_transitionTo].root.localScale = Vector3.one;
      ApplyHitBounds(_levels[_transitionTo]);
      RefreshFlowVisibility();
    }

    public void SetRunning(bool running)
    {
      _running = running;
      if (running && _productionVisualState != FilterProductionVisualState.Filtering)
        SetProductionVisual(FilterProductionVisualState.Filtering, 0f);
      else if (!running && _productionVisualState == FilterProductionVisualState.Filtering)
        SetProductionVisual(FilterProductionVisualState.Idle, 0f);
      RefreshFlowVisibility();
    }

    /// <summary>
    /// Updates the FILTER's presentation only. No bottle, storage, upgrade, or
    /// save value is read or written here.
    /// </summary>
    public void SetProductionVisual(FilterProductionVisualState state, float progress)
    {
      progress = Mathf.Clamp01(progress);
      if (_productionVisualState != state)
      {
        _productionVisualState = state;
        _productionStateEnteredAt = Time.unscaledTime;
      }
      _productionVisualProgress = progress;
      _running = state == FilterProductionVisualState.Filtering;
      RefreshFlowVisibility();
      ApplyLevelOneProductionVisual();
    }

    public void SetPipelineHighlighted(bool highlighted)
    {
      _pipelineHighlighted = highlighted;
    }

    public void SetHitTestEnabled(bool enabled)
    {
      if (_hitRect != null) _hitRect.raycastTarget = enabled;
    }

    private void Update()
    {
      UpdateLevelTransition();
      UpdateAnimation();
    }

    private void UpdateLevelTransition()
    {
      if (_transitionTo < 0 || _transitionTo >= _levels.Length) return;
      var duration = Mathf.Max(0.05f, _levelFadeSeconds);
      var t = Mathf.Clamp01((Time.unscaledTime - _transitionStartedAt) / duration);
      var eased = Mathf.SmoothStep(0f, 1f, t);
      var oldVisual = _transitionFrom >= 0 && _transitionFrom < _levels.Length ? _levels[_transitionFrom] : null;
      var newVisual = _levels[_transitionTo];
      if (oldVisual?.group != null) oldVisual.group.alpha = 1f - eased;
      if (newVisual?.group != null) newVisual.group.alpha = eased;
      if (oldVisual?.root != null) oldVisual.root.localScale = Vector3.one;
      if (newVisual?.root != null) newVisual.root.localScale = Vector3.one;
      if (t < 1f) return;
      ApplyImmediate(_level);
    }

    private void UpdateAnimation()
    {
      if (_levels.Length == 0) return;
      var active = _levels[Mathf.Clamp(_level - 1, 0, _levels.Length - 1)];
      if (active == null) return;
      var time = Time.unscaledTime;
      if (active.flowImage != null && active.flowImage.gameObject.activeSelf &&
          active.flowFrames != null && active.flowFrames.Length > 0)
      {
        var frame = Mathf.FloorToInt(time * _flowFramesPerSecond) % active.flowFrames.Length;
        active.flowImage.sprite = active.flowFrames[frame];
      }

      if (active.crankImage != null)
        active.crankImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _running ? -time * 46f : 0f);
      if (active.brushImage != null)
        active.brushImage.rectTransform.anchoredPosition = Vector2.up * (_running ? Mathf.Sin(time * 2.2f) * 9f : 0f);
      if (active.gaugeNeedleImage != null)
        active.gaugeNeedleImage.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
          _running ? Mathf.Sin(time * 1.35f) * 7f : -12f);

      if (active.root != null) active.root.localScale = Vector3.one;
      if (active.contentRoot != null) active.contentRoot.localScale = Vector3.one;
      if (active.baseImage != null)
      {
        var highlight = _pipelineHighlighted ? (Mathf.Sin(time * 4.5f) + 1f) * 0.5f : 0f;
        active.baseImage.color = Color.Lerp(Color.white, new Color(0.95f, 1f, 0.98f, 1f), highlight * 0.16f);
      }
      ApplyLevelOneProductionVisual();
    }

    private void ApplyImmediate(int level)
    {
      _level = Mathf.Clamp(level, 1, Mathf.Max(1, _levels.Length));
      _transitionFrom = -1;
      _transitionTo = -1;
      for (var i = 0; i < _levels.Length; i++)
      {
        var visual = _levels[i];
        if (visual?.root == null) continue;
        var selected = i == _level - 1;
        visual.root.gameObject.SetActive(selected);
        visual.root.localScale = Vector3.one;
        if (visual.group != null) visual.group.alpha = selected ? 1f : 0f;
      }
      ApplyHitBounds(_levels.Length > 0 ? _levels[_level - 1] : null);
      RefreshFlowVisibility();
      ApplyLevelOneProductionVisual();
    }

    public void EditorConfigure(LevelVisual[] levels, Image hitRect)
    {
      _levels = levels ?? Array.Empty<LevelVisual>();
      _hitRect = hitRect;
      _level = 1;
      ApplyImmediate(_level);
      SetHitTestEnabled(false);
    }

    private void RefreshFlowVisibility()
    {
      for (var i = 0; i < _levels.Length; i++)
      {
        var visual = _levels[i];
        if (visual?.flowImage == null) continue;
        var isLevelOne = i == 0;
        var show = isLevelOne
          ? _productionVisualState == FilterProductionVisualState.Filtering
          : _running;
        visual.flowImage.gameObject.SetActive(show);
      }
    }

    private void ApplyLevelOneProductionVisual()
    {
      if (_levels.Length == 0) return;
      var visual = _levels[0];
      if (visual == null) return;

      var filtering = _productionVisualState == FilterProductionVisualState.Filtering;
      var complete = _productionVisualState == FilterProductionVisualState.BottleComplete;
      var progress = complete ? 1f : filtering ? _productionVisualProgress : 0f;
      var time = Time.unscaledTime;

      if (visual.rawLiquidImage != null)
      {
        visual.rawLiquidImage.rectTransform.localScale = Vector3.one;
        visual.rawLiquidImage.fillAmount = Mathf.Lerp(1f, 0.86f, progress);
      }

      if (visual.rawParticlesImage != null)
      {
        // A tiny, slow drift keeps the raw material alive without flicker.
        visual.rawParticlesImage.rectTransform.anchoredPosition = new Vector2(
          Mathf.Sin(time * 0.31f) * 1.8f,
          Mathf.Sin(time * 0.23f + 0.8f) * 2.4f);
      }

      if (visual.filterCartridgeImage != null)
      {
        var oneShot = filtering ? Mathf.Sin(Mathf.Clamp01(progress * 5f) * Mathf.PI) : 0f;
        visual.filterCartridgeImage.rectTransform.localScale = Vector3.one;
        visual.filterCartridgeImage.color = Color.Lerp(Color.white,
          new Color(0.88f, 1f, 0.94f, 1f), oneShot * 0.45f);
      }

      if (visual.bottleFillImage != null)
      {
        visual.bottleFillImage.gameObject.SetActive(filtering || complete);
        visual.bottleFillImage.rectTransform.localScale = Vector3.one;
        visual.bottleFillImage.fillAmount = Mathf.Lerp(0.04f, 1f, progress);
      }

      var completionAge = complete ? Mathf.Max(0f, time - _productionStateEnteredAt) : 0f;
      var completionLift = complete ? Mathf.SmoothStep(0f, 6f, Mathf.Clamp01(completionAge / 0.32f)) : 0f;
      var completionGlow = complete && completionAge < 0.7f
        ? Mathf.Sin(Mathf.Clamp01(completionAge / 0.7f) * Mathf.PI)
        : 0f;
      ApplyBottleCompletionTransform(visual.bottleImage, completionLift,
        Color.Lerp(Color.white, new Color(0.88f, 1f, 0.95f, 1f), completionGlow));
      ApplyBottleCompletionTransform(visual.bottleFillImage, completionLift, Color.white);
    }

    private static void ApplyBottleCompletionTransform(
      Image image,
      float lift,
      Color color)
    {
      if (image == null) return;
      image.rectTransform.anchoredPosition = Vector2.up * lift;
      image.rectTransform.localScale = Vector3.one;
      image.color = color;
    }

    private static void ConfigureVerticalFill(Image image, float amount)
    {
      if (image == null) return;
      image.type = Image.Type.Filled;
      image.fillMethod = Image.FillMethod.Vertical;
      image.fillOrigin = (int)Image.OriginVertical.Bottom;
      image.fillClockwise = true;
      image.fillAmount = Mathf.Clamp01(amount);
      image.rectTransform.localScale = Vector3.one;
    }

    private void ApplyHitBounds(LevelVisual visual)
    {
      if (_hitRect == null) return;
      var bounds = visual == null
        ? new Rect(0f, 0f, 1f, 1f)
        : SanitizeNormalizedBounds(visual.normalizedHitBounds);
      var rect = _hitRect.rectTransform;
      rect.anchorMin = new Vector2(bounds.xMin, bounds.yMin);
      rect.anchorMax = new Vector2(bounds.xMax, bounds.yMax);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.anchoredPosition = Vector2.zero;
      rect.localScale = Vector3.one;
    }

    private static Vector2 SanitizeDisplayScale(Vector2 scale)
    {
      return new Vector2(
        scale.x > 0.05f ? scale.x : 1f,
        scale.y > 0.05f ? scale.y : 1f);
    }

    private static Rect SanitizeNormalizedBounds(Rect bounds)
    {
      if (bounds.width <= 0f || bounds.height <= 0f)
        return new Rect(0f, 0f, 1f, 1f);
      var xMin = Mathf.Clamp01(bounds.xMin);
      var yMin = Mathf.Clamp01(bounds.yMin);
      var xMax = Mathf.Clamp01(bounds.xMax);
      var yMax = Mathf.Clamp01(bounds.yMax);
      if (xMax <= xMin || yMax <= yMin) return new Rect(0f, 0f, 1f, 1f);
      return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static Image AddFullCanvasLayer(string name, RectTransform parent, Sprite sprite)
    {
      var image = AddImage(name, parent, sprite);
      Stretch(image.rectTransform);
      return image;
    }

    private static Image AddOptionalFullCanvasLayer(string name, RectTransform parent, Sprite sprite)
    {
      return sprite == null ? null : AddFullCanvasLayer(name, parent, sprite);
    }

    private static Image AddPivotedLayer(string name, RectTransform parent, Sprite sprite, Vector2 pivot)
    {
      var image = AddImage(name, parent, sprite);
      var rect = image.rectTransform;
      rect.anchorMin = rect.anchorMax = Vector2.zero;
      rect.pivot = pivot;
      rect.sizeDelta = new Vector2(1024f, 1024f);
      rect.anchoredPosition = Vector2.Scale(pivot, rect.sizeDelta);
      return image;
    }

    private static Image AddImage(string name, Transform parent, Sprite sprite)
    {
      var rect = CreateRect(name);
      rect.SetParent(parent, false);
      var image = rect.gameObject.AddComponent<Image>();
      image.sprite = sprite;
      image.preserveAspect = true;
      image.raycastTarget = false;
      image.color = Color.white;
      return image;
    }

    private static RectTransform CreateRect(string name)
    {
      return new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
    }
  }
}

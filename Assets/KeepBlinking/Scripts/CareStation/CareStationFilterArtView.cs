using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  public enum FilterProductionVisualState
  {
    Idle = 0,
    Filtering = 1,
    BottleComplete = 2,
  }

  /// <summary>
  /// Presentation-only FILTER art. It never changes production, inventory,
  /// upgrades, settlement, recipes, or save data.
  /// </summary>
  public sealed class CareStationFilterArtView : MonoBehaviour
  {
    [Serializable]
    public sealed class LevelVisual
    {
      public RectTransform root;
      public RectTransform contentRoot;
      public CanvasGroup group;

      // Shared/legacy fields retained for Level 2/3 and existing diagnostics.
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

      // Approved Level 1 hierarchy. MachineRoot and BottleFillAnchor are
      // siblings so a completed BottleRoot can later be reparented by Worker.
      public RectTransform machineRoot;
      public Image machineBaseImage;
      public RectMask2D rawLiquidMask;
      public Image rawLiquidBodyImage;
      public Image rawLiquidSurfaceImage;
      public Image filterBedImage;
      public Image filterDripImage;
      public Image outletFlowImage;
      public RectTransform bottleFillAnchor;
      public RectTransform bottleRoot;
      public RectTransform bottlePickupAnchor;
      public RectMask2D bottleLiquidMask;
      public Image bottleGlassImage;
      public Image bottleLiquidBodyImage;
      public Image bottleLiquidSurfaceImage;
      public Sprite[] filterDripFrames = Array.Empty<Sprite>();
      public Sprite[] outletFlowFrames = Array.Empty<Sprite>();
      public float rawLiquidMaxHeight;
      public float bottleLiquidMaxHeight;
    }

    private const float RawLiquidBottom = 0.558f;
    private const float BottleLiquidBottom = 0.115f;
    private const float DripOnlyProgress = 0.14f;
    private const float FlowTaperProgress = 0.86f;
    private const float LastDropSeconds = 0.62f;

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
    private int _productionStateVersion;
    private bool _integratedBottleVisible = true;

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
        var visual = CreateLevelShell(root, source);
        if (source.level == 1 && source.machineBaseSprite != null)
          BuildLevelOneVisual(visual, source);
        else
          BuildLegacyVisual(visual, source);
        levels[i] = visual;
      }

      var view = root.gameObject.AddComponent<CareStationFilterArtView>();
      view.EditorConfigure(levels, hitRect);
      return view;
    }

    private static LevelVisual CreateLevelShell(RectTransform root, CareStationFilterArtCatalog.LevelSprites source)
    {
      var levelRoot = CreateRect($"Filter L{source.level}");
      levelRoot.SetParent(root, false);
      Stretch(levelRoot);
      var contentRoot = CreateRect("Artwork");
      contentRoot.SetParent(levelRoot, false);
      Stretch(contentRoot);
      contentRoot.pivot = new Vector2(0.5f, 0f);
      return new LevelVisual
      {
        root = levelRoot,
        contentRoot = contentRoot,
        group = levelRoot.gameObject.AddComponent<CanvasGroup>(),
        crankPivot = source.crankPivot,
        gaugePivot = source.gaugePivot,
        displayScale = SanitizeDisplayScale(source.displayScale),
        normalizedHitBounds = SanitizeNormalizedBounds(source.normalizedHitBounds),
      };
    }

    private static void BuildLevelOneVisual(LevelVisual visual, CareStationFilterArtCatalog.LevelSprites source)
    {
      visual.machineRoot = CreateRect("MachineRoot");
      visual.machineRoot.SetParent(visual.contentRoot, false);
      Stretch(visual.machineRoot);

      visual.rawLiquidMask = AddRectMask("RawLiquidMask", visual.machineRoot);
      visual.rawLiquidMaxHeight = 82f;
      SetBottomAnchoredRect(visual.rawLiquidMask.rectTransform,
        new Vector2(0.5f, RawLiquidBottom), new Vector2(82f, visual.rawLiquidMaxHeight));
      visual.rawLiquidBodyImage = AddFixedBottomImage("RawLiquidBody", visual.rawLiquidMask.transform,
        source.rawLiquidBodySprite, new Vector2(82f, visual.rawLiquidMaxHeight));
      visual.rawLiquidSurfaceImage = AddPointLayer("RawLiquidSurface", visual.machineRoot,
        source.rawLiquidSurfaceSprite, new Vector2(0.5f, RawLiquidBottom), new Vector2(88f, 14f));

      visual.filterBedImage = AddPointLayer("FilterBed", visual.machineRoot, source.filterBedSprite,
        new Vector2(0.5f, 0.548f), new Vector2(98f, 44f));
      visual.filterDripImage = AddPointLayer("FilterDrips", visual.machineRoot,
        FirstFrame(source.filterDripFrames), new Vector2(0.5f, 0.493f), new Vector2(42f, 54f));
      visual.outletFlowImage = AddPointLayer("OutletFlow", visual.machineRoot,
        FirstFrame(source.outletFlowFrames), new Vector2(0.5f, 0.338f), new Vector2(34f, 88f));
      visual.machineBaseImage = AddFullCanvasLayer("MachineBase", visual.machineRoot, source.machineBaseSprite);

      // Compatibility aliases point at the same authored sprites; visual state
      // transitions cannot select the retired Level 1 sheets.
      visual.baseImage = visual.machineBaseImage;
      visual.flowImage = visual.outletFlowImage;
      visual.flowFrames = source.outletFlowFrames ?? Array.Empty<Sprite>();
      visual.filterDripFrames = source.filterDripFrames ?? Array.Empty<Sprite>();
      visual.outletFlowFrames = source.outletFlowFrames ?? Array.Empty<Sprite>();

      visual.bottleFillAnchor = CreateRect("BottleFillAnchor");
      visual.bottleFillAnchor.SetParent(visual.contentRoot, false);
      SetPointRect(visual.bottleFillAnchor, new Vector2(0.5f, 0.166f), new Vector2(72f, 108f));
      visual.bottleRoot = CreateRect("BottleRoot");
      visual.bottleRoot.SetParent(visual.bottleFillAnchor, false);
      Stretch(visual.bottleRoot);

      visual.bottleLiquidMask = AddRectMask("BottleLiquidMask", visual.bottleRoot);
      visual.bottleLiquidMaxHeight = 62f;
      SetBottomAnchoredRect(visual.bottleLiquidMask.rectTransform,
        new Vector2(0.5f, BottleLiquidBottom), new Vector2(43f, visual.bottleLiquidMaxHeight));
      visual.bottleLiquidBodyImage = AddFixedBottomImage("BottleLiquidBody",
        visual.bottleLiquidMask.transform, source.bottleLiquidBodySprite,
        new Vector2(43f, visual.bottleLiquidMaxHeight));
      visual.bottleLiquidSurfaceImage = AddPointLayer("BottleLiquidSurface", visual.bottleRoot,
        source.bottleLiquidSurfaceSprite, new Vector2(0.5f, BottleLiquidBottom), new Vector2(45f, 12f));
      visual.bottleGlassImage = AddFullCanvasLayer("BottleGlass", visual.bottleRoot, source.bottleGlassSprite);

      visual.bottlePickupAnchor = CreateRect("Carry/Pickup Anchor");
      visual.bottlePickupAnchor.SetParent(visual.bottleRoot, false);
      SetPointRect(visual.bottlePickupAnchor, new Vector2(0.5f, 0.82f), Vector2.one);

      visual.bottleImage = visual.bottleGlassImage;
      visual.bottleFillImage = visual.bottleLiquidBodyImage;
      if (source.badgeSprite != null)
        visual.badgeImage = AddFullCanvasLayer("Badge", visual.contentRoot, source.badgeSprite);

      SetMaskedLiquid(visual.rawLiquidMask, visual.rawLiquidBodyImage,
        visual.rawLiquidSurfaceImage, visual.rawLiquidMaxHeight, 1f);
      SetMaskedLiquid(visual.bottleLiquidMask, visual.bottleLiquidBodyImage,
        visual.bottleLiquidSurfaceImage, visual.bottleLiquidMaxHeight, 0f);
      visual.filterDripImage.gameObject.SetActive(false);
      visual.outletFlowImage.gameObject.SetActive(false);
    }

    private static void BuildLegacyVisual(LevelVisual visual, CareStationFilterArtCatalog.LevelSprites source)
    {
      visual.rawLiquidImage = AddOptionalFullCanvasLayer("Raw Liquid", visual.contentRoot, source.rawLiquidSprite);
      visual.rawParticlesImage = AddOptionalFullCanvasLayer("Raw Particles", visual.contentRoot, source.rawParticlesSprite);
      visual.bottleFillImage = AddOptionalFullCanvasLayer("Bottle Fill", visual.contentRoot, source.bottleFillSprite);
      visual.baseImage = AddFullCanvasLayer("Base", visual.contentRoot, source.baseSprite);
      visual.filterCartridgeImage = AddOptionalFullCanvasLayer("Filter Cartridge", visual.contentRoot, source.filterCartridgeSprite);
      visual.funnelAndPipeImage = AddOptionalFullCanvasLayer("Funnel And Pipe", visual.contentRoot, source.funnelAndPipeSprite);
      visual.flowImage = AddFullCanvasLayer("Flow", visual.contentRoot, FirstFrame(source.flowFrames));
      visual.bottleImage = AddOptionalFullCanvasLayer("Bottle", visual.contentRoot, source.bottleSprite);
      visual.badgeImage = AddFullCanvasLayer("Badge", visual.contentRoot, source.badgeSprite);
      visual.flowFrames = source.flowFrames ?? Array.Empty<Sprite>();
      if (source.level != 1 && source.crankSprite != null)
        visual.crankImage = AddPivotedLayer("Crank", visual.contentRoot, source.crankSprite, source.crankPivot);
      if (source.brushSprite != null)
        visual.brushImage = AddFullCanvasLayer("Automatic Brush", visual.contentRoot, source.brushSprite);
      if (source.gaugeNeedleSprite != null)
        visual.gaugeNeedleImage = AddPivotedLayer("Gauge Needle", visual.contentRoot,
          source.gaugeNeedleSprite, source.gaugePivot);
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

    public void SetProductionVisual(FilterProductionVisualState state, float progress)
    {
      progress = Mathf.Clamp01(progress);
      if (_productionVisualState != state)
      {
        _productionVisualState = state;
        _productionStateEnteredAt = Time.realtimeSinceStartup;
        _productionStateVersion++;
        if (state == FilterProductionVisualState.BottleComplete && isActiveAndEnabled)
          StartCoroutine(HideLastDropAfterRealtime(_productionStateVersion));
      }
      _productionVisualProgress = progress;
      _running = state == FilterProductionVisualState.Filtering;
      RefreshFlowVisibility();
      ApplyLevelOneProductionVisual(Time.unscaledTime);
    }

    private IEnumerator HideLastDropAfterRealtime(int stateVersion)
    {
      yield return new WaitForSecondsRealtime(LastDropSeconds);
      if (stateVersion != _productionStateVersion ||
          _productionVisualState != FilterProductionVisualState.BottleComplete) yield break;
      ApplyLevelOneProductionVisual(Time.unscaledTime);
    }

    public void SetPipelineHighlighted(bool highlighted)
    {
      _pipelineHighlighted = highlighted;
    }

    public void SetHitTestEnabled(bool enabled)
    {
      if (_hitRect != null) _hitRect.raycastTarget = enabled;
    }

    public void SetIntegratedBottleVisible(bool visible)
    {
      _integratedBottleVisible = visible;
      for (var index = 0; index < _levels.Length; index++)
      {
        var visual = _levels[index];
        if (visual == null) continue;
        if (visual.bottleFillAnchor != null) visual.bottleFillAnchor.gameObject.SetActive(visible);
        else
        {
          if (visual.bottleImage != null) visual.bottleImage.gameObject.SetActive(visible);
          if (visual.bottleFillImage != null) visual.bottleFillImage.gameObject.SetActive(visible);
        }
      }
      ApplyLevelOneProductionVisual(Time.unscaledTime);
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
      var isAuthoredLevelOne = _level == 1 && active.machineRoot != null;
      if (!isAuthoredLevelOne && active.flowImage != null && active.flowImage.gameObject.activeSelf &&
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
        active.baseImage.color = Color.Lerp(Color.white,
          new Color(0.95f, 1f, 0.98f, 1f), highlight * 0.16f);
      }
      ApplyLevelOneProductionVisual(time);
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
      ApplyLevelOneProductionVisual(Time.unscaledTime);
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
        if (visual == null) continue;
        if (i == 0 && visual.machineRoot != null)
        {
          ApplyLevelOneProductionVisual(Time.unscaledTime);
          continue;
        }
        if (visual.flowImage != null) visual.flowImage.gameObject.SetActive(_running);
      }
    }

    private void ApplyLevelOneProductionVisual(float time)
    {
      if (_levels.Length == 0) return;
      var visual = _levels[0];
      if (visual?.machineRoot == null) return;

      var filtering = _productionVisualState == FilterProductionVisualState.Filtering;
      var complete = _productionVisualState == FilterProductionVisualState.BottleComplete;
      var fillProgress = complete ? 1f : filtering ? _productionVisualProgress : 0f;
      SetMaskedLiquid(visual.rawLiquidMask, visual.rawLiquidBodyImage,
        visual.rawLiquidSurfaceImage, visual.rawLiquidMaxHeight, 1f - fillProgress);
      SetMaskedLiquid(visual.bottleLiquidMask, visual.bottleLiquidBodyImage,
        visual.bottleLiquidSurfaceImage, visual.bottleLiquidMaxHeight,
        _integratedBottleVisible ? fillProgress : 0f);

      if (visual.filterDripImage != null)
      {
        var showDrips = filtering && fillProgress < DripOnlyProgress;
        visual.filterDripImage.gameObject.SetActive(showDrips);
        if (showDrips)
          visual.filterDripImage.sprite = AnimatedFrame(visual.filterDripFrames, time, _flowFramesPerSecond);
      }

      if (visual.outletFlowImage != null)
      {
        var completionAge = complete
          ? Mathf.Max(0f, Time.realtimeSinceStartup - _productionStateEnteredAt)
          : 0f;
        var showOutlet = _integratedBottleVisible &&
                         (filtering && fillProgress >= DripOnlyProgress || complete && completionAge < LastDropSeconds);
        visual.outletFlowImage.gameObject.SetActive(showOutlet);
        if (showOutlet)
        {
          visual.outletFlowImage.sprite = complete
            ? LastFrame(visual.outletFlowFrames)
            : SelectOutletFrame(visual.outletFlowFrames, fillProgress, time, _flowFramesPerSecond);
        }
      }

      if (visual.bottleRoot != null)
      {
        visual.bottleRoot.gameObject.SetActive(_integratedBottleVisible);
        if (!_integratedBottleVisible) return;
        var completionAge = complete
          ? Mathf.Max(0f, Time.realtimeSinceStartup - _productionStateEnteredAt)
          : 0f;
        var completionLift = complete
          ? Mathf.SmoothStep(0f, 6f, Mathf.Clamp01(completionAge / 0.32f))
          : 0f;
        visual.bottleRoot.anchoredPosition = Vector2.up * completionLift;
        visual.bottleRoot.localScale = Vector3.one;
        if (visual.bottleGlassImage != null)
        {
          var glow = complete && completionAge < 0.7f
            ? Mathf.Sin(Mathf.Clamp01(completionAge / 0.7f) * Mathf.PI)
            : 0f;
          visual.bottleGlassImage.color = Color.Lerp(Color.white,
            new Color(0.88f, 1f, 0.95f, 1f), glow);
        }
      }
    }

    private static Sprite SelectOutletFrame(Sprite[] frames, float progress, float time, float fps)
    {
      if (frames == null || frames.Length == 0) return null;
      if (progress < FlowTaperProgress)
      {
        var steadyFrames = Mathf.Max(1, frames.Length - 2);
        return frames[Mathf.FloorToInt(time * fps) % steadyFrames];
      }
      var taper = Mathf.InverseLerp(FlowTaperProgress, 1f, progress);
      var index = Mathf.Clamp(Mathf.FloorToInt(taper * frames.Length), 0, frames.Length - 1);
      return frames[index];
    }

    private static Sprite AnimatedFrame(Sprite[] frames, float time, float fps)
    {
      if (frames == null || frames.Length == 0) return null;
      return frames[Mathf.FloorToInt(time * fps) % frames.Length];
    }

    private static Sprite FirstFrame(Sprite[] frames)
    {
      return frames != null && frames.Length > 0 ? frames[0] : null;
    }

    private static Sprite LastFrame(Sprite[] frames)
    {
      return frames != null && frames.Length > 0 ? frames[frames.Length - 1] : null;
    }

    private static void SetMaskedLiquid(RectMask2D mask, Image body, Image surface,
      float maximumHeight, float amount)
    {
      amount = Mathf.Clamp01(amount);
      if (mask != null)
      {
        var size = mask.rectTransform.sizeDelta;
        size.y = Mathf.Max(0.01f, maximumHeight * amount);
        mask.rectTransform.sizeDelta = size;
        mask.rectTransform.localScale = Vector3.one;
      }
      if (body != null)
      {
        body.gameObject.SetActive(amount > 0.001f);
        body.rectTransform.localScale = Vector3.one;
      }
      if (surface != null)
      {
        surface.gameObject.SetActive(amount > 0.001f);
        surface.rectTransform.anchoredPosition = Vector2.up * (maximumHeight * amount);
        surface.rectTransform.localScale = Vector3.one;
      }
    }

    private void ApplyHitBounds(LevelVisual visual)
    {
      if (_hitRect == null) return;
      var bounds = visual == null ? new Rect(0f, 0f, 1f, 1f) : SanitizeNormalizedBounds(visual.normalizedHitBounds);
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
      return new Vector2(scale.x > 0.05f ? scale.x : 1f, scale.y > 0.05f ? scale.y : 1f);
    }

    private static Rect SanitizeNormalizedBounds(Rect bounds)
    {
      if (bounds.width <= 0f || bounds.height <= 0f) return new Rect(0f, 0f, 1f, 1f);
      var xMin = Mathf.Clamp01(bounds.xMin);
      var yMin = Mathf.Clamp01(bounds.yMin);
      var xMax = Mathf.Clamp01(bounds.xMax);
      var yMax = Mathf.Clamp01(bounds.yMax);
      if (xMax <= xMin || yMax <= yMin) return new Rect(0f, 0f, 1f, 1f);
      return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private static RectMask2D AddRectMask(string name, Transform parent)
    {
      var rect = CreateRect(name);
      rect.SetParent(parent, false);
      return rect.gameObject.AddComponent<RectMask2D>();
    }

    private static Image AddFixedBottomImage(string name, Transform parent, Sprite sprite, Vector2 size)
    {
      var image = AddImage(name, parent, sprite);
      var rect = image.rectTransform;
      rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
      rect.pivot = new Vector2(0.5f, 0f);
      rect.anchoredPosition = Vector2.zero;
      rect.sizeDelta = size;
      return image;
    }

    private static Image AddPointLayer(string name, Transform parent, Sprite sprite, Vector2 anchor, Vector2 size)
    {
      var image = AddImage(name, parent, sprite);
      SetPointRect(image.rectTransform, anchor, size);
      return image;
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

    private static void SetBottomAnchoredRect(RectTransform rect, Vector2 anchor, Vector2 size)
    {
      rect.anchorMin = rect.anchorMax = anchor;
      rect.pivot = new Vector2(0.5f, 0f);
      rect.anchoredPosition = Vector2.zero;
      rect.sizeDelta = size;
      rect.localScale = Vector3.one;
    }

    private static void SetPointRect(RectTransform rect, Vector2 anchor, Vector2 size)
    {
      rect.anchorMin = rect.anchorMax = anchor;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = Vector2.zero;
      rect.sizeDelta = size;
      rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
      rect.localScale = Vector3.one;
    }
  }
}

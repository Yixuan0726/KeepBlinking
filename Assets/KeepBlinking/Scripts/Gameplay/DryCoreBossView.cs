using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  internal readonly struct CoverageTargetGeometry
  {
    internal Vector2 ScreenPosition { get; }
    internal float RadiusPixels { get; }

    internal CoverageTargetGeometry(Vector2 screenPosition, float radiusPixels)
    {
      ScreenPosition = screenPosition;
      RadiusPixels = Mathf.Max(0f, radiusPixels);
    }
  }

  internal sealed class DryCoreBossView : MonoBehaviour
  {
    private GameObject _canvasObject;
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;
    private RectTransform _safeRoot;
    private RectTransform _bossRoot;
    private RectTransform _coverageWave;
    private CanvasGroup _titleGroup;
    private Image[] _layers;
    private Image[] _coreRings;
    private Image[] _fragments;
    private DryCoreCrackGraphic _cracks;
    private DryCoreBossPromptView _prompt;
    private float _motionTime;
    private float _titleUntil;
    private float _damageFlashUntil;
    private bool _visible;
    private bool _coverageVisible;
    private bool _softBlinkReady;
    private float _coverageRadiusPixels;
    private float _activationPulseRemaining;

    internal DryCoreBossPromptView Prompt => _prompt;
    internal Vector2 BossViewportAnchor => new Vector2(0.5f, 0.68f);

    internal Vector2 BossCenterScreenPosition => _bossRoot == null
      ? new Vector2(Screen.width * 0.5f, Screen.height * 0.68f)
      : RectTransformUtility.WorldToScreenPoint(null, _bossRoot.position);

    internal void EnsureCreated()
    {
      if (_canvasObject != null)
      {
        return;
      }

      _safeRoot = FirstLevelUiFactory.CreateCanvas(transform, "Dry Core Boss Canvas", 2400, out _canvas, out _canvasGroup);
      _canvasObject = _safeRoot.parent.gameObject;
      _canvasGroup.alpha = 0f;
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;

      _bossRoot = FirstLevelUiFactory.CreateObject("Dry Core", _safeRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        _bossRoot,
        new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f),
        Vector2.zero, new Vector2(300f, 300f));

      var title = FirstLevelUiFactory.CreateText(
        "Boss Title", _safeRoot, "DRY CORE", 50f, FontStyles.Bold,
        TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.1f, 0.88f), new Vector2(0.9f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 70f));
      _titleGroup = title.gameObject.AddComponent<CanvasGroup>();
      _titleGroup.alpha = 0f;

      _layers = new Image[3];
      _layers[0] = CreateCoreLayer("Outer Shell", 294f, KeepBlinkingTheme.AccentWarm, 0f);
      _layers[1] = CreateCoreLayer("Middle Shell", 216f, KeepBlinkingTheme.TextPrimary, 13f);
      _layers[2] = CreateCoreLayer("Inner Shell", 140f, KeepBlinkingTheme.AccentPrimary, -9f);

      var crackObject = FirstLevelUiFactory.CreateObject("Dry Cracks", _bossRoot);
      FirstLevelUiFactory.Stretch(crackObject.GetComponent<RectTransform>(), new Vector2(18f, 18f), new Vector2(-18f, -18f));
      _cracks = crackObject.AddComponent<DryCoreCrackGraphic>();
      _cracks.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundTertiary, 0.7f);
      _cracks.raycastTarget = false;

      CreateCoreIndicators();
      CreateDecorativeFragments();

      _coverageWave = FirstLevelUiFactory.CreateImage(
        "Coverage Wave", _safeRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.32f), FirstLevelUiFactory.RingSprite).rectTransform;
      FirstLevelUiFactory.SetRect(_coverageWave, new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.68f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _coverageWave.gameObject.SetActive(false);
      _coverageWave.SetAsFirstSibling();

      _prompt = DryCoreBossPromptView.Create(_safeRoot);
      _canvasObject.SetActive(false);
    }

    internal void Show()
    {
      EnsureCreated();
      _visible = true;
      _canvasObject.SetActive(true);
      _canvasGroup.alpha = 0f;
      _titleUntil = Time.unscaledTime + 1.35f;
      SetCoreCount(3);
      _softBlinkReady = false;
      _activationPulseRemaining = 0f;
      _prompt.SetPrompt(DryCoreBossPrompt.None);
    }

    internal void Hide()
    {
      _visible = false;
      if (_canvasObject != null)
      {
        _canvasGroup.alpha = 0f;
        _canvasObject.SetActive(false);
      }
    }

    internal void TickVisuals(float deltaTime, bool allowMotion)
    {
      if (!_visible || _bossRoot == null)
      {
        return;
      }

      _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 1f, Time.unscaledDeltaTime * 2.8f);
      _titleGroup.alpha = Mathf.MoveTowards(_titleGroup.alpha, Time.unscaledTime < _titleUntil ? 1f : 0f, Time.unscaledDeltaTime * 3.5f);
      if (allowMotion)
      {
        _motionTime += Mathf.Max(0f, deltaTime);
      }

      var breathingAmount = _softBlinkReady ? 0.034f : 0.018f;
      var breathing = 1f + Mathf.Sin(_motionTime * 1.15f) * breathingAmount;
      if (_activationPulseRemaining > 0f)
      {
        _activationPulseRemaining = Mathf.Max(0f, _activationPulseRemaining - Mathf.Max(0f, deltaTime));
        var activationProgress = 1f - _activationPulseRemaining / 0.42f;
        breathing *= 1f - Mathf.Sin(Mathf.Clamp01(activationProgress) * Mathf.PI) * 0.075f;
      }
      _bossRoot.localScale = Vector3.one * breathing;
      var fragmentAngle = _motionTime * 7f * Mathf.Deg2Rad;
      for (var i = 0; i < _fragments.Length; i++)
      {
        var angle = fragmentAngle + (i == 0 ? 0.6f : Mathf.PI + 0.4f);
        _fragments[i].rectTransform.anchoredPosition =
          new Vector2(Mathf.Cos(angle) * (112f + i * 12f), Mathf.Sin(angle) * (78f + i * 8f));
      }

      if (Time.unscaledTime < _damageFlashUntil)
      {
        var flash = Mathf.Clamp01((_damageFlashUntil - Time.unscaledTime) / 0.34f);
        for (var i = 0; i < _layers.Length; i++)
        {
          if (_layers[i].gameObject.activeSelf)
          {
            _layers[i].color = Color.Lerp(GetLayerColor(i), KeepBlinkingTheme.TextPrimary, Mathf.Sin(flash * Mathf.PI) * 0.7f);
          }
        }
      }
      else
      {
        for (var i = 0; i < _layers.Length; i++)
        {
          var baseColor = GetLayerColor(i);
          var readyGlow = _softBlinkReady
            ? 0.07f + (Mathf.Sin(_motionTime * 1.15f) * 0.5f + 0.5f) * 0.08f
            : 0f;
          _layers[i].color = Color.Lerp(baseColor, KeepBlinkingTheme.AccentPrimary, readyGlow);
        }
      }

      if (_coverageVisible)
      {
        SetCoverageRadiusPixels(_coverageRadiusPixels);
      }
    }

    internal void SetPrompt(DryCoreBossPrompt prompt)
    {
      _prompt?.SetPrompt(prompt);
    }

    internal void SetSoftBlinkReady(bool ready)
    {
      _softBlinkReady = ready;
    }

    internal void PlaySoftBlinkActivation()
    {
      _softBlinkReady = false;
      _activationPulseRemaining = 0.42f;
    }

    internal void SetFragmentFeedbackCount(int count)
    {
      for (var i = 0; i < _fragments.Length; i++)
      {
        _fragments[i].gameObject.SetActive(i < Mathf.Clamp(count, 0, _fragments.Length));
      }
    }

    internal void BeginCoverage()
    {
      _coverageVisible = true;
      _coverageRadiusPixels = 0f;
      _coverageWave.gameObject.SetActive(true);
      SetCoverageRadiusPixels(0f);
    }

    internal void SetCoverageRadiusPixels(float radiusPixels)
    {
      if (_coverageWave == null || _canvas == null)
      {
        return;
      }

      _coverageRadiusPixels = Mathf.Max(0f, radiusPixels);
      var referenceRadius = _coverageRadiusPixels / Mathf.Max(0.01f, _canvas.scaleFactor);
      _coverageWave.sizeDelta = Vector2.one * referenceRadius * 2f;
      var alpha = Mathf.Lerp(0.12f, 0.36f, Mathf.Clamp01(referenceRadius / 360f));
      _coverageWave.GetComponent<Image>().color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, alpha);
    }

    internal void EndCoverage(bool successful)
    {
      _coverageVisible = false;
      if (_coverageWave != null)
      {
        if (!successful)
        {
          _coverageWave.sizeDelta = Vector2.zero;
        }
        _coverageWave.gameObject.SetActive(false);
      }
    }

    internal CoverageTargetGeometry[] CaptureCoverageTargets()
    {
      var targets = new List<CoverageTargetGeometry>(4)
      {
        new CoverageTargetGeometry(BossCenterScreenPosition, GetScreenRadius(_bossRoot, 0.5f)),
      };
      for (var i = 0; i < _fragments.Length; i++)
      {
        if (_fragments[i].gameObject.activeSelf)
        {
          targets.Add(new CoverageTargetGeometry(
            RectTransformUtility.WorldToScreenPoint(null, _fragments[i].rectTransform.position),
            GetScreenRadius(_fragments[i].rectTransform, 0.55f)));
        }
      }

      return targets.ToArray();
    }

    internal void ApplyCoreDamage(int remainingCores, int damage)
    {
      _damageFlashUntil = Time.unscaledTime + 0.34f;
      _cracks.DamageLevel = Mathf.Clamp(3 - remainingCores, 0, 3);
      SetCoreCount(remainingCores);
    }

    private Image CreateCoreLayer(string name, float size, Color color, float rotation)
    {
      var image = FirstLevelUiFactory.CreateImage(name, _bossRoot, color, CreatePolygonSprite(name, 12));
      FirstLevelUiFactory.SetRect(image.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * size);
      image.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
      return image;
    }

    private void CreateCoreIndicators()
    {
      _coreRings = new Image[3];
      for (var i = 0; i < _coreRings.Length; i++)
      {
        var ring = FirstLevelUiFactory.CreateImage("Core Ring " + (i + 1), _safeRoot, KeepBlinkingTheme.AccentWarm, FirstLevelUiFactory.RingSprite);
        FirstLevelUiFactory.SetRect(ring.rectTransform, new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.82f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 48f, 0f), new Vector2(32f, 32f));
        _coreRings[i] = ring;
      }
    }

    private void CreateDecorativeFragments()
    {
      _fragments = new Image[2];
      for (var i = 0; i < _fragments.Length; i++)
      {
        var fragment = FirstLevelUiFactory.CreateImage("Core Fragment " + (i + 1), _bossRoot, KeepBlinkingTheme.AccentWarm, CreatePolygonSprite("Fragment", 7));
        FirstLevelUiFactory.SetRect(fragment.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
        fragment.gameObject.SetActive(false);
        _fragments[i] = fragment;
      }
    }

    private void SetCoreCount(int remainingCores)
    {
      remainingCores = Mathf.Clamp(remainingCores, 0, 3);
      for (var i = 0; i < _layers.Length; i++)
      {
        _layers[i].gameObject.SetActive(i >= 3 - remainingCores);
      }
      for (var i = 0; i < _coreRings.Length; i++)
      {
        _coreRings[i].color = i < remainingCores
          ? KeepBlinkingTheme.AccentWarm
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.22f);
      }
    }

    private Color GetLayerColor(int index)
    {
      switch (index)
      {
        case 0: return KeepBlinkingTheme.AccentWarm;
        case 1: return KeepBlinkingTheme.TextPrimary;
        default: return KeepBlinkingTheme.AccentPrimary;
      }
    }

    private float GetScreenRadius(RectTransform rect, float multiplier)
    {
      return rect == null || _canvas == null ? 0f : rect.rect.width * rect.lossyScale.x * _canvas.scaleFactor * multiplier;
    }

    private static Sprite CreatePolygonSprite(string name, int sides)
    {
      const int size = 128;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
      {
        name = name + " Polygon",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var pixels = new Color32[size * size];
      var center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
      var radius = size * 0.47f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var delta = new Vector2(x, y) - center;
          var angle = Mathf.Atan2(delta.y, delta.x);
          var sector = Mathf.PI * 2f / sides;
          var local = Mathf.Repeat(angle + sector * 0.5f, sector) - sector * 0.5f;
          var boundary = radius * Mathf.Cos(sector * 0.5f) / Mathf.Max(0.001f, Mathf.Cos(local));
          var alpha = Mathf.Clamp01(boundary + 0.75f - delta.magnitude);
          var facet = 0.88f + 0.12f * Mathf.Cos(angle * 3f + delta.magnitude * 0.03f);
          pixels[y * size + x] = new Color(1f * facet, 1f * facet, 1f * facet, alpha);
        }
      }
      texture.SetPixels32(pixels);
      texture.Apply(false, true);
      var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
      sprite.hideFlags = HideFlags.HideAndDontSave;
      return sprite;
    }
  }

  internal sealed class DryCoreCrackGraphic : MaskableGraphic
  {
    private int _damageLevel;

    internal int DamageLevel
    {
      get => _damageLevel;
      set
      {
        _damageLevel = value;
        SetVerticesDirty();
      }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      for (var i = 0; i < _damageLevel; i++)
      {
        var angle = (35f + i * 117f) * Mathf.Deg2Rad;
        var start = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 18f;
        var mid = start + new Vector2(Mathf.Cos(angle + 0.36f), Mathf.Sin(angle + 0.36f)) * 42f;
        var end = mid + new Vector2(Mathf.Cos(angle - 0.22f), Mathf.Sin(angle - 0.22f)) * 44f;
        AddLine(vh, start, mid, 2.2f);
        AddLine(vh, mid, end, 1.8f);
        AddLine(vh, mid, mid + new Vector2(Mathf.Cos(angle + 1f), Mathf.Sin(angle + 1f)) * 22f, 1.4f);
      }
    }

    private void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width)
    {
      var direction = (to - from).normalized;
      var normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
      var index = vh.currentVertCount;
      vh.AddVert(from - normal, color, Vector2.zero);
      vh.AddVert(from + normal, color, Vector2.up);
      vh.AddVert(to + normal, color, Vector2.one);
      vh.AddVert(to - normal, color, Vector2.right);
      vh.AddTriangle(index, index + 1, index + 2);
      vh.AddTriangle(index, index + 2, index + 3);
    }
  }
}

using System;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  [DisallowMultipleComponent]
  public sealed class CareCrewArtView : MonoBehaviour
  {
    [Header("Identity")]
    [SerializeField] private CareCrewRole role;

    [Header("Character")]
    [SerializeField] private Image characterRenderer;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Sprite walkSprite;
    [SerializeField] private Sprite workSprite;
    [SerializeField] private RectTransform groundAnchor;
    [SerializeField] private RectTransform handAnchor;
    [SerializeField] private RectTransform feedbackRoot;
    [SerializeField] private Image feedbackRipple;

    [Header("Cart")]
    [SerializeField] private RectTransform cartRoot;
    [SerializeField] private Image cartRenderer;
    [SerializeField] private Sprite smallBasketSprite;
    [SerializeField] private Sprite deepCartSprite;
    [SerializeField] private Sprite careTowerSprite;
    [SerializeField] private RectTransform lowerSampleAnchors;
    [SerializeField] private RectTransform upperSampleAnchors;
    [SerializeField] private Image[] lowerBottleRenderers = Array.Empty<Image>();
    [SerializeField] private Image[] upperBottleRenderers = Array.Empty<Image>();

    [Header("Bottles")]
    [SerializeField] private Sprite emptyBottleSprite;
    [SerializeField] private Sprite mintBottleSprite;
    [SerializeField] private Sprite goldBottleSprite;

    [Header("Animation")]
    [SerializeField, Min(0.1f)] private float idleCycleSeconds = 1.8f;
    [SerializeField, Min(0.1f)] private float walkCycleSeconds = 0.45f;
    [SerializeField, Min(0.1f)] private float workCycleSeconds = 0.8f;
    [SerializeField, Min(0.1f)] private float restCycleSeconds = 2.4f;
    [SerializeField, Min(0.1f)] private float cheerSeconds = 0.6f;

    private CareCrewAnimationState _state = CareCrewAnimationState.Idle;
    private CareCartTier _cartTier = CareCartTier.SmallBasket;
    private CareCartTier _pendingCartTier = CareCartTier.SmallBasket;
    private CareCartLoadPreview _load = CareCartLoadPreview.Empty;
    private Vector2 _characterBasePosition;
    private Vector2 _cartBasePosition;
    private float _stateStartedAt;
    private float _cartTransitionStartedAt = -10f;
    private bool _cartSpriteSwapped;

    public CareCrewRole Role => role;
    public CareCrewAnimationState State => _state;
    public CareCartTier CartTier => _cartTier;
    public CareCartLoadPreview LoadPreview => _load;
    public Sprite EmptyBottleSprite => emptyBottleSprite;
    public Sprite MintBottleSprite => mintBottleSprite;
    public Sprite GoldBottleSprite => goldBottleSprite;
    public RectTransform GroundAnchor => groundAnchor;
    public RectTransform HandAnchor => handAnchor;
    public RectTransform CartRoot => cartRoot;
    public RectTransform LowerSampleAnchors => lowerSampleAnchors;
    public RectTransform UpperSampleAnchors => upperSampleAnchors;

    private void Awake()
    {
      CaptureBaseTransforms();
      ApplyStateSprite();
      ApplyCartSprite(_cartTier);
      ApplyLoad();
    }

    private void OnEnable()
    {
      CaptureBaseTransforms();
      _stateStartedAt = Time.unscaledTime;
    }

    private void CaptureBaseTransforms()
    {
      if (characterRenderer != null) _characterBasePosition = characterRenderer.rectTransform.anchoredPosition;
      if (cartRoot != null) _cartBasePosition = cartRoot.anchoredPosition;
    }

    public void SetState(CareCrewAnimationState state, bool restart = false)
    {
      if (!restart && _state == state) return;
      _state = state;
      _stateStartedAt = Time.unscaledTime;
      ApplyStateSprite();
      if (feedbackRipple != null && state != CareCrewAnimationState.Cheer)
        feedbackRipple.color = Color.clear;
    }

    public void SetCartTier(CareCartTier tier, bool animate = true)
    {
      if (role != CareCrewRole.CareCourier || cartRoot == null) return;
      if (_cartTier == tier && _pendingCartTier == tier) return;
      _pendingCartTier = tier;
      if (!animate)
      {
        _cartTier = tier;
        ApplyCartSprite(tier);
        ApplyLoad();
        return;
      }
      _cartTransitionStartedAt = Time.unscaledTime;
      _cartSpriteSwapped = false;
    }

    public void SetLoad(CareCartLoadPreview load)
    {
      _load = load;
      ApplyLoad();
    }

    public void SetPendingRatio(float normalizedPending)
    {
      SetLoad(CareStationArtLoadLogic.FromPendingRatio(normalizedPending));
    }

    private void Update()
    {
      if (characterRenderer == null) return;
      var elapsed = Mathf.Max(0f, Time.unscaledTime - _stateStartedAt);
      var cycle = CycleForState(_state);
      var normalized = cycle > 0f ? elapsed / cycle : 0f;
      var wave = Mathf.Sin(normalized * Mathf.PI * 2f);
      var characterRect = characterRenderer.rectTransform;
      var spriteGroundOffset = GroundOffsetForState(_state);
      var y = 0f;
      var angle = 0f;
      var scale = 1f;

      switch (_state)
      {
        case CareCrewAnimationState.Idle:
          y = wave * 1.8f;
          scale = 1f + wave * 0.004f;
          break;
        case CareCrewAnimationState.Walk:
          y = Mathf.Abs(wave) * 4f;
          angle = wave * 2f;
          break;
        case CareCrewAnimationState.Work:
          y = Mathf.Abs(wave) * 2f;
          angle = Mathf.Lerp(-1.5f, 2.5f, 0.5f + wave * 0.5f);
          break;
        case CareCrewAnimationState.Rest:
          y = wave * 1.2f;
          scale = 1f + wave * 0.006f;
          break;
        case CareCrewAnimationState.Cheer:
          var cheerT = Mathf.Clamp01(elapsed / Mathf.Max(0.1f, cheerSeconds));
          y = Mathf.Sin(cheerT * Mathf.PI) * 18f;
          if (feedbackRipple != null)
          {
            var rippleT = Mathf.Clamp01((cheerT - 0.62f) / 0.38f);
            feedbackRipple.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.45f, rippleT);
            feedbackRipple.color = new Color(0.48f, 0.88f, 0.76f, Mathf.Lerp(0.32f, 0f, rippleT));
          }
          break;
      }

      characterRect.anchoredPosition = _characterBasePosition + spriteGroundOffset + new Vector2(0f, y);
      characterRect.localRotation = Quaternion.Euler(0f, 0f, angle);
      characterRect.localScale = Vector3.one * scale;

      if (cartRoot != null && cartRoot.gameObject.activeSelf)
      {
        var suspension = Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f / 1.8f) * 1.2f;
        cartRoot.anchoredPosition = _cartBasePosition + new Vector2(0f, suspension);
        UpdateCartTransition();
      }
    }

    private void UpdateCartTransition()
    {
      if (cartRenderer == null || _cartTier == _pendingCartTier) return;
      var t = Mathf.Clamp01((Time.unscaledTime - _cartTransitionStartedAt) / 0.5f);
      if (!_cartSpriteSwapped && t >= 0.45f)
      {
        _cartSpriteSwapped = true;
        _cartTier = _pendingCartTier;
        ApplyCartSprite(_cartTier);
        ApplyLoad();
        if (feedbackRipple != null)
        {
          feedbackRipple.rectTransform.localScale = Vector3.one * 0.6f;
          feedbackRipple.color = new Color(0.48f, 0.88f, 0.76f, 0.28f);
        }
      }

      var fade = t < 0.45f ? 1f - t / 0.45f : (t - 0.45f) / 0.55f;
      cartRenderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(fade));
      var towerEmphasis = _pendingCartTier == CareCartTier.CareTower && t >= 0.45f ? Mathf.Sin((t - 0.45f) / 0.55f * Mathf.PI) * 0.04f : 0f;
      cartRenderer.rectTransform.localScale = new Vector3(Mathf.Lerp(0.95f, 1f, fade), Mathf.Lerp(0.95f, 1f + towerEmphasis, fade), 1f);
      if (t >= 1f)
      {
        cartRenderer.color = Color.white;
        cartRenderer.rectTransform.localScale = Vector3.one;
      }
    }

    private float CycleForState(CareCrewAnimationState state)
    {
      switch (state)
      {
        case CareCrewAnimationState.Walk: return walkCycleSeconds;
        case CareCrewAnimationState.Work: return workCycleSeconds;
        case CareCrewAnimationState.Rest: return restCycleSeconds;
        case CareCrewAnimationState.Cheer: return cheerSeconds;
        default: return idleCycleSeconds;
      }
    }

    private Vector2 GroundOffsetForState(CareCrewAnimationState state)
    {
      if (state == CareCrewAnimationState.Walk)
      {
        switch (role)
        {
          case CareCrewRole.DustKeeper: return new Vector2(0f, -59f);
          case CareCrewRole.DrySpotMender: return new Vector2(0f, -53f);
          case CareCrewRole.CareCourier: return new Vector2(0f, -55f);
          default: return new Vector2(0f, -57f);
        }
      }
      if (state == CareCrewAnimationState.Work)
      {
        switch (role)
        {
          case CareCrewRole.DustKeeper: return new Vector2(0f, -109f);
          case CareCrewRole.DrySpotMender: return new Vector2(0f, -99f);
          case CareCrewRole.CareCourier: return new Vector2(0f, -119f);
          default: return new Vector2(0f, -114f);
        }
      }
      return Vector2.zero;
    }

    private void ApplyStateSprite()
    {
      if (characterRenderer == null) return;
      characterRenderer.sprite = _state == CareCrewAnimationState.Walk ? walkSprite : _state == CareCrewAnimationState.Work ? workSprite : idleSprite;
      characterRenderer.preserveAspect = true;
      characterRenderer.color = Color.white;
    }

    private void ApplyCartSprite(CareCartTier tier)
    {
      if (cartRoot == null) return;
      cartRoot.gameObject.SetActive(role == CareCrewRole.CareCourier);
      if (cartRenderer == null) return;
      cartRenderer.sprite = tier == CareCartTier.CareTower ? careTowerSprite : tier == CareCartTier.DeepCart ? deepCartSprite : smallBasketSprite;
      cartRenderer.preserveAspect = true;
      cartRenderer.color = Color.white;
      var size = tier == CareCartTier.CareTower ? new Vector2(240f, 256f) : tier == CareCartTier.DeepCart ? new Vector2(220f, 170f) : new Vector2(180f, 124f);
      cartRenderer.rectTransform.pivot = new Vector2(1f, 0.45f);
      cartRenderer.rectTransform.anchoredPosition = Vector2.zero;
      cartRenderer.rectTransform.sizeDelta = size;
      ConfigureAnchorPositions(tier);
    }

    private void ConfigureAnchorPositions(CareCartTier tier)
    {
      if (lowerSampleAnchors != null) lowerSampleAnchors.gameObject.SetActive(role == CareCrewRole.CareCourier);
      if (upperSampleAnchors != null) upperSampleAnchors.gameObject.SetActive(role == CareCrewRole.CareCourier && tier == CareCartTier.CareTower);
      var lowerCount = tier == CareCartTier.SmallBasket ? 2 : 4;
      for (var i = 0; i < lowerBottleRenderers.Length; i++)
      {
        var image = lowerBottleRenderers[i];
        if (image == null) continue;
        var x = lowerCount == 2 ? -122f + i * 48f : -166f + i * 39f;
        var y = tier == CareCartTier.CareTower ? 32f : tier == CareCartTier.DeepCart ? 43f : 28f;
        image.rectTransform.anchoredPosition = new Vector2(x, y);
      }
      for (var i = 0; i < upperBottleRenderers.Length; i++)
      {
        var image = upperBottleRenderers[i];
        if (image == null) continue;
        image.rectTransform.anchoredPosition = new Vector2(-166f + i * 39f, 142f);
      }
    }

    private void ApplyLoad()
    {
      var capacity = CareStationArtLoadLogic.Capacity(_cartTier);
      var visible = CareStationArtLoadLogic.VisibleBottleCount(_cartTier, _load);
      for (var i = 0; i < 8; i++)
      {
        var image = i < 4
          ? (i < lowerBottleRenderers.Length ? lowerBottleRenderers[i] : null)
          : (i - 4 < upperBottleRenderers.Length ? upperBottleRenderers[i - 4] : null);
        if (image == null) continue;
        var slotAvailable = i < capacity;
        image.gameObject.SetActive(slotAvailable && i < visible);
        if (!image.gameObject.activeSelf) continue;
        image.sprite = CareStationArtLoadLogic.IsGold(_load, i) ? goldBottleSprite : mintBottleSprite;
        image.preserveAspect = true;
      }
    }

#if UNITY_EDITOR
    public void EditorConfigure(
      CareCrewRole configuredRole,
      Image configuredCharacterRenderer,
      Sprite configuredIdle,
      Sprite configuredWalk,
      Sprite configuredWork,
      RectTransform configuredGroundAnchor,
      RectTransform configuredHandAnchor,
      RectTransform configuredFeedbackRoot,
      Image configuredFeedbackRipple,
      RectTransform configuredCartRoot,
      Image configuredCartRenderer,
      Sprite configuredSmallCart,
      Sprite configuredDeepCart,
      Sprite configuredTower,
      RectTransform configuredLowerAnchors,
      RectTransform configuredUpperAnchors,
      Image[] configuredLowerBottles,
      Image[] configuredUpperBottles,
      Sprite configuredEmptyBottle,
      Sprite configuredMintBottle,
      Sprite configuredGoldBottle)
    {
      role = configuredRole;
      characterRenderer = configuredCharacterRenderer;
      idleSprite = configuredIdle;
      walkSprite = configuredWalk;
      workSprite = configuredWork;
      groundAnchor = configuredGroundAnchor;
      handAnchor = configuredHandAnchor;
      feedbackRoot = configuredFeedbackRoot;
      feedbackRipple = configuredFeedbackRipple;
      cartRoot = configuredCartRoot;
      cartRenderer = configuredCartRenderer;
      smallBasketSprite = configuredSmallCart;
      deepCartSprite = configuredDeepCart;
      careTowerSprite = configuredTower;
      lowerSampleAnchors = configuredLowerAnchors;
      upperSampleAnchors = configuredUpperAnchors;
      lowerBottleRenderers = configuredLowerBottles ?? Array.Empty<Image>();
      upperBottleRenderers = configuredUpperBottles ?? Array.Empty<Image>();
      emptyBottleSprite = configuredEmptyBottle;
      mintBottleSprite = configuredMintBottle;
      goldBottleSprite = configuredGoldBottle;
      _cartTier = CareCartTier.SmallBasket;
      _pendingCartTier = _cartTier;
      ApplyStateSprite();
      ApplyCartSprite(_cartTier);
      ApplyLoad();
    }
#endif
  }
}

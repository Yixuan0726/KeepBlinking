using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  internal class FirstLevelUpgradeView : MonoBehaviour
  {
    private const float ReferenceWidth = 1080f;
    private const float ReferenceHeight = 1920f;

    private readonly List<CardVisual> _cards = new List<CardVisual>(3);
    private readonly List<HudSlotVisual> _hudSlots = new List<HudSlotVisual>(4);
    private readonly Dictionary<FirstLevelModuleId, float> _moduleFlashUntil = new Dictionary<FirstLevelModuleId, float>();

    private GameObject _canvasObject;
    private Canvas _canvas;
    private RectTransform _safeAreaRoot;
    private GameObject _scrimObject;
    private GameObject _overlayContentObject;
    private RectTransform _overlayContentRoot;
    private TextMeshProUGUI _headerText;
    private TextMeshProUGUI _instructionText;
    private RectTransform _hudRoot;
    private TMP_FontAsset _fontAsset;
    private Texture2D _roundedFillTexture;
    private Texture2D _roundedBorderTexture;
    private Sprite _roundedFillSprite;
    private Sprite _roundedBorderSprite;
    private Rect _lastSafeArea;
    private Vector2Int _lastScreenSize;
    private int _focusedCardIndex = -1;
    private int _visualFocusedCardIndex = -1;
    private float _visualFocusHoldUntil = -1f;
    private int _selectedCardIndex = -1;
    private float _focusProgress;
    private bool _installing;
    private float _installProgress;
    private UpgradeModuleIconVisual _travellingInstallIcon;
    private Vector2 _installIconStart;
    private Vector2 _installIconTarget;
    private bool _layoutValidationPending;

    public bool IsVisible => _overlayContentObject != null && _overlayContentObject.activeSelf;

    public void EnsureCreated()
    {
      if (_canvasObject != null)
      {
        return;
      }

      CreateRuntimeResources();
      CreateCanvas();
      UpdateSafeArea(true);
    }

    public void Show(IReadOnlyList<FirstLevelModuleId> offer)
    {
      EnsureCreated();
      ClearCards();
      _focusedCardIndex = -1;
      _visualFocusedCardIndex = -1;
      _visualFocusHoldUntil = -1f;
      _selectedCardIndex = -1;
      _focusProgress = 0f;
      _installing = false;
      _installProgress = 0f;
      DestroyTravellingInstallIcon();

      _scrimObject.SetActive(true);
      _overlayContentObject.SetActive(true);
      for (var i = 0; i < offer.Count; i++)
      {
        _cards.Add(CreateCard(FirstLevelUpgradeCatalog.Get(offer[i]), i));
      }

      Canvas.ForceUpdateCanvases();
      UpdateSafeArea(true);
      UpdateInstruction();
      LayoutView();
      _layoutValidationPending = true;
    }

    public void Hide()
    {
      if (_canvasObject == null)
      {
        return;
      }

      _scrimObject.SetActive(false);
      _overlayContentObject.SetActive(false);
      _focusedCardIndex = -1;
      _visualFocusedCardIndex = -1;
      _selectedCardIndex = -1;
      _focusProgress = 0f;
      _installing = false;
      _installProgress = 0f;
      DestroyTravellingInstallIcon();
      ClearCards();
    }

    public bool TryGetCardAtScreenPosition(Vector2 screenPosition, out int cardIndex)
    {
      cardIndex = -1;
      if (!IsVisible)
      {
        return false;
      }

      for (var i = 0; i < _cards.Count; i++)
      {
        if (RectTransformUtility.RectangleContainsScreenPoint(_cards[i].Root, screenPosition, null))
        {
          cardIndex = i;
          return true;
        }
      }

      return false;
    }

    public void SetInteractionState(
      int focusedCardIndex,
      int selectedCardIndex,
      float focusProgress,
      bool installing,
      float installProgress)
    {
      _focusedCardIndex = focusedCardIndex;
      if (focusedCardIndex >= 0)
      {
        _visualFocusedCardIndex = focusedCardIndex;
        _visualFocusHoldUntil = Time.unscaledTime + 0.16f;
      }
      else if (Time.unscaledTime >= _visualFocusHoldUntil)
      {
        _visualFocusedCardIndex = -1;
      }
      _selectedCardIndex = selectedCardIndex;
      _focusProgress = Mathf.Clamp01(focusProgress);
      _installing = installing;
      _installProgress = Mathf.Clamp01(installProgress);
      UpdateInstruction();
    }

    public void BeginInstallAnimation(int selectedCardIndex, FirstLevelModuleId moduleId, int installedCount)
    {
      if (!IsVisible || selectedCardIndex < 0 || selectedCardIndex >= _cards.Count)
      {
        return;
      }

      DestroyTravellingInstallIcon();
      var sourceCard = _cards[selectedCardIndex];
      var iconObject = CreateUiObject("Installing Module Icon", _safeAreaRoot);
      var iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.sizeDelta = new Vector2(128f, 128f);
      _travellingInstallIcon = iconObject.AddComponent<UpgradeModuleIconVisual>();
      var definition = FirstLevelUpgradeCatalog.Get(moduleId);
      _travellingInstallIcon.Configure(definition.Category, definition.AccentColor, Mathf.Max(1, definition.Tier));
      _travellingInstallIcon.raycastTarget = false;
      _installIconStart = _safeAreaRoot.InverseTransformPoint(sourceCard.Icon.rectTransform.position);
      _installIconTarget = GetHudSlotLocalPosition(installedCount, installedCount + 1);
      iconRect.anchoredPosition = _installIconStart;
      iconRect.SetAsLastSibling();
    }

    public void SetInstalledModules(IReadOnlyList<FirstLevelModuleId> installedModules)
    {
      EnsureCreated();
      ClearHudSlots();
      for (var i = 0; i < installedModules.Count; i++)
      {
        _hudSlots.Add(CreateHudSlot(installedModules[i], i, installedModules.Count));
      }
    }

    public void FlashModule(FirstLevelModuleId moduleId)
    {
      _moduleFlashUntil[moduleId] = Time.unscaledTime + 0.8f;
    }

    public void SetHudVisible(bool visible)
    {
      EnsureCreated();
      if (_hudRoot != null)
      {
        _hudRoot.gameObject.SetActive(visible);
      }
    }

    private void Update()
    {
      if (_canvasObject == null)
      {
        return;
      }

      UpdateSafeArea(false);
      UpdateCardAnimations();
      UpdateHudAnimations();
      UpdateInstallTravel();
    }

    private void LateUpdate()
    {
      if (!_layoutValidationPending || !IsVisible)
      {
        return;
      }

      _layoutValidationPending = false;
      Canvas.ForceUpdateCanvases();
      ValidateTextOverflow();
    }

    private void CreateRuntimeResources()
    {
      _fontAsset = TMP_Settings.defaultFontAsset;
      if (_fontAsset == null)
      {
        _fontAsset = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
      }
      if (_fontAsset == null)
      {
        throw new System.InvalidOperationException("The TextMeshPro default font asset is unavailable.");
      }

      _roundedFillTexture = CreateRoundedTexture(false);
      _roundedBorderTexture = CreateRoundedTexture(true);
      _roundedFillSprite = Sprite.Create(
        _roundedFillTexture,
        new Rect(0f, 0f, _roundedFillTexture.width, _roundedFillTexture.height),
        new Vector2(0.5f, 0.5f),
        100f,
        0,
        SpriteMeshType.FullRect,
        new Vector4(18f, 18f, 18f, 18f));
      _roundedBorderSprite = Sprite.Create(
        _roundedBorderTexture,
        new Rect(0f, 0f, _roundedBorderTexture.width, _roundedBorderTexture.height),
        new Vector2(0.5f, 0.5f),
        100f,
        0,
        SpriteMeshType.FullRect,
        new Vector4(18f, 18f, 18f, 18f));
      _roundedFillSprite.hideFlags = HideFlags.HideAndDontSave;
      _roundedBorderSprite.hideFlags = HideFlags.HideAndDontSave;
    }

    private void CreateCanvas()
    {
      _canvasObject = new GameObject("First Level Upgrade Canvas");
      _canvasObject.transform.SetParent(transform, false);
      _canvas = _canvasObject.AddComponent<Canvas>();
      _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      _canvas.sortingOrder = 2200;
      var scaler = _canvasObject.AddComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
      scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
      scaler.matchWidthOrHeight = 0.5f;
      _canvasObject.AddComponent<GraphicRaycaster>();

      var scrim = CreateUiObject("Upgrade Scrim", _canvasObject.transform);
      var scrimRect = scrim.GetComponent<RectTransform>();
      Stretch(scrimRect);
      var scrimImage = scrim.AddComponent<Image>();
      scrimImage.color = new Color(9f / 255f, 19f / 255f, 18f / 255f, 0.86f);
      scrimImage.raycastTarget = false;
      _scrimObject = scrim;

      var safeArea = CreateUiObject("Safe Area", _canvasObject.transform);
      _safeAreaRoot = safeArea.GetComponent<RectTransform>();

      _overlayContentObject = CreateUiObject("Upgrade Content", _safeAreaRoot);
      _overlayContentRoot = _overlayContentObject.GetComponent<RectTransform>();
      Stretch(_overlayContentRoot);

      _headerText = CreateText("Header", _overlayContentRoot, "CHOOSE ONE", 54f, 42f, 58f, FontStyles.Bold, TextAlignmentOptions.Center, false);
      _instructionText = CreateText("Instruction", _overlayContentRoot, "TAP TO INSTALL", 25f, 22f, 28f, FontStyles.Bold, TextAlignmentOptions.Center, false);

      var hud = CreateUiObject("Module HUD Slots", _safeAreaRoot);
      _hudRoot = hud.GetComponent<RectTransform>();
      _hudRoot.anchorMin = new Vector2(0.5f, 0f);
      _hudRoot.anchorMax = new Vector2(0.5f, 0f);
      _hudRoot.pivot = new Vector2(0.5f, 0f);
      _hudRoot.sizeDelta = new Vector2(420f, 78f);
      _hudRoot.anchoredPosition = new Vector2(0f, 42f);

      _scrimObject.SetActive(false);
      _overlayContentObject.SetActive(false);
    }

    private CardVisual CreateCard(CareUpgradeDefinition definition, int index)
    {
      var rootObject = CreateUiObject($"Card {index + 1} {definition.Title}", _overlayContentRoot);
      var root = rootObject.GetComponent<RectTransform>();
      root.anchorMin = new Vector2(0.07f, 0.5f);
      root.anchorMax = new Vector2(0.93f, 0.5f);
      root.pivot = new Vector2(0.5f, 0.5f);
      var canvasGroup = rootObject.AddComponent<CanvasGroup>();

      var shadowObject = CreateUiObject("Shadow", root);
      var shadowRect = shadowObject.GetComponent<RectTransform>();
      Stretch(shadowRect);
      shadowRect.offsetMin = new Vector2(0f, -5f);
      shadowRect.offsetMax = new Vector2(0f, -5f);
      var shadow = shadowObject.AddComponent<Image>();
      shadow.sprite = _roundedFillSprite;
      shadow.type = Image.Type.Sliced;
      shadow.color = new Color(0f, 0f, 0f, 0.18f);
      shadow.raycastTarget = false;

      var surfaceObject = CreateUiObject("Surface", root);
      var surfaceRect = surfaceObject.GetComponent<RectTransform>();
      Stretch(surfaceRect);
      var surface = surfaceObject.AddComponent<Image>();
      surface.sprite = _roundedFillSprite;
      surface.type = Image.Type.Sliced;
      surface.color = new Color(30f / 255f, 43f / 255f, 40f / 255f, 0.96f);
      surface.raycastTarget = false;

      var borderObject = CreateUiObject("Thin Border", root);
      var borderRect = borderObject.GetComponent<RectTransform>();
      Stretch(borderRect);
      var border = borderObject.AddComponent<Image>();
      border.sprite = _roundedBorderSprite;
      border.type = Image.Type.Sliced;
      border.color = new Color(126f / 255f, 145f / 255f, 135f / 255f, 0.5f);
      border.raycastTarget = false;

      var progressObject = CreateUiObject("Selection Progress", root);
      var progressRect = progressObject.GetComponent<RectTransform>();
      Stretch(progressRect);
      var progress = progressObject.AddComponent<UpgradeFocusProgressGraphic>();
      progress.color = KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.9f);
      progress.raycastTarget = false;

      var accentObject = CreateUiObject("Accent Strip", root);
      var accentRect = accentObject.GetComponent<RectTransform>();
      accentRect.anchorMin = new Vector2(0f, 0.12f);
      accentRect.anchorMax = new Vector2(0f, 0.88f);
      accentRect.pivot = new Vector2(0f, 0.5f);
      accentRect.sizeDelta = new Vector2(8f, 0f);
      accentRect.anchoredPosition = new Vector2(18f, 0f);
      var accent = accentObject.AddComponent<Image>();
      accent.sprite = _roundedFillSprite;
      accent.type = Image.Type.Sliced;
      accent.color = KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.9f);
      accent.raycastTarget = false;

      var title = CreateText("Title", root, definition.Title, 46f, 34f, 47f, FontStyles.Bold, TextAlignmentOptions.Left, false);
      SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -32f), new Vector2(600f, 54f));
      title.color = KeepBlinkingTheme.TextPrimary;

      var delta = CreateText("Value", root, definition.Delta, 32f, 27f, 34f, FontStyles.Bold, TextAlignmentOptions.Left, false);
      SetRect(delta.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -88f), new Vector2(580f, 42f));
      delta.color = KeepBlinkingTheme.TextPrimary;

      var description = CreateText("Effect", root, definition.Description, 28f, 23f, 29f, FontStyles.Normal, TextAlignmentOptions.TopLeft, true);
      SetRect(description.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -142f), new Vector2(600f, 54f));
      description.color = KeepBlinkingTheme.TextSecondary;
      description.maxVisibleLines = 2;

      var category = CreateText("Category", root, definition.CategoryLabel, 24f, 20f, 25f, FontStyles.Bold, TextAlignmentOptions.Left, false);
      SetRect(category.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(62f, 20f), new Vector2(520f, 34f));
      category.characterSpacing = 2f;
      category.color = KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.96f);

      var iconObject = CreateUiObject("Module Icon", root);
      var iconRect = iconObject.GetComponent<RectTransform>();
      iconRect.anchorMin = new Vector2(1f, 0.5f);
      iconRect.anchorMax = new Vector2(1f, 0.5f);
      iconRect.pivot = new Vector2(0.5f, 0.5f);
      iconRect.sizeDelta = new Vector2(220f, 190f);
      iconRect.anchoredPosition = new Vector2(-132f, 0f);
      var icon = iconObject.AddComponent<CareUpgradePreviewGraphic>();
      icon.Configure(definition);
      icon.raycastTarget = false;

      return new CardVisual(root, canvasGroup, surface, border, progress, icon, category, title, description, delta);
    }

    private HudSlotVisual CreateHudSlot(FirstLevelModuleId moduleId, int index, int totalCount)
    {
      var definition = FirstLevelUpgradeCatalog.Get(moduleId);
      var slotObject = CreateUiObject($"HUD Module {definition.Title}", _hudRoot);
      var slotRect = slotObject.GetComponent<RectTransform>();
      slotRect.anchorMin = new Vector2(0.5f, 0.5f);
      slotRect.anchorMax = new Vector2(0.5f, 0.5f);
      slotRect.pivot = new Vector2(0.5f, 0.5f);
      slotRect.sizeDelta = new Vector2(62f, 62f);
      slotRect.anchoredPosition = GetHudSlotLocalPosition(index, totalCount) - _hudRoot.anchoredPosition;

      var surface = slotObject.AddComponent<Image>();
      surface.sprite = _roundedFillSprite;
      surface.type = Image.Type.Sliced;
      surface.color = new Color(27f / 255f, 40f / 255f, 37f / 255f, 0.9f);
      surface.raycastTarget = false;

      var borderObject = CreateUiObject("Border", slotRect);
      var borderRect = borderObject.GetComponent<RectTransform>();
      Stretch(borderRect);
      var border = borderObject.AddComponent<Image>();
      border.sprite = _roundedBorderSprite;
      border.type = Image.Type.Sliced;
      border.color = KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.48f);
      border.raycastTarget = false;

      var iconObject = CreateUiObject("Icon", slotRect);
      var iconRect = iconObject.GetComponent<RectTransform>();
      Stretch(iconRect);
      iconRect.offsetMin = new Vector2(8f, 8f);
      iconRect.offsetMax = new Vector2(-8f, -8f);
      var icon = iconObject.AddComponent<UpgradeModuleIconVisual>();
      icon.Configure(definition.Category, definition.AccentColor, CountCategoryLevel(moduleId));
      icon.raycastTarget = false;

      return new HudSlotVisual(moduleId, slotRect, border, icon);
    }

    private void UpdateSafeArea(bool force)
    {
      var safeArea = Screen.safeArea;
      var screenSize = new Vector2Int(Screen.width, Screen.height);
      if (safeArea.width <= 0f ||
          safeArea.height <= 0f ||
          safeArea.xMin < 0f ||
          safeArea.yMin < 0f ||
          safeArea.xMax > screenSize.x + 1f ||
          safeArea.yMax > screenSize.y + 1f)
      {
        safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
      }
      if (!force && safeArea == _lastSafeArea && screenSize == _lastScreenSize)
      {
        return;
      }

      _lastSafeArea = safeArea;
      _lastScreenSize = screenSize;
      var min = safeArea.position;
      var max = safeArea.position + safeArea.size;
      min.x /= Mathf.Max(1f, Screen.width);
      min.y /= Mathf.Max(1f, Screen.height);
      max.x /= Mathf.Max(1f, Screen.width);
      max.y /= Mathf.Max(1f, Screen.height);
      _safeAreaRoot.anchorMin = min;
      _safeAreaRoot.anchorMax = max;
      _safeAreaRoot.offsetMin = Vector2.zero;
      _safeAreaRoot.offsetMax = Vector2.zero;
      LayoutView();
      _layoutValidationPending = IsVisible;
    }

    private void LayoutView()
    {
      if (_safeAreaRoot == null)
      {
        return;
      }

      var safeHeight = Mathf.Max(960f, _safeAreaRoot.rect.height);
      var cardHeight = Mathf.Clamp(safeHeight * 0.172f, 264f, 332f);
      var spacing = Mathf.Clamp(safeHeight * 0.021f, 26f, 40f);
      var step = cardHeight + spacing;
      var groupOffsetY = -safeHeight * 0.015f;

      SetRect(_headerText.rectTransform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(0f, 72f));
      SetRect(_instructionText.rectTransform, new Vector2(0.06f, 1f), new Vector2(0.94f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -124f), new Vector2(0f, 42f));

      for (var i = 0; i < _cards.Count; i++)
      {
        var card = _cards[i];
        card.Root.sizeDelta = new Vector2(0f, cardHeight);
        card.BaseAnchoredPosition = new Vector2(0f, groupOffsetY + (1 - i) * step);
        card.Root.anchoredPosition = card.BaseAnchoredPosition;
      }

      LayoutHudSlots();
    }

    private void LayoutHudSlots()
    {
      for (var i = 0; i < _hudSlots.Count; i++)
      {
        _hudSlots[i].Root.anchoredPosition = GetHudSlotLocalPosition(i, _hudSlots.Count) - _hudRoot.anchoredPosition;
      }
    }

    private Vector2 GetHudSlotLocalPosition(int index, int totalCount)
    {
      var safeHeight = _safeAreaRoot != null ? _safeAreaRoot.rect.height : ReferenceHeight;
      var spacing = 76f;
      var totalWidth = Mathf.Max(0, totalCount - 1) * spacing;
      return new Vector2(-totalWidth * 0.5f + index * spacing, Mathf.Max(48f, safeHeight * 0.035f));
    }

    private void UpdateCardAnimations()
    {
      if (!IsVisible)
      {
        return;
      }

      if (_focusedCardIndex < 0 && Time.unscaledTime >= _visualFocusHoldUntil)
      {
        _visualFocusedCardIndex = -1;
      }

      var hasFocus = _visualFocusedCardIndex >= 0 || _selectedCardIndex >= 0;
      for (var i = 0; i < _cards.Count; i++)
      {
        var card = _cards[i];
        var isFocused = i == _visualFocusedCardIndex;
        var isSelected = i == _selectedCardIndex;
        var isOtherDuringInstall = _installing && !isSelected;
        var targetAlpha = isOtherDuringInstall
          ? 1f - Mathf.SmoothStep(0f, 1f, _installProgress)
          : hasFocus && !isFocused && !isSelected
            ? 0.76f
            : 1f;
        card.Group.alpha = Mathf.Lerp(card.Group.alpha, targetAlpha, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));

        var targetScale = isSelected ? (_installing ? 1.035f : 1.015f) : 1f;
        card.Root.localScale = Vector3.Lerp(card.Root.localScale, Vector3.one * targetScale, 1f - Mathf.Exp(-11f * Time.unscaledDeltaTime));
        var targetPosition = isSelected && _installing
          ? Vector2.Lerp(card.BaseAnchoredPosition, Vector2.zero, Mathf.SmoothStep(0f, 1f, _installProgress))
          : card.BaseAnchoredPosition;
        card.Root.anchoredPosition = Vector2.Lerp(card.Root.anchoredPosition, targetPosition, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));

        var baseSurface = new Color(30f / 255f, 43f / 255f, 40f / 255f, 0.96f);
        var focusedSurface = new Color(38f / 255f, 54f / 255f, 49f / 255f, 0.98f);
        card.Surface.color = Color.Lerp(card.Surface.color, isFocused || isSelected ? focusedSurface : baseSurface, 1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
        card.Border.color = Color.Lerp(
          card.Border.color,
          isFocused || isSelected
            ? KeepBlinkingTheme.WithAlpha(card.Icon.AccentColor, 0.82f)
            : new Color(126f / 255f, 145f / 255f, 135f / 255f, 0.5f),
          1f - Mathf.Exp(-9f * Time.unscaledDeltaTime));
        card.FocusProgress.Progress = isFocused && !isSelected
          ? (_focusedCardIndex == i ? _focusProgress : 0f)
          : isSelected ? 1f : 0f;
        card.Icon.Phase = isSelected && _installing
          ? _installProgress * 0.5f
          : Mathf.Repeat(Time.unscaledTime / 2f, 1f);
      }
    }

    private void UpdateHudAnimations()
    {
      for (var i = 0; i < _hudSlots.Count; i++)
      {
        var slot = _hudSlots[i];
        _moduleFlashUntil.TryGetValue(slot.ModuleId, out var flashUntil);
        var flash = Mathf.Clamp01((flashUntil - Time.unscaledTime) / 0.8f);
        slot.Root.localScale = Vector3.Lerp(slot.Root.localScale, Vector3.one * (1f + flash * 0.1f), 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        slot.Border.color = Color.Lerp(
          KeepBlinkingTheme.WithAlpha(slot.Icon.AccentColor, 0.46f),
          KeepBlinkingTheme.WithAlpha(slot.Icon.AccentColor, 0.98f),
          flash);
        slot.Icon.Phase = flash > 0f ? 1f - flash : 0.12f;
      }
    }

    private void UpdateInstallTravel()
    {
      if (_travellingInstallIcon == null)
      {
        return;
      }

      var eased = Mathf.SmoothStep(0f, 1f, _installProgress);
      _travellingInstallIcon.rectTransform.anchoredPosition = Vector2.Lerp(_installIconStart, _installIconTarget, eased);
      _travellingInstallIcon.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.48f, eased);
      _travellingInstallIcon.Phase = Mathf.Repeat(Time.unscaledTime / GetIconLoopSeconds(_travellingInstallIcon.Category), 1f);
    }

    private void UpdateInstruction()
    {
      if (_instructionText == null)
      {
        return;
      }

      _instructionText.text = _installing
        ? "INSTALLED"
        : "TAP TO INSTALL";
      _instructionText.color = _focusedCardIndex >= 0 || _selectedCardIndex >= 0
        ? KeepBlinkingTheme.TextPrimary
        : KeepBlinkingTheme.TextMuted;
    }

    private void ValidateTextOverflow()
    {
      for (var i = 0; i < _cards.Count; i++)
      {
        var card = _cards[i];
        card.Category.ForceMeshUpdate();
        card.Title.ForceMeshUpdate();
        card.Description.ForceMeshUpdate();
        card.Delta.ForceMeshUpdate();
        if (card.Category.isTextOverflowing ||
            card.Title.isTextOverflowing ||
            card.Description.isTextOverflowing ||
            card.Delta.isTextOverflowing)
        {
          Debug.LogWarning($"Upgrade card text overflow detected on {card.Title.text} at {Screen.width}x{Screen.height}.");
        }
      }
    }

    private int CountCategoryLevel(FirstLevelModuleId moduleId)
    {
      var category = FirstLevelUpgradeCatalog.Get(moduleId).Category;
      var count = 0;
      for (var i = 0; i < _hudSlots.Count; i++)
      {
        if (FirstLevelUpgradeCatalog.Get(_hudSlots[i].ModuleId).Category == category)
        {
          count++;
        }
      }
      return Mathf.Clamp(count + 1, 1, 3);
    }

    private static float GetIconLoopSeconds(FirstLevelModuleCategory category)
    {
      switch (category)
      {
        case FirstLevelModuleCategory.Blink:
          return 1.8f;
        case FirstLevelModuleCategory.Rest:
          return 2.6f;
        case FirstLevelModuleCategory.Distance:
          return 2.2f;
        default:
          return 2.4f;
      }
    }

    private TextMeshProUGUI CreateText(
      string objectName,
      Transform parent,
      string text,
      float fontSize,
      float minimumFontSize,
      float maximumFontSize,
      FontStyles style,
      TextAlignmentOptions alignment,
      bool wraps)
    {
      var textObject = CreateUiObject(objectName, parent);
      var tmp = textObject.AddComponent<TextMeshProUGUI>();
      tmp.font = _fontAsset;
      tmp.text = text;
      tmp.fontSize = fontSize;
      tmp.enableAutoSizing = true;
      tmp.fontSizeMin = minimumFontSize;
      tmp.fontSizeMax = maximumFontSize;
      tmp.fontStyle = style;
      tmp.alignment = alignment;
      tmp.textWrappingMode = wraps ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
      tmp.overflowMode = wraps ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
      tmp.margin = Vector4.zero;
      tmp.raycastTarget = false;
      return tmp;
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
      var gameObject = new GameObject(objectName, typeof(RectTransform));
      gameObject.transform.SetParent(parent, false);
      return gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
    }

    private static void SetRect(
      RectTransform rect,
      Vector2 anchorMin,
      Vector2 anchorMax,
      Vector2 pivot,
      Vector2 anchoredPosition,
      Vector2 sizeDelta)
    {
      rect.anchorMin = anchorMin;
      rect.anchorMax = anchorMax;
      rect.pivot = pivot;
      rect.anchoredPosition = anchoredPosition;
      rect.sizeDelta = sizeDelta;
    }

    private static Texture2D CreateRoundedTexture(bool borderOnly)
    {
      const int size = 64;
      const float cornerRadius = 15f;
      const float borderWidth = 1.5f;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = borderOnly ? "Upgrade Thin Border" : "Upgrade Rounded Fill",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };

      var colors = new Color32[size * size];
      var half = size * 0.5f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var px = Mathf.Abs(x + 0.5f - half) - (half - cornerRadius);
          var py = Mathf.Abs(y + 0.5f - half) - (half - cornerRadius);
          var outside = Mathf.Sqrt(Mathf.Max(px, 0f) * Mathf.Max(px, 0f) + Mathf.Max(py, 0f) * Mathf.Max(py, 0f));
          var signedDistance = outside + Mathf.Min(Mathf.Max(px, py), 0f) - cornerRadius;
          var alpha = borderOnly
            ? Mathf.Clamp01(1f - Mathf.Abs(signedDistance + borderWidth * 0.5f) / borderWidth)
            : Mathf.Clamp01(0.5f - signedDistance);
          colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
      }

      texture.SetPixels32(colors);
      texture.Apply(false, true);
      return texture;
    }

    private void ClearCards()
    {
      for (var i = 0; i < _cards.Count; i++)
      {
        if (_cards[i].Root != null)
        {
          Destroy(_cards[i].Root.gameObject);
        }
      }
      _cards.Clear();
    }

    private void ClearHudSlots()
    {
      for (var i = 0; i < _hudSlots.Count; i++)
      {
        if (_hudSlots[i].Root != null)
        {
          Destroy(_hudSlots[i].Root.gameObject);
        }
      }
      _hudSlots.Clear();
    }

    private void DestroyTravellingInstallIcon()
    {
      if (_travellingInstallIcon != null)
      {
        Destroy(_travellingInstallIcon.gameObject);
        _travellingInstallIcon = null;
      }
    }

    private void OnDestroy()
    {
      if (_roundedFillSprite != null) Destroy(_roundedFillSprite);
      if (_roundedBorderSprite != null) Destroy(_roundedBorderSprite);
      if (_roundedFillTexture != null) Destroy(_roundedFillTexture);
      if (_roundedBorderTexture != null) Destroy(_roundedBorderTexture);
    }

    private sealed class CardVisual
    {
      public RectTransform Root { get; }
      public CanvasGroup Group { get; }
      public Image Surface { get; }
      public Image Border { get; }
      public UpgradeFocusProgressGraphic FocusProgress { get; }
      public CareUpgradePreviewGraphic Icon { get; }
      public TextMeshProUGUI Category { get; }
      public TextMeshProUGUI Title { get; }
      public TextMeshProUGUI Description { get; }
      public TextMeshProUGUI Delta { get; }
      public Vector2 BaseAnchoredPosition { get; set; }

      public CardVisual(
        RectTransform root,
        CanvasGroup group,
        Image surface,
        Image border,
        UpgradeFocusProgressGraphic focusProgress,
        CareUpgradePreviewGraphic icon,
        TextMeshProUGUI category,
        TextMeshProUGUI title,
        TextMeshProUGUI description,
        TextMeshProUGUI delta)
      {
        Root = root;
        Group = group;
        Surface = surface;
        Border = border;
        FocusProgress = focusProgress;
        Icon = icon;
        Category = category;
        Title = title;
        Description = description;
        Delta = delta;
      }
    }

    private sealed class HudSlotVisual
    {
      public FirstLevelModuleId ModuleId { get; }
      public RectTransform Root { get; }
      public Image Border { get; }
      public UpgradeModuleIconVisual Icon { get; }

      public HudSlotVisual(FirstLevelModuleId moduleId, RectTransform root, Image border, UpgradeModuleIconVisual icon)
      {
        ModuleId = moduleId;
        Root = root;
        Border = border;
        Icon = icon;
      }
    }
  }

  internal sealed class UpgradeFocusProgressGraphic : MaskableGraphic
  {
    private float _progress;

    public float Progress
    {
      get => _progress;
      set
      {
        var clamped = Mathf.Clamp01(value);
        if (Mathf.Approximately(_progress, clamped)) return;
        _progress = clamped;
        SetVerticesDirty();
      }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      if (_progress <= 0f)
      {
        return;
      }

      var rect = rectTransform.rect;
      var inset = 3f;
      var left = rect.xMin + inset;
      var right = rect.xMax - inset;
      var bottom = rect.yMin + inset;
      var top = rect.yMax - inset;
      var points = new[]
      {
        new Vector2(0f, top),
        new Vector2(right, top),
        new Vector2(right, bottom),
        new Vector2(left, bottom),
        new Vector2(left, top),
        new Vector2(0f, top),
      };
      var segmentLengths = new float[points.Length - 1];
      var totalLength = 0f;
      for (var i = 0; i < segmentLengths.Length; i++)
      {
        segmentLengths[i] = Vector2.Distance(points[i], points[i + 1]);
        totalLength += segmentLengths[i];
      }

      var remaining = totalLength * _progress;
      for (var i = 0; i < segmentLengths.Length && remaining > 0f; i++)
      {
        var length = Mathf.Min(segmentLengths[i], remaining);
        var end = Vector2.Lerp(points[i], points[i + 1], length / Mathf.Max(0.001f, segmentLengths[i]));
        UpgradeUiMeshUtility.AddLine(vh, points[i], end, 2.2f, color);
        remaining -= length;
      }
    }
  }

  internal sealed class UpgradeModuleIconVisual : MonoBehaviour
  {
    public FirstLevelModuleCategory Category { get; private set; }
    public Color AccentColor { get; private set; }
    public int EvolutionLevel { get; private set; }
    public RectTransform rectTransform => transform as RectTransform;
    public bool raycastTarget { set { } }

    private RawImage _halo;
    private RawImage _lineArt;
    private Image _sample;
    private Texture2D _haloTexture;
    private Texture2D _lineTexture;
    private Sprite _sampleSprite;

    public float Phase
    {
      set => ApplyPhase(Mathf.Repeat(value, 1f));
    }

    public void Configure(FirstLevelModuleCategory category, Color accentColor, int evolutionLevel)
    {
      Category = category;
      AccentColor = accentColor;
      EvolutionLevel = Mathf.Clamp(evolutionLevel, 1, 3);
      RebuildVisual();
      ApplyPhase(0f);
    }

    private void RebuildVisual()
    {
      ClearVisual();
      _haloTexture = CreateHaloTexture();
      _lineTexture = CreateLineTexture(Category, AccentColor, EvolutionLevel);

      _halo = CreateRawImage("Soft Halo", _haloTexture, transform);
      Stretch(_halo.rectTransform, 0f);
      _halo.raycastTarget = false;

      _lineArt = CreateRawImage("Icon Line Art", _lineTexture, transform);
      Stretch(_lineArt.rectTransform, 0.08f);
      _lineArt.raycastTarget = false;

      if (Category == FirstLevelModuleCategory.Distance)
      {
        var sampleObject = new GameObject("Travelling Sample", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        sampleObject.transform.SetParent(transform, false);
        var sampleRect = sampleObject.GetComponent<RectTransform>();
        sampleRect.anchorMin = sampleRect.anchorMax = new Vector2(0.5f, 0.5f);
        sampleRect.pivot = new Vector2(0.5f, 0.5f);
        sampleRect.sizeDelta = new Vector2(18f, 18f);
        _sampleSprite = Sprite.Create(CreateSampleTexture(), new Rect(0f, 0f, 32f, 32f), new Vector2(0.5f, 0.5f), 32f);
        _sampleSprite.hideFlags = HideFlags.HideAndDontSave;
        _sample = sampleObject.GetComponent<Image>();
        _sample.sprite = _sampleSprite;
        _sample.color = AccentColor;
        _sample.raycastTarget = false;
      }
    }

    private void ApplyPhase(float phase)
    {
      if (_lineArt == null || _halo == null)
      {
        return;
      }

      var breathe = 0.5f + 0.5f * Mathf.Sin(phase * Mathf.PI * 2f);
      _halo.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, 0.72f + breathe * 0.1f);
      _halo.rectTransform.localScale = Vector3.one;

      switch (Category)
      {
        case FirstLevelModuleCategory.Blink:
          var focusBreath = 0.96f + breathe * 0.08f;
          _lineArt.rectTransform.localScale = new Vector3(focusBreath, focusBreath, 1f);
          _lineArt.color = new Color(1f, 1f, 1f, 0.82f + breathe * 0.16f);
          break;
        case FirstLevelModuleCategory.Rest:
          _lineArt.rectTransform.localScale = Vector3.one;
          _lineArt.color = new Color(1f, 1f, 1f, 0.84f + breathe * 0.14f);
          break;
        case FirstLevelModuleCategory.Distance:
          _lineArt.rectTransform.anchoredPosition = Vector2.zero;
          _lineArt.rectTransform.localScale = Vector3.one;
          if (_sample != null)
          {
            _sample.rectTransform.anchoredPosition = new Vector2(-46f, Mathf.Lerp(-42f, 52f, Mathf.SmoothStep(0f, 1f, phase)));
            _sample.color = new Color(AccentColor.r, AccentColor.g, AccentColor.b, Mathf.Sin(phase * Mathf.PI));
          }
          break;
        default:
          _lineArt.rectTransform.localEulerAngles = Vector3.zero;
          _lineArt.rectTransform.localScale = Vector3.one;
          _lineArt.color = new Color(1f, 1f, 1f, 0.86f + breathe * 0.12f);
          break;
      }
    }

    private static RawImage CreateRawImage(string name, Texture texture, Transform parent)
    {
      var child = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
      child.transform.SetParent(parent, false);
      var image = child.GetComponent<RawImage>();
      image.texture = texture;
      return image;
    }

    private static void Stretch(RectTransform rect, float insetRatio)
    {
      rect.anchorMin = new Vector2(insetRatio, insetRatio);
      rect.anchorMax = new Vector2(1f - insetRatio, 1f - insetRatio);
      rect.pivot = new Vector2(0.5f, 0.5f);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
    }

    private static Texture2D CreateHaloTexture()
    {
      const int size = 128;
      var texture = NewTexture(size, "Upgrade Icon Soft Halo");
      var colors = new Color32[size * size];
      var center = new Vector2(size * 0.5f, size * 0.5f);
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (size * 0.5f);
          var alpha = Mathf.Clamp01(1f - distance);
          alpha = alpha * alpha * 0.22f;
          colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
      }
      texture.SetPixels32(colors);
      texture.Apply(false, true);
      return texture;
    }

    private static Texture2D CreateLineTexture(FirstLevelModuleCategory category, Color accent, int level)
    {
      const int size = 128;
      var texture = NewTexture(size, $"Upgrade {category} Icon");
      var painter = new IconTexturePainter(size, accent);
      switch (category)
      {
        case FirstLevelModuleCategory.Blink:
          painter.DrawEye();
          break;
        case FirstLevelModuleCategory.Rest:
          painter.DrawRest();
          break;
        case FirstLevelModuleCategory.Distance:
          painter.DrawDistance();
          break;
        default:
          painter.DrawCombo(level);
          break;
      }
      texture.SetPixels32(painter.Pixels);
      texture.Apply(false, true);
      return texture;
    }

    private static Texture2D CreateSampleTexture()
    {
      const int size = 32;
      var texture = NewTexture(size, "Upgrade Travelling Sample");
      var colors = new Color32[size * size];
      var center = new Vector2(size * 0.5f, size * 0.5f);
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
          var alpha = Mathf.Clamp01((size * 0.34f - distance) / 1.2f);
          colors[y * size + x] = new Color(1f, 1f, 1f, alpha);
        }
      }
      texture.SetPixels32(colors);
      texture.Apply(false, true);
      return texture;
    }

    private static Texture2D NewTexture(int size, string name)
    {
      return new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = name,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
    }

    private void ClearVisual()
    {
      for (var i = transform.childCount - 1; i >= 0; i--)
      {
        Destroy(transform.GetChild(i).gameObject);
      }
      if (_haloTexture != null) Destroy(_haloTexture);
      if (_lineTexture != null) Destroy(_lineTexture);
      if (_sampleSprite != null) Destroy(_sampleSprite);
      _haloTexture = null;
      _lineTexture = null;
      _sampleSprite = null;
      _halo = null;
      _lineArt = null;
      _sample = null;
    }

    private void OnDestroy()
    {
      if (_haloTexture != null) Destroy(_haloTexture);
      if (_lineTexture != null) Destroy(_lineTexture);
      if (_sampleSprite != null) Destroy(_sampleSprite);
    }

    private sealed class IconTexturePainter
    {
      private readonly int _size;
      private readonly Color _color;
      internal Color32[] Pixels { get; }

      internal IconTexturePainter(int size, Color color)
      {
        _size = size;
        _color = color;
        Pixels = new Color32[size * size];
      }

      internal void DrawEye()
      {
        DrawParabola(new Vector2(64f, 63f), 43f, 20f, true, 3f, 0.98f);
        DrawParabola(new Vector2(64f, 63f), 43f, 20f, false, 3f, 0.72f);
        DrawCircle(new Vector2(64f, 63f), 14f, 3f, 0.9f);
        FillCircle(new Vector2(64f, 63f), 4.5f, 0.92f);
      }

      internal void DrawRest()
      {
        DrawParabola(new Vector2(64f, 69f), 42f, 16f, false, 3.2f, 0.96f);
        DrawArc(new Vector2(64f, 62f), 46f, 205f, 130f, 2.2f, 0.62f);
        for (var i = -2; i <= 2; i++)
        {
          var start = new Vector2(64f + i * 15f, 61f - Mathf.Abs(i) * 2f);
          DrawLine(start, start + new Vector2(i * 1.5f, -9f), 2f, 0.62f);
        }
      }

      internal void DrawDistance()
      {
        DrawLine(new Vector2(69f, 28f), new Vector2(101f, 28f), 3f, 0.9f);
        DrawLine(new Vector2(101f, 28f), new Vector2(101f, 101f), 3f, 0.9f);
        DrawLine(new Vector2(101f, 101f), new Vector2(69f, 101f), 3f, 0.9f);
        DrawLine(new Vector2(69f, 101f), new Vector2(69f, 28f), 3f, 0.9f);
        DrawLine(new Vector2(77f, 91f), new Vector2(93f, 91f), 2f, 0.72f);
        DrawArc(new Vector2(67f, 64f), 38f, 142f, 84f, 2.2f, 0.58f);
        DrawLine(new Vector2(34f, 43f), new Vector2(26f, 51f), 2.2f, 0.58f);
        DrawLine(new Vector2(34f, 43f), new Vector2(40f, 53f), 2.2f, 0.58f);
      }

      internal void DrawCombo(int level)
      {
        DrawCircle(new Vector2(64f, 64f), 42f, 2f, 0.5f);
        var count = Mathf.Clamp(level + 2, 3, 5);
        for (var i = 0; i < count; i++)
        {
          var angle = i * Mathf.PI * 2f / count - Mathf.PI * 0.5f;
          FillCircle(new Vector2(64f + Mathf.Cos(angle) * 35f, 64f + Mathf.Sin(angle) * 35f), 6f, 0.9f);
        }
      }

      private void DrawParabola(Vector2 center, float halfWidth, float height, bool upper, float width, float alpha)
      {
        var previous = center + new Vector2(-halfWidth, 0f);
        for (var i = 1; i <= 28; i++)
        {
          var x = Mathf.Lerp(-halfWidth, halfWidth, i / 28f);
          var normalized = x / halfWidth;
          var next = center + new Vector2(x, (1f - normalized * normalized) * height * (upper ? 1f : -1f));
          DrawLine(previous, next, width, alpha);
          previous = next;
        }
      }

      private void DrawArc(Vector2 center, float radius, float startDegrees, float sweepDegrees, float width, float alpha)
      {
        var previous = center + Direction(startDegrees) * radius;
        for (var i = 1; i <= 32; i++)
        {
          var next = center + Direction(startDegrees + sweepDegrees * i / 32f) * radius;
          DrawLine(previous, next, width, alpha);
          previous = next;
        }
      }

      private void DrawCircle(Vector2 center, float radius, float width, float alpha)
      {
        DrawArc(center, radius, 0f, 360f, width, alpha);
      }

      private void FillCircle(Vector2 center, float radius, float alpha)
      {
        var minX = Mathf.Max(0, Mathf.FloorToInt(center.x - radius - 1f));
        var maxX = Mathf.Min(_size - 1, Mathf.CeilToInt(center.x + radius + 1f));
        var minY = Mathf.Max(0, Mathf.FloorToInt(center.y - radius - 1f));
        var maxY = Mathf.Min(_size - 1, Mathf.CeilToInt(center.y + radius + 1f));
        for (var y = minY; y <= maxY; y++)
        {
          for (var x = minX; x <= maxX; x++)
          {
            var distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
            Blend(x, y, Mathf.Clamp01(radius + 0.75f - distance) * alpha);
          }
        }
      }

      private void DrawLine(Vector2 start, Vector2 end, float width, float alpha)
      {
        var minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.x, end.x) - width - 1f));
        var maxX = Mathf.Min(_size - 1, Mathf.CeilToInt(Mathf.Max(start.x, end.x) + width + 1f));
        var minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.y, end.y) - width - 1f));
        var maxY = Mathf.Min(_size - 1, Mathf.CeilToInt(Mathf.Max(start.y, end.y) + width + 1f));
        var segment = end - start;
        var lengthSquared = Mathf.Max(0.001f, segment.sqrMagnitude);
        for (var y = minY; y <= maxY; y++)
        {
          for (var x = minX; x <= maxX; x++)
          {
            var point = new Vector2(x + 0.5f, y + 0.5f);
            var t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            var distance = Vector2.Distance(point, start + segment * t);
            Blend(x, y, Mathf.Clamp01(width * 0.5f + 0.8f - distance) * alpha);
          }
        }
      }

      private void Blend(int x, int y, float alpha)
      {
        if (alpha <= 0f) return;
        var index = y * _size + x;
        var existing = Pixels[index];
        var existingAlpha = existing.a / 255f;
        var combinedAlpha = 1f - (1f - existingAlpha) * (1f - alpha);
        Pixels[index] = new Color(_color.r, _color.g, _color.b, combinedAlpha);
      }

      private static Vector2 Direction(float degrees)
      {
        var radians = degrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
      }
    }
  }

  internal static class UpgradeUiMeshUtility
  {
    internal static void AddLine(VertexHelper vh, Vector2 start, Vector2 end, float thickness, Color color)
    {
      var direction = end - start;
      if (direction.sqrMagnitude < 0.001f) return;
      var normal = new Vector2(-direction.y, direction.x).normalized * thickness * 0.5f;
      AddQuad(vh, start - normal, start + normal, end + normal, end - normal, color);
    }

    internal static void AddCircle(VertexHelper vh, Vector2 center, float radius, int segments, Color color)
    {
      var centerIndex = vh.currentVertCount;
      vh.AddVert(center, color, Vector2.zero);
      for (var i = 0; i <= segments; i++)
      {
        var angle = i * Mathf.PI * 2f / segments;
        vh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector2.zero);
      }
      for (var i = 0; i < segments; i++)
      {
        vh.AddTriangle(centerIndex, centerIndex + i + 1, centerIndex + i + 2);
      }
    }

    internal static void AddArc(VertexHelper vh, Vector2 center, float radius, float startDegrees, float sweepDegrees, int segments, float thickness, Color color)
    {
      var previous = center + Direction(startDegrees) * radius;
      for (var i = 1; i <= segments; i++)
      {
        var angle = startDegrees + sweepDegrees * i / segments;
        var next = center + Direction(angle) * radius;
        AddLine(vh, previous, next, thickness, color);
        previous = next;
      }
    }

    internal static void AddParabolicArc(VertexHelper vh, Vector2 center, float halfWidth, float height, bool upper, int segments, float thickness, Color color)
    {
      var previous = center + new Vector2(-halfWidth, 0f);
      for (var i = 1; i <= segments; i++)
      {
        var x = Mathf.Lerp(-halfWidth, halfWidth, i / (float)segments);
        var normalized = x / Mathf.Max(0.001f, halfWidth);
        var y = (1f - normalized * normalized) * height * (upper ? 1f : -1f);
        var next = center + new Vector2(x, y);
        AddLine(vh, previous, next, thickness, color);
        previous = next;
      }
    }

    internal static void AddRectOutline(VertexHelper vh, Vector2 center, float halfWidth, float halfHeight, float thickness, Color color)
    {
      var topLeft = center + new Vector2(-halfWidth, halfHeight);
      var topRight = center + new Vector2(halfWidth, halfHeight);
      var bottomRight = center + new Vector2(halfWidth, -halfHeight);
      var bottomLeft = center + new Vector2(-halfWidth, -halfHeight);
      AddLine(vh, topLeft, topRight, thickness, color);
      AddLine(vh, topRight, bottomRight, thickness, color);
      AddLine(vh, bottomRight, bottomLeft, thickness, color);
      AddLine(vh, bottomLeft, topLeft, thickness, color);
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
    {
      var index = vh.currentVertCount;
      vh.AddVert(a, color, Vector2.zero);
      vh.AddVert(b, color, Vector2.zero);
      vh.AddVert(c, color, Vector2.zero);
      vh.AddVert(d, color, Vector2.zero);
      vh.AddTriangle(index, index + 1, index + 2);
      vh.AddTriangle(index, index + 2, index + 3);
    }

    private static Vector2 Direction(float degrees)
    {
      var radians = degrees * Mathf.Deg2Rad;
      return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
  }
}

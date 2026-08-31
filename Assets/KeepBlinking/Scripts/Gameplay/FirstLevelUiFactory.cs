using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  internal static class FirstLevelUiFactory
  {
    internal const float ReferenceWidth = 1080f;
    internal const float ReferenceHeight = 1920f;

    private static TMP_FontAsset _font;
    private static Sprite _roundedSprite;
    private static Sprite _ringSprite;
    private static Sprite _circleSprite;

    internal static TMP_FontAsset Font
    {
      get
      {
        if (_font == null)
        {
          _font = TMP_Settings.defaultFontAsset;
          if (_font == null)
          {
            _font = Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");
          }
        }

        return _font;
      }
    }

    internal static Sprite RoundedSprite => _roundedSprite != null ? _roundedSprite : (_roundedSprite = CreateRoundedSprite());
    internal static Sprite RingSprite => _ringSprite != null ? _ringSprite : (_ringSprite = CreateRingSprite());
    internal static Sprite CircleSprite => _circleSprite != null ? _circleSprite : (_circleSprite = CreateCircleSprite());

    internal static RectTransform CreateCanvas(
      Transform parent,
      string name,
      int sortingOrder,
      out Canvas canvas,
      out CanvasGroup canvasGroup)
    {
      EnsureEventSystem();
      var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
      canvasObject.transform.SetParent(parent, false);
      canvas = canvasObject.GetComponent<Canvas>();
      canvas.enabled = true;
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      canvas.sortingOrder = sortingOrder;
      var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
      raycaster.enabled = true;
      var scaler = canvasObject.GetComponent<CanvasScaler>();
      scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
      scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
      scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
      scaler.matchWidthOrHeight = 0.5f;
      canvasGroup = canvasObject.GetComponent<CanvasGroup>();
      canvasGroup.alpha = 1f;
      canvasGroup.interactable = true;
      canvasGroup.blocksRaycasts = true;

      var safe = CreateObject("Safe Area", canvasObject.transform).GetComponent<RectTransform>();
      Stretch(safe);
      safe.gameObject.AddComponent<SafeAreaRuntimeFitter>();
      return safe;
    }

    internal static GameObject CreateObject(string name, Transform parent)
    {
      var gameObject = new GameObject(name, typeof(RectTransform));
      gameObject.transform.SetParent(parent, false);
      return gameObject;
    }

    internal static Image CreateImage(string name, Transform parent, Color color, Sprite sprite = null)
    {
      var image = CreateObject(name, parent).AddComponent<Image>();
      image.color = color;
      image.sprite = sprite;
      image.type = sprite == RoundedSprite ? Image.Type.Sliced : Image.Type.Simple;
      image.raycastTarget = false;
      return image;
    }

    internal static TextMeshProUGUI CreateText(
      string name,
      Transform parent,
      string value,
      float fontSize,
      FontStyles style,
      TextAlignmentOptions alignment,
      Color color,
      bool wrap = false)
    {
      var text = CreateObject(name, parent).AddComponent<TextMeshProUGUI>();
      text.font = Font;
      text.text = value;
      text.fontSize = fontSize;
      text.fontStyle = style;
      text.alignment = alignment;
      text.color = color;
      text.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
      text.overflowMode = TextOverflowModes.Ellipsis;
      text.raycastTarget = false;
      return text;
    }

    internal static Button CreateButton(string name, Transform parent, string label, Color accent)
    {
      var root = CreateObject(name, parent);
      var image = root.AddComponent<Image>();
      image.sprite = RoundedSprite;
      image.type = Image.Type.Sliced;
      image.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.98f);
      var button = root.AddComponent<Button>();
      var colors = button.colors;
      colors.normalColor = Color.white;
      colors.highlightedColor = Color.Lerp(Color.white, accent, 0.16f);
      colors.pressedColor = Color.Lerp(Color.white, accent, 0.3f);
      colors.selectedColor = colors.highlightedColor;
      colors.fadeDuration = 0.16f;
      button.colors = colors;

      var border = CreateImage("Border", root.transform, KeepBlinkingTheme.WithAlpha(accent, 0.72f), RoundedSprite);
      Stretch(border.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));
      border.transform.SetAsFirstSibling();
      var fill = CreateImage("Inner", root.transform, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.98f), RoundedSprite);
      Stretch(fill.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
      fill.transform.SetSiblingIndex(1);

      var text = CreateText("Label", root.transform, label, 26f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      Stretch(text.rectTransform, new Vector2(16f, 8f), new Vector2(-16f, -8f));
      return button;
    }

    internal static void SetRect(
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

    internal static void Stretch(RectTransform rect)
    {
      Stretch(rect, Vector2.zero, Vector2.zero);
    }

    internal static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
      rect.anchorMin = Vector2.zero;
      rect.anchorMax = Vector2.one;
      rect.offsetMin = offsetMin;
      rect.offsetMax = offsetMax;
    }

    internal static void RecoverUiInput(Transform uiRoot, CanvasGroup canvasGroup = null)
    {
      EnsureEventSystem();
      if (uiRoot == null) return;

      var canvas = uiRoot.GetComponentInParent<Canvas>(true);
      if (canvas == null) return;
      canvas.gameObject.SetActive(true);
      canvas.enabled = true;
      var raycaster = canvas.GetComponent<GraphicRaycaster>();
      if (raycaster == null) raycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
      raycaster.enabled = true;

      var group = canvasGroup != null ? canvasGroup : canvas.GetComponent<CanvasGroup>();
      if (group == null) group = canvas.gameObject.AddComponent<CanvasGroup>();
      group.alpha = 1f;
      group.interactable = true;
      group.blocksRaycasts = true;
    }

    internal static bool IsUiInputInfrastructureHealthy(Transform uiRoot)
    {
      if (uiRoot == null) return false;
      var canvas = uiRoot.GetComponentInParent<Canvas>(true);
      var raycaster = canvas != null ? canvas.GetComponent<GraphicRaycaster>() : null;
      var eventSystem = EventSystem.current;
      var module = eventSystem != null ? eventSystem.currentInputModule : null;
      if (canvas == null || !canvas.isActiveAndEnabled ||
          raycaster == null || !raycaster.isActiveAndEnabled ||
          eventSystem == null || !eventSystem.isActiveAndEnabled ||
          module == null || !module.isActiveAndEnabled || !module.IsModuleSupported())
        return false;
      if (module is StandaloneInputModule) return true;
      if (!(module is InputSystemUIInputModule inputSystemModule)) return false;
      return HasCompleteInputActions(inputSystemModule);
    }

    private static EventSystem EnsureEventSystem()
    {
      return EnsureEventSystem(null);
    }

    private static EventSystem EnsureEventSystem(EventSystem preferredEventSystem)
    {
      var eventSystems = Object.FindObjectsByType<EventSystem>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);
      var eventSystem = preferredEventSystem != null && preferredEventSystem.gameObject.activeInHierarchy
        ? preferredEventSystem
        : ChooseEventSystem(eventSystems);
      if (eventSystem == null)
      {
        // Never reactivate an arbitrary disabled scene hierarchy just because it
        // happens to contain an EventSystem. A dedicated root object is safer
        // and remains available when the inactive scene object is destroyed.
        var eventSystemObject = new GameObject("[KeepBlinking] EventSystem", typeof(EventSystem));
        eventSystem = eventSystemObject.GetComponent<EventSystem>();
        if (Application.isPlaying)
        {
          Object.DontDestroyOnLoad(eventSystemObject);
        }
      }

      eventSystem.enabled = true;
      // EventSystem only registers itself in the static current list during Play Mode.
      // Assigning current from an edit-time preview logs an error and cannot succeed.
      if (Application.isPlaying)
      {
        EventSystem.current = eventSystem;
      }

      EnsureCompatibleInputModule(eventSystem);
      return eventSystem;
    }

    private static EventSystem ChooseEventSystem(EventSystem[] eventSystems)
    {
      if (EventSystem.current != null && EventSystem.current.gameObject.activeInHierarchy)
      {
        return EventSystem.current;
      }

      for (var index = 0; index < eventSystems.Length; index++)
      {
        var candidate = eventSystems[index];
        if (candidate != null && candidate.gameObject.activeInHierarchy && candidate.enabled)
        {
          return candidate;
        }
      }

      for (var index = 0; index < eventSystems.Length; index++)
      {
        var candidate = eventSystems[index];
        if (candidate != null && candidate.gameObject.activeInHierarchy)
        {
          return candidate;
        }
      }

      return null;
    }

    private static void EnsureCompatibleInputModule(EventSystem eventSystem)
    {
      var inputSystemModules = eventSystem.GetComponents<InputSystemUIInputModule>();
      var standaloneModules = eventSystem.GetComponents<StandaloneInputModule>();
      BaseInputModule selectedModule = null;

      for (var index = 0; index < inputSystemModules.Length; index++)
      {
        if (inputSystemModules[index].enabled)
        {
          selectedModule = inputSystemModules[index];
          break;
        }
      }

      if (selectedModule == null)
      {
        for (var index = 0; index < standaloneModules.Length; index++)
        {
          if (standaloneModules[index].enabled)
          {
            selectedModule = standaloneModules[index];
            break;
          }
        }
      }

      if (selectedModule == null && inputSystemModules.Length > 0)
      {
        selectedModule = inputSystemModules[0];
      }
      if (selectedModule == null && standaloneModules.Length > 0)
      {
        selectedModule = standaloneModules[0];
      }
      if (selectedModule == null)
      {
        selectedModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
      }

      var allModules = eventSystem.GetComponents<BaseInputModule>();
      for (var index = 0; index < allModules.Length; index++)
      {
        allModules[index].enabled = allModules[index] == selectedModule;
      }

      selectedModule.enabled = true;
      if (selectedModule is InputSystemUIInputModule inputSystemModule &&
          !HasCompleteInputActions(inputSystemModule))
      {
        inputSystemModule.AssignDefaultActions();
      }
    }

    private static bool HasCompleteInputActions(InputSystemUIInputModule module)
    {
      return module != null &&
             module.actionsAsset != null &&
             module.point != null &&
             module.leftClick != null &&
             module.scrollWheel != null &&
             module.move != null &&
             module.submit != null &&
             module.cancel != null;
    }

    private static Sprite CreateRoundedSprite()
    {
      const int size = 64;
      const float radius = 14f;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
      {
        name = "KeepBlinking Rounded UI",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var colors = new Color32[size * size];
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var dx = Mathf.Max(0f, Mathf.Abs(x - (size - 1) * 0.5f) - ((size - 1) * 0.5f - radius));
          var dy = Mathf.Max(0f, Mathf.Abs(y - (size - 1) * 0.5f) - ((size - 1) * 0.5f - radius));
          var distance = Mathf.Sqrt(dx * dx + dy * dy);
          var alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(radius + 0.5f - distance) * 255f);
          colors[y * size + x] = new Color32(255, 255, 255, alpha);
        }
      }
      texture.SetPixels32(colors);
      texture.Apply(false, true);
      var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(14f, 14f, 14f, 14f));
      sprite.hideFlags = HideFlags.HideAndDontSave;
      return sprite;
    }

    private static Sprite CreateRingSprite()
    {
      const int size = 128;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
      {
        name = "KeepBlinking UI Ring",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var colors = new Color32[size * size];
      var center = (size - 1) * 0.5f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
          var alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - center * 0.84f) / 2.2f);
          colors[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
        }
      }
      texture.SetPixels32(colors);
      texture.Apply(false, true);
      var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
      sprite.hideFlags = HideFlags.HideAndDontSave;
      return sprite;
    }

    private static Sprite CreateCircleSprite()
    {
      const int size = 96;
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
      {
        name = "KeepBlinking UI Circle",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var colors = new Color32[size * size];
      var center = (size - 1) * 0.5f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
          var alpha = Mathf.Clamp01(center + 0.5f - distance);
          colors[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
        }
      }
      texture.SetPixels32(colors);
      texture.Apply(false, true);
      var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
      sprite.hideFlags = HideFlags.HideAndDontSave;
      return sprite;
    }
  }

  internal sealed class SafeAreaRuntimeFitter : MonoBehaviour
  {
    private Rect _lastSafeArea;
    private Vector2Int _lastScreenSize;

    private void OnEnable()
    {
      Apply(true);
    }

    private void Update()
    {
      Apply(false);
    }

    private void Apply(bool force)
    {
      var screenSize = new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
      var safeArea = Screen.safeArea;
      if (safeArea.width <= 0f || safeArea.height <= 0f)
      {
        safeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
      }

      if (!force && safeArea == _lastSafeArea && screenSize == _lastScreenSize)
      {
        return;
      }

      _lastSafeArea = safeArea;
      _lastScreenSize = screenSize;
      var rect = (RectTransform)transform;
      rect.anchorMin = new Vector2(safeArea.xMin / screenSize.x, safeArea.yMin / screenSize.y);
      rect.anchorMax = new Vector2(safeArea.xMax / screenSize.x, safeArea.yMax / screenSize.y);
      rect.offsetMin = Vector2.zero;
      rect.offsetMax = Vector2.zero;
    }
  }
}

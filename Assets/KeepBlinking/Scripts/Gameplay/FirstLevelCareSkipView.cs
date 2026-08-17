using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class FirstLevelCareSkipView : MonoBehaviour
  {
    private CanvasGroup _group;
    private Button _button;
    private FirstLevelCareFlowController _flow;
    private bool _pressed;
    private FirstLevelCareFlowState _lastState = FirstLevelCareFlowState.Dormant;

    public static FirstLevelCareSkipView Instance { get; private set; }

    public static FirstLevelCareSkipView EnsureExists(FirstLevelCareFlowController flow)
    {
      if (Instance == null) Instance = FindFirstObjectByType<FirstLevelCareSkipView>();
      if (Instance == null)
      {
        var owner = new GameObject("First Level Care Skip View");
        Instance = owner.AddComponent<FirstLevelCareSkipView>();
      }
      Instance._flow = flow;
      return Instance;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
      Build();
      SetVisible(false, FirstLevelCareFlowState.Dormant);
    }

    public void SetVisible(bool visible, FirstLevelCareFlowState state)
    {
      if (_group == null) return;
      if (!visible || state != _lastState) _pressed = false;
      _lastState = state;
      _group.alpha = visible ? 1f : 0f;
      _group.interactable = visible && !_pressed;
      _group.blocksRaycasts = visible && !_pressed;
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Care Skip Canvas", 365, out _, out _group);
      _button = FirstLevelUiFactory.CreateButton("Skip Current Care Step", safe, "SKIP", KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(
        _button.GetComponent<RectTransform>(),
        new Vector2(0f, 0f),
        new Vector2(0f, 0f),
        Vector2.zero,
        new Vector2(28f, 86f),
        new Vector2(148f, 54f));
      _button.onClick.AddListener(HandlePressed);
    }

    private void HandlePressed()
    {
      if (_pressed || _flow == null) return;
      _pressed = true;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      _flow.SkipCurrentStep();
    }

    private void OnDestroy()
    {
      if (_button != null) _button.onClick.RemoveListener(HandlePressed);
      if (Instance == this) Instance = null;
    }
  }
}

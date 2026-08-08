using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class ScreenDownRestView : MonoBehaviour
  {
    private CanvasGroup _canvasGroup;
    private Image _dim;
    private RectTransform _seedRoot;
    private Image _shell;
    private Image _center;
    private RectTransform _phone;
    private Image _ripple;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _prompt;
    private Button _skip;
    private ScreenDownRestState _state;
    private float _stateChangedAt;

    private void Awake()
    {
      Build();
      Hide();
    }

    public void Show()
    {
      if (_canvasGroup == null) Build();
      _canvasGroup.alpha = 1f;
      _canvasGroup.interactable = true;
      _canvasGroup.blocksRaycasts = true;
    }

    public void Hide()
    {
      if (_canvasGroup == null) return;
      _canvasGroup.alpha = 0f;
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;
    }

    public void ShowReturnNeutral(string prompt)
    {
      Show();
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;
      _dim.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundTertiary, 0.18f);
      _seedRoot.gameObject.SetActive(true);
      _seedRoot.localScale = Vector3.one * 0.72f;
      _shell.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.58f);
      _center.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f);
      _ripple.color = Color.clear;
      _phone.localRotation = Quaternion.identity;
      _title.text = string.Empty;
      _prompt.text = string.IsNullOrEmpty(prompt) ? "RETURN TO CENTER" : prompt;
      _skip.gameObject.SetActive(false);
    }

    public void SetState(ScreenDownRestState state)
    {
      _state = state;
      _stateChangedAt = Time.unscaledTime;
      if (_title == null) return;
      _title.text = state == ScreenDownRestState.RestSeedIntro ? "REST" : string.Empty;
      _prompt.text = GetPrompt(state);
      _skip.gameObject.SetActive(state != ScreenDownRestState.SeedBloom);
    }

    public void Render(ScreenDownRestState state, float restProgress, bool simulateDown, bool simulateReturn)
    {
      if (_seedRoot == null) return;
      var now = Time.unscaledTime;
      var breath = 1f + Mathf.Sin(now * 1.35f) * 0.025f;
      var bloom = state == ScreenDownRestState.SeedBloom
        ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((now - _stateChangedAt) / 1.15f))
        : 0f;
      _seedRoot.localScale = Vector3.one * (breath + bloom * 0.18f);
      _shell.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, Mathf.Lerp(0.9f, 0.2f, bloom));
      _center.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, Mathf.Lerp(0.7f, 1f, bloom));
      _ripple.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, bloom * (1f - bloom) * 1.5f);
      _ripple.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.4f, 3.6f, bloom);

      var turning = state == ScreenDownRestState.PromptScreenDown || state == ScreenDownRestState.WaitScreenDown;
      var targetRotation = turning || state == ScreenDownRestState.Resting || simulateDown ? 180f : 0f;
      if (state == ScreenDownRestState.WaitNormalOrientation || state == ScreenDownRestState.WaitFaceReturn || simulateReturn)
        targetRotation = 0f;
      var z = Mathf.LerpAngle(_phone.localEulerAngles.z, targetRotation, 1f - Mathf.Exp(-2.1f * Time.unscaledDeltaTime));
      _phone.localRotation = Quaternion.Euler(0f, 0f, z);

      var resting = state == ScreenDownRestState.Resting || state == ScreenDownRestState.ReadyToReturn;
      var restBreath = resting ? Mathf.Sin(now * 0.72f) * 0.025f : 0f;
      _dim.color = KeepBlinkingTheme.WithAlpha(
        KeepBlinkingTheme.BackgroundTertiary,
        resting ? 0.84f + restBreath : 0.58f);
      _seedRoot.gameObject.SetActive(!resting || state == ScreenDownRestState.ReadyToReturn);
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Screen-Down Rest Canvas", 420, out _, out _canvasGroup);
      _dim = FirstLevelUiFactory.CreateImage("Visual Dimming", safe, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundTertiary, 0.58f));
      FirstLevelUiFactory.Stretch(_dim.rectTransform);

      _seedRoot = FirstLevelUiFactory.CreateObject("Screen Rest Phone", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_seedRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 90f), new Vector2(430f, 430f));

      _ripple = FirstLevelUiFactory.CreateImage("Mint Bloom", _seedRoot, Color.clear, FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.Stretch(_ripple.rectTransform, new Vector2(60f, 60f), new Vector2(-60f, -60f));
      _shell = FirstLevelUiFactory.CreateImage("Warm White Rest Ring", _seedRoot, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.Stretch(_shell.rectTransform, new Vector2(80f, 80f), new Vector2(-80f, -80f));
      _center = FirstLevelUiFactory.CreateImage("Muted Mint Center", _seedRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(_center.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(104f, 104f));

      _phone = FirstLevelUiFactory.CreateObject("Sand Gold Phone Turn", _seedRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_phone, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(132f, 0f), new Vector2(76f, 128f));
      var phoneBody = FirstLevelUiFactory.CreateImage("Phone Outline", _phone, KeepBlinkingTheme.AccentWarm, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(phoneBody.rectTransform);
      var phoneScreen = FirstLevelUiFactory.CreateImage("Phone Screen", _phone, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundTertiary, 0.94f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(phoneScreen.rectTransform, new Vector2(8f, 12f), new Vector2(-8f, -12f));

      _title = FirstLevelUiFactory.CreateText("Seed Name", safe, string.Empty, 30f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 355f), new Vector2(640f, 64f));
      _prompt = FirstLevelUiFactory.CreateText("Prompt", safe, string.Empty, 34f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_prompt.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -250f), new Vector2(820f, 120f));

      _skip = FirstLevelUiFactory.CreateButton("Skip", safe, "SKIP", KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(_skip.GetComponent<RectTransform>(), Vector2.one, Vector2.one, Vector2.one, new Vector2(-36f, -42f), new Vector2(180f, 68f));
      _skip.onClick.AddListener(() => ScreenDownRestController.Instance?.Skip());
    }

    private static string GetPrompt(ScreenDownRestState state)
    {
      switch (state)
      {
        case ScreenDownRestState.WaitingForCameraReady: return "CAMERA READY";
        case ScreenDownRestState.PromptScreenDown:
        case ScreenDownRestState.WaitScreenDown: return "TURN SCREEN DOWN";
        case ScreenDownRestState.ReadyToReturn:
        case ScreenDownRestState.WaitNormalOrientation: return "READY TO RETURN";
        case ScreenDownRestState.WaitFaceReturn: return "LOOK AT THE SCREEN";
        case ScreenDownRestState.SeedBloom: return "REST";
        case ScreenDownRestState.SensorUnavailable:
#if UNITY_EDITOR || DEVELOPMENT_BUILD
          return "PRESS F11 TO SIMULATE";
#else
          return "MOTION SENSOR UNAVAILABLE";
#endif
        default: return string.Empty;
      }
    }
  }
}

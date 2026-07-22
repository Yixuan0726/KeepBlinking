using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public enum ComfortCheckPhase
  {
    PreSession,
    PostSession,
  }

  public readonly struct ComfortScores
  {
    public int EyeStrain { get; }
    public int Dryness { get; }
    public int VisualFatigue { get; }

    public ComfortScores(int eyeStrain, int dryness, int visualFatigue)
    {
      EyeStrain = Mathf.Clamp(eyeStrain, 0, 10);
      Dryness = Mathf.Clamp(dryness, 0, 10);
      VisualFatigue = Mathf.Clamp(visualFatigue, 0, 10);
    }
  }

  public sealed class SessionComfortCheckController : MonoBehaviour
  {
    private readonly Slider[] _sliders = new Slider[3];
    private readonly TextMeshProUGUI[] _valueLabels = new TextMeshProUGUI[3];

    private GameObject _canvasObject;
    private CanvasGroup _canvasGroup;
    private ComfortCheckPhase _phase;
    private bool _isOpen;
    private float _targetAlpha;

    public bool IsOpen => _isOpen;
    public ComfortCheckPhase Phase => _phase;

    public event Action<ComfortCheckPhase, ComfortScores?> Completed;

    public void EnsureCreated()
    {
      if (_canvasObject != null)
      {
        return;
      }

      var safeRoot = FirstLevelUiFactory.CreateCanvas(transform, "Quick Comfort Check Canvas", 3100, out _, out _canvasGroup);
      _canvasObject = safeRoot.parent.gameObject;
      _canvasGroup.alpha = 0f;
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;

      var scrim = FirstLevelUiFactory.CreateImage(
        "Comfort Scrim",
        safeRoot.parent,
        new Color(5f / 255f, 8f / 255f, 9f / 255f, 0.9f));
      FirstLevelUiFactory.Stretch(scrim.rectTransform);
      scrim.transform.SetAsFirstSibling();

      var panel = FirstLevelUiFactory.CreateImage(
        "Comfort Panel",
        safeRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.98f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(
        panel.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(900f, 1260f));

      var border = FirstLevelUiFactory.CreateImage(
        "Comfort Border",
        panel.transform,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.5f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(border.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));
      border.transform.SetAsFirstSibling();
      var inner = FirstLevelUiFactory.CreateImage(
        "Comfort Inner",
        panel.transform,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.98f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(inner.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
      inner.transform.SetSiblingIndex(1);

      var title = FirstLevelUiFactory.CreateText(
        "Title", panel.transform, "QUICK COMFORT CHECK", 46f, FontStyles.Bold,
        TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(0f, 64f));

      var question = FirstLevelUiFactory.CreateText(
        "Question", panel.transform, "How do your eyes feel right now?", 30f, FontStyles.Normal,
        TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(question.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -142f), new Vector2(0f, 48f));

      CreateScoreRow(panel.rectTransform, 0, "EYE STRAIN", -270f);
      CreateScoreRow(panel.rectTransform, 1, "DRYNESS", -505f);
      CreateScoreRow(panel.rectTransform, 2, "VISUAL FATIGUE", -740f);

      var submit = FirstLevelUiFactory.CreateButton("Submit", panel.transform, "SUBMIT", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect(submit.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-190f, 92f), new Vector2(330f, 88f));
      submit.onClick.AddListener(Submit);

      var skip = FirstLevelUiFactory.CreateButton("Skip", panel.transform, "SKIP", KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(skip.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(190f, 92f), new Vector2(330f, 88f));
      skip.onClick.AddListener(Skip);

      _canvasObject.SetActive(false);
    }

    public void Show(ComfortCheckPhase phase)
    {
      EnsureCreated();
      _phase = phase;
      for (var i = 0; i < _sliders.Length; i++)
      {
        _sliders[i].SetValueWithoutNotify(0f);
        UpdateValueLabel(i, 0f);
      }

      _isOpen = true;
      _targetAlpha = 1f;
      _canvasObject.SetActive(true);
      _canvasGroup.alpha = 0f;
      _canvasGroup.interactable = true;
      _canvasGroup.blocksRaycasts = true;
    }

    private void Update()
    {
      if (_canvasObject == null || !_canvasObject.activeSelf)
      {
        return;
      }

      _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, _targetAlpha, Time.unscaledDeltaTime * 5f);
      if (!_isOpen && _canvasGroup.alpha <= 0.001f)
      {
        _canvasObject.SetActive(false);
      }
    }

    private void CreateScoreRow(RectTransform panel, int index, string labelValue, float topOffset)
    {
      var row = FirstLevelUiFactory.CreateObject(labelValue + " Row", panel);
      FirstLevelUiFactory.SetRect(
        row.GetComponent<RectTransform>(),
        new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
        new Vector2(0f, topOffset), new Vector2(760f, 190f));

      var label = FirstLevelUiFactory.CreateText(
        "Label", row.transform, labelValue, 28f, FontStyles.Bold,
        TextAlignmentOptions.Left, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.78f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 44f));

      var value = FirstLevelUiFactory.CreateText(
        "Value", row.transform, "0", 34f, FontStyles.Bold,
        TextAlignmentOptions.Right, KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(0.78f, 1f), Vector2.one, Vector2.one, Vector2.zero, new Vector2(0f, 44f));
      _valueLabels[index] = value;

      var sliderObject = FirstLevelUiFactory.CreateObject("Slider", row.transform);
      var sliderRect = sliderObject.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(sliderRect, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(0f, 54f));
      var slider = sliderObject.AddComponent<Slider>();
      slider.minValue = 0f;
      slider.maxValue = 10f;
      slider.wholeNumbers = true;

      var background = FirstLevelUiFactory.CreateImage("Track", sliderObject.transform, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.8f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(background.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 10f));
      var fillArea = FirstLevelUiFactory.CreateObject("Fill Area", sliderObject.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(fillArea, new Vector2(8f, 0f), new Vector2(-8f, 0f));
      var fill = FirstLevelUiFactory.CreateImage("Fill", fillArea, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(fill.rectTransform, Vector2.zero, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 10f));
      var handleArea = FirstLevelUiFactory.CreateObject("Handle Area", sliderObject.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(handleArea, new Vector2(8f, 0f), new Vector2(-8f, 0f));
      var handle = FirstLevelUiFactory.CreateImage("Handle", handleArea, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.CircleSprite);
      handle.raycastTarget = true;
      handle.rectTransform.sizeDelta = new Vector2(38f, 38f);
      slider.fillRect = fill.rectTransform;
      slider.handleRect = handle.rectTransform;
      slider.targetGraphic = handle;
      slider.direction = Slider.Direction.LeftToRight;
      var capturedIndex = index;
      slider.onValueChanged.AddListener(valueChanged => UpdateValueLabel(capturedIndex, valueChanged));
      _sliders[index] = slider;

      var none = FirstLevelUiFactory.CreateText("None", row.transform, "NONE", 18f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(none.rectTransform, new Vector2(0f, 0f), new Vector2(0.3f, 0f), new Vector2(0f, 0f), new Vector2(0f, 2f), new Vector2(0f, 30f));
      var severe = FirstLevelUiFactory.CreateText("Severe", row.transform, "SEVERE", 18f, FontStyles.Bold, TextAlignmentOptions.Right, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(severe.rectTransform, new Vector2(0.7f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 2f), new Vector2(0f, 30f));
    }

    private void UpdateValueLabel(int index, float value)
    {
      if (index >= 0 && index < _valueLabels.Length && _valueLabels[index] != null)
      {
        _valueLabels[index].text = Mathf.RoundToInt(value).ToString();
      }
    }

    private void Submit()
    {
      if (!_isOpen)
      {
        return;
      }

      Complete(new ComfortScores(
        Mathf.RoundToInt(_sliders[0].value),
        Mathf.RoundToInt(_sliders[1].value),
        Mathf.RoundToInt(_sliders[2].value)));
    }

    private void Skip()
    {
      if (_isOpen)
      {
        Complete(null);
      }
    }

    private void Complete(ComfortScores? scores)
    {
      var phase = _phase;
      _isOpen = false;
      _targetAlpha = 0f;
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;
      if (Completed == null)
      {
        return;
      }

      var handlers = Completed.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action<ComfortCheckPhase, ComfortScores?>)handlers[i]).Invoke(phase, scores);
        }
        catch (Exception exception)
        {
          Debug.LogError("KeepBlinking comfort-check observer failed.", this);
          Debug.LogException(exception, this);
        }
      }
    }
  }
}

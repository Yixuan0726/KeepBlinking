using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public enum StudyFeedbackMode
  {
    PlayerFeedback,
    Blinded,
  }

  internal sealed class SessionReportView : MonoBehaviour
  {
    private readonly List<CanvasGroup> _revealRows = new List<CanvasGroup>(12);
    private GameObject _canvasObject;
    private CanvasGroup _canvasGroup;
    private RectTransform _panel;
    private RectTransform _continueButtonRect;
    private TextMeshProUGUI _subjectText;
    private TextMeshProUGUI _sessionText;
    private TextMeshProUGUI _summaryText;
    private TextMeshProUGUI _comfortUnavailableText;
    private TextMeshProUGUI _comfortSubtitle;
    private TextMeshProUGUI[] _comfortHeaders;
    private TextMeshProUGUI _safetyTitle;
    private TextMeshProUGUI _safetyBody;
    private CanvasGroup _stampGroup;
    private TextMeshProUGUI[] _behaviorValues;
    private ComfortReportRow[] _comfortRows;
    private EdgeOrbitHarvestMvp _gameplay;
    private AudioSource _audioSource;
    private AudioClip _stampClip;
    private float _openedAt;
    private int _openBlinkSerial;
    private float _continueGazeSeconds;
    private float _revealElapsed;
    private bool _isOpen;
    private bool _stampPlayed;

    internal bool IsOpen => _isOpen;
    internal event Action ContinueRequested;

    internal void Initialize(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted -= HandleBlinkInputAccepted;
      }
      _gameplay = gameplay;
      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted += HandleBlinkInputAccepted;
      }
      EnsureCreated();
    }

    internal void Show(SessionReportData data, StudyFeedbackMode feedbackMode)
    {
      EnsureCreated();
      Populate(data, feedbackMode);
      _isOpen = true;
      _canvasObject.SetActive(true);
      _canvasGroup.alpha = 0f;
      _panel.anchoredPosition = new Vector2(0f, -34f);
      _openedAt = Time.unscaledTime;
      _openBlinkSerial = _gameplay != null ? _gameplay.AcceptedBlinkSerial : 0;
      _continueGazeSeconds = 0f;
      _revealElapsed = 0f;
      _stampPlayed = false;
      _stampGroup.alpha = 0f;
      for (var i = 0; i < _revealRows.Count; i++)
      {
        _revealRows[i].alpha = 0f;
      }
    }

    internal void HideImmediate()
    {
      _isOpen = false;
      if (_canvasObject != null)
      {
        _canvasObject.SetActive(false);
      }
    }

    internal void BeginClose()
    {
      _isOpen = false;
    }

    internal void SetCloseAlpha(float alpha)
    {
      if (_canvasGroup != null)
      {
        _canvasGroup.alpha = Mathf.Clamp01(alpha);
      }
    }

    private void EnsureCreated()
    {
      if (_canvasObject != null)
      {
        return;
      }

      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Session Report Canvas", 3300, out _, out _canvasGroup);
      _canvasObject = safe.parent.gameObject;
      var scrim = FirstLevelUiFactory.CreateImage("Report Scrim", safe.parent, new Color(5f / 255f, 8f / 255f, 9f / 255f, 0.92f));
      FirstLevelUiFactory.Stretch(scrim.rectTransform);
      scrim.transform.SetAsFirstSibling();

      var panelImage = FirstLevelUiFactory.CreateImage("Report Card", safe, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.985f), FirstLevelUiFactory.RoundedSprite);
      _panel = panelImage.rectTransform;
      FirstLevelUiFactory.SetRect(_panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(940f, 1710f));
      var border = FirstLevelUiFactory.CreateImage("Report Border", _panel, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.48f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(border.rectTransform, new Vector2(2f, 2f), new Vector2(-2f, -2f));
      border.transform.SetAsFirstSibling();
      var inner = FirstLevelUiFactory.CreateImage("Report Inner", _panel, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.985f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(inner.rectTransform, new Vector2(4f, 4f), new Vector2(-4f, -4f));
      inner.transform.SetSiblingIndex(1);

      CreateHeader();
      CreateBehaviorSection();
      CreateComfortSection();
      CreateFooter();
      CreateStampAudio();
      _canvasObject.SetActive(false);
    }

    private void CreateHeader()
    {
      var unit = FirstLevelUiFactory.CreateText("Unit", _panel, "SILENT OBSERVATION UNIT", 20f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(unit.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(0f, 1f), new Vector2(54f, -38f), new Vector2(-54f, 34f));
      var title = FirstLevelUiFactory.CreateText("Report Title", _panel, "SESSION REPORT", 50f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0.72f, 1f), new Vector2(0f, 1f), new Vector2(54f, -78f), new Vector2(-54f, 66f));
      _subjectText = FirstLevelUiFactory.CreateText("Subject", _panel, string.Empty, 22f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(_subjectText.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, 1f), new Vector2(54f, -146f), new Vector2(-54f, 34f));
      _sessionText = FirstLevelUiFactory.CreateText("Session", _panel, string.Empty, 22f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(_sessionText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.76f, 1f), new Vector2(0f, 1f), new Vector2(0f, -146f), new Vector2(0f, 34f));

      var stamp = FirstLevelUiFactory.CreateImage("Session Complete Stamp", _panel, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.08f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(stamp.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.one, new Vector2(-42f, -42f), new Vector2(230f, 92f));
      _stampGroup = stamp.gameObject.AddComponent<CanvasGroup>();
      var stampText = FirstLevelUiFactory.CreateText("Stamp Text", stamp.transform, "SESSION\nCOMPLETE", 22f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.Stretch(stampText.rectTransform, new Vector2(12f, 8f), new Vector2(-12f, -8f));
    }

    private void CreateBehaviorSection()
    {
      var heading = CreateSectionHeading("BEHAVIOR", -222f, KeepBlinkingTheme.AccentPrimary);
      _revealRows.Add(heading.gameObject.AddComponent<CanvasGroup>());
      var labels = new[] { "NATURAL BLINKS", "REST CYCLES", "DISTANCE SHIFTS", "FULL LOOPS" };
      var accents = new[]
      {
        KeepBlinkingTheme.AccentPrimary,
        (Color)new Color32(0x91, 0xB8, 0xD0, 0xFF),
        KeepBlinkingTheme.AccentWarm,
        KeepBlinkingTheme.TextPrimary,
      };
      _behaviorValues = new TextMeshProUGUI[labels.Length];
      for (var i = 0; i < labels.Length; i++)
      {
        var column = i % 2;
        var row = i / 2;
        var item = FirstLevelUiFactory.CreateImage("Behavior " + labels[i], _panel, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.76f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(item.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(54f + column * 424f, -292f - row * 126f), new Vector2(398f, 104f));
        var dot = FirstLevelUiFactory.CreateImage("Icon", item.transform, accents[i], FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(42f, 0f), new Vector2(22f, 22f));
        var label = FirstLevelUiFactory.CreateText("Label", item.transform, labels[i], 20f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextSecondary);
        FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0f, 0f), new Vector2(0.78f, 1f), new Vector2(0f, 0.5f), new Vector2(72f, 0f), new Vector2(-72f, 0f));
        var value = FirstLevelUiFactory.CreateText("Value", item.transform, "0", 38f, FontStyles.Bold, TextAlignmentOptions.Center, accents[i]);
        FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(0.78f, 0f), Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _behaviorValues[i] = value;
        _revealRows.Add(item.gameObject.AddComponent<CanvasGroup>());
      }
    }

    private void CreateComfortSection()
    {
      var heading = CreateSectionHeading("SELF-REPORTED COMFORT", -558f, new Color32(0x91, 0xB8, 0xD0, 0xFF));
      _revealRows.Add(heading.gameObject.AddComponent<CanvasGroup>());
      _comfortSubtitle = FirstLevelUiFactory.CreateText("Comfort Subtitle", _panel, "LOWER = LESS DISCOMFORT", 18f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(_comfortSubtitle.rectTransform, new Vector2(0f, 1f), new Vector2(0.7f, 1f), new Vector2(0f, 1f), new Vector2(54f, -600f), new Vector2(-54f, 30f));

      var headers = new[] { "BEFORE", "AFTER", "CHANGE" };
      _comfortHeaders = new TextMeshProUGUI[headers.Length];
      for (var i = 0; i < headers.Length; i++)
      {
        var header = FirstLevelUiFactory.CreateText("Comfort " + headers[i], _panel, headers[i], 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted);
        FirstLevelUiFactory.SetRect(header.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(518f + i * 116f, -642f), new Vector2(112f, 28f));
        _comfortHeaders[i] = header;
      }

      var names = new[] { "EYE STRAIN", "DRYNESS", "VISUAL FATIGUE" };
      _comfortRows = new ComfortReportRow[names.Length];
      for (var i = 0; i < names.Length; i++)
      {
        _comfortRows[i] = CreateComfortRow(names[i], -690f - i * 154f);
        _revealRows.Add(_comfortRows[i].Group);
      }

      _comfortUnavailableText = FirstLevelUiFactory.CreateText("Comfort Unavailable", _panel, "COMFORT DATA NOT AVAILABLE", 28f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(_comfortUnavailableText.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -790f), new Vector2(0f, 72f));

      _summaryText = FirstLevelUiFactory.CreateText("Comfort Summary", _panel, string.Empty, 25f, FontStyles.Normal, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_summaryText.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1190f), new Vector2(0f, 76f));
      _revealRows.Add(_summaryText.gameObject.AddComponent<CanvasGroup>());

      _safetyTitle = FirstLevelUiFactory.CreateText("Safety Title", _panel, "HIGH DISCOMFORT REPORTED", 24f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(_safetyTitle.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1270f), new Vector2(0f, 38f));
      _safetyBody = FirstLevelUiFactory.CreateText(
        "Safety Body", _panel,
        "Stop screen use and take a break.\nIf pain or blurred vision persists,\nseek advice from an eye care professional.",
        21f, FontStyles.Normal, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm, true);
      FirstLevelUiFactory.SetRect(_safetyBody.rectTransform, new Vector2(0.08f, 1f), new Vector2(0.92f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -1310f), new Vector2(0f, 102f));
    }

    private void CreateFooter()
    {
      var disclaimer = FirstLevelUiFactory.CreateText(
        "Disclaimer", _panel,
        "This report reflects self-reported comfort,\nnot a medical diagnosis.",
        18f, FontStyles.Normal, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted, true);
      FirstLevelUiFactory.SetRect(disclaimer.rectTransform, new Vector2(0.08f, 0f), new Vector2(0.92f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 154f), new Vector2(0f, 60f));

      var button = FirstLevelUiFactory.CreateButton("Continue", _panel, "CONTINUE", KeepBlinkingTheme.AccentPrimary);
      _continueButtonRect = button.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_continueButtonRect, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 46f), new Vector2(410f, 84f));
      button.onClick.AddListener(RequestContinue);
    }

    private TextMeshProUGUI CreateSectionHeading(string value, float topOffset, Color accent)
    {
      var text = FirstLevelUiFactory.CreateText("Section " + value, _panel, value, 25f, FontStyles.Bold, TextAlignmentOptions.Left, accent);
      FirstLevelUiFactory.SetRect(text.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(54f, topOffset), new Vector2(-108f, 36f));
      return text;
    }

    private ComfortReportRow CreateComfortRow(string labelValue, float topOffset)
    {
      var root = FirstLevelUiFactory.CreateObject("Comfort " + labelValue, _panel);
      var rect = root.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(rect, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(54f, topOffset), new Vector2(-108f, 132f));
      var label = FirstLevelUiFactory.CreateText("Label", root.transform, labelValue, 21f, FontStyles.Bold, TextAlignmentOptions.Left, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0.46f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(0f, 34f));
      var graphObject = FirstLevelUiFactory.CreateObject("Comparison Graph", root.transform);
      var graphRect = graphObject.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(graphRect, new Vector2(0f, 0f), new Vector2(0.46f, 0f), new Vector2(0f, 0f), new Vector2(0f, 18f), new Vector2(0f, 58f));
      var graph = graphObject.AddComponent<ComfortComparisonGraphic>();
      graph.color = KeepBlinkingTheme.TextPrimary;
      graph.raycastTarget = false;
      var before = FirstLevelUiFactory.CreateText("Before", root.transform, "0", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(before.rectTransform, new Vector2(0.52f, 0f), new Vector2(0.64f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var after = FirstLevelUiFactory.CreateText("After", root.transform, "0", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect(after.rectTransform, new Vector2(0.66f, 0f), new Vector2(0.78f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var change = FirstLevelUiFactory.CreateText("Change", root.transform, "0", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(change.rectTransform, new Vector2(0.80f, 0f), Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      return new ComfortReportRow(root.AddComponent<CanvasGroup>(), before, after, change, graph);
    }

    private void Populate(SessionReportData data, StudyFeedbackMode feedbackMode)
    {
      _subjectText.text = "SUBJECT " + data.SubjectId;
      _sessionText.text = $"SESSION {data.SessionIndex:00} / {data.TotalSessions:00}";
      _behaviorValues[0].text = data.SoftBlinkCount.ToString();
      _behaviorValues[1].text = data.ValidRestCycleCount.ToString();
      _behaviorValues[2].text = data.DistanceShiftCount.ToString();
      _behaviorValues[3].text = data.FullLoopCount.ToString();

      var hasComfort = data.PreComfortScores.HasValue && data.PostComfortScores.HasValue;
      var blinded = feedbackMode == StudyFeedbackMode.Blinded;
      _comfortSubtitle.gameObject.SetActive(!blinded && hasComfort);
      for (var i = 0; i < _comfortHeaders.Length; i++)
      {
        _comfortHeaders[i].gameObject.SetActive(!blinded && hasComfort);
      }
      _comfortUnavailableText.gameObject.SetActive(!hasComfort && !blinded);
      for (var i = 0; i < _comfortRows.Length; i++)
      {
        _comfortRows[i].Group.gameObject.SetActive(hasComfort && !blinded);
      }

      if (blinded)
      {
        _comfortUnavailableText.gameObject.SetActive(true);
        _comfortUnavailableText.text = "COMFORT CHECK RECORDED";
        _summaryText.gameObject.SetActive(false);
      }
      else if (!hasComfort)
      {
        _comfortUnavailableText.text = "COMFORT DATA NOT AVAILABLE";
        _summaryText.gameObject.SetActive(false);
      }
      else
      {
        _summaryText.gameObject.SetActive(true);
        var before = data.PreComfortScores.Value;
        var after = data.PostComfortScores.Value;
        SetComfortRow(0, before.EyeStrain, after.EyeStrain);
        SetComfortRow(1, before.Dryness, after.Dryness);
        SetComfortRow(2, before.VisualFatigue, after.VisualFatigue);
        _summaryText.text = GetComfortSummary(before, after);
      }

      var highDiscomfort = data.PostComfortScores.HasValue && IsHighDiscomfort(data.PreComfortScores, data.PostComfortScores.Value);
      _safetyTitle.gameObject.SetActive(highDiscomfort);
      _safetyBody.gameObject.SetActive(highDiscomfort);
    }

    private void SetComfortRow(int index, int before, int after)
    {
      var change = after - before;
      _comfortRows[index].Before.text = before.ToString();
      _comfortRows[index].After.text = after.ToString();
      _comfortRows[index].Change.text = change > 0 ? "+" + change : change.ToString();
      _comfortRows[index].Change.color = change < 0
        ? KeepBlinkingTheme.AccentPrimary
        : change > 0 ? KeepBlinkingTheme.AccentWarm : KeepBlinkingTheme.TextPrimary;
      _comfortRows[index].Graph.SetValues(before, after);
    }

    private static string GetComfortSummary(ComfortScores before, ComfortScores after)
    {
      var changes = new[]
      {
        after.EyeStrain - before.EyeStrain,
        after.Dryness - before.Dryness,
        after.VisualFatigue - before.VisualFatigue,
      };
      var anyLower = Array.Exists(changes, value => value < 0);
      var anyHigher = Array.Exists(changes, value => value > 0);
      if (anyLower && !anyHigher)
      {
        return "You reported less discomfort after this session.";
      }
      if (!anyLower && !anyHigher)
      {
        return "No change was reported after this session.";
      }
      if (!anyLower && anyHigher)
      {
        return "You reported more discomfort after this session.";
      }
      return "Your responses were mixed after this session.";
    }

    private static bool IsHighDiscomfort(ComfortScores? before, ComfortScores after)
    {
      if (after.EyeStrain >= 8 || after.Dryness >= 8 || after.VisualFatigue >= 8)
      {
        return true;
      }
      if (!before.HasValue)
      {
        return false;
      }
      var initial = before.Value;
      return after.EyeStrain - initial.EyeStrain >= 3 ||
             after.Dryness - initial.Dryness >= 3 ||
             after.VisualFatigue - initial.VisualFatigue >= 3;
    }

    private void Update()
    {
      if (!_isOpen)
      {
        return;
      }

      _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, 1f, Time.unscaledDeltaTime * 2.5f);
      _panel.anchoredPosition = Vector2.Lerp(_panel.anchoredPosition, Vector2.zero, 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime));
      _revealElapsed += Time.unscaledDeltaTime;
      for (var i = 0; i < _revealRows.Count; i++)
      {
        var target = _revealElapsed >= 0.2f + i * 0.08f ? 1f : 0f;
        _revealRows[i].alpha = Mathf.MoveTowards(_revealRows[i].alpha, target, Time.unscaledDeltaTime * 6f);
      }
      var stampTarget = _revealElapsed >= 1.05f ? 1f : 0f;
      _stampGroup.alpha = Mathf.MoveTowards(_stampGroup.alpha, stampTarget, Time.unscaledDeltaTime * 5f);
      if (!_stampPlayed && stampTarget > 0f)
      {
        _stampPlayed = true;
        _audioSource.PlayOneShot(_stampClip, 0.14f);
      }

      UpdateContinueGaze(Time.unscaledDeltaTime);
    }

    private void UpdateContinueGaze(float deltaTime)
    {
      if (_gameplay == null || Time.unscaledTime - _openedAt < 0.8f || !_gameplay.HasCurrentGazeInput)
      {
        _continueGazeSeconds = 0f;
        return;
      }
      if (RectTransformUtility.RectangleContainsScreenPoint(_continueButtonRect, _gameplay.CurrentGazeScreenPosition, null))
      {
        _continueGazeSeconds = Mathf.Min(0.5f, _continueGazeSeconds + deltaTime);
      }
      else
      {
        _continueGazeSeconds = 0f;
      }
    }

    private void HandleBlinkInputAccepted()
    {
      if (!_isOpen || _gameplay == null || Time.unscaledTime - _openedAt < 0.8f ||
          _gameplay.AcceptedBlinkSerial <= _openBlinkSerial || _continueGazeSeconds < 0.5f)
      {
        return;
      }
      RequestContinue();
    }

    private void RequestContinue()
    {
      if (_isOpen)
      {
        ContinueRequested?.Invoke();
      }
    }

    private void CreateStampAudio()
    {
      _audioSource = gameObject.AddComponent<AudioSource>();
      _audioSource.playOnAwake = false;
      _audioSource.spatialBlend = 0f;
      const int sampleRate = 44100;
      const float duration = 0.24f;
      var samples = new float[Mathf.CeilToInt(sampleRate * duration)];
      for (var i = 0; i < samples.Length; i++)
      {
        var t = i / (float)sampleRate;
        var envelope = Mathf.Sin(Mathf.PI * i / Mathf.Max(1, samples.Length - 1));
        samples[i] = (Mathf.Sin(Mathf.PI * 2f * 262f * t) + Mathf.Sin(Mathf.PI * 2f * 330f * t)) * envelope * 0.055f;
      }
      _stampClip = AudioClip.Create("Session Report Stamp", samples.Length, 1, sampleRate, false);
      _stampClip.SetData(samples, 0);
    }

    private void OnDestroy()
    {
      if (_gameplay != null)
      {
        _gameplay.BlinkInputAccepted -= HandleBlinkInputAccepted;
      }
      if (_stampClip != null)
      {
        Destroy(_stampClip);
      }
    }

    private readonly struct ComfortReportRow
    {
      internal CanvasGroup Group { get; }
      internal TextMeshProUGUI Before { get; }
      internal TextMeshProUGUI After { get; }
      internal TextMeshProUGUI Change { get; }
      internal ComfortComparisonGraphic Graph { get; }

      internal ComfortReportRow(CanvasGroup group, TextMeshProUGUI before, TextMeshProUGUI after, TextMeshProUGUI change, ComfortComparisonGraphic graph)
      {
        Group = group;
        Before = before;
        After = after;
        Change = change;
        Graph = graph;
      }
    }
  }

  internal sealed class ComfortComparisonGraphic : MaskableGraphic
  {
    private int _before;
    private int _after;

    internal void SetValues(int before, int after)
    {
      _before = Mathf.Clamp(before, 0, 10);
      _after = Mathf.Clamp(after, 0, 10);
      SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      var rect = GetPixelAdjustedRect();
      var left = rect.xMin + 10f;
      var right = rect.xMax - 10f;
      var y = rect.center.y;
      AddLine(vh, new Vector2(left, y), new Vector2(right, y), 2f, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.8f));
      var beforeX = Mathf.Lerp(left, right, _before / 10f);
      var afterX = Mathf.Lerp(left, right, _after / 10f);
      AddLine(vh, new Vector2(beforeX, y), new Vector2(afterX, y), 3f, _after <= _before ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.AccentWarm);
      AddRing(vh, new Vector2(beforeX, y), 7f, KeepBlinkingTheme.TextPrimary);
      AddDisc(vh, new Vector2(afterX, y), 6f, KeepBlinkingTheme.AccentPrimary);
    }

    private static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color color)
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

    private static void AddRing(VertexHelper vh, Vector2 center, float radius, Color color)
    {
      const int segments = 16;
      for (var i = 0; i < segments; i++)
      {
        var a0 = i * Mathf.PI * 2f / segments;
        var a1 = (i + 1) * Mathf.PI * 2f / segments;
        AddLine(vh, center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * radius, center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * radius, 2f, color);
      }
    }

    private static void AddDisc(VertexHelper vh, Vector2 center, float radius, Color color)
    {
      const int segments = 16;
      var centerIndex = vh.currentVertCount;
      vh.AddVert(center, color, new Vector2(0.5f, 0.5f));
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
  }
}

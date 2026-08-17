#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using KeepBlinking.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  internal sealed class CareStationDevelopmentOverlay : MonoBehaviour
  {
    private CareStationController _controller;
    private RectTransform _panel;
    private RectTransform _researchPanel;
    private TextMeshProUGUI _state;
    private TextMeshProUGUI _distanceDiagnostics;

    internal void Bind(CareStationController controller)
    {
      _controller = controller;
      Build();
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Care Station Development Tools", 900, out _, out _);
      _panel = FirstLevelUiFactory.CreateObject("Development Panel", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_panel, new Vector2(0.02f, 0.03f), new Vector2(0.98f, 0.97f), new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
      var bg = FirstLevelUiFactory.CreateImage("Background", _panel, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(bg.rectTransform);
      _state = FirstLevelUiFactory.CreateText("State", _panel, "CARE STATION", 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_state.rectTransform, new Vector2(0.05f, 0.91f), new Vector2(0.95f, 0.99f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _distanceDiagnostics = FirstLevelUiFactory.CreateText(
        "Distance Diagnostics",
        _panel,
        string.Empty,
        11f,
        FontStyles.Normal,
        TextAlignmentOptions.TopLeft,
        KeepBlinkingTheme.TextSecondary,
        false);
      FirstLevelUiFactory.SetRect(_distanceDiagnostics.rectTransform, new Vector2(0.05f, 0.77f), new Vector2(0.95f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      AddButton("Training 1", 0.69f, 0.03f, 0.255f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Training, 0));
      AddButton("Training 2", 0.69f, 0.265f, 0.49f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Training, 1));
      AddButton("Training 3", 0.69f, 0.51f, 0.735f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Training, 2));
      AddButton("Training 4", 0.69f, 0.745f, 0.97f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Training, 3));
      AddButton("Single Recipe", 0.61f, 0.03f, 0.32f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Single));
      AddButton("Double Recipe", 0.61f, 0.355f, 0.645f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Double));
      AddButton("Triple Recipe", 0.61f, 0.68f, 0.97f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Triple));
      AddButton("Complete Action", 0.53f, 0.03f, 0.32f, () => _controller.AdvanceRecipeStepDevelopmentTest());
      AddButton("Advance Recipe", 0.53f, 0.355f, 0.645f, () => _controller.AdvanceRecipeStepDevelopmentTest());
      AddButton("Reset Recipe", 0.53f, 0.68f, 0.97f, () => _controller.ResetRecipeDevelopmentTest());
      AddButton("Reset Training", 0.45f, 0.03f, 0.20f, () => _controller.ResetTrainingProgressDevelopment());
      AddButton("RESEARCH TOOLS", 0.45f, 0.215f, 0.385f, ShowResearchPanel);
      AddButton("FILL STORAGE", 0.45f, 0.40f, 0.575f, () => _controller.FillStorageDevelopment());
      AddButton("FREE ONE SLOT", 0.45f, 0.59f, 0.765f, () => _controller.FreeOneStorageSlotDevelopment());
      AddButton("SIMULATE OFFLINE FULL", 0.45f, 0.78f, 0.97f, () => _controller.SimulateOfflineFullDevelopment());
      AddButton("Screen Down", 0.37f, 0.03f, 0.255f, () => _controller.StartCareActionDevelopmentTest(CareActionType.ScreenDown));
      AddButton("Closed-Eye", 0.37f, 0.265f, 0.49f, () => _controller.StartCareActionDevelopmentTest(CareActionType.ClosedEyeRest));
      AddButton("Focus Shift", 0.37f, 0.51f, 0.735f, () => _controller.StartCareActionDevelopmentTest(CareActionType.FocusShift));
      AddButton("Eye Circles", 0.37f, 0.745f, 0.97f, () => _controller.StartCareActionDevelopmentTest(CareActionType.GuidedEyeCircles));
      AddButton("Pause", 0.29f, 0.03f, 0.255f, () => _controller.PauseCareActionDevelopmentTest());
      AddButton("Resume", 0.29f, 0.265f, 0.49f, () => _controller.ResumeCareActionDevelopmentTest());
      AddButton("Complete Step", 0.29f, 0.51f, 0.735f, () => _controller.CompleteCareActionStepDevelopmentTest());
      AddButton("10x Timer", 0.29f, 0.745f, 0.97f, () => _controller.ToggleDevelopmentCareSpeed());
      AddButton("Eyes Closed", 0.21f, 0.03f, 0.255f, () => _controller.SimulateEyesClosedForDevelopment(true));
      AddButton("Eyes Open", 0.21f, 0.265f, 0.49f, () => _controller.SimulateEyesClosedForDevelopment(false));
      AddButton("Distance 2%", 0.21f, 0.51f, 0.735f, () => _controller.SimulateCurrentDistanceProgressForDevelopment(0f));
      AddButton("Distance 6%", 0.21f, 0.745f, 0.97f, () => _controller.SimulateCurrentDistanceProgressForDevelopment(1f));
      AddButton("Shift 1", 0.13f, 0.03f, 0.255f, () => _controller.JumpToShift(1));
      AddButton("Shift 2", 0.13f, 0.265f, 0.49f, () => _controller.JumpToShift(2));
      AddButton("Shift 3", 0.13f, 0.51f, 0.735f, () => _controller.JumpToShift(3));
      AddButton("Offline 4h", 0.13f, 0.745f, 0.97f, () => _controller.SimulateOffline(TimeSpan.FromHours(4)));
      AddButton("Clear Save", 0.05f, 0.03f, 0.255f, () => _controller.ClearStationSave());
      AddButton("Return Neutral", 0.05f, 0.265f, 0.49f, () => _controller.SimulateNeutralForDevelopment());
      AddButton("Reset Action", 0.05f, 0.51f, 0.735f, () => _controller.ResetCareActionDevelopmentTest());
      AddButton("START NEXT SHIFT", 0.05f, 0.745f, 0.97f, () => _controller.StartNextShiftDevelopment());
      BuildResearchPanel(safe);
      _panel.gameObject.SetActive(false);
    }

    private void BuildResearchPanel(RectTransform safe)
    {
      _researchPanel = FirstLevelUiFactory.CreateObject("Research Development Panel", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_researchPanel, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var bg = FirstLevelUiFactory.CreateImage("Background", _researchPanel, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(bg.rectTransform);
      var title = FirstLevelUiFactory.CreateText("Title", _researchPanel, "RESEARCH TOOLS", 24f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.96f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      AddButtonTo(_researchPanel, "SET PRE SCORES", 0.73f, 0.08f, 0.47f, () => _controller.SetPreScoresDevelopment());
      AddButtonTo(_researchPanel, "SET POST SCORES", 0.73f, 0.53f, 0.92f, () => _controller.SetPostScoresDevelopment());
      AddButtonTo(_researchPanel, "SKIP PRE CHECK", 0.61f, 0.08f, 0.47f, () => _controller.SkipPreCheckDevelopment());
      AddButtonTo(_researchPanel, "SKIP POST CHECK", 0.61f, 0.53f, 0.92f, () => _controller.SkipPostCheckDevelopment());
      AddButtonTo(_researchPanel, "COMPLETE REPORT", 0.49f, 0.08f, 0.47f, () => _controller.CompleteReportDevelopment());
      AddButtonTo(_researchPanel, "EXPORT RESEARCH DATA", 0.49f, 0.53f, 0.92f, () => _controller.ExportResearchDataDevelopment());
      AddButtonTo(_researchPanel, "OPEN RESEARCH FOLDER", 0.37f, 0.08f, 0.47f, () => _controller.OpenResearchFolderDevelopment());
      AddButtonTo(_researchPanel, "CLEAR RESEARCH DATA", 0.37f, 0.53f, 0.92f, () => _controller.ClearResearchDataDevelopment());
      AddButtonTo(_researchPanel, "RECIPE HISTORY", 0.25f, 0.08f, 0.47f, () => _controller.ShowRecipeHistoryDevelopment());
      AddButtonTo(_researchPanel, "BACK", 0.25f, 0.53f, 0.92f, HideResearchPanel);
      AddButtonTo(_researchPanel, "TEST CLOSE CUE", 0.13f, 0.08f, 0.47f, () => _controller.TestCloseCueDevelopment());
      AddButtonTo(_researchPanel, "TEST OPEN CUE", 0.13f, 0.53f, 0.92f, () => _controller.TestOpenCueDevelopment());
      _researchPanel.gameObject.SetActive(false);
    }

    private void ShowResearchPanel()
    {
      _panel.gameObject.SetActive(false);
      _researchPanel.gameObject.SetActive(true);
    }

    private void HideResearchPanel()
    {
      _researchPanel.gameObject.SetActive(false);
      _panel.gameObject.SetActive(true);
    }

    private void AddButton(string label, float y, float minX, float maxX, Action action)
    {
      AddButtonTo(_panel, label, y, minX, maxX, action);
    }

    private static void AddButtonTo(RectTransform parent, string label, float y, float minX, float maxX, Action action)
    {
      var button = FirstLevelUiFactory.CreateButton(label, parent, label, KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)button.transform, new Vector2(minX, y), new Vector2(maxX, y + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      button.onClick.AddListener(() => action?.Invoke());
    }

    private void Update()
    {
      var keyboard = Keyboard.current;
      if (keyboard != null && keyboard.f6Key.wasPressedThisFrame)
      {
        var opening = !_panel.gameObject.activeSelf && !_researchPanel.gameObject.activeSelf;
        _panel.gameObject.SetActive(opening);
        _researchPanel.gameObject.SetActive(false);
      }
      if (keyboard != null)
      {
        if (keyboard.digit1Key.wasPressedThisFrame) _controller.JumpToShift(1);
        if (keyboard.digit2Key.wasPressedThisFrame) _controller.JumpToShift(2);
        if (keyboard.digit3Key.wasPressedThisFrame) _controller.JumpToShift(3);
        if (keyboard.tKey.wasPressedThisFrame) _controller.ToggleDevelopmentCareSpeed();
        if (keyboard.bKey.wasPressedThisFrame)
          _controller.SimulateEyesClosedForDevelopment(!(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed));
        if (keyboard.nKey.wasPressedThisFrame) _controller.SimulateNeutralForDevelopment();
        if (keyboard.pKey.wasPressedThisFrame) _controller.SimulatePushAwayForDevelopment();
        if (keyboard.oKey.wasPressedThisFrame) _controller.SimulateOffline(TimeSpan.FromHours(4));
        if (keyboard.rKey.wasPressedThisFrame) _controller.ClearStationSave();
      }
      if (_panel.gameObject.activeSelf && _controller != null && _controller.SaveData != null)
      {
        _state.text = $"{_controller.State}  -  SHIFT {_controller.SaveData.currentShift}\n{_controller.DevelopmentCareActionStatus}";
        _distanceDiagnostics.text =
          _controller.DevelopmentRecipeStatus + "\n" +
          _controller.DevelopmentReturnDiagnostics + "\n" +
          _controller.DevelopmentCollectionDiagnostics + "\n" +
          CareAudioFeedbackController.EnsureExists().DevelopmentAudioDiagnostics;
      }
    }
  }
}
#endif

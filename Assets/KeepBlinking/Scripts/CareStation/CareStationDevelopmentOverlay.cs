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
    private RectTransform _audioPanel;
    private RectTransform _motionPanel;
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
      AddButton("CARE AUDIO", 0.61f, 0.03f, 0.32f, ShowAudioPanel);
      AddButton("Double Recipe", 0.61f, 0.355f, 0.645f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Double));
      AddButton("Triple Recipe", 0.61f, 0.68f, 0.97f, () => _controller.StartRecipeDevelopmentTest(CareRecipeType.Triple));
      AddButton("Complete Action", 0.53f, 0.03f, 0.32f, () => _controller.AdvanceRecipeStepDevelopmentTest());
      AddButton("Reset Care Intros", 0.53f, 0.355f, 0.645f, () => _controller.ResetCareIntrosDevelopment());
      AddButton("Reset Recipe", 0.53f, 0.68f, 0.97f, () => _controller.ResetRecipeDevelopmentTest());
      AddButton("Reset Training", 0.45f, 0.03f, 0.20f, () => _controller.ResetTrainingProgressDevelopment());
      AddButton("RESEARCH TOOLS", 0.45f, 0.215f, 0.385f, ShowResearchPanel);
      AddButton("FILL STORAGE", 0.45f, 0.40f, 0.575f, () => _controller.FillStorageDevelopment());
      AddButton("FREE ONE SLOT", 0.45f, 0.59f, 0.765f, () => _controller.FreeOneStorageSlotDevelopment());
      AddButton("SIMULATE OFFLINE FULL", 0.45f, 0.78f, 0.97f, () => _controller.SimulateOfflineFullDevelopment());
      AddButton("Focus Shift", 0.37f, 0.03f, 0.255f, () => _controller.StartCareActionDevelopmentTest(CareActionType.FocusShift));
      AddButton("Pilot Routine", 0.37f, 0.265f, 0.49f, () => _controller.StartCareActionDevelopmentTest(CareActionType.PilotEyeRoutine));
      AddButton("Guided Movement", 0.37f, 0.51f, 0.735f, () => _controller.StartCareActionDevelopmentTest(CareActionType.GuidedEyeCircles));
      AddButton("Closed-Eye Rest", 0.37f, 0.745f, 0.97f, () => _controller.StartCareActionDevelopmentTest(CareActionType.ClosedEyeRest));
      AddButton("MOTION TOOLS", 0.29f, 0.03f, 0.255f, ShowMotionPanel);
      AddButton("Resume", 0.29f, 0.265f, 0.49f, () => _controller.ResumeCareActionDevelopmentTest());
      AddButton("Complete Step", 0.29f, 0.51f, 0.735f, () => _controller.CompleteCareActionStepDevelopmentTest());
      AddButton("10x Timer", 0.29f, 0.745f, 0.97f, () => _controller.ToggleDevelopmentCareSpeed());
      AddButton("Eyes Closed", 0.21f, 0.03f, 0.255f, () => _controller.SimulateEyesClosedForDevelopment(true));
      AddButton("Eyes Open", 0.21f, 0.265f, 0.49f, () => _controller.SimulateEyesClosedForDevelopment(false));
      AddButton("Test Rest Music", 0.21f, 0.51f, 0.735f, () => _controller.TestRestMusicDevelopment());
      AddButton("Stop Care Audio", 0.21f, 0.745f, 0.97f, () => _controller.StopAllCareAudioDevelopment());
      AddButton("Shift 1", 0.13f, 0.03f, 0.255f, () => _controller.JumpToShift(1));
      AddButton("Shift 2", 0.13f, 0.265f, 0.49f, () => _controller.JumpToShift(2));
      AddButton("Shift 3", 0.13f, 0.51f, 0.735f, () => _controller.JumpToShift(3));
      AddButton("Offline 4h", 0.13f, 0.745f, 0.97f, () => _controller.SimulateOffline(TimeSpan.FromHours(4)));
      AddButton("DUMP UI INPUT", 0.05f, 0.03f, 0.255f, () => _controller.DumpUiInputDevelopment());
      AddButton("CLEAR STALE UI LOCK", 0.05f, 0.265f, 0.49f, () => _controller.ClearStaleUiLockDevelopment());
      AddButton("Reset Action", 0.05f, 0.51f, 0.735f, () => _controller.ResetCareActionDevelopmentTest());
      AddButton("START NEXT SHIFT", 0.05f, 0.745f, 0.97f, () => _controller.StartNextShiftDevelopment());
      BuildResearchPanel(safe);
      BuildAudioPanel(safe);
      BuildMotionPanel(safe);
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
      AddButtonTo(_researchPanel, "TEST CLOSE CUE", 0.13f, 0.05f, 0.34f, () => _controller.TestCloseCueDevelopment());
      AddButtonTo(_researchPanel, "TEST OPEN CUE", 0.13f, 0.355f, 0.645f, () => _controller.TestOpenCueDevelopment());
      AddButtonTo(_researchPanel, "TEST GUIDED OPEN", 0.13f, 0.66f, 0.95f, () => _controller.TestGuidedOpenCueDevelopment());
      AddButtonTo(_researchPanel, "ADD 1 GOLD", 0.02f, 0.02f, 0.245f, () => _controller.AddOneGoldDevelopment());
      AddButtonTo(_researchPanel, "FREE 4 STORAGE SLOTS", 0.02f, 0.255f, 0.49f, () => _controller.FreeFourStorageSlotsDevelopment());
      AddButtonTo(_researchPanel, "FORCE UPGRADE CHECK", 0.02f, 0.51f, 0.745f, () => _controller.ForceUpgradeCheckDevelopment());
      AddButtonTo(_researchPanel, "TEST NO-AFFORDABLE-UPGRADE", 0.02f, 0.755f, 0.98f, () => _controller.TestNoAffordableUpgradeDevelopment());
      _researchPanel.gameObject.SetActive(false);
    }

    private void ShowResearchPanel()
    {
      _panel.gameObject.SetActive(false);
      _researchPanel.gameObject.SetActive(true);
      _audioPanel.gameObject.SetActive(false);
      _motionPanel.gameObject.SetActive(false);
      _motionPanel.gameObject.SetActive(false);
    }

    private void HideResearchPanel()
    {
      _researchPanel.gameObject.SetActive(false);
      _panel.gameObject.SetActive(true);
    }

    private void BuildAudioPanel(RectTransform safe)
    {
      _audioPanel = FirstLevelUiFactory.CreateObject("Care Audio Development Panel", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_audioPanel, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var bg = FirstLevelUiFactory.CreateImage("Background", _audioPanel, KeepBlinkingTheme.SurfaceOverlay,
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(bg.rectTransform);
      var title = FirstLevelUiFactory.CreateText("Title", _audioPanel, "CARE AUDIO", 24f, FontStyles.Bold,
        TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.96f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      AddButtonTo(_audioPanel, "FOCUS AMBIENCE", 0.73f, 0.05f, 0.47f,
        () => _controller.TestActionAmbienceDevelopment(CareActionType.FocusShift));
      AddButtonTo(_audioPanel, "PILOT AMBIENCE", 0.73f, 0.53f, 0.95f,
        () => _controller.TestActionAmbienceDevelopment(CareActionType.PilotEyeRoutine));
      AddButtonTo(_audioPanel, "GUIDED AMBIENCE", 0.66f, 0.05f, 0.47f,
        () => _controller.TestActionAmbienceDevelopment(CareActionType.GuidedEyeCircles));
      AddButtonTo(_audioPanel, "REST AMBIENCE", 0.66f, 0.53f, 0.95f,
        () => _controller.TestActionAmbienceDevelopment(CareActionType.ClosedEyeRest));
      AddButtonTo(_audioPanel, "TEST BENEFIT VOICE", 0.59f, 0.05f, 0.47f, () => _controller.TestBenefitVoiceDevelopment());
      AddButtonTo(_audioPanel, "TEST ALMOST COMPLETE", 0.59f, 0.53f, 0.95f, () => _controller.TestAlmostCompleteVoiceDevelopment());
      AddButtonTo(_audioPanel, "TEST VOICE DUCKING", 0.45f, 0.05f, 0.47f, () => _controller.TestVoiceDuckingDevelopment());
      AddButtonTo(_audioPanel, "TEST PILOT COMPLETE", 0.45f, 0.53f, 0.95f, () => _controller.TestPilotCompletionDevelopment());
      AddButtonTo(_audioPanel, "TEST GUIDED OPEN", 0.31f, 0.05f, 0.47f, () => _controller.TestGuidedOpenCueDevelopment());
      AddButtonTo(_audioPanel, "TEST REST OPEN", 0.31f, 0.53f, 0.95f, () => _controller.TestOpenCueDevelopment());
      AddButtonTo(_audioPanel, "STOP ALL CARE AUDIO", 0.17f, 0.05f, 0.47f, () => _controller.StopAllCareAudioDevelopment());
      AddButtonTo(_audioPanel, "BACK", 0.17f, 0.53f, 0.95f, HideAudioPanel);
      _audioPanel.gameObject.SetActive(false);
    }

    private void ShowAudioPanel()
    {
      _panel.gameObject.SetActive(false);
      _researchPanel.gameObject.SetActive(false);
      _audioPanel.gameObject.SetActive(true);
      _motionPanel.gameObject.SetActive(false);
    }

    private void HideAudioPanel()
    {
      _audioPanel.gameObject.SetActive(false);
      _panel.gameObject.SetActive(true);
    }

    private void BuildMotionPanel(RectTransform safe)
    {
      _motionPanel = FirstLevelUiFactory.CreateObject("Care Motion Development Panel", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_motionPanel, new Vector2(0.04f, 0.08f), new Vector2(0.96f, 0.92f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var bg = FirstLevelUiFactory.CreateImage("Background", _motionPanel, KeepBlinkingTheme.SurfaceOverlay,
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(bg.rectTransform);
      var title = FirstLevelUiFactory.CreateText("Title", _motionPanel, "GUIDED / PILOT", 24f,
        FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.08f, 0.86f), new Vector2(0.92f, 0.96f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      AddButtonTo(_motionPanel, "FULLSCREEN PILOT", 0.73f, 0.03f, 0.32f,
        () => RunMotionPreview(_controller.PreviewFullscreenPilotDevelopment));
      AddButtonTo(_motionPanel, "PILOT > GUIDED", 0.73f, 0.355f, 0.645f,
        () => RunMotionPreview(_controller.PreviewPilotToGuidedTransitionDevelopment));
      AddButtonTo(_motionPanel, "TOGGLE STATION HUD", 0.73f, 0.68f, 0.97f,
        () => RunMotionPreview(_controller.ToggleStationHudDuringGuidanceDevelopment));
      AddButtonTo(_motionPanel, "ADJUST GUIDE SIZE", 0.59f, 0.03f, 0.32f,
        () => RunMotionPreview(_controller.AdjustPilotAxisRangeDevelopment));
      AddButtonTo(_motionPanel, "ADJUST WORKER SIZE", 0.59f, 0.355f, 0.645f,
        () => RunMotionPreview(_controller.AdjustGuidanceWorkerSizeDevelopment));
      AddButtonTo(_motionPanel, "ADJUST EYE SIZE", 0.59f, 0.68f, 0.97f,
        () => RunMotionPreview(_controller.AdjustGuidanceEyeSizeDevelopment));
      AddButtonTo(_motionPanel, "ADJUST PUPIL RANGE", 0.45f, 0.03f, 0.32f,
        () => RunMotionPreview(_controller.AdjustPilotPupilRangeDevelopment));
      AddButtonTo(_motionPanel, "SHOW SAFE AREA", 0.45f, 0.355f, 0.645f,
        () => RunMotionPreview(_controller.ToggleGuidanceSafeAreaDevelopment));
      AddButtonTo(_motionPanel, "CAPTURE PILOT LAYOUT", 0.45f, 0.68f, 0.97f,
        () => RunMotionPreview(_controller.CapturePilotLayoutDevelopment));
      AddButtonTo(_motionPanel, "TEST VERTICAL", 0.31f, 0.03f, 0.32f,
        () => RunMotionPreview(() => _controller.PreviewPilotAxisDevelopment(0)));
      AddButtonTo(_motionPanel, "TEST HORIZONTAL", 0.31f, 0.355f, 0.645f,
        () => RunMotionPreview(() => _controller.PreviewPilotAxisDevelopment(1)));
      AddButtonTo(_motionPanel, "TEST DIAGONAL A", 0.31f, 0.68f, 0.97f,
        () => RunMotionPreview(() => _controller.PreviewPilotAxisDevelopment(2)));
      AddButtonTo(_motionPanel, "TEST DIAGONAL B", 0.17f, 0.03f, 0.32f,
        () => RunMotionPreview(() => _controller.PreviewPilotAxisDevelopment(3)));
      AddButtonTo(_motionPanel, "TEST CLOCKWISE", 0.17f, 0.355f, 0.645f,
        () => RunMotionPreview(() => _controller.PreviewGuidedDirectionDevelopment(false)));
      AddButtonTo(_motionPanel, "TEST COUNTERCLOCKWISE", 0.17f, 0.68f, 0.97f,
        () => RunMotionPreview(() => _controller.PreviewGuidedDirectionDevelopment(true)));
      AddButtonTo(_motionPanel, "BACK", 0.03f, 0.05f, 0.95f, HideMotionPanel);
      _motionPanel.gameObject.SetActive(false);
    }

    private void RunMotionPreview(Action preview)
    {
      HideMotionPanel();
      _panel.gameObject.SetActive(false);
      preview?.Invoke();
    }

    private void ShowMotionPanel()
    {
      _panel.gameObject.SetActive(false);
      _researchPanel.gameObject.SetActive(false);
      _audioPanel.gameObject.SetActive(false);
      _motionPanel.gameObject.SetActive(true);
    }

    private void HideMotionPanel()
    {
      _motionPanel.gameObject.SetActive(false);
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
        var opening = !_panel.gameObject.activeSelf && !_researchPanel.gameObject.activeSelf &&
                      !_audioPanel.gameObject.activeSelf && !_motionPanel.gameObject.activeSelf;
        _panel.gameObject.SetActive(opening);
        _researchPanel.gameObject.SetActive(false);
        _audioPanel.gameObject.SetActive(false);
        _motionPanel.gameObject.SetActive(false);
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
      }
      if (_panel.gameObject.activeSelf && _controller != null && _controller.SaveData != null)
      {
        _state.text = $"{_controller.State}  -  SHIFT {_controller.SaveData.currentShift}\n{_controller.DevelopmentCareActionStatus}";
        _distanceDiagnostics.text =
          _controller.DevelopmentRecipeStatus + "\n" +
          _controller.DevelopmentReturnDiagnostics + "\n" +
          _controller.DevelopmentCollectionDiagnostics + "\n" +
          CareAudioFeedbackController.EnsureExists().DevelopmentAudioDiagnostics + "\n" +
          $"Voice playing: {CareVoiceService.EnsureExists().LastSpokenText}\n" +
          $"Voice requested: {CareVoiceService.EnsureExists().LastRequestedText}";
      }
    }
  }
}
#endif

using System;
using System.Collections.Generic;
using KeepBlinking.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationView : MonoBehaviour
  {
    private readonly List<CareCrewPlaceholderView> _crew = new List<CareCrewPlaceholderView>(5);
    private readonly List<RectTransform> _carts = new List<RectTransform>(5);
    private readonly List<Image> _xpVisuals = new List<Image>(24);
    private readonly List<Image> _dustGroups = new List<Image>(3);
    private readonly List<Image> _dryCracks = new List<Image>(3);
    private readonly Dictionary<CareStationUpgradeId, Button> _upgradeButtons = new Dictionary<CareStationUpgradeId, Button>(3);
    private readonly Dictionary<CareStationUpgradeId, TextMeshProUGUI> _upgradeCardTexts = new Dictionary<CareStationUpgradeId, TextMeshProUGUI>(3);
    private RectTransform _safe;
    private RectTransform _content;
    private RectTransform _stationStage;
    private RectTransform _transportRoot;
    private CanvasGroup _group;
    private RectTransform _incidentRoot;
    private RectTransform _incidentHitRect;
    private Image _incidentCore;
    private Image _incidentRing;
    private TextMeshProUGUI _incidentLabel;
    private RectTransform _actionRoot;
    private CanvasGroup _actionGroup;
    private TextMeshProUGUI _actionPrompt;
    private TextMeshProUGUI _recipeTitle;
    private TextMeshProUGUI _recipeStepText;
    private Image _actionProgress;
    private Image _actionVisualRing;
    private Image _distanceCoreFill;
    private Image _distanceWave;
    private readonly List<Image> _distanceStepLights = new List<Image>(4);
    private readonly List<RectTransform> _distanceGuideDots = new List<RectTransform>(4);
    private readonly List<Image> _recipeStepDots = new List<Image>(3);
    private readonly List<Image> _routineDockDots = new List<Image>(4);
    private readonly List<TextMeshProUGUI> _routineDockLabels = new List<TextMeshProUGUI>(4);
    private readonly List<Image> _navigationTabs = new List<Image>(3);
    private readonly List<TextMeshProUGUI> _navigationLabels = new List<TextMeshProUGUI>(3);
    private readonly List<Image> _stationTracks = new List<Image>(3);
    private readonly List<Image> _pressLayers = new List<Image>(2);
    private readonly Vector2[] _productionRoute =
    {
      new Vector2(0.12f, 0.29f),
      new Vector2(0.22f, 0.75f),
      new Vector2(0.78f, 0.74f),
      new Vector2(0.50f, 0.82f),
      new Vector2(0.82f, 0.12f),
      new Vector2(0.14f, 0.12f),
    };
    private RectTransform _guidedOrbitDot;
    private RectTransform _routineDock;
    private TextMeshProUGUI _routineDockTitle;
    private TextMeshProUGUI _routinePrimaryText;
    private RectTransform _navigationRoot;
    private RectTransform _productionBottle;
    private Vector2 _productionCartHome;
    private Image _careDimmer;
    private RectTransform _phoneIcon;
    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _xpReady;
    private TextMeshProUGUI _stationText;
    private TextMeshProUGUI _fullBottleText;
    private TextMeshProUGUI _goldBottleText;
    private TextMeshProUGUI _storageText;
    private Image _storageFill;
    private RectTransform _welcomeRoot;
    private TextMeshProUGUI _welcomeLines;
    private TextMeshProUGUI _welcomeTitle;
    private RectTransform _upgradeRoot;
    private TextMeshProUGUI _upgradeTitle;
    private RectTransform _completeRoot;
    private TextMeshProUGUI _completeText;
    private readonly List<Image> _completeStepIcons = new List<Image>(3);
    private RectTransform _surveyRoot;
    private TextMeshProUGUI _surveyTitle;
    private readonly List<TextMeshProUGUI> _surveyValues = new List<TextMeshProUGUI>(4);
    private Button _surveyContinueButton;
    private bool _surveyIsPost;
    private CareSubjectiveScores _surveyDraft = new CareSubjectiveScores();
    private RectTransform _reportRoot;
    private TextMeshProUGUI _reportText;
    private readonly List<Image> _reportStepIcons = new List<Image>(4);
    private Button _endShiftButton;
    private Button _changeStepButton;
    private RectTransform _changeStepConfirmRoot;
    private Button _fallbackButton;
    private Button _returnFallbackButton;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private Button _skipCareStepButton;
#endif
    private RectTransform _storageTank;
    private RectTransform _cart;
    private RectTransform _distanceSafetyRoot;
    private RectTransform _restIcon;
    private Image _filterBody;
    private Image _tankBody;
    private Image _tankLevel;
    private Image _careCoreInner;
    private CareStationSaveData _stationSave;
    private int _pendingBottleValue;
    private int _pendingGoldBottleCount;
    private float _repairPulseUntil;
    private bool _incidentSelectable;
    private bool _touchConsumed;
    private CareActionType _renderedCareActionType;
    private CareActionInternalPhase _renderedCareActionPhase;
    private float _actionStepPulseUntil;
    private float _pipelinePulseUntil;
    private int _pipelineMask;
    private Vector3 _storageBaseScale = Vector3.one;
    private bool _storageFull;
    private bool _productionAnimating;
    private float _productionAnimationStartedAt;

    public event Action IncidentSelected;
    public event Action ContinueSelected;
    public event Action FallbackCollectSelected;
    public event Action ReturnFallbackSelected;
    public event Action ChangeStepSelected;
    public event Action UseRestSelected;
    public event Action KeepStepSelected;
    public event Action EndShiftSelected;
    public event Action<bool, CareSubjectiveScores> SubjectiveScoresChanged;
    public event Action<bool, CareSubjectiveScores> SubjectiveScoresSubmitted;
    public event Action<bool> SubjectiveScoresSkipped;
    public event Action CareReportDoneSelected;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public event Action SkipCareStepSelected;
#endif
    public event Action<CareStationUpgradeId> UpgradeSelected;

    public void Build()
    {
      if (_safe != null) return;
      _safe = FirstLevelUiFactory.CreateCanvas(transform, "Eye Care Station Canvas", 500, out _, out _group);
      var background = FirstLevelUiFactory.CreateImage("Station Backdrop", _safe, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.08f));
      FirstLevelUiFactory.Stretch(background.rectTransform);

      _content = FirstLevelUiFactory.CreateObject("Comfort Padded Content", _safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_content, new Vector2(28f, 34f), new Vector2(-28f, -42f));

      BuildHud();
      BuildStationStage();
      BuildStorage();
      BuildCrew();
      BuildCareRoutineDock();
      BuildNavigation();
      BuildIncident();
      _careDimmer = FirstLevelUiFactory.CreateImage("Care Dimmer", _content, Color.clear);
      FirstLevelUiFactory.Stretch(_careDimmer.rectTransform);
      BuildActionOverlay();
      BuildChangeStepConfirmation();
      BuildWelcome();
      BuildUpgrade();
      BuildSubjectiveCheck();
      BuildCareReport();
      BuildComplete();
      BuildDistanceSafetyWarning();
      HideAllModals();
    }

    public void ApplyStation(CareStationSaveData save)
    {
      if (save == null) return;
      _stationSave = save;
      for (var i = 0; i < _crew.Count; i++) _crew[i].gameObject.SetActive(i < save.crewCount);
      var constructionScale = 1f + Mathf.Min(3, save.stationConstructionState) * 0.04f;
      var storageScale = save.storageLevel == 2 ? new Vector3(1.16f, 1.08f, 1f)
        : save.storageLevel == 3 ? new Vector3(1.34f, 1.14f, 1f)
        : save.storageLevel >= 4 ? new Vector3(1.54f, 1.22f, 1f) : Vector3.one;
      _storageBaseScale = Vector3.Scale(storageScale, new Vector3(constructionScale, constructionScale, 1f));
      _storageTank.localScale = _storageBaseScale;
      var extraTank = _storageTank.Find("Extra Container");
      if (extraTank != null) extraTank.gameObject.SetActive(save.storageLevel >= 2);
      var tierThree = _storageTank.Find("Tier 3 Container");
      if (tierThree != null) tierThree.gameObject.SetActive(save.storageLevel >= 3);
      var tierFour = _storageTank.Find("Tier 4 Container");
      if (tierFour != null) tierFour.gameObject.SetActive(save.storageLevel >= 4);
      var cartScale = CartScale(save.cartCapacity);
      for (var i = 0; i < _carts.Count; i++)
      {
        _carts[i].gameObject.SetActive(i < CareStationShiftRules.ConcurrentCartCount(save));
        _carts[i].localScale = cartScale;
      }
      RefreshResourceHud();
    }

    public void SetCrewState(CareCrewState state)
    {
      for (var i = 0; i < _crew.Count; i++)
        if (_crew[i].gameObject.activeSelf) _crew[i].SetState(state);
    }

    public void ShowWelcome(CareStationOfflineResult result)
    {
      HideAllModals();
      _welcomeRoot.gameObject.SetActive(true);
      if (_welcomeTitle != null) _welcomeTitle.text = "WHILE YOU WERE AWAY";
      var lines = new List<string>(5);
      if (result.ExperienceMade > 0) lines.Add($"+{result.ExperienceMade} FULL BOTTLES");
      var gold = _stationSave != null ? Mathf.Max(0, _stationSave.lastOfflineStoredGoldBottles) : 0;
      if (gold > 0) lines.Add($"+{gold} GOLD {(gold == 1 ? "BOTTLE" : "BOTTLES")}");
      if (result.CreditedDuration > TimeSpan.Zero)
        lines.Add($"{(int)result.CreditedDuration.TotalHours}H {result.CreditedDuration.Minutes:D2}M WORKED");
      if (_stationSave != null && _stationSave.offlineProductionPausedByFullStorage)
      {
        lines.Add("STORAGE FULL");
        lines.Add("PRODUCTION PAUSED");
      }
      _welcomeLines.text = string.Join("\n", lines);
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
    }

    public void ShowIncident(CareStationIncidentType incident, bool selectable)
    {
      HideAllModals();
      _incidentRoot.gameObject.SetActive(true);
      _incidentSelectable = selectable;
      _incidentLabel.text = incident == CareStationIncidentType.DrySpot ? "DRY SPOT" : incident == CareStationIncidentType.EyeGunk ? "EYE GUNK" : "DUST";
      var color = incident == CareStationIncidentType.DrySpot ? KeepBlinkingTheme.AccentWarm : KeepBlinkingTheme.TextPrimary;
      _incidentCore.color = KeepBlinkingTheme.WithAlpha(color, incident == CareStationIncidentType.EyeGunk ? 0.82f : 0.35f);
      _incidentRing.color = KeepBlinkingTheme.WithAlpha(color, selectable ? 0.68f : 0.25f);
      _incidentCore.rectTransform.sizeDelta = incident == CareStationIncidentType.EyeGunk ? new Vector2(176f, 112f) : new Vector2(145f, 145f);
      for (var i = 0; i < _dustGroups.Count; i++) _dustGroups[i].gameObject.SetActive(incident == CareStationIncidentType.Dust);
      for (var i = 0; i < _dryCracks.Count; i++) _dryCracks[i].gameObject.SetActive(incident == CareStationIncidentType.DrySpot);
      _statusText.text = selectable ? "TAP TO HELP" : string.Empty;
      SetRoutinePrimary(selectable ? "START CARE" : "CARE NEEDED");
      SetProductionAnimation(false);
    }

    public void ShowStationWorking()
    {
      HideAllModals();
      _statusText.text = "STATION WORKING";
      SetCrewState(CareCrewState.Work);
      SetRoutinePrimary("START CARE");
      SetProductionAnimation(true);
    }

    public void ShowInspectionIntro(CareStationSaveData save)
    {
      HideAllModals();
      ApplyStation(save);
      _statusText.text = "STATION INSPECTION\nCOMPLETE ALL SYSTEM CHECKS";
      ConfigureInspection(save);
      SetCrewState(CareCrewState.Rest);
      SetRoutinePrimary("START CARE");
      SetProductionAnimation(false);
    }

    public void ConfigureInspection(CareStationSaveData save)
    {
      if (save == null) return;
      var check = Mathf.Clamp(save.inspectionCurrentCheck, 0, 3);
      if (_recipeTitle != null) _recipeTitle.text = "STATION INSPECTION";
      if (_recipeStepText != null) _recipeStepText.text = check <= 0
        ? "FILTER CHECK"
        : check == 1 ? "FLOW CHECK" : "CORE CHECK";
      for (var i = 0; i < _recipeStepDots.Count; i++)
      {
        _recipeStepDots[i].gameObject.SetActive(true);
        var bit = 1 << i;
        _recipeStepDots[i].color = (save.inspectionCompletedMask & bit) != 0
          ? KeepBlinkingTheme.AccentPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f);
      }
      RefreshRoutineDock(save.currentRecipe);
      if (_filterBody != null) _filterBody.color = (save.inspectionCompletedMask & CareStationInspectionRules.FilterCheck) != 0
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.22f);
      if (_tankBody != null) _tankBody.color = (save.inspectionCompletedMask & CareStationInspectionRules.FlowCheck) != 0
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.22f);
      var coreComplete = (save.inspectionCompletedMask & CareStationInspectionRules.CoreCheck) != 0;
      for (var i = 0; i < _pressLayers.Count; i++)
        _pressLayers[i].color = coreComplete
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.22f);
      if (_careCoreInner != null) _careCoreInner.color = coreComplete
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.64f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.96f);
    }

    public void ShowInspectionPassed(CareStationSaveData save)
    {
      HideAllModals();
      ApplyStation(save);
      ConfigureInspection(save);
      _statusText.text = "INSPECTION PASSED\nSTATION LEVEL 2";
      _repairPulseUntil = Time.unscaledTime + 1.4f;
      SetCrewState(CareCrewState.Cheer);
      SetRoutinePrimary("CONTINUE");
    }

    public void ShowBottleProduction(int value, int goldBottleCount)
    {
      HideAllModals();
      SetPendingXp(value, goldBottleCount);
      _statusText.text = "BOTTLES READY";
      SetCrewState(CareCrewState.Work);
      SetRoutinePrimary("SEND BOTTLES");
      SetProductionAnimation(false);
    }

    public void ShowOfflineBottles(int value, bool includesShiftSupply = false)
    {
      HideAllModals();
      SetPendingXp(value);
      _statusText.text = value > 0 ? "BOTTLES READY" : string.Empty;
      SetCrewState(CareCrewState.Carry);
      SetRoutinePrimary("START CARE");
      SetProductionAnimation(false);
    }

    public void ShowAction(string prompt, float progress, bool dimmed, string status = "")
    {
      _renderedCareActionType = CareActionType.None;
      _welcomeRoot.gameObject.SetActive(false);
      _upgradeRoot.gameObject.SetActive(false);
      _completeRoot.gameObject.SetActive(false);
      _incidentRoot.gameObject.SetActive(false);
      _actionRoot.gameObject.SetActive(true);
      _actionPrompt.text = ResolveActionLabel(prompt, status);
      SetRoutinePrimary(_actionPrompt.text);
      SetProductionAnimation(false);
      _statusText.text = string.Empty;
      _actionProgress.fillAmount = Mathf.Clamp01(progress);
      _actionGroup.alpha = dimmed ? 0.48f : 1f;
      _careDimmer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackdropClosedEye, dimmed ? 0.52f : 0f);
      _phoneIcon.gameObject.SetActive(prompt == "SCREEN DOWN" || prompt == "SEND BOTTLES");
      _restIcon.gameObject.SetActive(prompt == "REST" || prompt == "OPEN YOUR EYES" || status == "CLOSE YOUR EYES");
    }

    public void ConfigureRecipe(CareRecipeSaveData recipe)
    {
      if (_recipeTitle == null || _recipeStepText == null) return;
      if (recipe == null || recipe.ActionCount <= 0)
      {
        _recipeTitle.text = string.Empty;
        _recipeStepText.text = string.Empty;
        for (var i = 0; i < _recipeStepDots.Count; i++) _recipeStepDots[i].gameObject.SetActive(false);
        RefreshRoutineDock(null);
        return;
      }

      var training = CareRecipeGenerator.TrainingIndex(recipe);
      _recipeTitle.text = recipe.recipeType == CareRecipeType.Training && training >= 0
        ? $"TRAINING {training + 1} / 4"
        : "CARE ROUTINE";
      var visibleStep = recipe.recipeCompleted
        ? recipe.ActionCount
        : Mathf.Clamp(recipe.currentActionIndex + 1, 1, recipe.ActionCount);
      _recipeStepText.text = $"STEP {visibleStep} / {recipe.ActionCount}";
      for (var i = 0; i < _recipeStepDots.Count; i++)
      {
        var dot = _recipeStepDots[i];
        dot.gameObject.SetActive(i < recipe.ActionCount);
        if (i >= recipe.ActionCount) continue;
        dot.color = recipe.IsStepCompleted(i)
          ? KeepBlinkingTheme.AccentPrimary
          : i == recipe.currentActionIndex && !recipe.recipeCompleted
            ? KeepBlinkingTheme.TextPrimary
            : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f);
      }
      RefreshRoutineDock(recipe);
    }

    public void PlayRecipePipelineStep(int completedStepIndex, int actionCount)
    {
      _pipelineMask |= CareRecipePipeline.StageMaskForCompletion(completedStepIndex, actionCount);
      _pipelinePulseUntil = Time.unscaledTime + 0.55f;
      ApplyPipelineVisuals();
    }

    public void RestoreRecipePipeline(CareRecipeSaveData recipe)
    {
      _pipelineMask = 0;
      if (recipe != null)
      {
        for (var index = 0; index < recipe.ActionCount; index++)
          if (recipe.IsStepCompleted(index))
            _pipelineMask |= CareRecipePipeline.StageMaskForCompletion(index, recipe.ActionCount);
      }
      ApplyPipelineVisuals();
    }

    public void ShowRecipeStepFeedback(CareRecipeSaveData recipe)
    {
      _actionRoot.gameObject.SetActive(false);
      _incidentRoot.gameObject.SetActive(false);
      _careDimmer.color = Color.clear;
      _statusText.text = "CARE ROUTINE";
      SetCrewState(CareCrewState.Work);
      ConfigureRecipe(recipe);
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
    }

    private void ApplyPipelineVisuals()
    {
      var off = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.34f);
      var on = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.78f);
      if (_filterBody != null) _filterBody.color = (_pipelineMask & CareRecipePipeline.Filter) != 0 ? on : off;
      if (_tankBody != null) _tankBody.color = (_pipelineMask & CareRecipePipeline.Tank) != 0 ? on : off;
      if (_tankLevel != null) _tankLevel.color = (_pipelineMask & CareRecipePipeline.Tank) != 0
        ? KeepBlinkingTheme.AccentPrimary
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.28f);
      for (var i = 0; i < _pressLayers.Count; i++)
        _pressLayers[i].color = (_pipelineMask & CareRecipePipeline.Press) != 0
          ? on
          : KeepBlinkingTheme.WithAlpha(i == 0 ? KeepBlinkingTheme.TextSecondary : KeepBlinkingTheme.AccentSoft, 0.36f);
      if (_careCoreInner != null) _careCoreInner.color = (_pipelineMask & CareRecipePipeline.Press) != 0
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.42f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.96f);
      for (var i = 0; i < _stationTracks.Count; i++)
      {
        var bit = i == 0 ? CareRecipePipeline.Filter : i == 1 ? CareRecipePipeline.Tank : CareRecipePipeline.Press;
        _stationTracks[i].color = (_pipelineMask & bit) != 0
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.68f)
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.22f);
      }
    }

    public void ShowScreenDownDemo(bool showText)
    {
      ShowAction("SCREEN DOWN", 0f, false);
      if (!showText) _actionPrompt.text = string.Empty;
    }

    public void SetActionProgress(float progress, string status = null)
    {
      _actionProgress.fillAmount = Mathf.Clamp01(progress);
      if (status != null)
      {
        _actionPrompt.text = status;
        _statusText.text = string.Empty;
      }
    }

    public void RenderCareAction(
      CareActionType type,
      CareActionInternalPhase phase,
      string prompt,
      float progress,
      float distanceRatio,
      float directionProgress,
      CareDistanceDirection direction,
      int completedDistanceSteps)
    {
      ShowAction(prompt, progress, type == CareActionType.ClosedEyeRest ||
                                  (type == CareActionType.GuidedEyeCircles &&
                                   (int)phase >= (int)CareActionInternalPhase.GuidedPromptClose));
      if (type == CareActionType.FocusShift && _renderedCareActionPhase != phase)
        _actionStepPulseUntil = Time.unscaledTime + 0.35f;
      _renderedCareActionType = type;
      _renderedCareActionPhase = phase;
      _phoneIcon.gameObject.SetActive(type == CareActionType.ScreenDown);
      _restIcon.gameObject.SetActive(type == CareActionType.ClosedEyeRest || type == CareActionType.GuidedEyeCircles);
      if (_guidedOrbitDot != null)
      {
        var preview = type == CareActionType.GuidedEyeCircles &&
                      (phase == CareActionInternalPhase.GuidedPreviewClockwise ||
                       phase == CareActionInternalPhase.GuidedPreviewCounterClockwise);
        _guidedOrbitDot.gameObject.SetActive(preview);
        if (preview)
        {
          var orbitDirection = phase == CareActionInternalPhase.GuidedPreviewClockwise ? -1f : 1f;
          var angle = orbitDirection * Time.unscaledTime * Mathf.PI * 0.5f;
          _guidedOrbitDot.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 96f;
        }
      }
      if (_actionVisualRing != null)
      {
        var scale = type == CareActionType.FocusShift
          ? direction == CareDistanceDirection.Closer
            ? Mathf.Lerp(1f, 1.28f, directionProgress)
            : Mathf.Lerp(1f, 0.78f, directionProgress)
          : 1f;
        _actionVisualRing.rectTransform.localScale = Vector3.one * scale;
        if (_actionStepPulseUntil > Time.unscaledTime)
          _actionVisualRing.rectTransform.localScale *= Mathf.Lerp(1.12f, 1f, 1f - (_actionStepPulseUntil - Time.unscaledTime) / 0.35f);
        _actionVisualRing.color = KeepBlinkingTheme.WithAlpha(
          type == CareActionType.FocusShift ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.BorderReadable,
          type == CareActionType.FocusShift ? 0.58f : 0.35f);
      }
      RenderDistanceFeedback(
        type == CareActionType.FocusShift,
        direction,
        directionProgress,
        completedDistanceSteps);
    }

    public void ShowDistanceCollection(
      int pendingBottleValue,
      CareDistanceDirection direction,
      float progress,
      string distanceState,
      bool fallbackAvailable,
      string promptOverride = null)
    {
      var prompt = !string.IsNullOrEmpty(promptOverride)
        ? promptOverride
        : distanceState == "SENSOR UNAVAILABLE"
        ? "SENSOR UNAVAILABLE"
        : distanceState == "TRACKING LOST"
          ? "TRACKING LOST"
          : direction == CareDistanceDirection.Closer ? "MOVE CLOSER" : "MOVE AWAY";
      ShowAction(prompt, progress, false, string.Empty);
      _phoneIcon.gameObject.SetActive(true);
      _fallbackButton.gameObject.SetActive(false);
      _returnFallbackButton.gameObject.SetActive(fallbackAvailable);
      _actionProgress.fillAmount = Mathf.Clamp01(progress);
      RenderDistanceFeedback(true, direction, progress, -1);

      if (_cart != null)
      {
        var baseScale = CartScale(_stationSave != null ? _stationSave.cartCapacity : 4);
        var response = direction == CareDistanceDirection.Away
          ? Mathf.Lerp(1f, 1.1f, progress)
          : Mathf.Lerp(1f, 1.04f, progress);
        _cart.localScale = Vector3.Scale(baseScale, new Vector3(response, response, 1f));
      }
      for (var i = 0; i < _xpVisuals.Count; i++)
      {
        if (!_xpVisuals[i].gameObject.activeSelf) continue;
        var bottleResponse = direction == CareDistanceDirection.Away
          ? Mathf.Lerp(1f, 1.12f, progress)
          : 1f;
        _xpVisuals[i].rectTransform.localScale = Vector3.one * bottleResponse;
      }
      if (pendingBottleValue > 0) _statusText.text = $"BOTTLES READY  {pendingBottleValue}";
    }

    private void RenderDistanceFeedback(
      bool visible,
      CareDistanceDirection direction,
      float progress,
      int completedSteps)
    {
      progress = Mathf.Clamp01(progress);
      if (_distanceCoreFill != null)
      {
        _distanceCoreFill.gameObject.SetActive(visible);
        _distanceCoreFill.fillAmount = progress;
        _distanceCoreFill.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.18f + progress * 0.58f);
        _distanceCoreFill.rectTransform.localScale = direction == CareDistanceDirection.Closer
          ? Vector3.one * Mathf.Lerp(0.72f, 1.15f, progress)
          : Vector3.one * Mathf.Lerp(1f, 0.76f, progress);
      }
      if (_distanceWave != null)
      {
        _distanceWave.gameObject.SetActive(visible);
        var outward = direction == CareDistanceDirection.Away;
        _distanceWave.rectTransform.localScale = Vector3.one * (outward
          ? Mathf.Lerp(0.78f, 1.42f, progress)
          : Mathf.Lerp(1.3f, 0.82f, progress));
        _distanceWave.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.12f + progress * 0.42f);
      }
      for (var i = 0; i < _distanceGuideDots.Count; i++)
      {
        var dot = _distanceGuideDots[i];
        dot.gameObject.SetActive(visible);
        var angle = i * Mathf.PI * 0.5f;
        var radius = direction == CareDistanceDirection.Away
          ? Mathf.Lerp(74f, 154f, progress)
          : Mathf.Lerp(154f, 74f, progress);
        dot.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
      }
      for (var i = 0; i < _distanceStepLights.Count; i++)
      {
        _distanceStepLights[i].gameObject.SetActive(visible && completedSteps >= 0);
        _distanceStepLights[i].color = i < completedSteps
          ? KeepBlinkingTheme.AccentPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.18f);
      }
    }

    private void ResetTransportDistanceResponse()
    {
      if (_cart != null)
      {
        _cart.localScale = CartScale(_stationSave != null ? _stationSave.cartCapacity : 4);
      }
      for (var i = 0; i < _xpVisuals.Count; i++)
        _xpVisuals[i].rectTransform.localScale = Vector3.one;
    }

    public void ShowRepairReveal(CareStationIncidentType incident)
    {
      _actionRoot.gameObject.SetActive(false);
      _incidentRoot.gameObject.SetActive(true);
      _careDimmer.color = Color.clear;
      _incidentSelectable = false;
      _incidentLabel.text = incident == CareStationIncidentType.DrySpot ? "DRY SPOT" : incident == CareStationIncidentType.EyeGunk ? "EYE GUNK" : "DUST";
      _incidentCore.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.2f);
      _incidentRing.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.9f);
      _repairPulseUntil = Time.unscaledTime + 1.4f;
      _statusText.text = "REPAIR COMPLETE";
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
    }

    public void ShowSendXp(int pendingXp, bool fallbackAvailable)
    {
      ShowAction("SEND BOTTLES", 0f, false, pendingXp > 0 ? $"BOTTLES READY  {pendingXp}" : string.Empty);
      _fallbackButton.gameObject.SetActive(fallbackAvailable);
      SetRoutinePrimary("SEND BOTTLES");
      SetProductionAnimation(false);
    }

    public void ShowCollecting(int remainingValue)
    {
      // Collection is represented by the real bottle flight. Reusing the care
      // action overlay here left its full progress disc parked over CARE CORE.
      HideAllModals();
      _statusText.text = remainingValue > 0 ? "STORING BOTTLES" : string.Empty;
      SetRoutinePrimary("STORING BOTTLES");
      SetProductionAnimation(false);
      RenderDistanceFeedback(false, CareDistanceDirection.None, 0f, -1);
      ResetTransportDistanceResponse();
      SetCrewState(CareCrewState.Carry);
    }

    public void SetFallbackAvailable(bool available)
    {
      if (_fallbackButton != null) _fallbackButton.gameObject.SetActive(available);
    }

    public void SetReturnFallbackAvailable(bool available)
    {
      if (_returnFallbackButton != null) _returnFallbackButton.gameObject.SetActive(available);
    }

    public void SetCareActionChangeAvailable(bool available)
    {
      if (_changeStepButton != null)
        _changeStepButton.gameObject.SetActive(available &&
          (_changeStepConfirmRoot == null || !_changeStepConfirmRoot.gameObject.activeSelf));
    }

    public void ShowCareStepChangeConfirmation()
    {
      if (_changeStepConfirmRoot == null) return;
      _changeStepButton?.gameObject.SetActive(false);
      if (_actionGroup != null)
      {
        _actionGroup.interactable = false;
        _actionGroup.blocksRaycasts = false;
      }
      _changeStepConfirmRoot.gameObject.SetActive(true);
    }

    public void HideCareStepChangeConfirmation()
    {
      if (_changeStepConfirmRoot != null) _changeStepConfirmRoot.gameObject.SetActive(false);
      if (_actionGroup != null)
      {
        _actionGroup.interactable = true;
        _actionGroup.blocksRaycasts = true;
      }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public void SetCareActionSkipAvailable(bool available)
    {
      if (_skipCareStepButton != null) _skipCareStepButton.gameObject.SetActive(available);
    }
#endif

    public void SetDistanceSafetyWarning(bool visible)
    {
      if (_distanceSafetyRoot != null) _distanceSafetyRoot.gameObject.SetActive(visible);
    }

    public void SetPendingXp(int value, int goldBottleCount = 0)
    {
      _pendingBottleValue = Mathf.Max(0, value);
      _pendingGoldBottleCount = Mathf.Clamp(goldBottleCount, 0, _pendingBottleValue);
      _xpReady.text = string.Empty;
      RefreshResourceHud();
      var visible = Mathf.Clamp(value <= 0 ? 0 : Mathf.CeilToInt(value / 5f), 0, _xpVisuals.Count);
      for (var i = 0; i < _xpVisuals.Count; i++)
      {
        var shown = i < visible;
        _xpVisuals[i].gameObject.SetActive(shown);
        if (shown) _xpVisuals[i].color = i >= visible - goldBottleCount
          ? KeepBlinkingTheme.AccentWarm
          : KeepBlinkingTheme.AccentPrimary;
        if (shown && _xpVisuals[i].transform.childCount > 0 && _xpVisuals[i].transform.GetChild(0).TryGetComponent<Image>(out var neck))
          neck.color = _xpVisuals[i].color;
      }
    }

    public void ShowUpgrade(CareStationSaveData save, CareStationUpgradeConfiguration configuration = null)
    {
      configuration = configuration ?? new CareStationUpgradeConfiguration();
      HideAllModals();
      _upgradeRoot.gameObject.SetActive(true);
      SetNavigationSelection(1);
      SetRoutinePrimary("UPGRADE");
      SetProductionAnimation(false);
      if (_upgradeTitle != null) _upgradeTitle.text = "STATION UPGRADE";
      ApplyStation(save);
      foreach (var pair in _upgradeButtons)
      {
        var level = CareStationShiftRules.GetUpgradeLevel(save, pair.Key);
        var availability = CareStationShiftRules.EvaluateUpgrade(save, pair.Key, configuration);
        var maximum = availability.IsMaximum;
        pair.Value.interactable = availability.CanPurchase;
        var group = pair.Value.GetComponent<CanvasGroup>();
        if (group == null) group = pair.Value.gameObject.AddComponent<CanvasGroup>();
        group.alpha = pair.Value.interactable ? 1f : maximum ? 0.42f : 0.68f;
        if (maximum && _upgradeCardTexts.TryGetValue(pair.Key, out var text))
        {
          var title = pair.Key == CareStationUpgradeId.MoreWorkers ? "MORE WORKERS"
            : pair.Key == CareStationUpgradeId.LargerStorage ? "LARGER STORAGE" : "BIGGER CART";
          var effect = pair.Key == CareStationUpgradeId.MoreWorkers ? "More carts at once."
            : pair.Key == CareStationUpgradeId.LargerStorage ? "Hold more bottles." : "Carry more each trip.";
          if (maximum)
          {
            text.text = $"{title}\nLEVEL {level}   MAX\n{effect}\n{configuration.Value(pair.Key, level)}";
          }
          else
          {
            var cost = configuration.Cost(pair.Key, level);
            var costText = cost.goldBottles > 0
              ? $"{cost.fullBottles} FULL + {cost.goldBottles} GOLD"
              : $"{cost.fullBottles} FULL";
            text.text = $"{title}\nLEVEL {level}\n{effect}\n{configuration.Value(pair.Key, level)}  →  {configuration.Value(pair.Key, level + 1)}     {costText}";
          }
        }
      }
      // Use the same availability result as purchase execution for every
      // non-maximum card. This second formatting pass also replaces legacy
      // encoded arrow text from older graybox builds.
      foreach (var pair in _upgradeCardTexts)
      {
        var level = CareStationShiftRules.GetUpgradeLevel(save, pair.Key);
        if (level >= CareStationUpgradeConfiguration.MaximumLevel) continue;
        var availability = CareStationShiftRules.EvaluateUpgrade(save, pair.Key, configuration);
        var title = pair.Key == CareStationUpgradeId.MoreWorkers ? "MORE WORKERS"
          : pair.Key == CareStationUpgradeId.LargerStorage ? "LARGER STORAGE" : "BIGGER CART";
        var effect = pair.Key == CareStationUpgradeId.MoreWorkers ? "More carts at once."
          : pair.Key == CareStationUpgradeId.LargerStorage ? "Hold more bottles." : "Carry more each trip.";
        var cost = availability.Cost;
        var costText = cost.goldBottles > 0
          ? $"{cost.fullBottles} FULL + {cost.goldBottles} GOLD"
          : $"{cost.fullBottles} FULL";
        var reasonLine = string.IsNullOrEmpty(availability.PlayerReason)
          ? string.Empty
          : "\n" + availability.PlayerReason;
        pair.Value.text = $"{title}\nLEVEL {level}\n{effect}\n{configuration.Value(pair.Key, level)} -> {configuration.Value(pair.Key, level + 1)}   {costText}{reasonLine}";
      }
    }

    public void ShowSubjectiveCheck(bool post, CareSubjectiveScores scores)
    {
      HideAllModals();
      _surveyIsPost = post;
      _surveyDraft = scores?.Clone() ?? new CareSubjectiveScores();
      _surveyRoot.gameObject.SetActive(true);
      _surveyTitle.text = post ? "POST-CARE CHECK" : "PRE-CARE CHECK";
      SetRoutinePrimary(post ? "VIEW REPORT" : "CONTINUE");
      SetProductionAnimation(false);
      RefreshSurvey();
    }

    public void ShowCareReport(CareStationSaveData save)
    {
      HideAllModals();
      _reportRoot.gameObject.SetActive(true);
      SetNavigationSelection(2);
      _reportText.text = CareReportFormatter.Build(save);
      SetRoutinePrimary("DONE");
      SetProductionAnimation(false);
      var recipe = save?.currentRecipe;
      for (var index = 0; index < _reportStepIcons.Count; index++)
      {
        var visible = recipe != null && index < recipe.ActionCount;
        _reportStepIcons[index].gameObject.SetActive(visible);
        if (visible) _reportStepIcons[index].color = recipe.IsStepCompleted(index)
          ? KeepBlinkingTheme.AccentPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f);
      }
    }

    public void ShowStorageFull(CareStationSaveData save, CareStationUpgradeConfiguration configuration)
    {
      ShowUpgrade(save, configuration);
      if (_upgradeTitle != null) _upgradeTitle.text = "STORAGE FULL\nPRODUCTION PAUSED";
      _statusText.text = string.Empty;
      SetCrewState(CareCrewState.Rest);
      SetRoutinePrimary("STORAGE FULL");
      SetProductionAnimation(false);
    }

    public bool IsUpgradeInteractable(CareStationUpgradeId upgrade)
    {
      return _upgradeButtons.TryGetValue(upgrade, out var button) && button.interactable;
    }

    public void ShowShiftComplete(CareStationSaveData save)
    {
      HideAllModals();
      _completeRoot.gameObject.SetActive(true);
      _completeText.text = $"SHIFT COMPLETE\nCARE ROUTINE COMPLETE\n\nFULL BOTTLES  {Mathf.Max(0, save?.shiftStoredFullBottles ?? 0)}\nGOLD BOTTLES  {Mathf.Max(0, save?.shiftStoredGoldBottles ?? 0)}";
      var recipe = save?.currentRecipe;
      for (var index = 0; index < _completeStepIcons.Count; index++)
      {
        var visible = recipe != null && index < recipe.ActionCount;
        _completeStepIcons[index].gameObject.SetActive(visible);
        if (visible) _completeStepIcons[index].color = recipe.IsStepCompleted(index)
          ? KeepBlinkingTheme.AccentPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f);
      }
      _endShiftButton.gameObject.SetActive(true);
      SetCrewState(CareCrewState.Cheer);
      SetRoutinePrimary("END SHIFT");
      SetProductionAnimation(false);
    }

    public void ShowAutoShift()
    {
      HideAllModals();
      _statusText.text = "CREW AT WORK  ·  RETURN LATER";
      SetCrewState(CareCrewState.Work);
      SetRoutinePrimary("CREW AT WORK");
      SetProductionAnimation(true);
    }

    public void HideAllModals()
    {
      if (_welcomeRoot != null) _welcomeRoot.gameObject.SetActive(false);
      if (_incidentRoot != null) _incidentRoot.gameObject.SetActive(false);
      if (_actionRoot != null) _actionRoot.gameObject.SetActive(false);
      if (_upgradeRoot != null) _upgradeRoot.gameObject.SetActive(false);
      if (_completeRoot != null) _completeRoot.gameObject.SetActive(false);
      if (_surveyRoot != null) _surveyRoot.gameObject.SetActive(false);
      if (_reportRoot != null) _reportRoot.gameObject.SetActive(false);
      if (_changeStepConfirmRoot != null) _changeStepConfirmRoot.gameObject.SetActive(false);
      if (_actionGroup != null)
      {
        _actionGroup.interactable = true;
        _actionGroup.blocksRaycasts = true;
      }
      if (_changeStepButton != null) _changeStepButton.gameObject.SetActive(false);
      if (_fallbackButton != null) _fallbackButton.gameObject.SetActive(false);
      if (_returnFallbackButton != null) _returnFallbackButton.gameObject.SetActive(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_skipCareStepButton != null) _skipCareStepButton.gameObject.SetActive(false);
#endif
      RenderDistanceFeedback(false, CareDistanceDirection.None, 0f, -1);
      ResetTransportDistanceResponse();
      if (_careDimmer != null) _careDimmer.color = Color.clear;
      _incidentSelectable = false;
      _statusText.text = string.Empty;
      SetNavigationSelection(0);
    }

    private void Update()
    {
      var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f);
      if (_incidentRoot != null && _incidentRoot.gameObject.activeSelf)
      {
        _incidentRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.06f, pulse);
        if (_repairPulseUntil > Time.unscaledTime)
          _incidentCore.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.25f, 1f - (_repairPulseUntil - Time.unscaledTime) / 1.4f);
      }
      if (_phoneIcon != null && _phoneIcon.gameObject.activeSelf &&
          (_actionPrompt.text == "SCREEN DOWN" || _renderedCareActionType == CareActionType.ScreenDown))
        _phoneIcon.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(0f, 180f, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 0.8f)));
      if (_storageTank != null && _storageFull)
        _storageTank.localScale = _storageBaseScale * Mathf.Lerp(0.985f, 1.025f, pulse);
      var pipelinePulse = _pipelinePulseUntil > Time.unscaledTime
        ? 1f + Mathf.Sin((1f - (_pipelinePulseUntil - Time.unscaledTime) / 0.55f) * Mathf.PI) * 0.08f
        : 1f;
      if (_filterBody != null) _filterBody.rectTransform.localScale = Vector3.one * (((_pipelineMask & CareRecipePipeline.Filter) != 0) ? pipelinePulse : 1f);
      if (_tankBody != null) _tankBody.rectTransform.localScale = Vector3.one * (((_pipelineMask & CareRecipePipeline.Tank) != 0) ? pipelinePulse : 1f);
      for (var i = 0; i < _pressLayers.Count; i++)
        _pressLayers[i].rectTransform.localScale = Vector3.one * (((_pipelineMask & CareRecipePipeline.Press) != 0) ? pipelinePulse : 1f);
      UpdateProductionAnimation();
      PollIncidentTouch();
    }

    private void PollIncidentTouch()
    {
      var pressed = false;
      var position = Vector2.zero;
      if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
      {
        pressed = true;
        position = Touchscreen.current.primaryTouch.position.ReadValue();
      }
      else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
      {
        pressed = true;
        position = Mouse.current.position.ReadValue();
      }
      if (!pressed)
      {
        _touchConsumed = false;
        return;
      }
      if (_touchConsumed) return;
      _touchConsumed = true;
      if (_welcomeRoot.gameObject.activeSelf)
      {
        ContinueSelected?.Invoke();
        return;
      }
      if (_incidentSelectable && _incidentHitRect.gameObject.activeInHierarchy &&
          RectTransformUtility.RectangleContainsScreenPoint(_incidentHitRect, position))
        IncidentSelected?.Invoke();
    }

    private void BuildStationStage()
    {
      _stationStage = FirstLevelUiFactory.CreateObject("Station Stage", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_stationStage, new Vector2(0.03f, 0.31f), new Vector2(0.97f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var stage = FirstLevelUiFactory.CreateImage("Stage Surface", _stationStage, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.18f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(stage.rectTransform);
      var stationLoop = FirstLevelUiFactory.CreateImage("Station Logistics Loop", _stationStage, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.18f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(stationLoop.rectTransform, new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.49f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(730f, 620f));

      CreateTrack("Filter Track", new Vector2(0.28f, 0.68f), new Vector2(0.47f, 0.5f));
      CreateTrack("Tank Track", new Vector2(0.72f, 0.68f), new Vector2(0.53f, 0.5f));
      CreateTrack("Press Track", new Vector2(0.5f, 0.74f), new Vector2(0.5f, 0.56f));

      BuildFilterDevice();
      BuildTankDevice();
      BuildPressDevice();
      BuildCareCore();
    }

    private void BuildFilterDevice()
    {
      var root = FirstLevelUiFactory.CreateObject("Filter Device", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.22f, 0.76f), new Vector2(0.22f, 0.76f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(230f, 142f));
      var frame = FirstLevelUiFactory.CreateImage("Wide Filter Body", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.34f), FirstLevelUiFactory.RoundedSprite);
      _filterBody = frame;
      FirstLevelUiFactory.SetRect(frame.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210f, 88f));
      var intake = FirstLevelUiFactory.CreateImage("Filter Intake", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.92f), FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(intake.rectTransform, new Vector2(0.2f, 0.57f), new Vector2(0.2f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(52f, 52f));
      for (var i = 0; i < 3; i++)
      {
        var slat = FirstLevelUiFactory.CreateImage("Filter Slat", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.4f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(slat.rectTransform, new Vector2(0.62f, 0.57f), new Vector2(0.62f, 0.57f), new Vector2(0.5f, 0.5f), new Vector2(i * 24f - 24f, 0f), new Vector2(8f, 54f));
      }
      CreateDeviceLabel(root, "FILTER");
    }

    private void BuildTankDevice()
    {
      var root = FirstLevelUiFactory.CreateObject("Tank Device", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.78f, 0.75f), new Vector2(0.78f, 0.75f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 188f));
      var body = FirstLevelUiFactory.CreateImage("Tall Tank Body", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.34f), FirstLevelUiFactory.RoundedSprite);
      _tankBody = body;
      FirstLevelUiFactory.SetRect(body.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112f, 134f));
      var level = FirstLevelUiFactory.CreateImage("Tank Level", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.5f), FirstLevelUiFactory.RoundedSprite);
      _tankLevel = level;
      FirstLevelUiFactory.SetRect(level.rectTransform, new Vector2(0.5f, 0.39f), new Vector2(0.5f, 0.39f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(88f, 55f));
      CreateDeviceLabel(root, "TANK");
    }

    private void BuildPressDevice()
    {
      var root = FirstLevelUiFactory.CreateObject("Press Device", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 150f));
      for (var i = 0; i < 2; i++)
      {
        var layer = FirstLevelUiFactory.CreateImage("Press Layer", root, KeepBlinkingTheme.WithAlpha(i == 0 ? KeepBlinkingTheme.TextSecondary : KeepBlinkingTheme.AccentSoft, 0.36f), FirstLevelUiFactory.RoundedSprite);
        _pressLayers.Add(layer);
        FirstLevelUiFactory.SetRect(layer.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), new Vector2(0f, i * 47f - 23f), new Vector2(i == 0 ? 168f : 136f, 36f));
      }
      var stem = FirstLevelUiFactory.CreateImage("Press Stem", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.5f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(stem.rectTransform, new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.62f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 72f));
      CreateDeviceLabel(root, "PRESS");
    }

    private void BuildCareCore()
    {
      var root = FirstLevelUiFactory.CreateObject("Care Core Platform", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(440f, 230f));
      var outline = FirstLevelUiFactory.CreateImage("Care Core Outline", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.48f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(outline.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(410f, 164f));
      var inner = FirstLevelUiFactory.CreateImage("Care Core Inner", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.96f), FirstLevelUiFactory.RoundedSprite);
      _careCoreInner = inner;
      FirstLevelUiFactory.SetRect(inner.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(388f, 142f));
      for (var side = -1; side <= 1; side += 2)
      {
        var leaf = FirstLevelUiFactory.CreateImage("Care Core Leaf", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.18f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(leaf.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.5f), new Vector2(side * 83f, 0f), new Vector2(196f, 112f));
        leaf.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * 13f);
        leaf.raycastTarget = false;
      }
      var coreNode = FirstLevelUiFactory.CreateImage("Care Core Node", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.72f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(coreNode.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(46f, 46f));
      coreNode.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
      coreNode.raycastTarget = false;
      var seam = FirstLevelUiFactory.CreateImage("Care Core Seam", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.22f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(seam.rectTransform, new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.56f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(275f, 4f));
      var label = FirstLevelUiFactory.CreateText("Care Core Label", root, "CARE CORE", 21f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0.2f, 0f), new Vector2(0.8f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void CreateTrack(string name, Vector2 from, Vector2 to)
    {
      var center = (from + to) * 0.5f;
      var delta = to - from;
      var line = FirstLevelUiFactory.CreateImage(name, _stationStage, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.22f), FirstLevelUiFactory.RoundedSprite);
      _stationTracks.Add(line);
      FirstLevelUiFactory.SetRect(line.rectTransform, center, center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(delta.magnitude * 880f, 5f));
      line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
    }

    private static void CreateDeviceLabel(Transform root, string label)
    {
      var text = FirstLevelUiFactory.CreateText(label + " Label", root, label, 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void BuildStorage()
    {
      _transportRoot = FirstLevelUiFactory.CreateObject("Bottle Transport", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_transportRoot, new Vector2(0.02f, 0.015f), new Vector2(0.98f, 0.25f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var transportPanel = FirstLevelUiFactory.CreateImage("Transport Surface", _transportRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.12f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(transportPanel.rectTransform);

      var rail = FirstLevelUiFactory.CreateImage("Bottle Rail", _transportRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.32f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(rail.rectTransform, new Vector2(0.48f, 0.54f), new Vector2(0.48f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(430f, 8f));
      for (var i = 0; i < 5; i++)
      {
        var marker = FirstLevelUiFactory.CreateImage("Rail Marker", _transportRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.24f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(marker.rectTransform, new Vector2(0.32f + i * 0.08f, 0.54f), new Vector2(0.32f + i * 0.08f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(12f, 12f));
      }

      _storageTank = FirstLevelUiFactory.CreateObject("Bottle Storage Tank", _transportRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_storageTank, new Vector2(0.14f, 0.52f), new Vector2(0.14f, 0.52f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 112f));
      var tank = FirstLevelUiFactory.CreateImage("Tank", _storageTank, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.25f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(tank.rectTransform);
      var storageLabel = FirstLevelUiFactory.CreateText("Storage Label", _storageTank, "STORAGE", 17f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(storageLabel.rectTransform, new Vector2(0.08f, 0.56f), new Vector2(0.92f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var storageTrack = FirstLevelUiFactory.CreateImage("Storage Capacity Track", _storageTank, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.72f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(storageTrack.rectTransform, new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.26f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(170f, 15f));
      _storageFill = FirstLevelUiFactory.CreateImage("Storage Capacity Fill", storageTrack.transform, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(_storageFill.rectTransform, Vector2.zero, Vector2.zero);
      _storageFill.type = Image.Type.Filled;
      _storageFill.fillMethod = Image.FillMethod.Horizontal;
      _storageFill.fillOrigin = 0;
      var extra = FirstLevelUiFactory.CreateImage("Extra Container", _storageTank, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.18f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(extra.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(90f, 78f));
      extra.gameObject.SetActive(false);
      var tierThree = FirstLevelUiFactory.CreateImage("Tier 3 Container", _storageTank, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.16f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(tierThree.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 11f), new Vector2(160f, 42f));
      tierThree.gameObject.SetActive(false);
      var tierFour = FirstLevelUiFactory.CreateImage("Tier 4 Container", _storageTank, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.14f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(tierFour.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 55f), new Vector2(122f, 34f));
      tierFour.gameObject.SetActive(false);
      for (var cartIndex = 0; cartIndex < 5; cartIndex++)
      {
        var cartName = cartIndex == 0 ? "Bottle Cart" : $"Bottle Cart {cartIndex + 1}";
        var cart = FirstLevelUiFactory.CreateObject(cartName, _transportRoot).GetComponent<RectTransform>();
        var x = 0.79f + (cartIndex % 3) * 0.055f;
        var y = 0.42f + (cartIndex / 3) * 0.28f;
        FirstLevelUiFactory.SetRect(cart, new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(125f, 62f));
        var bed = FirstLevelUiFactory.CreateImage("Cart Bed", cart, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.58f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(bed.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(-10f, -24f));
        for (var wheelIndex = 0; wheelIndex < 2; wheelIndex++)
        {
          var wheel = FirstLevelUiFactory.CreateImage("Wheel", cart, KeepBlinkingTheme.BorderReadable, FirstLevelUiFactory.CircleSprite);
          FirstLevelUiFactory.SetRect(wheel.rectTransform, new Vector2(wheelIndex == 0 ? 0.25f : 0.75f, 0f), new Vector2(wheelIndex == 0 ? 0.25f : 0.75f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), new Vector2(22f, 22f));
        }
        cart.gameObject.SetActive(cartIndex < 2);
        _carts.Add(cart);
        if (cartIndex == 0) _cart = cart;
      }
      if (_cart != null) _productionCartHome = _cart.anchoredPosition;
      _productionBottle = FirstLevelUiFactory.CreateObject("Representative Production Bottle", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_productionBottle, new Vector2(0.12f, 0.29f), new Vector2(0.12f, 0.29f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 38f));
      var productionBottleBody = FirstLevelUiFactory.CreateImage("Bottle Body", _productionBottle, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.76f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(productionBottleBody.rectTransform);
      productionBottleBody.raycastTarget = false;
      var productionBottleNeck = FirstLevelUiFactory.CreateImage("Bottle Neck", _productionBottle, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.76f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(productionBottleNeck.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -1f), new Vector2(12f, 9f));
      productionBottleNeck.raycastTarget = false;
      _productionBottle.gameObject.SetActive(false);

      var emptyRack = FirstLevelUiFactory.CreateText("Empty Rack Label", _stationStage, "EMPTY RACK", 14f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(emptyRack.rectTransform, new Vector2(0.03f, 0.245f), new Vector2(0.22f, 0.30f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      emptyRack.raycastTarget = false;
      for (var i = 0; i < 24; i++)
      {
        var xp = FirstLevelUiFactory.CreateImage("Pending Bottle", _transportRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.78f), FirstLevelUiFactory.RoundedSprite);
        var column = i % 12;
        var row = i / 12;
        var x = 0.29f + column * 0.035f;
        var y = 0.42f + row * 0.28f;
        FirstLevelUiFactory.SetRect(xp.rectTransform, new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(19f, 28f));
        var neck = FirstLevelUiFactory.CreateImage("Bottle Neck", xp.transform, xp.color, FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(neck.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -1f), new Vector2(11f, 8f));
        xp.gameObject.SetActive(false);
        _xpVisuals.Add(xp);
      }
    }

    private void BuildCrew()
    {
      var positions = new[] { new Vector2(0.17f, 0.55f), new Vector2(0.83f, 0.55f), new Vector2(0.32f, 0.31f), new Vector2(0.68f, 0.31f), new Vector2(0.5f, 0.24f) };
      for (var i = 0; i < positions.Length; i++)
      {
        var root = FirstLevelUiFactory.CreateObject($"Care Crew {i + 1}", _stationStage).GetComponent<RectTransform>();
        FirstLevelUiFactory.SetRect(root, positions[i], positions[i], new Vector2(0.5f, 0f), Vector2.zero, new Vector2(76f, 108f));
        var body = FirstLevelUiFactory.CreateImage("Body", root, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.82f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(body.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(56f, 62f));
        var head = FirstLevelUiFactory.CreateImage("Head", root, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(head.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 62f), new Vector2(44f, 44f));
        var crew = root.gameObject.AddComponent<CareCrewPlaceholderView>();
        crew.gameObject.SetActive(i < 2);
        _crew.Add(crew);
      }
    }

    private void BuildCareRoutineDock()
    {
      _routineDock = FirstLevelUiFactory.CreateObject("Care Routine Dock", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_routineDock, new Vector2(0.03f, 0.09f), new Vector2(0.97f, 0.29f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var surface = FirstLevelUiFactory.CreateImage("Routine Surface", _routineDock, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.74f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(surface.rectTransform);
      surface.raycastTarget = false;
      _routineDockTitle = FirstLevelUiFactory.CreateText("Routine Title", _routineDock, "CARE ROUTINE", 23f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_routineDockTitle.rectTransform, new Vector2(0.08f, 0.76f), new Vector2(0.92f, 0.96f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _routineDockTitle.raycastTarget = false;

      for (var index = 0; index < 4; index++)
      {
        var x = 0.16f + index * 0.225f;
        var dot = FirstLevelUiFactory.CreateImage("Routine Step Dot", _routineDock, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(x, 0.58f), new Vector2(x, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(25f, 25f));
        dot.raycastTarget = false;
        _routineDockDots.Add(dot);
        var label = FirstLevelUiFactory.CreateText("Routine Step Label", _routineDock, string.Empty, 13f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted, true);
        FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(x - 0.105f, 0.31f), new Vector2(x + 0.105f, 0.49f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        label.raycastTarget = false;
        _routineDockLabels.Add(label);
      }

      var primary = FirstLevelUiFactory.CreateImage("Routine Primary Prompt", _routineDock, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.22f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(primary.rectTransform, new Vector2(0.20f, 0.04f), new Vector2(0.80f, 0.27f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      primary.raycastTarget = false;
      _routinePrimaryText = FirstLevelUiFactory.CreateText("Routine Primary Text", primary.transform, "STATION WORKING", 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.Stretch(_routinePrimaryText.rectTransform, new Vector2(12f, 4f), new Vector2(-12f, -4f));
      _routinePrimaryText.raycastTarget = false;

      if (_statusText != null) _statusText.gameObject.SetActive(false);
      if (_xpReady != null) _xpReady.gameObject.SetActive(false);
      RefreshRoutineDock(null);
    }

    private void BuildNavigation()
    {
      _navigationRoot = FirstLevelUiFactory.CreateObject("Station Navigation", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_navigationRoot, new Vector2(0.03f, 0.01f), new Vector2(0.97f, 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var labels = new[] { "STATION", "UPGRADES", "REPORTS" };
      for (var index = 0; index < labels.Length; index++)
      {
        var min = new Vector2(index / 3f + 0.008f, 0.04f);
        var max = new Vector2((index + 1) / 3f - 0.008f, 0.96f);
        var selected = index == 0;
        var tab = FirstLevelUiFactory.CreateImage(labels[index] + " Tab", _navigationRoot,
          KeepBlinkingTheme.WithAlpha(selected ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.SurfaceElevated, selected ? 0.38f : 0.42f),
          FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(tab.rectTransform, min, max, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        tab.raycastTarget = false;
        _navigationTabs.Add(tab);
        var text = FirstLevelUiFactory.CreateText(labels[index] + " Label", tab.transform, labels[index], 16f, FontStyles.Bold, TextAlignmentOptions.Center,
          selected ? KeepBlinkingTheme.TextPrimary : KeepBlinkingTheme.TextMuted);
        FirstLevelUiFactory.Stretch(text.rectTransform, new Vector2(4f, 3f), new Vector2(-4f, -3f));
        text.raycastTarget = false;
        _navigationLabels.Add(text);
      }
    }

    private void SetNavigationSelection(int selectedIndex)
    {
      for (var index = 0; index < _navigationTabs.Count; index++)
      {
        var selected = index == selectedIndex;
        _navigationTabs[index].color = KeepBlinkingTheme.WithAlpha(
          selected ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.SurfaceElevated,
          selected ? 0.38f : 0.42f);
        if (index < _navigationLabels.Count)
          _navigationLabels[index].color = selected ? KeepBlinkingTheme.TextPrimary : KeepBlinkingTheme.TextMuted;
      }
    }

    private void RefreshRoutineDock(CareRecipeSaveData recipe)
    {
      if (_routineDockTitle == null) return;
      var training = recipe == null ? -1 : CareRecipeGenerator.TrainingIndex(recipe);
      _routineDockTitle.text = recipe != null && recipe.recipeType == CareRecipeType.Training && training >= 0
        ? $"TRAINING {training + 1} / 4"
        : "CARE ROUTINE";
      for (var index = 0; index < _routineDockDots.Count; index++)
      {
        var visible = recipe != null && index < recipe.ActionCount;
        _routineDockDots[index].gameObject.SetActive(visible);
        _routineDockLabels[index].gameObject.SetActive(visible);
        if (!visible) continue;
        var action = recipe.actionList[index];
        _routineDockLabels[index].text = ShortActionLabel(action);
        _routineDockDots[index].color = recipe.IsStepCompleted(index)
          ? KeepBlinkingTheme.AccentPrimary
          : index == recipe.currentActionIndex && !recipe.recipeCompleted
            ? KeepBlinkingTheme.TextPrimary
            : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f);
      }
    }

    private void SetRoutinePrimary(string text)
    {
      if (_routinePrimaryText != null) _routinePrimaryText.text = text ?? string.Empty;
    }

    private void SetProductionAnimation(bool active)
    {
      if (_productionAnimating == active) return;
      _productionAnimating = active;
      _productionAnimationStartedAt = Time.unscaledTime;
      if (!active)
      {
        if (_productionBottle != null) _productionBottle.gameObject.SetActive(false);
        if (_cart != null) _cart.anchoredPosition = _productionCartHome;
        if (_tankLevel != null) _tankLevel.rectTransform.localScale = Vector3.one;
        if (_pressLayers.Count > 0)
          _pressLayers[0].rectTransform.anchoredPosition = new Vector2(0f, -23f);
      }
    }

    private void UpdateProductionAnimation()
    {
      if (_productionBottle == null || !_productionAnimating || _storageFull)
      {
        if (_productionBottle != null) _productionBottle.gameObject.SetActive(false);
        return;
      }

      // A representative 10 second bottle route followed by an 8 second quiet
      // interval. It is deliberately visual-only: authoritative production is
      // settled by CareStationProductionController and the save service.
      var cycle = Mathf.Repeat(Time.unscaledTime - _productionAnimationStartedAt, 18f);
      if (cycle >= 10f)
      {
        _productionBottle.gameObject.SetActive(false);
        if (_cart != null) _cart.anchoredPosition = _productionCartHome;
        return;
      }

      _productionBottle.gameObject.SetActive(true);
      var route = Mathf.Clamp(cycle / 9f, 0f, 0.9999f) * (_productionRoute.Length - 1);
      var segment = Mathf.Clamp(Mathf.FloorToInt(route), 0, _productionRoute.Length - 2);
      var local = Mathf.SmoothStep(0f, 1f, route - segment);
      var anchor = Vector2.Lerp(_productionRoute[segment], _productionRoute[segment + 1], local);
      _productionBottle.anchorMin = anchor;
      _productionBottle.anchorMax = anchor;
      _productionBottle.anchoredPosition = Vector2.zero;

      if (_tankLevel != null)
      {
        var tankFill = segment == 1 ? local : segment > 1 ? 1f : 0.24f;
        _tankLevel.rectTransform.localScale = new Vector3(1f, Mathf.Lerp(0.24f, 1f, tankFill), 1f);
      }
      if (_pressLayers.Count > 0)
      {
        var press = segment == 2 ? Mathf.Sin(local * Mathf.PI) : 0f;
        _pressLayers[0].rectTransform.anchoredPosition = new Vector2(0f, -23f - press * 13f);
      }
      if (_cart != null)
      {
        var transport = segment >= 3 ? Mathf.Clamp01((cycle - 6f) / 3f) : 0f;
        _cart.anchoredPosition = _productionCartHome + new Vector2(Mathf.Lerp(0f, -120f, transport), 0f);
      }
    }

    private static string ShortActionLabel(CareActionType action)
    {
      switch (action)
      {
        case CareActionType.ScreenDown: return "SCREEN\nDOWN";
        case CareActionType.ClosedEyeRest: return "REST\nEYES";
        case CareActionType.FocusShift: return "FOCUS\nSHIFT";
        case CareActionType.GuidedEyeCircles: return "GUIDED\nCIRCLES";
        default: return string.Empty;
      }
    }

    private void BuildIncident()
    {
      _incidentRoot = FirstLevelUiFactory.CreateObject("Station Incident", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_incidentRoot, new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.43f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(460f, 270f));
      _incidentHitRect = _incidentRoot;
      _incidentRing = FirstLevelUiFactory.CreateImage("Incident Ring", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.5f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(_incidentRing.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 190f));
      _incidentCore = FirstLevelUiFactory.CreateImage("Incident Core", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.4f), FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(_incidentCore.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(145f, 145f));
      var dustOffsets = new[] { new Vector2(-112f, 42f), new Vector2(105f, 32f), new Vector2(0f, -74f) };
      for (var i = 0; i < dustOffsets.Length; i++)
      {
        var dust = FirstLevelUiFactory.CreateImage("Dust Group", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.42f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dust.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), dustOffsets[i], new Vector2(42f + i * 8f, 42f + i * 8f));
        _dustGroups.Add(dust);
      }
      for (var i = 0; i < 3; i++)
      {
        var crack = FirstLevelUiFactory.CreateImage("Dry Crack", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.72f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(crack.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 28f, (i % 2 == 0 ? 1f : -1f) * 10f), new Vector2(7f, 108f));
        crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -24f + i * 24f);
        crack.gameObject.SetActive(false);
        _dryCracks.Add(crack);
      }
      _incidentLabel = FirstLevelUiFactory.CreateText("Incident Label", _incidentRoot, "DUST", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_incidentLabel.rectTransform, new Vector2(0.15f, 0f), new Vector2(0.85f, 0.2f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void BuildActionOverlay()
    {
      _actionRoot = FirstLevelUiFactory.CreateObject("Care Action", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_actionRoot, new Vector2(0.08f, 0.19f), new Vector2(0.92f, 0.86f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _actionGroup = _actionRoot.gameObject.AddComponent<CanvasGroup>();
      var panel = FirstLevelUiFactory.CreateImage("Action Surface", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceOverlay, 0.72f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(panel.rectTransform);
      _recipeTitle = FirstLevelUiFactory.CreateText("Recipe Title", _actionRoot, "CARE ROUTINE", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_recipeTitle.rectTransform, new Vector2(0.05f, 0.9f), new Vector2(0.95f, 0.97f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _recipeStepText = FirstLevelUiFactory.CreateText("Recipe Step", _actionRoot, "STEP 1 / 1", 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(_recipeStepText.rectTransform, new Vector2(0.25f, 0.84f), new Vector2(0.75f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      for (var i = 0; i < 3; i++)
      {
        var dot = FirstLevelUiFactory.CreateImage("Recipe Step Dot", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 34f, 0f), new Vector2(14f, 14f));
        _recipeStepDots.Add(dot);
      }
      _actionVisualRing = FirstLevelUiFactory.CreateImage("Action Ring", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.35f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(_actionVisualRing.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 300f));
      _actionProgress = FirstLevelUiFactory.CreateImage("Action Progress", _actionRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
      _actionProgress.type = Image.Type.Filled;
      _actionProgress.fillMethod = Image.FillMethod.Radial360;
      _actionProgress.fillOrigin = 2;
      FirstLevelUiFactory.SetRect(_actionProgress.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(178f, 178f));
      _distanceCoreFill = FirstLevelUiFactory.CreateImage("Distance Core Fill", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.18f), FirstLevelUiFactory.CircleSprite);
      _distanceCoreFill.type = Image.Type.Filled;
      _distanceCoreFill.fillMethod = Image.FillMethod.Radial360;
      FirstLevelUiFactory.SetRect(_distanceCoreFill.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
      _distanceWave = FirstLevelUiFactory.CreateImage("Distance Wave", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.2f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(_distanceWave.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 260f));
      for (var i = 0; i < 4; i++)
      {
        var dot = FirstLevelUiFactory.CreateImage("Distance Guide Dot", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.5f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
        _distanceGuideDots.Add(dot.rectTransform);

        var step = FirstLevelUiFactory.CreateImage("Focus Step", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.18f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(step.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.5f), new Vector2((i - 1.5f) * 42f, 0f), new Vector2(18f, 18f));
        _distanceStepLights.Add(step);
      }
      RenderDistanceFeedback(false, CareDistanceDirection.None, 0f, -1);
      _phoneIcon = FirstLevelUiFactory.CreateObject("Phone Icon", _actionRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_phoneIcon, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92f, 154f));
      var phone = FirstLevelUiFactory.CreateImage("Phone", _phoneIcon, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.78f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(phone.rectTransform);
      var screen = FirstLevelUiFactory.CreateImage("Screen", _phoneIcon, KeepBlinkingTheme.BackgroundPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(screen.rectTransform, new Vector2(8f, 12f), new Vector2(-8f, -12f));
      _restIcon = FirstLevelUiFactory.CreateObject("Closed Eye Icon", _actionRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_restIcon, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210f, 100f));
      var upperLid = FirstLevelUiFactory.CreateImage("Upper Lid", _restIcon, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(upperLid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(190f, 9f));
      upperLid.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 5f);
      var lowerLid = FirstLevelUiFactory.CreateImage("Lower Lid", _restIcon, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.45f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(lowerLid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(190f, 7f));
      lowerLid.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -5f);
      _guidedOrbitDot = FirstLevelUiFactory.CreateObject("Guided Orbit Dot", _restIcon).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_guidedOrbitDot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22f, 22f));
      var guidedDot = FirstLevelUiFactory.CreateImage("Mint Guide", _guidedOrbitDot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.Stretch(guidedDot.rectTransform);
      _guidedOrbitDot.gameObject.SetActive(false);
      _restIcon.gameObject.SetActive(false);
      _actionPrompt = FirstLevelUiFactory.CreateText("Action Prompt", _actionRoot, "REST", 38f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_actionPrompt.rectTransform, new Vector2(0f, 0.12f), new Vector2(1f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _fallbackButton = FirstLevelUiFactory.CreateButton("Collect Fallback", _actionRoot, "COLLECT", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_fallbackButton.transform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 92f));
      _fallbackButton.onClick.AddListener(() => FallbackCollectSelected?.Invoke());
      _returnFallbackButton = FirstLevelUiFactory.CreateButton("Return Fallback", _actionRoot, "CONTINUE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_returnFallbackButton.transform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 92f));
      _returnFallbackButton.onClick.AddListener(() => ReturnFallbackSelected?.Invoke());
      _returnFallbackButton.gameObject.SetActive(false);
      _changeStepButton = FirstLevelUiFactory.CreateButton("Change Care Step", _actionRoot, "CHANGE STEP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)_changeStepButton.transform, new Vector2(0.18f, 0.055f), new Vector2(0.18f, 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 70f));
      _changeStepButton.onClick.AddListener(() => ChangeStepSelected?.Invoke());
      _changeStepButton.gameObject.SetActive(false);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _skipCareStepButton = FirstLevelUiFactory.CreateButton("Skip Care Step", _actionRoot, "SKIP STEP", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_skipCareStepButton.transform, new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.07f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 82f));
      _skipCareStepButton.onClick.AddListener(() => SkipCareStepSelected?.Invoke());
      _skipCareStepButton.gameObject.SetActive(false);
#endif
    }

    private void BuildWelcome()
    {
      _welcomeRoot = FirstLevelUiFactory.CreateObject("Welcome Back", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_welcomeRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(780f, 540f));
      var panel = FirstLevelUiFactory.CreateImage("Panel", _welcomeRoot, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(panel.rectTransform);
      _welcomeTitle = FirstLevelUiFactory.CreateText("Title", _welcomeRoot, "WHILE YOU WERE AWAY", 48f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_welcomeTitle.rectTransform, new Vector2(0f, 0.68f), Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _welcomeLines = FirstLevelUiFactory.CreateText("Results", _welcomeRoot, string.Empty, 30f, FontStyles.Normal, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary, true);
      FirstLevelUiFactory.SetRect(_welcomeLines.rectTransform, new Vector2(0.12f, 0.18f), new Vector2(0.88f, 0.66f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void BuildChangeStepConfirmation()
    {
      _changeStepConfirmRoot = FirstLevelUiFactory.CreateObject("Change Step Confirmation", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_changeStepConfirmRoot, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.73f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var shade = FirstLevelUiFactory.CreateImage("Shade", _changeStepConfirmRoot, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(shade.rectTransform);
      var prompt = FirstLevelUiFactory.CreateText("Prompt", _changeStepConfirmRoot, "USE CLOSED-EYE REST INSTEAD?", 34f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(prompt.rectTransform, new Vector2(0.08f, 0.48f), new Vector2(0.92f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var use = FirstLevelUiFactory.CreateButton("Use Rest", _changeStepConfirmRoot, "USE REST", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)use.transform, new Vector2(0.08f, 0.13f), new Vector2(0.47f, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      use.onClick.AddListener(() => UseRestSelected?.Invoke());
      var keep = FirstLevelUiFactory.CreateButton("Keep Step", _changeStepConfirmRoot, "KEEP STEP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)keep.transform, new Vector2(0.53f, 0.13f), new Vector2(0.92f, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      keep.onClick.AddListener(() => KeepStepSelected?.Invoke());
      _changeStepConfirmRoot.gameObject.SetActive(false);
    }

    private void BuildUpgrade()
    {
      _upgradeRoot = FirstLevelUiFactory.CreateObject("Station Upgrade", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_upgradeRoot);
      var shade = FirstLevelUiFactory.CreateImage("Shade", _upgradeRoot, KeepBlinkingTheme.SurfaceScrim);
      FirstLevelUiFactory.Stretch(shade.rectTransform);
      _upgradeTitle = FirstLevelUiFactory.CreateText("Upgrade Title", _upgradeRoot, "STATION UPGRADE", 42f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_upgradeTitle.rectTransform, new Vector2(0.08f, 0.82f), new Vector2(0.92f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      CreateUpgradeCard(CareStationUpgradeId.MoreWorkers, 0.66f);
      CreateUpgradeCard(CareStationUpgradeId.LargerStorage, 0.47f);
      CreateUpgradeCard(CareStationUpgradeId.BiggerCart, 0.28f);
    }

    private void CreateUpgradeCard(CareStationUpgradeId id, float y)
    {
      var button = FirstLevelUiFactory.CreateButton(id.ToString(), _upgradeRoot, string.Empty, KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)button.transform, new Vector2(0.08f, y), new Vector2(0.92f, y + 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var cardText = FirstLevelUiFactory.CreateText("Card Text", button.transform, string.Empty, 22f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(cardText.rectTransform, new Vector2(0.055f, 0.08f), new Vector2(0.95f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      button.onClick.AddListener(() => UpgradeSelected?.Invoke(id));
      _upgradeButtons[id] = button;
      _upgradeCardTexts[id] = cardText;
    }

    private void BuildSubjectiveCheck()
    {
      _surveyRoot = FirstLevelUiFactory.CreateObject("Care Check", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_surveyRoot);
      var shade = FirstLevelUiFactory.CreateImage("Shade", _surveyRoot, KeepBlinkingTheme.SurfaceScrim);
      FirstLevelUiFactory.Stretch(shade.rectTransform);
      var panel = FirstLevelUiFactory.CreateImage("Panel", _surveyRoot, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(panel.rectTransform, new Vector2(0.06f, 0.12f), new Vector2(0.94f, 0.9f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _surveyTitle = FirstLevelUiFactory.CreateText("Title", panel.transform, "PRE-CARE CHECK", 38f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_surveyTitle.rectTransform, new Vector2(0.06f, 0.84f), new Vector2(0.94f, 0.96f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var labels = new[] { "EYE COMFORT", "DRYNESS", "EYE STRAIN", "FOCUS DIFFICULTY" };
      var ranges = new[] { "0  VERY UNCOMFORTABLE     10  VERY COMFORTABLE", "0  NONE     4  SEVERE", "0  NONE     4  SEVERE", "0  NONE     4  SEVERE" };
      for (var index = 0; index < labels.Length; index++)
      {
        var rowY = 0.70f - index * 0.145f;
        var label = FirstLevelUiFactory.CreateText("Score Label", panel.transform, labels[index], 24f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextPrimary);
        FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0.08f, rowY), new Vector2(0.46f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var range = FirstLevelUiFactory.CreateText("Score Range", panel.transform, ranges[index], 13f, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextSecondary, true);
        FirstLevelUiFactory.SetRect(range.rectTransform, new Vector2(0.08f, rowY - 0.05f), new Vector2(0.5f, rowY + 0.012f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var captured = index;
        var minus = FirstLevelUiFactory.CreateButton("Decrease Score", panel.transform, "-", KeepBlinkingTheme.SurfaceElevated);
        FirstLevelUiFactory.SetRect((RectTransform)minus.transform, new Vector2(0.55f, rowY - 0.02f), new Vector2(0.66f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        minus.onClick.AddListener(() => AdjustSurveyScore(captured, -1));
        var value = FirstLevelUiFactory.CreateText("Score Value", panel.transform, "--", 32f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentPrimary);
        FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(0.67f, rowY - 0.02f), new Vector2(0.79f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _surveyValues.Add(value);
        var plus = FirstLevelUiFactory.CreateButton("Increase Score", panel.transform, "+", KeepBlinkingTheme.SurfaceElevated);
        FirstLevelUiFactory.SetRect((RectTransform)plus.transform, new Vector2(0.80f, rowY - 0.02f), new Vector2(0.91f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        plus.onClick.AddListener(() => AdjustSurveyScore(captured, 1));
      }
      _surveyContinueButton = FirstLevelUiFactory.CreateButton("Continue Care Check", panel.transform, "CONTINUE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_surveyContinueButton.transform, new Vector2(0.12f, 0.055f), new Vector2(0.60f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _surveyContinueButton.onClick.AddListener(() =>
      {
        if (!_surveyDraft.HasAllResponses) return;
        var submitted = _surveyDraft.Clone();
        submitted.submitted = true;
        submitted.skipped = false;
        SubjectiveScoresSubmitted?.Invoke(_surveyIsPost, submitted);
      });
      var skip = FirstLevelUiFactory.CreateButton("Skip Care Check", panel.transform, "SKIP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)skip.transform, new Vector2(0.64f, 0.055f), new Vector2(0.88f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      skip.onClick.AddListener(() => SubjectiveScoresSkipped?.Invoke(_surveyIsPost));
      _surveyRoot.gameObject.SetActive(false);
    }

    private void BuildCareReport()
    {
      _reportRoot = FirstLevelUiFactory.CreateObject("Care Report", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_reportRoot);
      var shade = FirstLevelUiFactory.CreateImage("Shade", _reportRoot, KeepBlinkingTheme.SurfaceScrim);
      FirstLevelUiFactory.Stretch(shade.rectTransform);
      var panel = FirstLevelUiFactory.CreateImage("Panel", _reportRoot, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(panel.rectTransform, new Vector2(0.07f, 0.08f), new Vector2(0.93f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _reportText = FirstLevelUiFactory.CreateText("Report Text", panel.transform, "CARE REPORT", 24f, FontStyles.Normal, TextAlignmentOptions.TopLeft, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_reportText.rectTransform, new Vector2(0.08f, 0.20f), new Vector2(0.92f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      for (var index = 0; index < 4; index++)
      {
        var icon = FirstLevelUiFactory.CreateImage("Report Step", panel.transform, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(icon.rectTransform, new Vector2(0.5f, 0.17f), new Vector2(0.5f, 0.17f), new Vector2(0.5f, 0.5f), new Vector2((index - 1.5f) * 52f, 0f), new Vector2(24f, 24f));
        _reportStepIcons.Add(icon);
      }
      var done = FirstLevelUiFactory.CreateButton("Done", panel.transform, "DONE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)done.transform, new Vector2(0.22f, 0.035f), new Vector2(0.78f, 0.13f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      done.onClick.AddListener(() => CareReportDoneSelected?.Invoke());
      _reportRoot.gameObject.SetActive(false);
    }

    private void AdjustSurveyScore(int index, int delta)
    {
      if (_surveyDraft == null) _surveyDraft = new CareSubjectiveScores();
      var maximum = index == 0 ? 10 : 4;
      var current = index == 0 ? _surveyDraft.comfort
        : index == 1 ? _surveyDraft.dryness
        : index == 2 ? _surveyDraft.eyeStrain : _surveyDraft.focusDifficulty;
      current = current < 0 ? (delta > 0 ? 0 : maximum) : Mathf.Clamp(current + delta, 0, maximum);
      if (index == 0) _surveyDraft.comfort = current;
      else if (index == 1) _surveyDraft.dryness = current;
      else if (index == 2) _surveyDraft.eyeStrain = current;
      else _surveyDraft.focusDifficulty = current;
      SubjectiveScoresChanged?.Invoke(_surveyIsPost, _surveyDraft.Clone());
      RefreshSurvey();
    }

    private void RefreshSurvey()
    {
      if (_surveyDraft == null) _surveyDraft = new CareSubjectiveScores();
      var values = new[] { _surveyDraft.comfort, _surveyDraft.dryness, _surveyDraft.eyeStrain, _surveyDraft.focusDifficulty };
      for (var index = 0; index < _surveyValues.Count; index++)
        _surveyValues[index].text = values[index] < 0 ? "--" : values[index].ToString();
      _surveyContinueButton.interactable = _surveyDraft.HasAllResponses;
      var text = _surveyContinueButton.GetComponentInChildren<TextMeshProUGUI>();
      if (text != null) text.text = _surveyIsPost ? "VIEW REPORT" : "CONTINUE";
    }

    private void BuildComplete()
    {
      _completeRoot = FirstLevelUiFactory.CreateObject("Shift Complete", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_completeRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 560f));
      var panel = FirstLevelUiFactory.CreateImage("Panel", _completeRoot, KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(panel.rectTransform);
      _completeText = FirstLevelUiFactory.CreateText("Complete Text", _completeRoot, "SHIFT COMPLETE", 43f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_completeText.rectTransform, new Vector2(0.08f, 0.31f), new Vector2(0.92f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      for (var index = 0; index < 3; index++)
      {
        var icon = FirstLevelUiFactory.CreateImage("Completed Care Step", _completeRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(icon.rectTransform, new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.5f), new Vector2((index - 1) * 54f, 0f), new Vector2(24f, 24f));
        _completeStepIcons.Add(icon);
      }
      _endShiftButton = FirstLevelUiFactory.CreateButton("End Shift", _completeRoot, "END SHIFT", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_endShiftButton.transform, new Vector2(0.22f, 0.07f), new Vector2(0.78f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _endShiftButton.onClick.AddListener(() => EndShiftSelected?.Invoke());
    }

    private void BuildHud()
    {
      var top = FirstLevelUiFactory.CreateImage("Station Status Bar", _content, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.72f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(top.rectTransform, new Vector2(0.03f, 0.89f), new Vector2(0.97f, 0.98f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _stationText = FirstLevelUiFactory.CreateText("Station Number", top.transform, "STATION 1", 22f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_stationText.rectTransform, new Vector2(0.035f, 0.08f), new Vector2(0.28f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      CreateBottleCounter(top.transform, "Full Bottle Counter", new Vector2(0.31f, 0.1f), new Vector2(0.52f, 0.9f), KeepBlinkingTheme.AccentPrimary, out _fullBottleText);
      CreateBottleCounter(top.transform, "Gold Bottle Counter", new Vector2(0.54f, 0.1f), new Vector2(0.75f, 0.9f), KeepBlinkingTheme.AccentWarm, out _goldBottleText);
      _storageText = FirstLevelUiFactory.CreateText("Storage Capacity", top.transform, "0 / 24", 21f, FontStyles.Bold, TextAlignmentOptions.MidlineRight, KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(_storageText.rectTransform, new Vector2(0.75f, 0.08f), new Vector2(0.96f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      _xpReady = FirstLevelUiFactory.CreateText("Bottles Ready Compatibility", _content, string.Empty, 20f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(_xpReady.rectTransform, new Vector2(0.3f, 0.205f), new Vector2(0.7f, 0.235f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _statusText = FirstLevelUiFactory.CreateText("Primary Station Prompt", _content, string.Empty, 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary, false);
      FirstLevelUiFactory.SetRect(_statusText.rectTransform, new Vector2(0.1f, 0.205f), new Vector2(0.9f, 0.245f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void BuildDistanceSafetyWarning()
    {
      _distanceSafetyRoot = FirstLevelUiFactory.CreateObject("Distance Safety", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_distanceSafetyRoot, new Vector2(0.5f, 0.71f), new Vector2(0.5f, 0.71f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 190f));
      var phone = FirstLevelUiFactory.CreateImage("Phone", _distanceSafetyRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.82f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(phone.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(72f, 126f));
      var screen = FirstLevelUiFactory.CreateImage("Screen", phone.transform, KeepBlinkingTheme.BackgroundPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(screen.rectTransform, new Vector2(7f, 10f), new Vector2(-7f, -10f));
      for (var side = -1; side <= 1; side += 2)
      {
        var shaft = FirstLevelUiFactory.CreateImage("Outward Arrow", _distanceSafetyRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(shaft.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(side * 93f, 0f), new Vector2(78f, 8f));
        var head = FirstLevelUiFactory.CreateImage("Arrow Head", _distanceSafetyRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(side * 130f, side * 10f), new Vector2(30f, 8f));
        head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * 42f);
      }
      _distanceSafetyRoot.gameObject.SetActive(false);
    }

    private static void CreateBottleCounter(Transform parent, string name, Vector2 min, Vector2 max, Color color, out TextMeshProUGUI value)
    {
      var icon = FirstLevelUiFactory.CreateImage(name + " Icon", parent, color, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(icon.rectTransform, new Vector2(min.x, 0.5f), new Vector2(min.x, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, -3f), new Vector2(22f, 31f));
      var neck = FirstLevelUiFactory.CreateImage(name + " Neck", icon.transform, color, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(neck.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, -1f), new Vector2(11f, 8f));
      value = FirstLevelUiFactory.CreateText(name + " Value", parent, "0", 22f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(min.x + 0.045f, min.y), max, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void RefreshResourceHud()
    {
      if (_fullBottleText == null) return;
      var stored = _stationSave == null ? 0 : Mathf.Max(0, _stationSave.storedFullBottles + _stationSave.storedGoldBottles);
      var capacity = _stationSave == null ? 24 : Mathf.Max(1, _stationSave.storageHours);
      _storageFull = stored >= capacity;
      _stationText.text = $"STATION {(_stationSave == null ? 1 : Mathf.Max(1, _stationSave.stationLevel))}";
      _fullBottleText.text = (_stationSave == null ? 0 : Mathf.Max(0, _stationSave.storedFullBottles)).ToString();
      _goldBottleText.text = (_stationSave == null ? 0 : Mathf.Max(0, _stationSave.storedGoldBottles)).ToString();
      _storageText.text = $"{Mathf.Min(stored, capacity)} / {capacity}";
      if (_storageFill != null)
      {
        var fill = Mathf.Clamp01(stored / (float)capacity);
        _storageFill.fillAmount = fill;
        _storageFill.color = fill >= 0.85f ? KeepBlinkingTheme.AccentWarm : KeepBlinkingTheme.AccentPrimary;
      }
    }

    private static Vector3 CartScale(int capacity)
    {
      if (capacity >= 12) return new Vector3(1.48f, 1.32f, 1f);
      if (capacity >= 8) return new Vector3(1.32f, 1.22f, 1f);
      if (capacity >= 6) return new Vector3(1.14f, 1.10f, 1f);
      return Vector3.one;
    }

    private static string ResolveActionLabel(string prompt, string status)
    {
      if (!string.IsNullOrWhiteSpace(status))
      {
        if (!status.StartsWith("BOTTLES READY", StringComparison.Ordinal)) return status;
      }
      if (prompt == "REST") return "CLOSE YOUR EYES";
      if (prompt == "OPEN YOUR EYES") return "RETURN";
      return prompt ?? string.Empty;
    }
  }

  internal sealed class CareCrewPlaceholderView : MonoBehaviour
  {
    private CareCrewState _state;
    private RectTransform _rect;
    private Vector2 _restPosition;
    private float _stateStartedAt;

    private void Awake()
    {
      _rect = (RectTransform)transform;
      _restPosition = _rect.anchoredPosition;
    }

    public void SetState(CareCrewState state)
    {
      if (_state == state) return;
      _state = state;
      _stateStartedAt = Time.unscaledTime;
    }

    private void Update()
    {
      if (_rect == null) return;
      var t = Time.unscaledTime - _stateStartedAt;
      var speed = _state == CareCrewState.Walk || _state == CareCrewState.Carry ? 13f : _state == CareCrewState.Work ? 8f : 3.5f;
      var amplitude = _state == CareCrewState.Cheer
        ? Mathf.Max(0f, Mathf.Sin(t * Mathf.PI / 0.6f)) * 18f
        : _state == CareCrewState.Rest ? 2f : 4f;
      _rect.anchoredPosition = _restPosition + Vector2.up * (Mathf.Sin(t * speed) * amplitude);
      _rect.localRotation = Quaternion.Euler(0f, 0f, _state == CareCrewState.Walk ? Mathf.Sin(t * 9f) * 2f : 0f);
    }
  }

}

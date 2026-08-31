using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using KeepBlinking.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationView : MonoBehaviour
  {
    [SerializeField, Range(6f, 18f)] private float _pilotPupilRange = 13f;
    [SerializeField, Range(96f, 144f)] private float _pilotAxisRange = 128f;
    private readonly List<CareStationWorkerArtView> _crew = new List<CareStationWorkerArtView>(3);
    private readonly List<bool> _crewVisibilityBeforeGuidance = new List<bool>(3);
    private readonly List<GameObject> _stationLabels = new List<GameObject>(4);
    private readonly List<RectTransform> _carts = new List<RectTransform>(5);
    private readonly List<Image> _xpVisuals = new List<Image>(24);
    private readonly List<Image> _dustGroups = new List<Image>(3);
    private readonly List<Image> _dryCracks = new List<Image>(3);
    private readonly Dictionary<CareStationUpgradeId, Button> _upgradeButtons = new Dictionary<CareStationUpgradeId, Button>(3);
    private readonly Dictionary<CareStationUpgradeId, TextMeshProUGUI> _upgradeCardTexts = new Dictionary<CareStationUpgradeId, TextMeshProUGUI>(3);
    private RectTransform _safe;
    private RectTransform _content;
    private CanvasGroup _contentGroup;
    private RectTransform _hudRoot;
    private RectTransform _stationStage;
    private CanvasGroup _stationStageGroup;
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
    private TextMeshProUGUI _actionPurpose;
    private TextMeshProUGUI _recipeTitle;
    private TextMeshProUGUI _recipeStepText;
    private Image _actionProgress;
    private Image _actionVisualRing;
    private Image _distanceCoreFill;
    private Image _distanceWave;
    private readonly List<Image> _distanceStepLights = new List<Image>(4);
    private readonly List<RectTransform> _distanceGuideDots = new List<RectTransform>(4);
    private readonly List<Image> _recipeStepDots = new List<Image>(4);
    private readonly List<Image> _routineDockDots = new List<Image>(4);
    private readonly List<TextMeshProUGUI> _routineDockLabels = new List<TextMeshProUGUI>(4);
    private readonly List<Image> _navigationTabs = new List<Image>(3);
    private readonly List<Button> _navigationButtons = new List<Button>(3);
    private readonly List<TextMeshProUGUI> _navigationLabels = new List<TextMeshProUGUI>(3);
    private readonly List<Button> _deviceButtons = new List<Button>(3);
    private readonly Dictionary<Button, UnityAction> _ownedButtonBindings = new Dictionary<Button, UnityAction>();
    private readonly Dictionary<RectTransform, CanvasGroup> _panelGroups = new Dictionary<RectTransform, CanvasGroup>();
    private readonly Dictionary<Graphic, bool> _panelGraphicRaycastDefaults = new Dictionary<Graphic, bool>();
    private readonly List<Image> _stationTracks = new List<Image>(3);
    private readonly List<Image> _packerLayers = new List<Image>(2);
    private readonly List<Image> _liquidTransportSegments = new List<Image>(8);
    private readonly List<Image> _manualCarryMarkers = new List<Image>(12);
    private readonly List<Image> _conveyorSegments = new List<Image>(16);
    private static readonly Vector2 FillerBottleAnchor = new Vector2(0.73f, 0.655f);
    private static readonly Vector2 PackerBottleAnchor = new Vector2(0.73f, 0.335f);
    private static readonly Vector2 StorageBottleAnchor = new Vector2(0.27f, 0.295f);
    private static readonly Vector2 ManualFillerPickupAnchor = new Vector2(0.86f, 0.58f);
    private static readonly Vector2 ManualPackerHandoffAnchor = new Vector2(0.84f, 0.39f);
    private static readonly Vector2 ManualStoragePickupAnchor = new Vector2(0.55f, 0.31f);
    private static readonly Color WorkshopBackdrop = new Color32(24, 53, 49, 255);
    private static readonly Color WorkshopWall = new Color32(35, 72, 66, 255);
    private static readonly Color WorkshopFloor = new Color32(43, 65, 57, 255);
    private static readonly Color WorkshopOutline = new Color32(60, 43, 31, 255);
    private static readonly Color WorkshopWood = new Color32(119, 77, 42, 255);
    private static readonly Color WorkshopWoodLight = new Color32(151, 102, 56, 255);
    private static readonly Color WorkshopMetal = new Color32(67, 103, 114, 255);
    private static readonly Color WorkshopMetalLight = new Color32(100, 132, 137, 255);
    private static readonly Color WorkshopPaper = new Color32(218, 196, 148, 255);
    private static readonly Color WorkshopPaperDim = new Color32(174, 157, 122, 255);
    private static readonly Color WorkshopInk = new Color32(68, 48, 34, 255);
    private static readonly Color WorkshopMint = new Color32(101, 194, 171, 255);
    private RectTransform _guidedOrbitDot;
    private RectTransform _pilotRoot;
    private readonly List<Image> _pilotAxes = new List<Image>(4);
    private readonly List<Image> _pilotEndpoints = new List<Image>(8);
    private RectTransform _pilotLeftPupil;
    private RectTransform _pilotRightPupil;
    private RectTransform _pilotGuideDot;
    private RectTransform _routineDock;
    private Button _routinePrimaryButton;
    private TextMeshProUGUI _routineDockTitle;
    private TextMeshProUGUI _routineHintText;
    private TextMeshProUGUI _routinePrimaryText;
    private RectTransform _navigationRoot;
    private Image _upgradeOpportunityDot;
    private RectTransform _productionBottle;
    private Image _productionBottleBody;
    private Image _productionBottleLiquid;
    private RectTransform _productionBottleLiquidMask;
    private Image _productionBottleLiquidSurface;
    private Image _productionBottleCap;
    private Image _productionBottleLabel;
    private Image _productionPackage;
    private Image _baseInputPipe;
    private Image _filteredLiquidPipe;
    private Image _bottleConveyor;
    private Image _packedBottleRoute;
    private Image _storageToCartRoute;
    private RectTransform _manualCarryRoot;
    private RectTransform _basicConveyorRoot;
    private RectTransform _manualFilterHoseRoot;
    private RectTransform _fixedFilterPipeRoot;
    private RectTransform _workerFillerPickupAnchor;
    private RectTransform _workerPackerHandoffAnchor;
    private RectTransform _workerStorageHandoffAnchor;
    private CareProductionTransportMode _transportMode = CareProductionTransportMode.ManualCarry;
    private Vector2 _productionCartHome;
    private Image _careDimmer;
    private RectTransform _phoneIcon;
    private TextMeshProUGUI _statusText;
    private TextMeshProUGUI _xpReady;
    private TextMeshProUGUI _stationText;
    private TextMeshProUGUI _fullBottleText;
    private TextMeshProUGUI _goldBottleText;
    private TextMeshProUGUI _storageText;
    private TextMeshProUGUI _filterStatusText;
    private TextMeshProUGUI _fillerStatusText;
    private TextMeshProUGUI _packerStatusText;
    private TextMeshProUGUI _storageStatusText;
    private TextMeshProUGUI _cartStatusText;
    private TextMeshProUGUI _toastText;
    private Image _storageFill;
    private RectTransform _welcomeRoot;
    private TextMeshProUGUI _welcomeLines;
    private TextMeshProUGUI _welcomeTitle;
    private RectTransform _upgradeRoot;
    private TextMeshProUGUI _upgradeTitle;
    private RectTransform _completeRoot;
    private TextMeshProUGUI _completeText;
    private readonly List<Image> _completeStepIcons = new List<Image>(4);
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
    private RectTransform _storageTank;
    private RectTransform _cart;
    private RectTransform _distanceSafetyRoot;
    private RectTransform _restIcon;
    private CareStationFilterArtView _filterArt;
    private Image _filterBody;
    private Image _fillerBody;
    private Image _fillerLevel;
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
    private float _focusLegPulseUntil;
    private float _careEnergyPulseUntil;
    private CareDistanceDirection _focusLegPulseDirection;
    private bool _focusLegPulseActive;
    private int _pipelineMask;
    private Vector3 _storageBaseScale = Vector3.one;
    private bool _storageFull;
    private bool _productionAnimating;
    private CareProductionStage _renderedProductionStage = CareProductionStage.None;
    private float _upgradeFeedbackUntil;
    private CareStationUpgradeId _upgradeFeedbackCard;
    private float _toastUntil;
    private EyeMovementGuidanceOverlay _eyeMovementGuidance;
    private bool _guidanceMode;
    private bool _guidanceHudDebugVisible;
    private bool _hudWasVisible;
    private bool _transportWasVisible;
    private bool _routineWasVisible;
    private bool _navigationWasVisible;
    private bool _actionWasVisible;
    private bool _incidentWasVisible;
    private float _developmentGuidancePreviewUntil = -1f;
    private readonly List<CareEnergyFlight> _careEnergyFlights = new List<CareEnergyFlight>(8);

    private sealed class CareEnergyFlight
    {
      public RectTransform rect;
      public Image image;
      public Vector2 start;
      public Vector2 end;
      public float startedAt;
      public float duration;
    }

    public event Action IncidentSelected;
    public event Action StartCareSelected;
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
    public event Action<int> NavigationSelected;
    public event Action<string> DeviceSelected;
    public event Action UpgradeBackSelected;
    public event Action<CareStationUpgradeId> UpgradeSelected;

    public void Build()
    {
      if (_safe != null)
      {
        FirstLevelUiFactory.RecoverUiInput(_safe, _group);
        BindInputHandlers();
        RecoverBaseInputIfUnblocked();
        return;
      }
      _safe = FirstLevelUiFactory.CreateCanvas(transform, "Eye Care Station Canvas", 500, out _, out _group);
      var background = FirstLevelUiFactory.CreateImage("Station Backdrop", _safe, WorkshopBackdrop);
      FirstLevelUiFactory.Stretch(background.rectTransform);
      BuildWorkshopBackdrop(background.transform);

      _content = FirstLevelUiFactory.CreateObject("Comfort Padded Content", _safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_content, new Vector2(28f, 34f), new Vector2(-28f, -42f));
      _contentGroup = _content.gameObject.AddComponent<CanvasGroup>();

      BuildHud();
      BuildStationStage();
      BuildStorage();
      BuildCareRoutineDock();
      BuildNavigation();
      // v21 removes Incident cards from the player flow. Legacy fields remain
      // deserializable, but no Incident presentation is constructed.
      _careDimmer = FirstLevelUiFactory.CreateImage("Care Dimmer", _content, Color.clear);
      FirstLevelUiFactory.Stretch(_careDimmer.rectTransform);
      _careDimmer.raycastTarget = false;
      BuildActionOverlay();
      _eyeMovementGuidance = EyeMovementGuidanceOverlay.Create(_safe);
      BuildChangeStepConfirmation();
      BuildWelcome();
      BuildUpgrade();
      BuildSubjectiveCheck();
      BuildCareReport();
      BuildComplete();
      BuildDistanceSafetyWarning();
      BuildToast();
      // Modal scrims must never cover the persistent bottom navigation.
      _navigationRoot.SetAsLastSibling();
      HideAllModals();
      BindInputHandlers();
      FirstLevelUiFactory.RecoverUiInput(_safe, _group);
    }

    public void ApplyStation(CareStationSaveData save)
    {
      if (save == null) return;
      _stationSave = save;
      // Formal workers are not part of the Station L1 visual pass. Keep every
      // legacy droplet worker hidden while the persisted production state and
      // reserved handoff anchors remain available for the future art hookup.
      for (var i = 0; i < _crew.Count; i++)
        _crew[i].gameObject.SetActive(false);
      if (_filterArt != null) _filterArt.SetLevel(1, true);
      CareProductionTransportRules.Synchronize(save);
      ApplyTransportModeVisuals(save.productionTransportMode);
      var constructionScale = 1f + Mathf.Min(3, save.stationConstructionState) * 0.012f;
      var storageScale = save.storageLevel == 2 ? new Vector3(1.03f, 1.02f, 1f)
        : save.storageLevel == 3 ? new Vector3(1.06f, 1.04f, 1f)
        : save.storageLevel >= 4 ? new Vector3(1.09f, 1.06f, 1f) : Vector3.one;
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
      SetUpgradeOpportunity(save.upgradeOffered);
    }

    internal CareProductionTransportMode VisibleTransportMode => _transportMode;

    private void BuildWorkshopBackdrop(Transform parent)
    {
      var wall = FirstLevelUiFactory.CreateImage("Warm Teal Workshop Wall", parent, WorkshopWall);
      FirstLevelUiFactory.SetRect(wall.rectTransform, Vector2.zero, new Vector2(1f, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      wall.transform.SetAsFirstSibling();
      var floor = FirstLevelUiFactory.CreateImage("Workshop Floor", parent, WorkshopFloor);
      FirstLevelUiFactory.SetRect(floor.rectTransform, Vector2.zero, new Vector2(1f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      floor.transform.SetSiblingIndex(1);
      for (var index = 0; index < 5; index++)
      {
        var seam = FirstLevelUiFactory.CreateImage(
          "Irregular Wall Seam " + index,
          parent,
          new Color32(54, 86, 76, 96),
          FirstLevelUiFactory.RoundedSprite);
        var y = 0.15f + index * 0.145f;
        FirstLevelUiFactory.SetRect(seam.rectTransform, new Vector2(0.02f, y), new Vector2(0.98f, y), new Vector2(0.5f, 0.5f), new Vector2(index % 2 == 0 ? -7f : 5f, 0f), new Vector2(-22f, 3f));
        seam.rectTransform.localRotation = Quaternion.Euler(0f, 0f, index % 2 == 0 ? -0.35f : 0.28f);
        seam.raycastTarget = false;
      }
    }

    public void SetCrewState(CareCrewState state)
    {
      var workTargets = new[]
      {
        CareStationDisplayNames.Filter,
        CareStationDisplayNames.Filler,
        CareStationDisplayNames.Packer,
      };
      for (var i = 0; i < _crew.Count; i++)
        if (_crew[i].gameObject.activeSelf)
          _crew[i].SetState(state, state == CareCrewState.Work ? workTargets[i % workTargets.Length] : string.Empty);
    }

    public void ShowWelcome(CareStationOfflineResult result)
    {
      HideAllModals();
      SetPanelVisible(_welcomeRoot, true);
      if (_welcomeTitle != null) _welcomeTitle.text = "WHILE YOU WERE AWAY";
      var lines = new List<string>(5);
      if (_stationSave != null && _stationSave.lastCartCoinsEarned > 0)
        lines.Add($"+{_stationSave.lastCartCoinsEarned} COINS");
      if (_stationSave != null && _stationSave.lastAutoProducedBottles > 0)
        lines.Add($"+{_stationSave.lastAutoProducedBottles} FULL BOTTLES");
      if (result.CreditedDuration > TimeSpan.Zero)
        lines.Add($"{(int)result.CreditedDuration.TotalHours}H {result.CreditedDuration.Minutes:D2}M WORKED");
      if (_stationSave != null && _stationSave.offlineProductionPausedByFullStorage)
      {
        lines.Add("STORAGE FULL");
        lines.Add("PRODUCTION PAUSED");
      }
      _welcomeLines.text = string.Join("\n", lines);
      if (_stationSave != null && _stationSave.lastCartCoinsEarned > 0)
        SetFactoryStatus(string.Empty, "IDLE", "IDLE", "IDLE", "IDLE", "SELLING");
      else
        SetFactoryStatus(string.Empty);
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
      if (_storageToCartRoute != null)
        _storageToCartRoute.color = KeepBlinkingTheme.WithAlpha(
          _stationSave != null && _stationSave.lastCartCoinsEarned > 0
            ? KeepBlinkingTheme.AccentWarm
            : KeepBlinkingTheme.BorderReadable,
          _stationSave != null && _stationSave.lastCartCoinsEarned > 0 ? 0.86f : 0.3f);
    }

    public void ShowIncident(CareStationIncidentType incident, bool selectable)
    {
      // Legacy callable kept for binary/test compatibility. It deliberately
      // renders the normal station instead of any retired Incident content.
      ShowStationWorking();
    }

    public void ShowStationWorking()
    {
      HideAllModals();
      SetFactoryStatus(string.Empty);
      SetCrewState(CareCrewState.Work);
      SetRoutinePrimary("START CARE");
      SetProductionAnimation(false);
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
      var filterCheckComplete = (save.inspectionCompletedMask & CareStationInspectionRules.FilterCheck) != 0;
      if (_filterArt != null) _filterArt.SetPipelineHighlighted(filterCheckComplete);
      if (_filterBody != null) _filterBody.color = filterCheckComplete
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.22f);
      if (_fillerBody != null) _fillerBody.color = (save.inspectionCompletedMask & CareStationInspectionRules.FlowCheck) != 0
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.22f);
      var coreComplete = (save.inspectionCompletedMask & CareStationInspectionRules.CoreCheck) != 0;
      for (var i = 0; i < _packerLayers.Count; i++)
        _packerLayers[i].color = coreComplete
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

    public void ShowProductionStage(
      CareProductionStage stage,
      float progress,
      CareStationSaveData save)
    {
      progress = Mathf.Clamp01(progress);
      if (_renderedProductionStage != stage)
      {
        HideAllModals();
        _renderedProductionStage = stage;
      }
      _stationSave = save ?? _stationSave;
      SetProductionAnimation(stage != CareProductionStage.None);
      if (_routinePrimaryButton != null) _routinePrimaryButton.interactable = false;

      var bottleVisible = stage >= CareProductionStage.FillerCreateBottle;
      if (_productionBottle != null) _productionBottle.gameObject.SetActive(bottleVisible);
      if (_productionBottleLiquid != null)
      {
        var fill = stage == CareProductionStage.FillerFilling
          ? progress
          : stage > CareProductionStage.FillerFilling ? 1f : 0f;
        SetProductionBottleFill(fill, bottleVisible);
      }
      if (_productionBottleBody != null)
      {
        var alpha = stage == CareProductionStage.FillerCreateBottle ? Mathf.Lerp(0.2f, 0.76f, progress) : 0.76f;
        _productionBottleBody.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, alpha);
      }
      if (_productionBottleCap != null)
      {
        _productionBottleCap.gameObject.SetActive(stage >= CareProductionStage.PackerCapping);
        _productionBottleCap.color = KeepBlinkingTheme.WithAlpha(
          WorkshopMetal,
          stage == CareProductionStage.PackerCapping ? progress : 1f);
      }
      if (_productionBottleLabel != null)
      {
        _productionBottleLabel.gameObject.SetActive(stage >= CareProductionStage.PackerLabeling);
        _productionBottleLabel.color = KeepBlinkingTheme.WithAlpha(
          WorkshopPaper,
          stage == CareProductionStage.PackerLabeling ? progress : 0.88f);
      }
      if (_productionPackage != null)
      {
        _productionPackage.gameObject.SetActive(stage >= CareProductionStage.PackerPackaging);
        _productionPackage.color = KeepBlinkingTheme.WithAlpha(
          WorkshopPaper,
          stage == CareProductionStage.PackerPackaging ? Mathf.Lerp(0.05f, 0.42f, progress) : 0.42f);
      }

      var anchor = FillerBottleAnchor;
      if (stage == CareProductionStage.TransferToPacker)
        anchor = _transportMode == CareProductionTransportMode.ManualCarry
          ? EvaluatePolyline(
            new[] { FillerBottleAnchor, ManualFillerPickupAnchor, ManualPackerHandoffAnchor, PackerBottleAnchor },
            Mathf.SmoothStep(0f, 1f, progress))
          : Vector2.Lerp(FillerBottleAnchor, PackerBottleAnchor, Mathf.SmoothStep(0f, 1f, progress));
      else if (stage >= CareProductionStage.PackerCapping && stage < CareProductionStage.TransferToStorage)
        anchor = PackerBottleAnchor;
      else if (stage == CareProductionStage.TransferToStorage)
        anchor = _transportMode == CareProductionTransportMode.ManualCarry
          ? EvaluatePolyline(
            new[] { PackerBottleAnchor, ManualStoragePickupAnchor, StorageBottleAnchor },
            Mathf.SmoothStep(0f, 1f, progress))
          : Vector2.Lerp(PackerBottleAnchor, StorageBottleAnchor, Mathf.SmoothStep(0f, 1f, progress));
      else if (stage == CareProductionStage.WaitingForStorage)
        anchor = StorageBottleAnchor;
      if (_productionBottle != null)
      {
        _productionBottle.anchorMin = anchor;
        _productionBottle.anchorMax = anchor;
        _productionBottle.anchoredPosition = Vector2.zero;
        _productionBottle.localScale = stage == CareProductionStage.FillerCreateBottle
          ? Vector3.one * Mathf.Lerp(0.72f, 1f, progress)
          : Vector3.one;
      }

      var filterActive = stage == CareProductionStage.FilterProcessing ||
                         stage == CareProductionStage.TransferFilteredLiquid;
      var fillerActive = stage >= CareProductionStage.FillerCreateBottle &&
                         stage <= CareProductionStage.TransferToPacker;
      var packerActive = stage >= CareProductionStage.PackerCapping &&
                         stage <= CareProductionStage.TransferToStorage;
      _pipelineMask = (filterActive ? CareRecipePipeline.Filter : 0) |
                      (fillerActive ? CareRecipePipeline.Filler : 0) |
                      (packerActive ? CareRecipePipeline.Packer : 0) |
                      (stage != CareProductionStage.None ? CareRecipePipeline.Rail : 0);
      ApplyPipelineVisuals();
      if (_filterArt != null)
      {
        _filterArt.SetIntegratedBottleVisible(false);
        _filterArt.SetProductionVisual(
          filterActive ? FilterProductionVisualState.Filtering : FilterProductionVisualState.Idle,
          stage == CareProductionStage.TransferFilteredLiquid ? 1f : progress);
      }
      if (_baseInputPipe != null)
        _baseInputPipe.color = KeepBlinkingTheme.WithAlpha(
          filterActive ? WorkshopMint : WorkshopPaperDim,
          filterActive ? 0.92f : 0.34f);
      SetRouteColor(
        _liquidTransportSegments,
        stage == CareProductionStage.TransferFilteredLiquid,
        WorkshopMetalLight);
      SetRouteColor(
        _manualCarryMarkers,
        stage == CareProductionStage.TransferToPacker || stage == CareProductionStage.TransferToStorage,
        WorkshopPaperDim);
      SetRouteColor(
        _conveyorSegments,
        stage == CareProductionStage.TransferToPacker || stage == CareProductionStage.TransferToStorage,
        WorkshopMetal);
      if (_storageToCartRoute != null)
        _storageToCartRoute.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.3f);

      ApplyProductionStageStatus(stage);
      SetRoutinePrimary(stage == CareProductionStage.WaitingForStorage
        ? "START CARE"
        : "CARE AFTER THIS BOTTLE");
      RefreshResourceHud();
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
      ExitEyeMovementGuidance(true);
      _renderedCareActionType = CareActionType.None;
      SetPanelVisible(_welcomeRoot, false);
      SetPanelVisible(_upgradeRoot, false);
      SetPanelVisible(_completeRoot, false);
      SetPanelVisible(_incidentRoot, false);
      SetPanelVisible(_actionRoot, true);
      _actionPrompt.text = ResolveActionLabel(prompt, status);
      if (_actionPurpose != null) _actionPurpose.text = string.Empty;
      SetRoutinePrimary(_actionPrompt.text);
      SetProductionAnimation(false);
      _statusText.text = string.Empty;
      _actionProgress.fillAmount = Mathf.Clamp01(progress);
      _actionGroup.alpha = dimmed ? 0.48f : 1f;
      _careDimmer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackdropClosedEye, dimmed ? 0.52f : 0f);
      _phoneIcon.gameObject.SetActive(prompt == "SEND BOTTLES");
      _restIcon.gameObject.SetActive(prompt == "REST" || prompt == "OPEN YOUR EYES" || status == "CLOSE YOUR EYES");
      if (_pilotRoot != null) _pilotRoot.gameObject.SetActive(false);
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

      _recipeTitle.text = RoutineTitle(recipe);
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

    public void ShowCareRoutineIntro(CareRecipeSaveData recipe)
    {
      ShowAction("EYE CARE ROUTINE", 0f, false);
      if (_actionPurpose != null)
        _actionPurpose.text = "2–3 MINUTES\nCOMPLETE EVERY PLANNED STEP.";
      ConfigureRecipe(recipe);
      SetRoutinePrimary("EYE CARE ROUTINE");
      if (_careCoreInner != null)
        _careCoreInner.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.42f);
    }

    public void PlayRecipePipelineStep(
      int completedStepIndex,
      int actionCount,
      CareActionType action = CareActionType.None)
    {
      _pipelineMask |= action == CareActionType.None
        ? CareRecipePipeline.StageMaskForCompletion(completedStepIndex, actionCount)
        : CareRecipePipeline.StageMaskForAction(action);
      _pipelinePulseUntil = Time.unscaledTime + 0.55f;
      ApplyPipelineVisuals();
    }

    public void PlayFocusLegFeedback(CareDistanceDirection direction)
    {
      if (direction != CareDistanceDirection.Closer && direction != CareDistanceDirection.Away) return;
      _focusLegPulseDirection = direction;
      _focusLegPulseUntil = Time.unscaledTime + 0.65f;
      _focusLegPulseActive = true;
      var on = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.86f);
      if (direction == CareDistanceDirection.Closer)
      {
        for (var i = 0; i < _packerLayers.Count; i++) _packerLayers[i].color = on;
      }
      else
      {
        if (_fillerBody != null) _fillerBody.color = on;
        if (_fillerLevel != null) _fillerLevel.color = KeepBlinkingTheme.AccentPrimary;
      }
      if (_careCoreInner != null)
        _careCoreInner.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.5f);
    }

    public void RestoreRecipePipeline(CareRecipeSaveData recipe)
    {
      _pipelineMask = 0;
      if (recipe != null)
      {
        for (var index = 0; index < recipe.ActionCount; index++)
          if (recipe.IsStepCompleted(index))
            _pipelineMask |= CareRecipePipeline.StageMaskForAction(recipe.actionList[index]);
      }
      ApplyPipelineVisuals();
    }

    public void ShowRecipeStepFeedback(
      CareRecipeSaveData recipe,
      CareActionType completedAction = CareActionType.None,
      int energyGranted = 0)
    {
      var continueToGuided = completedAction == CareActionType.PilotEyeRoutine && recipe != null &&
                             !recipe.recipeCompleted && recipe.CurrentAction == CareActionType.GuidedEyeCircles;
      if (continueToGuided)
      {
        EnterEyeMovementGuidance();
        _eyeMovementGuidance.PresentPilotToGuidedHold(energyGranted);
        SetPanelVisible(_actionRoot, false);
        SetPanelVisible(_incidentRoot, false);
        if (_goldBottleText != null && _stationSave != null)
          _goldBottleText.text = Mathf.Max(0, _stationSave.careEnergy).ToString();
        if (energyGranted > 0) PlayCareEnergyFlight(energyGranted);
        ConfigureRecipe(recipe);
        return;
      }
      ExitEyeMovementGuidance(false);
      SetPanelVisible(_actionRoot, false);
      SetPanelVisible(_incidentRoot, false);
      _careDimmer.color = Color.clear;
      _statusText.text = recipe != null && recipe.recipeCompleted
        ? "ROUTINE COMPLETE · 12 ENERGY"
        : energyGranted > 0
          ? $"+{energyGranted} CARE ENERGY"
          : completedAction == CareActionType.ClosedEyeRest ? "REST COMPLETE" : "STEP COMPLETE";
      if (_goldBottleText != null && _stationSave != null)
        _goldBottleText.text = Mathf.Max(0, _stationSave.careEnergy).ToString();
      if (energyGranted > 0) PlayCareEnergyFlight(energyGranted);
      SetCrewState(CareCrewState.Work);
      ConfigureRecipe(recipe);
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
    }

    private void ApplyPipelineVisuals()
    {
      var off = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.34f);
      var on = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.78f);
      var filterActive = (_pipelineMask & CareRecipePipeline.Filter) != 0;
      if (_filterArt != null) _filterArt.SetPipelineHighlighted(filterActive);
      if (_filterBody != null) _filterBody.color = filterActive ? on : off;
      if (_fillerBody != null) _fillerBody.color = (_pipelineMask & CareRecipePipeline.Filler) != 0 ? on : off;
      if (_fillerLevel != null) _fillerLevel.color = (_pipelineMask & CareRecipePipeline.Filler) != 0
        ? KeepBlinkingTheme.AccentPrimary
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.28f);
      for (var i = 0; i < _packerLayers.Count; i++)
        _packerLayers[i].color = (_pipelineMask & CareRecipePipeline.Packer) != 0
          ? on
          : KeepBlinkingTheme.WithAlpha(i == 0 ? KeepBlinkingTheme.TextSecondary : KeepBlinkingTheme.AccentSoft, 0.36f);
      if (_careCoreInner != null) _careCoreInner.color = (_pipelineMask & CareRecipePipeline.CareCore) != 0
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.42f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.96f);
      for (var i = 0; i < _stationTracks.Count; i++)
      {
        var bit = i == 0 ? CareRecipePipeline.Filter : i == 1 ? CareRecipePipeline.Filler : CareRecipePipeline.Packer;
        _stationTracks[i].color = (_pipelineMask & bit) != 0
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.68f)
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.22f);
      }
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
      int completedDistanceSteps,
      bool showIntroPurpose = false)
    {
      if (type == CareActionType.PilotEyeRoutine || type == CareActionType.GuidedEyeCircles)
      {
        EnterEyeMovementGuidance();
        _eyeMovementGuidance.Present(type, phase, prompt);
        SetPanelVisible(_actionRoot, false);
        SetPanelVisible(_incidentRoot, false);
        _changeStepButton.gameObject.SetActive(false);
        _fallbackButton.gameObject.SetActive(false);
        _returnFallbackButton.gameObject.SetActive(false);
        _statusText.text = string.Empty;
        _renderedCareActionType = type;
        _renderedCareActionPhase = phase;
        return;
      }

      ExitEyeMovementGuidance(true);
      var guidedEyesClosed = type == CareActionType.GuidedEyeCircles &&
                             (phase == CareActionInternalPhase.GuidedPromptClose ||
                              phase == CareActionInternalPhase.GuidedClosedRest ||
                              phase == CareActionInternalPhase.GuidedWaitReopen);
      ShowAction(prompt, progress, type == CareActionType.ClosedEyeRest || guidedEyesClosed);
      if (type == CareActionType.FocusShift && _renderedCareActionPhase != phase)
        _actionStepPulseUntil = Time.unscaledTime + 0.35f;
      _renderedCareActionType = type;
      _renderedCareActionPhase = phase;
      if (_actionPurpose != null)
      {
        var intro = phase == CareActionInternalPhase.FocusIntro ||
                    phase == CareActionInternalPhase.ClosedEyeIntro ||
                    phase == CareActionInternalPhase.PilotIntro ||
                    showIntroPurpose && type == CareActionType.GuidedEyeCircles &&
                    (phase == CareActionInternalPhase.GuidedPreviewClockwise ||
                     phase == CareActionInternalPhase.GuidedPreviewCounterClockwise);
        var safety = type == CareActionType.GuidedEyeCircles || type == CareActionType.PilotEyeRoutine
          ? "\nMOVE GENTLY. STOP IF UNCOMFORTABLE."
          : string.Empty;
        _actionPurpose.text = intro
          ? CareActionLibrary.Purpose(type) + safety + "\n" + CareActionLibrary.StationPurpose(type)
          : CareActionLibrary.StationPurpose(type);
      }
      _phoneIcon.gameObject.SetActive(false);
      _restIcon.gameObject.SetActive(type == CareActionType.ClosedEyeRest ||
                                     guidedEyesClosed || type == CareActionType.GuidedEyeCircles);
      if (_pilotRoot != null) _pilotRoot.gameObject.SetActive(type == CareActionType.PilotEyeRoutine);
      if (_guidedOrbitDot != null)
      {
        var circles = type == CareActionType.GuidedEyeCircles && !guidedEyesClosed;
        _guidedOrbitDot.gameObject.SetActive(circles);
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

    public void RenderCareActionMotionData(CareActionSaveData data)
    {
      if (data == null) return;
      if (data.actionType == CareActionType.PilotEyeRoutine || data.actionType == CareActionType.GuidedEyeCircles)
      {
        _eyeMovementGuidance?.Render(data);
        return;
      }
      if (data.actionType == CareActionType.GuidedEyeCircles && _guidedOrbitDot != null &&
          _guidedOrbitDot.gameObject.activeSelf)
      {
        var counter = data.internalPhase == CareActionInternalPhase.GuidedCounterClockwise;
        var turns = data.guidedLapCount + Mathf.Clamp01(data.guidedNormalizedProgress);
        var angle = (counter ? 1f : -1f) * turns * Mathf.PI * 2f + Mathf.PI * 0.5f;
        _guidedOrbitDot.anchoredPosition = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 76f;
        var lap = Mathf.Clamp(data.guidedLapCount + 1, 1, 3);
        if (data.internalPhase == CareActionInternalPhase.GuidedClockwise)
          _actionPrompt.text = $"CLOCKWISE\n{lap} / 3";
        else if (data.internalPhase == CareActionInternalPhase.GuidedCounterClockwise)
          _actionPrompt.text = $"COUNTERCLOCKWISE\n{lap} / 3";
      }
      if (data.actionType != CareActionType.PilotEyeRoutine || _pilotRoot == null) return;
      RenderPilot(data);
    }

    public void AdjustPilotPupilRangeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeMovementGuidance?.AdjustPupilRangeDevelopment();
#endif
    }

    public void AdjustPilotAxisRangeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeMovementGuidance?.AdjustGuideSizeDevelopment();
#endif
    }

    public void PreviewFullscreenPilotDevelopment(int axis = 0)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Build();
      axis = Mathf.Clamp(axis, 0, 3);
      var data = new CareActionSaveData
      {
        actionType = CareActionType.PilotEyeRoutine,
        stage = CareActionStage.Active,
        internalPhase = axis == 0 ? CareActionInternalPhase.PilotVertical :
          axis == 1 ? CareActionInternalPhase.PilotHorizontal :
          axis == 2 ? CareActionInternalPhase.PilotDiagonalA : CareActionInternalPhase.PilotDiagonalB,
        pilotCurrentAxis = axis,
        pilotCurrentRound = axis == 0 ? 1 : 2,
        pilotCurrentEndpoint = 1,
        pilotNormalizedMoveProgress = 0.25f,
      };
      RenderCareAction(data.actionType, data.internalPhase, CareActionRuntimePromptForPilot(axis),
        0.4f, 1f, 0f, CareDistanceDirection.None, 0);
      RenderCareActionMotionData(data);
      _developmentGuidancePreviewUntil = Time.unscaledTime + 15f;
#endif
    }

    public void PreviewPilotToGuidedTransitionDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Build();
      EnterEyeMovementGuidance();
      _eyeMovementGuidance?.PresentPilotToGuidedHold();
      _developmentGuidancePreviewUntil = Time.unscaledTime + 15f;
#endif
    }

    public void ToggleStationHudDuringGuidanceDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _guidanceHudDebugVisible = !_guidanceHudDebugVisible;
      if (_guidanceMode) SetStationHudForGuidance(_guidanceHudDebugVisible);
#endif
    }

    public void AdjustGuidanceWorkerSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeMovementGuidance?.AdjustWorkerSizeDevelopment();
#endif
    }

    public void AdjustGuidanceEyeSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeMovementGuidance?.AdjustEyeSizeDevelopment();
#endif
    }

    public void ToggleGuidanceSafeAreaDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeMovementGuidance?.ToggleSafeAreaDevelopment();
#endif
    }

    public void CapturePilotLayoutDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      PreviewFullscreenPilotDevelopment();
      StartCoroutine(CapturePilotLayoutAfterFrame());
#endif
    }

    private IEnumerator CapturePilotLayoutAfterFrame()
    {
      yield return new WaitForEndOfFrame();
      var folder = Path.Combine(Application.persistentDataPath, "KeepBlinking", "Captures");
      Directory.CreateDirectory(folder);
      var path = Path.Combine(folder, $"pilot-layout-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
      ScreenCapture.CaptureScreenshot(path);
      Debug.Log($"[CareStation] Pilot layout captured: {path}");
    }

    private void EnterEyeMovementGuidance()
    {
      if (_guidanceMode) return;
      _guidanceMode = true;
      _hudWasVisible = _hudRoot != null && _hudRoot.gameObject.activeSelf;
      _transportWasVisible = _transportRoot != null && _transportRoot.gameObject.activeSelf;
      _routineWasVisible = _routineDock != null && _routineDock.gameObject.activeSelf;
      _navigationWasVisible = _navigationRoot != null && _navigationRoot.gameObject.activeSelf;
      _actionWasVisible = _actionRoot != null && _actionRoot.gameObject.activeSelf;
      _incidentWasVisible = _incidentRoot != null && _incidentRoot.gameObject.activeSelf;
      _crewVisibilityBeforeGuidance.Clear();
      for (var index = 0; index < _crew.Count; index++)
        _crewVisibilityBeforeGuidance.Add(_crew[index].gameObject.activeSelf);
      SetStationHudForGuidance(false);
      if (_contentGroup != null)
      {
        _contentGroup.alpha = 1f;
        _contentGroup.interactable = false;
        _contentGroup.blocksRaycasts = false;
      }
      SetProductionAnimation(false);
    }

    private void ExitEyeMovementGuidance(bool immediate)
    {
      var ownedGuidanceWasVisible = _guidanceMode || (_eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible);
      _guidanceMode = false;
      _developmentGuidancePreviewUntil = -1f;
      if (ownedGuidanceWasVisible) RestoreStationHudAfterGuidance();
      if (_contentGroup != null)
      {
        _contentGroup.alpha = 1f;
        _contentGroup.interactable = true;
        _contentGroup.blocksRaycasts = true;
      }
      if (_group != null)
      {
        _group.alpha = 1f;
        _group.interactable = true;
        _group.blocksRaycasts = true;
      }
      if (_eyeMovementGuidance != null)
      {
        if (immediate) _eyeMovementGuidance.HideImmediate();
        else _eyeMovementGuidance.HideAnimated();
      }
    }

    private void SetStationHudForGuidance(bool visible)
    {
      if (_hudRoot != null) _hudRoot.gameObject.SetActive(visible);
      if (_transportRoot != null) _transportRoot.gameObject.SetActive(visible);
      if (_routineDock != null) _routineDock.gameObject.SetActive(visible);
      if (_navigationRoot != null) _navigationRoot.gameObject.SetActive(visible);
      SetPanelVisible(_actionRoot, false);
      SetPanelVisible(_incidentRoot, false);
      if (_stationStageGroup != null)
      {
        _stationStageGroup.alpha = visible ? 1f : 0.12f;
        _stationStageGroup.interactable = false;
        _stationStageGroup.blocksRaycasts = false;
      }
      for (var index = 0; index < _stationLabels.Count; index++)
        _stationLabels[index].SetActive(visible);
      for (var index = 0; index < _crew.Count; index++)
        _crew[index].gameObject.SetActive(visible &&
          (index >= _crewVisibilityBeforeGuidance.Count || _crewVisibilityBeforeGuidance[index]));
    }

    private void RestoreStationHudAfterGuidance()
    {
      if (_hudRoot != null) _hudRoot.gameObject.SetActive(_hudWasVisible);
      if (_transportRoot != null) _transportRoot.gameObject.SetActive(_transportWasVisible);
      if (_routineDock != null) _routineDock.gameObject.SetActive(_routineWasVisible);
      if (_navigationRoot != null) _navigationRoot.gameObject.SetActive(_navigationWasVisible);
      SetPanelVisible(_actionRoot, _actionWasVisible);
      SetPanelVisible(_incidentRoot, _incidentWasVisible);
      if (_stationStageGroup != null)
      {
        _stationStageGroup.alpha = 1f;
        _stationStageGroup.interactable = true;
        _stationStageGroup.blocksRaycasts = true;
      }
      for (var index = 0; index < _stationLabels.Count; index++)
        _stationLabels[index].SetActive(true);
      for (var index = 0; index < _crew.Count; index++)
        _crew[index].gameObject.SetActive(index < _crewVisibilityBeforeGuidance.Count &&
          _crewVisibilityBeforeGuidance[index]);
    }

    private void RenderPilot(CareActionSaveData data)
    {
      var axis = Mathf.Clamp(data.pilotCurrentAxis, 0, 3);
      for (var index = 0; index < _pilotAxes.Count; index++)
        _pilotAxes[index].color = index < axis
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.34f)
          : index == axis
            ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.72f)
            : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.18f);
      var target = PilotGuidePosition(axis, data.pilotNormalizedMoveProgress);
      if (_pilotGuideDot != null) _pilotGuideDot.anchoredPosition = target;
      var pupil = Vector2.ClampMagnitude(target / Mathf.Max(1f, _pilotAxisRange) * _pilotPupilRange,
        _pilotPupilRange);
      if (_pilotLeftPupil != null) _pilotLeftPupil.anchoredPosition = Vector2.Lerp(_pilotLeftPupil.anchoredPosition, pupil, 0.28f);
      if (_pilotRightPupil != null) _pilotRightPupil.anchoredPosition = Vector2.Lerp(_pilotRightPupil.anchoredPosition, pupil, 0.28f);
      if (data.internalPhase == CareActionInternalPhase.PilotTransition)
      {
        _actionPrompt.text = "AXES COMPLETE\nNEXT: SLOW CIRCLES";
        return;
      }
      _actionPrompt.text = $"{CareActionRuntimePromptForPilot(axis)}\nAXIS {axis + 1} / 4   ROUND {Mathf.Clamp(data.pilotCurrentRound + 1, 1, 3)} / 3";
    }

    private static string CareActionRuntimePromptForPilot(int axis)
    {
      return axis == 0 ? "LOOK UP AND DOWN" : axis == 1 ? "LOOK LEFT AND RIGHT" : "FOLLOW THE DIAGONAL";
    }

    private Vector2 PilotGuidePosition(int axis, float progress)
    {
      var first = axis == 0 ? Vector2.up : axis == 1 ? Vector2.left : axis == 2
        ? new Vector2(-0.707f, 0.707f) : new Vector2(0.707f, 0.707f);
      var second = -first;
      progress = Mathf.Clamp01(progress);
      if (progress < 0.25f) return Vector2.Lerp(Vector2.zero, first * _pilotAxisRange, progress * 4f);
      if (progress < 0.5f) return Vector2.Lerp(first * _pilotAxisRange, Vector2.zero, (progress - 0.25f) * 4f);
      if (progress < 0.75f) return Vector2.Lerp(Vector2.zero, second * _pilotAxisRange, (progress - 0.5f) * 4f);
      return Vector2.Lerp(second * _pilotAxisRange, Vector2.zero, (progress - 0.75f) * 4f);
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

    public void ShowRepairReveal()
    {
      HideAllModals();
      SetPanelVisible(_actionRoot, false);
      _careDimmer.color = Color.clear;
      _incidentSelectable = false;
      _repairPulseUntil = Time.unscaledTime + 1.4f;
      _statusText.text = "CARE ROUTINE COMPLETE";
      SetRoutinePrimary("CONTINUE");
      SetProductionAnimation(false);
    }

    public void ShowRepairReveal(CareStationIncidentType legacyIncident)
    {
      ShowRepairReveal();
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
      SetPanelVisible(_changeStepConfirmRoot, true);
    }

    public void HideCareStepChangeConfirmation()
    {
      SetPanelVisible(_changeStepConfirmRoot, false);
      if (_actionGroup != null)
      {
        var actionVisible = _actionRoot != null && _actionRoot.gameObject.activeInHierarchy;
        _actionGroup.interactable = actionVisible;
        _actionGroup.blocksRaycasts = actionVisible;
      }
    }


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

    public void ShowUpgrade(
      CareStationSaveData save,
      CareStationUpgradeConfiguration configuration = null,
      CareEconomyConfiguration economy = null)
    {
      configuration = configuration ?? new CareStationUpgradeConfiguration();
      economy = economy ?? new CareEconomyConfiguration();
      HideAllModals();
      SetPanelVisible(_upgradeRoot, true);
      SetNavigationSelection(1);
      SetRoutinePrimary("UPGRADE");
      SetProductionAnimation(false);
      if (_upgradeTitle != null) _upgradeTitle.text = "STATION UPGRADE";
      ApplyStation(save);
      foreach (var pair in _upgradeButtons)
      {
        var level = CareStationShiftRules.GetUpgradeLevel(save, pair.Key);
        var availability = CareStationShiftRules.EvaluateUpgrade(save, pair.Key, configuration, economy);
        var maximum = availability.IsMaximum;
        // Keep unavailable cards clickable so the authoritative purchase path
        // can explain exactly what is missing instead of silently doing nothing.
        pair.Value.interactable = true;
        var group = pair.Value.GetComponent<CanvasGroup>();
        if (group == null) group = pair.Value.gameObject.AddComponent<CanvasGroup>();
        group.alpha = availability.CanPurchase ? 1f : maximum ? 0.42f : 0.68f;
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
            var costText = $"{availability.CoinCost} COINS";
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
        var availability = CareStationShiftRules.EvaluateUpgrade(save, pair.Key, configuration, economy);
        var title = pair.Key == CareStationUpgradeId.MoreWorkers ? "MORE WORKERS"
          : pair.Key == CareStationUpgradeId.LargerStorage ? "LARGER STORAGE" : "BIGGER CART";
        var effect = pair.Key == CareStationUpgradeId.MoreWorkers ? "More carts at once."
          : pair.Key == CareStationUpgradeId.LargerStorage ? "Hold more bottles." : "Carry more each trip.";
        var costText = $"{availability.CoinCost} COINS";
        var reasonLine = string.IsNullOrEmpty(availability.PlayerReason)
          ? string.Empty
          : $"\n<color=#{ColorUtility.ToHtmlStringRGB(KeepBlinkingTheme.AccentWarm)}>{availability.PlayerReason}</color>";
        pair.Value.text = $"{title}\nLEVEL {level}\n{effect}\n{configuration.Value(pair.Key, level)} -> {configuration.Value(pair.Key, level + 1)}   {costText}{reasonLine}";
        pair.Value.color = KeepBlinkingTheme.TextPrimary;
      }
      SetUpgradeOpportunity(save != null && save.upgradeOffered);
    }

    public void ShowUpgradeFeedback(CareStationUpgradeId upgrade, string reason)
    {
      if (string.IsNullOrEmpty(reason) || !_upgradeCardTexts.TryGetValue(upgrade, out var text)) return;
      text.color = KeepBlinkingTheme.AccentWarm;
      if (text.text.IndexOf(reason, StringComparison.Ordinal) < 0) text.text += "\n" + reason;
      _upgradeFeedbackCard = upgrade;
      _upgradeFeedbackUntil = Time.unscaledTime + 1.15f;
    }

    public void SetUpgradeOpportunity(bool visible)
    {
      if (_upgradeOpportunityDot != null) _upgradeOpportunityDot.gameObject.SetActive(visible);
    }

    public void ShowSubjectiveCheck(bool post, CareSubjectiveScores scores)
    {
      HideAllModals();
      _surveyIsPost = post;
      _surveyDraft = scores?.Clone() ?? new CareSubjectiveScores();
      SetPanelVisible(_surveyRoot, true);
      _surveyTitle.text = post ? "POST-CARE CHECK" : "PRE-CARE CHECK";
      SetRoutinePrimary(post ? "VIEW REPORT" : "CONTINUE");
      SetProductionAnimation(false);
      RefreshSurvey();
    }

    public void ShowCareReport(CareStationSaveData save)
    {
      HideAllModals();
      SetPanelVisible(_reportRoot, true);
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

    public void ShowStorageFullStation(CareStationSaveData save)
    {
      HideAllModals();
      ApplyStation(save);
      SetFactoryStatus(string.Empty, "IDLE", "IDLE", "IDLE", "STORAGE FULL", "READY");
      SetCrewState(CareCrewState.Rest);
      var pendingCare = save != null && save.pendingFullBottleShipment > 0;
      var pendingOffline = save != null && save.pendingOfflineXP > save.collectedOfflineBottleValue;
      SetRoutinePrimary(pendingCare || pendingOffline ? "FREE STORAGE TO CONTINUE" : "START CARE");
      SetProductionAnimation(false);
    }

    public void ShowStationUpgradeResult(string title, int previousValue, int currentValue)
    {
      if (_toastText == null) return;
      _toastText.text = $"{title}\n{previousValue} -> {currentValue}";
      _toastText.gameObject.SetActive(true);
      _toastUntil = Time.unscaledTime + 2.2f;
    }

    public void ShowTransportUpgradeUnlocked()
    {
      if (_toastText == null) return;
      _toastText.text = "PRODUCTION LINE UPGRADED\nCONVEYOR UNLOCKED";
      _toastText.gameObject.SetActive(true);
      _toastUntil = Time.unscaledTime + 3.2f;
    }

    public bool IsUpgradeVisible => _upgradeRoot != null && _upgradeRoot.gameObject.activeSelf;

    internal string UiInputLockDescription
    {
      get
      {
        if (_eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible)
        {
          var owner = _developmentGuidancePreviewUntil > Time.unscaledTime
            ? "DevelopmentGuidancePreview"
            : _guidanceMode ? "EyeMovementGuidance" : "STALE EyeMovementGuidance";
          return $"owner={owner} visible=true shield={_eyeMovementGuidance.InputShieldActive} contentInteractable={(_contentGroup == null || _contentGroup.interactable)} contentBlocks={(_contentGroup == null || _contentGroup.blocksRaycasts)}";
        }
        if (_group != null && (!_group.interactable || !_group.blocksRaycasts))
          return $"owner=RootCanvasGroup interactable={_group.interactable} blocksRaycasts={_group.blocksRaycasts} alpha={_group.alpha:0.###}";
        if (_contentGroup != null && (!_contentGroup.interactable || !_contentGroup.blocksRaycasts))
          return $"owner=StationContent interactable={_contentGroup.interactable} blocksRaycasts={_contentGroup.blocksRaycasts} alpha={_contentGroup.alpha:0.###}";
        return "owner=NONE";
      }
    }

    internal void SynchronizeUiInputOwnership(bool guidanceExpected)
    {
      var guidanceOwnsInput = _eyeMovementGuidance != null &&
                              _eyeMovementGuidance.IsVisible &&
                              _eyeMovementGuidance.InputShieldActive;
      if (guidanceExpected && guidanceOwnsInput) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_developmentGuidancePreviewUntil > Time.unscaledTime) return;
#endif
      if (_guidanceMode || (_eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible) ||
          (_contentGroup != null && (!_contentGroup.interactable || !_contentGroup.blocksRaycasts)))
        ExitEyeMovementGuidance(true);
      RecoverBaseInputIfUnblocked();
    }

    internal bool ClearStaleUiInputLock(bool legitimateGuidanceLock)
    {
      if (legitimateGuidanceLock && _eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible)
        return false;

      ExitEyeMovementGuidance(true);
      _guidanceHudDebugVisible = false;
      if (_hudRoot != null) _hudRoot.gameObject.SetActive(true);
      if (_transportRoot != null) _transportRoot.gameObject.SetActive(true);
      if (_routineDock != null) _routineDock.gameObject.SetActive(true);
      if (_navigationRoot != null)
      {
        _navigationRoot.gameObject.SetActive(true);
        _navigationRoot.SetAsLastSibling();
      }
      RecoverBaseInputIfUnblocked();
      BindInputHandlers();
      return true;
    }

    internal void RebindInputHandlers()
    {
      Build();
      BindInputHandlers();
    }

    public bool IsUpgradeInteractable(CareStationUpgradeId upgrade)
    {
      return _stationSave != null && CareStationShiftRules.CanPurchaseUpgrade(
        _stationSave,
        upgrade,
        new CareStationUpgradeConfiguration());
    }

    public void ShowShiftComplete(CareStationSaveData save)
    {
      HideAllModals();
      SetPanelVisible(_completeRoot, true);
      _completeText.text = $"SHIFT COMPLETE\nCARE ROUTINE COMPLETE\n\nCARE ENERGY  {Mathf.Max(0, save?.careEnergy ?? 0)}\nCOINS  {Mathf.Max(0, save?.coins ?? 0)}";
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
      SetProductionAnimation(false);
    }

    public void HideAllModals()
    {
      ExitEyeMovementGuidance(true);
      SetPanelVisible(_welcomeRoot, false);
      SetPanelVisible(_incidentRoot, false);
      SetPanelVisible(_actionRoot, false);
      SetPanelVisible(_upgradeRoot, false);
      SetPanelVisible(_completeRoot, false);
      SetPanelVisible(_surveyRoot, false);
      SetPanelVisible(_reportRoot, false);
      SetPanelVisible(_changeStepConfirmRoot, false);
      if (_changeStepButton != null) _changeStepButton.gameObject.SetActive(false);
      if (_fallbackButton != null) _fallbackButton.gameObject.SetActive(false);
      if (_returnFallbackButton != null) _returnFallbackButton.gameObject.SetActive(false);
      RenderDistanceFeedback(false, CareDistanceDirection.None, 0f, -1);
      ResetTransportDistanceResponse();
      if (_careDimmer != null) _careDimmer.color = Color.clear;
      _incidentSelectable = false;
      _statusText.text = string.Empty;
      SetNavigationSelection(0);
      if (_navigationRoot != null) _navigationRoot.SetAsLastSibling();
      RecoverBaseInputIfUnblocked();
    }

    internal bool HasVisibleModal =>
      IsActive(_welcomeRoot) || IsActive(_actionRoot) || IsActive(_upgradeRoot) ||
      IsActive(_completeRoot) || IsActive(_surveyRoot) || IsActive(_reportRoot) ||
      IsActive(_changeStepConfirmRoot) ||
      (_eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible);

    private static bool IsActive(RectTransform root)
    {
      return root != null && root.gameObject.activeInHierarchy;
    }

    private void SetPanelVisible(RectTransform root, bool visible)
    {
      if (root == null) return;
      if (!_panelGroups.TryGetValue(root, out var group) || group == null)
      {
        group = root.GetComponent<CanvasGroup>();
        if (group == null) group = root.gameObject.AddComponent<CanvasGroup>();
        _panelGroups[root] = group;
      }

      var graphics = root.GetComponentsInChildren<Graphic>(true);
      for (var index = 0; index < graphics.Length; index++)
      {
        var graphic = graphics[index];
        if (!_panelGraphicRaycastDefaults.ContainsKey(graphic))
          _panelGraphicRaycastDefaults[graphic] = graphic.raycastTarget;
      }

      if (visible)
      {
        root.gameObject.SetActive(true);
        group.interactable = true;
        group.blocksRaycasts = true;
        for (var index = 0; index < graphics.Length; index++)
          graphics[index].raycastTarget = _panelGraphicRaycastDefaults[graphics[index]];
        return;
      }

      group.interactable = false;
      group.blocksRaycasts = false;
      for (var index = 0; index < graphics.Length; index++) graphics[index].raycastTarget = false;
      root.gameObject.SetActive(false);
    }

    private void RecoverBaseInputIfUnblocked()
    {
      if (_eyeMovementGuidance != null && _eyeMovementGuidance.IsVisible) return;
      // A domain reload or interrupted transition can leave the guidance
      // surface active after the non-serialized visibility snapshot has been
      // lost. Once no guidance surface owns input, restore the complete Station
      // shell explicitly instead of relying on that stale snapshot.
      if (_hudRoot != null) _hudRoot.gameObject.SetActive(true);
      if (_transportRoot != null) _transportRoot.gameObject.SetActive(true);
      if (_routineDock != null) _routineDock.gameObject.SetActive(true);
      if (_group != null)
      {
        _group.alpha = 1f;
        _group.interactable = true;
        _group.blocksRaycasts = true;
      }
      if (_contentGroup != null)
      {
        _contentGroup.alpha = 1f;
        _contentGroup.interactable = true;
        _contentGroup.blocksRaycasts = true;
      }
      if (_stationStageGroup != null)
      {
        _stationStageGroup.alpha = 1f;
        _stationStageGroup.interactable = true;
        _stationStageGroup.blocksRaycasts = true;
      }
      if (_navigationRoot != null)
      {
        _navigationRoot.gameObject.SetActive(true);
        _navigationRoot.SetAsLastSibling();
      }
      for (var index = 0; index < _navigationButtons.Count; index++)
        if (_navigationButtons[index] != null) _navigationButtons[index].interactable = true;
    }

    private void Update()
    {
      RecoverUiInputInfrastructureIfNeeded();
      if (_guidanceMode && (_eyeMovementGuidance == null || !_eyeMovementGuidance.IsVisible))
        ExitEyeMovementGuidance(true);
      if (!_guidanceMode && (_eyeMovementGuidance == null || !_eyeMovementGuidance.IsVisible))
        RecoverBaseInputIfUnblocked();
      var pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f);
      if (_incidentRoot != null && _incidentRoot.gameObject.activeSelf)
      {
        _incidentRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.06f, pulse);
        if (_repairPulseUntil > Time.unscaledTime)
          _incidentCore.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.85f, 1.25f, 1f - (_repairPulseUntil - Time.unscaledTime) / 1.4f);
      }
      if (_storageTank != null && _storageFull)
        _storageTank.localScale = _storageBaseScale * Mathf.Lerp(0.985f, 1.025f, pulse);
      var pipelinePulse = _pipelinePulseUntil > Time.unscaledTime
        ? 1f + Mathf.Sin((1f - (_pipelinePulseUntil - Time.unscaledTime) / 0.55f) * Mathf.PI) * 0.08f
        : 1f;
      var focusLegPulse = _focusLegPulseUntil > Time.unscaledTime
        ? 1f + Mathf.Sin((1f - (_focusLegPulseUntil - Time.unscaledTime) / 0.65f) * Mathf.PI) * 0.1f
        : 1f;
      if (_focusLegPulseActive && _focusLegPulseUntil <= Time.unscaledTime)
      {
        _focusLegPulseActive = false;
        ApplyPipelineVisuals();
      }
      if (_upgradeFeedbackUntil > 0f && Time.unscaledTime >= _upgradeFeedbackUntil)
      {
        if (_upgradeCardTexts.TryGetValue(_upgradeFeedbackCard, out var feedbackText))
          feedbackText.color = KeepBlinkingTheme.TextPrimary;
        _upgradeFeedbackUntil = 0f;
        _upgradeFeedbackCard = CareStationUpgradeId.None;
      }
      if (_toastUntil > 0f && Time.unscaledTime >= _toastUntil)
      {
        if (_toastText != null) _toastText.gameObject.SetActive(false);
        _toastUntil = 0f;
      }
      if (_filterBody != null) _filterBody.rectTransform.localScale = Vector3.one * (((_pipelineMask & CareRecipePipeline.Filter) != 0) ? pipelinePulse : 1f);
      if (_fillerBody != null)
        _fillerBody.rectTransform.localScale = Vector3.one * (_focusLegPulseActive && _focusLegPulseDirection == CareDistanceDirection.Away
          ? focusLegPulse
          : ((_pipelineMask & CareRecipePipeline.Filler) != 0) ? pipelinePulse : 1f);
      for (var i = 0; i < _packerLayers.Count; i++)
        _packerLayers[i].rectTransform.localScale = Vector3.one * (_focusLegPulseActive && _focusLegPulseDirection == CareDistanceDirection.Closer
          ? focusLegPulse
          : ((_pipelineMask & CareRecipePipeline.Packer) != 0) ? pipelinePulse : 1f);
      if (_careCoreInner != null)
      {
        var energyPulse = _careEnergyPulseUntil > Time.unscaledTime
          ? 1f + Mathf.Sin((1f - (_careEnergyPulseUntil - Time.unscaledTime) / 0.9f) * Mathf.PI) * 0.18f
          : 1f;
        _careCoreInner.rectTransform.localScale = Vector3.one * Mathf.Max(
          ((_pipelineMask & CareRecipePipeline.CareCore) != 0) ? pipelinePulse : 1f,
          energyPulse);
      }
      UpdateCareEnergyFlights();
      UpdateProductionAnimation();
      PollIncidentTouch();
    }

    private void PlayCareEnergyFlight(int amount)
    {
      if (_safe == null || _goldBottleText == null) return;
      Canvas.ForceUpdateCanvases();
      var source = _actionRoot != null ? _actionRoot : _careCoreInner?.rectTransform;
      if (source == null) return;
      RectTransformUtility.ScreenPointToLocalPointInRectangle(
        _safe,
        RectTransformUtility.WorldToScreenPoint(null, source.position),
        null,
        out var start);
      RectTransformUtility.ScreenPointToLocalPointInRectangle(
        _safe,
        RectTransformUtility.WorldToScreenPoint(null, _goldBottleText.rectTransform.position),
        null,
        out var end);
      var count = Mathf.Clamp(amount, 3, 8);
      for (var index = 0; index < count; index++)
      {
        var image = FirstLevelUiFactory.CreateImage(
          $"Care Energy Reward Particle {index + 1}",
          _safe,
          WorkshopMint,
          FirstLevelUiFactory.CircleSprite);
        image.raycastTarget = false;
        FirstLevelUiFactory.SetRect(
          image.rectTransform,
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          Vector2.zero,
          Vector2.one * Mathf.Lerp(8f, 13f, index / Mathf.Max(1f, count - 1f)));
        var offset = new Vector2((index % 3 - 1) * 16f, (index / 3) * 11f);
        image.rectTransform.anchoredPosition = start + offset;
        image.transform.SetAsLastSibling();
        _careEnergyFlights.Add(new CareEnergyFlight
        {
          rect = image.rectTransform,
          image = image,
          start = start + offset,
          end = end,
          startedAt = Time.unscaledTime + index * 0.045f,
          duration = 0.62f + index * 0.025f,
        });
      }
      _careEnergyPulseUntil = Time.unscaledTime + 0.9f;
      if (_careCoreInner != null) _careCoreInner.color = WorkshopMint;
    }

    private void UpdateCareEnergyFlights()
    {
      for (var index = _careEnergyFlights.Count - 1; index >= 0; index--)
      {
        var flight = _careEnergyFlights[index];
        if (flight?.rect == null)
        {
          _careEnergyFlights.RemoveAt(index);
          continue;
        }
        var t = Mathf.Clamp01((Time.unscaledTime - flight.startedAt) / Mathf.Max(0.05f, flight.duration));
        var curved = Mathf.SmoothStep(0f, 1f, t);
        var arc = Vector2.up * Mathf.Sin(t * Mathf.PI) * 44f;
        flight.rect.anchoredPosition = Vector2.LerpUnclamped(flight.start, flight.end, curved) + arc;
        flight.rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.55f, t);
        if (flight.image != null)
          flight.image.color = KeepBlinkingTheme.WithAlpha(WorkshopMint, Mathf.Lerp(1f, 0.25f, t));
        if (t < 1f) continue;
        Destroy(flight.rect.gameObject);
        _careEnergyFlights.RemoveAt(index);
      }
      if (_careEnergyFlights.Count == 0 && _careEnergyPulseUntil > 0f &&
          Time.unscaledTime >= _careEnergyPulseUntil)
      {
        _careEnergyPulseUntil = 0f;
        ApplyPipelineVisuals();
      }
    }

    private void RecoverUiInputInfrastructureIfNeeded()
    {
      if (_safe == null) return;
      if (FirstLevelUiFactory.IsUiInputInfrastructureHealthy(_safe)) return;
      FirstLevelUiFactory.RecoverUiInput(_safe, _group);
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
      _stationStageGroup = _stationStage.gameObject.AddComponent<CanvasGroup>();
      FirstLevelUiFactory.SetRect(_stationStage, new Vector2(0.025f, 0.205f), new Vector2(0.975f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var stage = FirstLevelUiFactory.CreateImage("Open Workshop Scene", _stationStage, KeepBlinkingTheme.WithAlpha(WorkshopWall, 0.38f));
      FirstLevelUiFactory.Stretch(stage.rectTransform);
      var floor = FirstLevelUiFactory.CreateImage("Workshop Floor Strip", _stationStage, KeepBlinkingTheme.WithAlpha(WorkshopFloor, 0.88f));
      FirstLevelUiFactory.SetRect(floor.rectTransform, Vector2.zero, new Vector2(1f, 0.37f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var upperBeam = CreateWorkshopPart("Upper Timber Beam", _stationStage, new Vector2(0.5f, 0.535f), new Vector2(910f, 24f), WorkshopWood, -0.25f, 6f);
      upperBeam.color = KeepBlinkingTheme.WithAlpha(WorkshopWood, 0.52f);
      var lowerBeam = CreateWorkshopPart("Lower Timber Beam", _stationStage, new Vector2(0.5f, 0.075f), new Vector2(910f, 22f), WorkshopWood, 0.18f, 6f);
      lowerBeam.color = KeepBlinkingTheme.WithAlpha(WorkshopWood, 0.44f);

      BuildCareEnergySource();
      BuildLiquidTransportRoutes();
      BuildManualCarryRoute();
      BuildBasicConveyorRoute();

      BuildFilterDevice();
      BuildFillerDevice();
      BuildPackerDevice();
      BuildWorkerHandoffAnchors();
    }

    private void BuildFilterDevice()
    {
      var root = CreateWorkshopDeviceRoot("Filter Device", CareStationDisplayNames.Filter, new Vector2(0.26f, 0.755f), new Vector2(360f, 500f), -0.8f, out _filterStatusText);
      var filterCatalog = Resources.Load<CareStationFilterArtCatalog>("CareStation/Filter/CareStationFilterArtCatalog");
      if (filterCatalog != null)
      {
        _filterArt = CareStationFilterArtView.Create(root, filterCatalog);
        _filterArt.name = "FILTER L1 Authored Machine Art";
        var artRect = _filterArt.GetComponent<RectTransform>();
        FirstLevelUiFactory.SetRect(artRect, new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.54f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 450f));
        _filterArt.SetHitTestEnabled(false);
        _filterArt.SetIntegratedBottleVisible(false);
        _filterArt.SetLevel(1, true);
        _filterArt.gameObject.SetActive(true);
      }
      else
      {
        // The former procedural FILTER silhouette is intentionally not a
        // fallback for authored art. A missing catalog should be obvious in
        // development instead of silently restoring the obsolete L1 shape.
        Debug.LogWarning("Care Station FILTER art catalog is missing; authored FILTER art was not created.");
      }
      _filterBody = null;
    }

    private void BuildFillerDevice()
    {
      var root = CreateWorkshopDeviceRoot("Filler Device", CareStationDisplayNames.Filler, new Vector2(0.73f, 0.755f), new Vector2(365f, 455f), 0.65f, out _fillerStatusText);
      CreateWorkshopPart("Filler Left Timber Leg", root, new Vector2(0.26f, 0.34f), new Vector2(34f, 175f), WorkshopWood, 1.2f);
      CreateWorkshopPart("Filler Right Metal Leg", root, new Vector2(0.74f, 0.34f), new Vector2(34f, 175f), WorkshopMetal, -1.1f);
      CreateWorkshopPart("Filler Left Foot", root, new Vector2(0.23f, 0.14f), new Vector2(92f, 28f), WorkshopWoodLight, -1f);
      CreateWorkshopPart("Filler Right Foot", root, new Vector2(0.77f, 0.14f), new Vector2(92f, 28f), WorkshopMetalLight, 1f);
      CreateWorkshopPart("Filler Work Shelf", root, new Vector2(0.5f, 0.31f), new Vector2(256f, 40f), WorkshopMetal, -0.45f);
      var tank = CreateWorkshopPart("Filler Liquid Reservoir", root, new Vector2(0.5f, 0.69f), new Vector2(198f, 132f), WorkshopMetalLight, 0.45f, 8f);
      _fillerBody = tank;
      CreateWorkshopPart("Filler Reservoir Top", root, new Vector2(0.5f, 0.835f), new Vector2(226f, 32f), WorkshopMetal, -0.7f);
      CreateWorkshopPart("Filler Reservoir Rim", root, new Vector2(0.5f, 0.54f), new Vector2(220f, 28f), WorkshopMetal, 0.55f);
      var window = CreateWorkshopPart("Filler Mint Sight Glass", root, new Vector2(0.5f, 0.69f), new Vector2(132f, 72f), KeepBlinkingTheme.WithAlpha(WorkshopMint, 0.48f), -0.3f, 5f);
      _fillerLevel = window;
      CreateWorkshopPart("Filler Bent Feed Pipe", root, new Vector2(0.23f, 0.70f), new Vector2(76f, 17f), WorkshopMetal, -7f, 5f);
      CreateWorkshopPart("Filler Nozzle Stem", root, new Vector2(0.5f, 0.425f), new Vector2(20f, 86f), WorkshopMetalLight, 0.8f, 5f);
      CreateWorkshopPart("Filler Nozzle Tip", root, new Vector2(0.5f, 0.335f), new Vector2(34f, 24f), WorkshopOutline, -0.8f, 3f);
      CreateWorkshopPart("Empty Bottle Receiving Dock", root, new Vector2(0.5f, 0.19f), new Vector2(132f, 24f), WorkshopPaperDim, 0.65f, 5f);
      AddWorkshopRivets(root, new[] { new Vector2(0.22f, 0.31f), new Vector2(0.78f, 0.31f), new Vector2(0.23f, 0.83f), new Vector2(0.77f, 0.83f) });
    }

    private void BuildPackerDevice()
    {
      var root = CreateWorkshopDeviceRoot("Packer Device", CareStationDisplayNames.Packer, new Vector2(0.73f, 0.365f), new Vector2(365f, 395f), -0.55f, out _packerStatusText);
      CreateWorkshopPart("Packer Left Timber Post", root, new Vector2(0.24f, 0.48f), new Vector2(34f, 206f), WorkshopWood, -1.4f);
      CreateWorkshopPart("Packer Right Metal Post", root, new Vector2(0.76f, 0.48f), new Vector2(34f, 206f), WorkshopMetal, 1.1f);
      var topBeam = CreateWorkshopPart("Packer Capping Gantry", root, new Vector2(0.5f, 0.77f), new Vector2(258f, 44f), WorkshopMetal, 0.7f);
      _packerLayers.Add(topBeam);
      var table = CreateWorkshopPart("Packer Packaging Table", root, new Vector2(0.5f, 0.22f), new Vector2(268f, 42f), WorkshopWoodLight, -0.45f);
      _packerLayers.Add(table);
      CreateWorkshopPart("Packer Press Shaft", root, new Vector2(0.54f, 0.57f), new Vector2(28f, 112f), WorkshopMetalLight, 0.5f);
      CreateWorkshopPart("Packer Cap Head", root, new Vector2(0.54f, 0.425f), new Vector2(76f, 30f), WorkshopOutline, -0.7f, 4f);
      var labelRoll = CreateWorkshopPart("Packer Label Roll", root, new Vector2(0.22f, 0.56f), new Vector2(74f, 74f), WorkshopPaper, -1.2f, 7f, FirstLevelUiFactory.CircleSprite);
      var labelHub = FirstLevelUiFactory.CreateImage("Packer Label Roll Hub", labelRoll.transform, WorkshopOutline, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(labelHub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
      CreateWorkshopPart("Packer Left Wrapper Arm", root, new Vector2(0.38f, 0.36f), new Vector2(82f, 20f), WorkshopMetalLight, -24f, 5f);
      CreateWorkshopPart("Packer Right Wrapper Arm", root, new Vector2(0.68f, 0.35f), new Vector2(82f, 20f), WorkshopMetalLight, 23f, 5f);
      CreateWorkshopPart("Packer Box Guide Left", root, new Vector2(0.30f, 0.19f), new Vector2(48f, 48f), WorkshopPaperDim, -4f, 5f);
      CreateWorkshopPart("Packer Box Guide Right", root, new Vector2(0.72f, 0.19f), new Vector2(48f, 48f), WorkshopPaperDim, 3f, 5f);
      AddWorkshopRivets(root, new[] { new Vector2(0.18f, 0.77f), new Vector2(0.82f, 0.77f), new Vector2(0.18f, 0.22f), new Vector2(0.82f, 0.22f) });
    }

    private RectTransform CreateWorkshopDeviceRoot(string objectName, string label, Vector2 anchor, Vector2 size, float signRotation, out TextMeshProUGUI statusText)
    {
      var root = FirstLevelUiFactory.CreateObject(objectName, _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, size);
      var frame = root.gameObject.AddComponent<Image>();
      frame.color = Color.clear;
      frame.raycastTarget = true;
      var button = root.gameObject.AddComponent<Button>();
      button.targetGraphic = frame;
      var colors = button.colors;
      colors.normalColor = Color.clear;
      colors.highlightedColor = KeepBlinkingTheme.WithAlpha(WorkshopMint, 0.035f);
      colors.pressedColor = KeepBlinkingTheme.WithAlpha(WorkshopMint, 0.07f);
      colors.selectedColor = Color.clear;
      colors.disabledColor = Color.clear;
      button.colors = colors;
      RegisterButtonBinding(button, () => DeviceSelected?.Invoke(label));
      _deviceButtons.Add(button);

      var sign = FirstLevelUiFactory.CreateImage(label + " Handwritten Sign", root, WorkshopPaper, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(sign.rectTransform, new Vector2(0.5f, 0.035f), new Vector2(0.5f, 0.035f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(178f, 42f));
      sign.rectTransform.localRotation = Quaternion.Euler(0f, 0f, signRotation);
      var nameText = FirstLevelUiFactory.CreateText(label + " Label", sign.transform, label, 20f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopInk);
      FirstLevelUiFactory.Stretch(nameText.rectTransform, new Vector2(8f, 3f), new Vector2(-8f, -3f));

      var statusTag = FirstLevelUiFactory.CreateImage(label + " Active Status Tag", root, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(statusTag.rectTransform, new Vector2(0.5f, 0.145f), new Vector2(0.5f, 0.145f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(205f, 36f));
      statusText = FirstLevelUiFactory.CreateText(label + " Status", statusTag.transform, string.Empty, 15f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopMint);
      FirstLevelUiFactory.Stretch(statusText.rectTransform, new Vector2(8f, 2f), new Vector2(-8f, -2f));
      statusTag.gameObject.SetActive(false);
      _stationLabels.Add(nameText.gameObject);
      return root;
    }

    private void BuildCareEnergySource()
    {
      var root = FirstLevelUiFactory.CreateObject("Care Energy Source", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.07f, 0.77f), new Vector2(0.07f, 0.77f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112f, 150f));
      var nodeOutline = FirstLevelUiFactory.CreateImage("Care Energy Brass Housing", root, WorkshopOutline, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(nodeOutline.rectTransform, new Vector2(0.5f, 0.61f), new Vector2(0.5f, 0.61f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78f, 78f));
      var node = FirstLevelUiFactory.CreateImage("Care Energy Meter", nodeOutline.transform, WorkshopMint, FirstLevelUiFactory.CircleSprite);
      _careCoreInner = node;
      FirstLevelUiFactory.SetRect(node.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
      var boltUpper = FirstLevelUiFactory.CreateImage("Care Energy Bolt Upper", node.transform, WorkshopPaper, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(boltUpper.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-5f, 7f), new Vector2(12f, 28f));
      boltUpper.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -24f);
      var boltLower = FirstLevelUiFactory.CreateImage("Care Energy Bolt Lower", node.transform, WorkshopPaper, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(boltLower.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(5f, -7f), new Vector2(12f, 28f));
      boltLower.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -24f);
      var sign = FirstLevelUiFactory.CreateImage("Care Energy Paper Label", root, WorkshopPaperDim, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(sign.rectTransform, new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 40f));
      sign.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
      var label = FirstLevelUiFactory.CreateText("Care Energy Source Label", sign.transform, "CARE ENERGY", 13f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopInk);
      FirstLevelUiFactory.Stretch(label.rectTransform, new Vector2(4f, 2f), new Vector2(-4f, -2f));
    }

    private void BuildLiquidTransportRoutes()
    {
      _baseInputPipe = CreatePathSegment(
        _stationStage,
        "Care Energy Cable",
        new Vector2(0.105f, 0.77f),
        new Vector2(0.145f, 0.77f),
        7f,
        KeepBlinkingTheme.WithAlpha(WorkshopMint, 0.46f));

      _manualFilterHoseRoot = FirstLevelUiFactory.CreateObject("L1 Manual Liquid Hose", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_manualFilterHoseRoot);
      var hosePoints = new[]
      {
        new Vector2(0.385f, 0.735f),
        new Vector2(0.45f, 0.705f),
        new Vector2(0.56f, 0.705f),
        new Vector2(0.595f, 0.745f),
      };
      for (var index = 0; index < hosePoints.Length - 1; index++)
      {
        CreatePathSegment(_manualFilterHoseRoot, "Soft Hose Outline " + index, hosePoints[index], hosePoints[index + 1], 17f, KeepBlinkingTheme.WithAlpha(WorkshopOutline, 0.86f));
        var inner = CreatePathSegment(_manualFilterHoseRoot, "Soft Liquid Hose " + index, hosePoints[index], hosePoints[index + 1], 8f, KeepBlinkingTheme.WithAlpha(WorkshopMetalLight, 0.72f));
        _liquidTransportSegments.Add(inner);
        if (_filteredLiquidPipe == null) _filteredLiquidPipe = inner;
      }

      _fixedFilterPipeRoot = FirstLevelUiFactory.CreateObject("L2 Fixed Liquid Pipe And Pump", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_fixedFilterPipeRoot);
      var pipePoints = new[]
      {
        new Vector2(0.385f, 0.75f),
        new Vector2(0.385f, 0.815f),
        new Vector2(0.59f, 0.815f),
        new Vector2(0.59f, 0.75f),
      };
      for (var index = 0; index < pipePoints.Length - 1; index++)
      {
        CreatePathSegment(_fixedFilterPipeRoot, "Fixed Pipe Outline " + index, pipePoints[index], pipePoints[index + 1], 20f, KeepBlinkingTheme.WithAlpha(WorkshopOutline, 0.9f));
        var inner = CreatePathSegment(_fixedFilterPipeRoot, "Fixed Liquid Pipe " + index, pipePoints[index], pipePoints[index + 1], 10f, KeepBlinkingTheme.WithAlpha(WorkshopMetal, 0.86f));
        _liquidTransportSegments.Add(inner);
      }
      var pumpOutline = FirstLevelUiFactory.CreateImage("L2 Pump Housing", _fixedFilterPipeRoot, WorkshopOutline, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(pumpOutline.rectTransform, new Vector2(0.49f, 0.815f), new Vector2(0.49f, 0.815f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
      var pump = FirstLevelUiFactory.CreateImage("L2 Small Pump", pumpOutline.transform, WorkshopMetalLight, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(pump.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 38f));
      var pumpHub = FirstLevelUiFactory.CreateImage("L2 Pump Hub", pump.transform, WorkshopPaperDim, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(pumpHub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));
    }

    private void BuildManualCarryRoute()
    {
      _manualCarryRoot = FirstLevelUiFactory.CreateObject("L1 Manual Bottle Carry Path", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_manualCarryRoot);
      var markers = new[]
      {
        new Vector2(0.82f, 0.60f), new Vector2(0.85f, 0.54f), new Vector2(0.84f, 0.47f), new Vector2(0.82f, 0.41f),
        new Vector2(0.64f, 0.315f), new Vector2(0.56f, 0.305f), new Vector2(0.48f, 0.30f), new Vector2(0.40f, 0.295f),
      };
      for (var index = 0; index < markers.Length; index++)
      {
        var marker = FirstLevelUiFactory.CreateImage("Manual Carry Footstep " + index, _manualCarryRoot, KeepBlinkingTheme.WithAlpha(WorkshopPaperDim, 0.34f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(marker.rectTransform, markers[index], markers[index], new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 9f));
        marker.rectTransform.localRotation = Quaternion.Euler(0f, 0f, index < 4 ? 62f + (index % 2 == 0 ? -8f : 7f) : (index % 2 == 0 ? -7f : 8f));
        marker.raycastTarget = false;
        _manualCarryMarkers.Add(marker);
      }
    }

    private void BuildBasicConveyorRoute()
    {
      _basicConveyorRoot = FirstLevelUiFactory.CreateObject("L2 Basic Bottle Conveyor", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_basicConveyorRoot);
      CreateConveyorSpan("Filler To Packer Conveyor", new Vector2(0.73f, 0.585f), new Vector2(0.73f, 0.445f), 4);
      CreateConveyorSpan("Packer To Storage Conveyor", new Vector2(0.62f, 0.295f), new Vector2(0.39f, 0.295f), 5);
    }

    private void CreateConveyorSpan(string name, Vector2 from, Vector2 to, int rollerCount)
    {
      CreatePathSegment(_basicConveyorRoot, name + " Brown Outline", from, to, 34f, KeepBlinkingTheme.WithAlpha(WorkshopOutline, 0.92f));
      var belt = CreatePathSegment(_basicConveyorRoot, name + " Blue Gray Belt", from, to, 24f, KeepBlinkingTheme.WithAlpha(WorkshopMetal, 0.92f));
      _conveyorSegments.Add(belt);
      if (_bottleConveyor == null) _bottleConveyor = belt;
      else if (_packedBottleRoute == null) _packedBottleRoute = belt;
      for (var index = 0; index < rollerCount; index++)
      {
        var t = rollerCount <= 1 ? 0.5f : index / (float)(rollerCount - 1);
        var roller = FirstLevelUiFactory.CreateImage(name + " Roller " + index, _basicConveyorRoot, WorkshopPaperDim, FirstLevelUiFactory.CircleSprite);
        var anchor = Vector2.Lerp(from, to, t);
        FirstLevelUiFactory.SetRect(roller.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(15f, 15f));
        roller.raycastTarget = false;
        _conveyorSegments.Add(roller);
      }
    }

    private void BuildWorkerHandoffAnchors()
    {
      _workerFillerPickupAnchor = CreateReservedHandoffAnchor("Worker Bottle Pickup Anchor", ManualFillerPickupAnchor);
      _workerPackerHandoffAnchor = CreateReservedHandoffAnchor("Worker Packer Handoff Anchor", ManualPackerHandoffAnchor);
      _workerStorageHandoffAnchor = CreateReservedHandoffAnchor("Worker Storage Handoff Anchor", StorageBottleAnchor);
    }

    private RectTransform CreateReservedHandoffAnchor(string name, Vector2 anchor)
    {
      var root = FirstLevelUiFactory.CreateObject(name, _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 36f));
      return root;
    }

    private Image CreateWorkshopPart(
      string name,
      Transform parent,
      Vector2 anchor,
      Vector2 size,
      Color fill,
      float rotation = 0f,
      float outlinePad = 6f,
      Sprite sprite = null)
    {
      sprite = sprite == null ? FirstLevelUiFactory.RoundedSprite : sprite;
      var outline = FirstLevelUiFactory.CreateImage(name + " Outline", parent, WorkshopOutline, sprite);
      FirstLevelUiFactory.SetRect(outline.rectTransform, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, size + Vector2.one * outlinePad);
      outline.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
      var part = FirstLevelUiFactory.CreateImage(name, outline.transform, fill, sprite);
      FirstLevelUiFactory.Stretch(part.rectTransform, Vector2.one * (outlinePad * 0.5f), Vector2.one * (-outlinePad * 0.5f));
      part.raycastTarget = false;
      return part;
    }

    private Button CreateWorkshopButton(string name, Transform parent, Color material)
    {
      var root = FirstLevelUiFactory.CreateObject(name, parent);
      var plate = root.AddComponent<Image>();
      plate.sprite = FirstLevelUiFactory.RoundedSprite;
      plate.type = Image.Type.Sliced;
      plate.color = material;
      plate.raycastTarget = true;
      var button = root.AddComponent<Button>();
      button.targetGraphic = plate;
      var colors = button.colors;
      colors.normalColor = Color.white;
      colors.highlightedColor = Color.Lerp(Color.white, WorkshopMint, 0.16f);
      colors.pressedColor = Color.Lerp(Color.white, WorkshopMint, 0.28f);
      colors.selectedColor = colors.highlightedColor;
      button.colors = colors;
      var inner = FirstLevelUiFactory.CreateImage("Worn Inner Plate", root.transform, Color.Lerp(material, WorkshopPaperDim, 0.12f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(inner.rectTransform, new Vector2(5f, 5f), new Vector2(-5f, -5f));
      for (var index = 0; index < 2; index++)
      {
        var tack = FirstLevelUiFactory.CreateImage("Handmade Tack " + index, root.transform, WorkshopPaperDim, FirstLevelUiFactory.CircleSprite);
        var x = index == 0 ? 0.08f : 0.92f;
        FirstLevelUiFactory.SetRect(tack.rectTransform, new Vector2(x, 0.5f), new Vector2(x, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10f, 10f));
      }
      return button;
    }

    private void AddWorkshopRivets(Transform parent, Vector2[] anchors)
    {
      if (anchors == null) return;
      for (var index = 0; index < anchors.Length; index++)
      {
        var rivet = FirstLevelUiFactory.CreateImage("Brass Rivet " + index, parent, WorkshopPaperDim, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(rivet.rectTransform, anchors[index], anchors[index], new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
      }
    }

    private Image CreatePathSegment(Transform parent, string name, Vector2 from, Vector2 to, float width, Color color)
    {
      var center = (from + to) * 0.5f;
      var logicalDelta = new Vector2((to.x - from.x) * 980f, (to.y - from.y) * 1320f);
      var line = FirstLevelUiFactory.CreateImage(name, parent, color, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(line.rectTransform, center, center, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(logicalDelta.magnitude, width));
      line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(logicalDelta.y, logicalDelta.x) * Mathf.Rad2Deg);
      line.raycastTarget = false;
      return line;
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
      _stationLabels.Add(label.gameObject);
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

    private Image CreateProductionLink(string name, Vector2 from, Vector2 to, bool registerTrack = true)
    {
      var center = (from + to) * 0.5f;
      var delta = to - from;
      var line = FirstLevelUiFactory.CreateImage(
        name,
        _stationStage,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.34f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(
        line.rectTransform,
        center,
        center,
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(delta.magnitude * 880f, 8f));
      line.rectTransform.localRotation = Quaternion.Euler(
        0f,
        0f,
        Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
      line.raycastTarget = false;
      if (registerTrack) _stationTracks.Add(line);
      return line;
    }

    private void CreateDeviceLabel(Transform root, string label)
    {
      var text = FirstLevelUiFactory.CreateText(label + " Label", root, label, 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _stationLabels.Add(text.gameObject);
    }

    private void BuildStorage()
    {
      _transportRoot = FirstLevelUiFactory.CreateObject("Bottle Transport", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_transportRoot);

      var saleDrop = CreatePathSegment(_transportRoot, "Storage Exit Path", new Vector2(0.27f, 0.205f), new Vector2(0.27f, 0.115f), 9f, KeepBlinkingTheme.WithAlpha(WorkshopPaperDim, 0.3f));
      saleDrop.transform.SetAsFirstSibling();
      _storageToCartRoute = CreatePathSegment(_transportRoot, "Storage To Cart Floor Path", new Vector2(0.27f, 0.115f), new Vector2(0.80f, 0.115f), 9f, KeepBlinkingTheme.WithAlpha(WorkshopPaperDim, 0.3f));
      _storageToCartRoute.transform.SetAsFirstSibling();

      _storageTank = FirstLevelUiFactory.CreateObject("Bottle Storage", _transportRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_storageTank, new Vector2(0.27f, 0.295f), new Vector2(0.27f, 0.295f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340f, 270f));
      CreateWorkshopPart("Storage Left Timber Upright", _storageTank, new Vector2(0.13f, 0.49f), new Vector2(34f, 218f), WorkshopWood, -1.1f);
      CreateWorkshopPart("Storage Right Timber Upright", _storageTank, new Vector2(0.87f, 0.49f), new Vector2(34f, 218f), WorkshopWood, 0.9f);
      CreateWorkshopPart("Storage Roof", _storageTank, new Vector2(0.5f, 0.88f), new Vector2(286f, 38f), WorkshopMetal, -0.65f);
      CreateWorkshopPart("Storage Upper Shelf", _storageTank, new Vector2(0.5f, 0.58f), new Vector2(282f, 26f), WorkshopWoodLight, 0.55f);
      CreateWorkshopPart("Storage Lower Shelf", _storageTank, new Vector2(0.5f, 0.31f), new Vector2(286f, 28f), WorkshopWoodLight, -0.45f);
      var storageSign = FirstLevelUiFactory.CreateImage("Storage Paper Sign", _storageTank, WorkshopPaper, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(storageSign.rectTransform, new Vector2(0.5f, 0.97f), new Vector2(0.5f, 0.97f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(178f, 42f));
      storageSign.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 0.8f);
      var storageLabel = FirstLevelUiFactory.CreateText("Storage Label", storageSign.transform, "STORAGE", 19f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopInk);
      FirstLevelUiFactory.Stretch(storageLabel.rectTransform, new Vector2(6f, 2f), new Vector2(-6f, -2f));
      var storageTrack = FirstLevelUiFactory.CreateImage("Storage Capacity Track", _storageTank, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(storageTrack.rectTransform, new Vector2(0.5f, 0.13f), new Vector2(0.5f, 0.13f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(246f, 18f));
      _storageFill = FirstLevelUiFactory.CreateImage("Storage Capacity Fill", storageTrack.transform, WorkshopMint, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(_storageFill.rectTransform, Vector2.zero, Vector2.zero);
      _storageFill.type = Image.Type.Filled;
      _storageFill.fillMethod = Image.FillMethod.Horizontal;
      _storageFill.fillOrigin = 0;
      var storageStatusTag = FirstLevelUiFactory.CreateImage("Storage Active Status Tag", _storageTank, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(storageStatusTag.rectTransform, new Vector2(0.5f, -0.015f), new Vector2(0.5f, -0.015f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 62f));
      _storageStatusText = FirstLevelUiFactory.CreateText("Storage Status", storageStatusTag.transform, string.Empty, 17f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopMint, true);
      FirstLevelUiFactory.Stretch(_storageStatusText.rectTransform, new Vector2(8f, 2f), new Vector2(-8f, -2f));
      storageStatusTag.gameObject.SetActive(false);

      _cart = FirstLevelUiFactory.CreateObject("Bottle Cart", _transportRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_cart, new Vector2(0.88f, 0.12f), new Vector2(0.88f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 145f));
      var cartSign = FirstLevelUiFactory.CreateImage("Cart Paper Sign", _cart, WorkshopPaperDim, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(cartSign.rectTransform, new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(118f, 36f));
      cartSign.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
      var cartLabel = FirstLevelUiFactory.CreateText("Cart Label", cartSign.transform, "CART", 17f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopInk);
      FirstLevelUiFactory.Stretch(cartLabel.rectTransform, new Vector2(4f, 2f), new Vector2(-4f, -2f));
      CreateWorkshopPart("Cart Wooden Box", _cart, new Vector2(0.5f, 0.52f), new Vector2(154f, 64f), WorkshopWood, -1.2f);
      CreateWorkshopPart("Cart Metal Rim", _cart, new Vector2(0.5f, 0.72f), new Vector2(170f, 24f), WorkshopMetalLight, 0.7f);
      CreateWorkshopPart("Cart Handle", _cart, new Vector2(0.91f, 0.53f), new Vector2(70f, 17f), WorkshopMetal, -6f, 5f);
      for (var wheelIndex = 0; wheelIndex < 2; wheelIndex++)
      {
        var wheel = FirstLevelUiFactory.CreateImage("Cart Wheel", _cart, WorkshopOutline, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(wheel.rectTransform, new Vector2(wheelIndex == 0 ? 0.28f : 0.72f, 0.22f), new Vector2(wheelIndex == 0 ? 0.28f : 0.72f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
        var hub = FirstLevelUiFactory.CreateImage("Brass Wheel Hub", wheel.transform, WorkshopPaperDim, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(hub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(17f, 17f));
      }
      var cartStatusTag = FirstLevelUiFactory.CreateImage("Cart Active Status Tag", _cart, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(cartStatusTag.rectTransform, new Vector2(0.5f, -0.03f), new Vector2(0.5f, -0.03f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(142f, 34f));
      _cartStatusText = FirstLevelUiFactory.CreateText("Cart Status", cartStatusTag.transform, string.Empty, 14f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopMint);
      FirstLevelUiFactory.Stretch(_cartStatusText.rectTransform, new Vector2(6f, 2f), new Vector2(-6f, -2f));
      cartStatusTag.gameObject.SetActive(false);
      _carts.Add(_cart);
      if (_cart != null) _productionCartHome = _cart.anchoredPosition;
      BuildProductionBottle();

      var coinTag = FirstLevelUiFactory.CreateImage("Coins Exit Tag", _transportRoot, WorkshopPaper, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(coinTag.rectTransform, new Vector2(0.965f, 0.12f), new Vector2(0.965f, 0.12f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(62f, 62f));
      var cartSaleLabel = FirstLevelUiFactory.CreateText("Cart Sale Label", coinTag.transform, "COINS", 12f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopInk);
      FirstLevelUiFactory.Stretch(cartSaleLabel.rectTransform, new Vector2(3f, 2f), new Vector2(-3f, -2f));

      var filterCatalog = Resources.Load<CareStationFilterArtCatalog>("CareStation/Filter/CareStationFilterArtCatalog");
      var storageBottleSprite = FindLevelOneSprites(filterCatalog)?.bottleGlassSprite;
      for (var i = 0; i < 12; i++)
      {
        var xp = FirstLevelUiFactory.CreateImage("Stored Bottle Marker", _storageTank, KeepBlinkingTheme.WithAlpha(Color.white, 0.9f), storageBottleSprite);
        xp.preserveAspect = true;
        var column = i % 6;
        var row = i / 6;
        var x = 0.22f + column * 0.112f;
        var y = 0.42f + row * 0.27f;
        FirstLevelUiFactory.SetRect(xp.rectTransform, new Vector2(x, y), new Vector2(x, y), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(25f, 38f));
        xp.gameObject.SetActive(false);
        _xpVisuals.Add(xp);
      }
    }

    private void BuildProductionBottle()
    {
      var filterCatalog = Resources.Load<CareStationFilterArtCatalog>("CareStation/Filter/CareStationFilterArtCatalog");
      var l1 = FindLevelOneSprites(filterCatalog);
      _productionBottle = FirstLevelUiFactory.CreateObject("Representative Production Bottle", _stationStage).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_productionBottle, FillerBottleAnchor, FillerBottleAnchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56f, 84f));

      _productionPackage = FirstLevelUiFactory.CreateImage("Bottle Paper Package", _productionBottle, KeepBlinkingTheme.WithAlpha(WorkshopPaper, 0.34f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(_productionPackage.rectTransform, new Vector2(-8f, -6f), new Vector2(8f, 9f));
      _productionPackage.raycastTarget = false;

      var liquidMaskObject = FirstLevelUiFactory.CreateObject("Bottle Liquid Mask", _productionBottle);
      _productionBottleLiquidMask = liquidMaskObject.GetComponent<RectTransform>();
      _productionBottleLiquidMask.anchorMin = _productionBottleLiquidMask.anchorMax = new Vector2(0.5f, 0.08f);
      _productionBottleLiquidMask.pivot = new Vector2(0.5f, 0f);
      _productionBottleLiquidMask.anchoredPosition = Vector2.zero;
      _productionBottleLiquidMask.sizeDelta = new Vector2(34f, 0f);
      liquidMaskObject.AddComponent<RectMask2D>();
      _productionBottleLiquid = FirstLevelUiFactory.CreateImage("Bottle Liquid Body", _productionBottleLiquidMask, Color.white, l1?.bottleLiquidBodySprite);
      _productionBottleLiquid.preserveAspect = true;
      FirstLevelUiFactory.SetRect(_productionBottleLiquid.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(34f, 50f));
      _productionBottleLiquidSurface = FirstLevelUiFactory.CreateImage("Bottle Liquid Surface", _productionBottle, Color.white, l1?.bottleLiquidSurfaceSprite);
      _productionBottleLiquidSurface.preserveAspect = true;
      FirstLevelUiFactory.SetRect(_productionBottleLiquidSurface.rectTransform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(36f, 10f));

      _productionBottleBody = FirstLevelUiFactory.CreateImage("Bottle Glass", _productionBottle, Color.white, l1?.bottleGlassSprite);
      _productionBottleBody.preserveAspect = true;
      FirstLevelUiFactory.Stretch(_productionBottleBody.rectTransform);
      _productionBottleCap = FirstLevelUiFactory.CreateImage("Bottle Cap", _productionBottle, WorkshopMetal, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(_productionBottleCap.rectTransform, new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 9f));
      _productionBottleLabel = FirstLevelUiFactory.CreateImage("Bottle Warm Paper Label", _productionBottle, WorkshopPaper, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(_productionBottleLabel.rectTransform, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(38f, 20f));
      _productionBottle.gameObject.SetActive(false);
    }

    private static CareStationFilterArtCatalog.LevelSprites FindLevelOneSprites(CareStationFilterArtCatalog catalog)
    {
      if (catalog == null || catalog.Levels == null) return null;
      for (var index = 0; index < catalog.Levels.Length; index++)
        if (catalog.Levels[index] != null && catalog.Levels[index].level == 1)
          return catalog.Levels[index];
      return null;
    }

    private void BuildCrew()
    {
      var positions = new[]
      {
        new Vector2(0.20f, 0.49f),
        new Vector2(0.80f, 0.49f),
        new Vector2(0.50f, 0.30f),
      };
      for (var i = 0; i < positions.Length; i++)
      {
        var crew = CareStationWorkerArtView.Create(_stationStage, i, positions[i]);
        crew.gameObject.SetActive(i == 0);
        _crew.Add(crew);
      }
    }

    private void BuildCareRoutineDock()
    {
      _routineDock = FirstLevelUiFactory.CreateObject("Care Routine Dock", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_routineDock, new Vector2(0.035f, 0.082f), new Vector2(0.965f, 0.19f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var shadow = FirstLevelUiFactory.CreateImage("Routine Wood Backing", _routineDock, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(shadow.rectTransform);
      shadow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -0.18f);
      var surface = FirstLevelUiFactory.CreateImage("Routine Paper Sheet", _routineDock, WorkshopPaperDim, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(surface.rectTransform, new Vector2(6f, 6f), new Vector2(-6f, -6f));
      surface.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 0.22f);
      surface.raycastTarget = false;
      _routineDockTitle = FirstLevelUiFactory.CreateText("Routine Title", _routineDock, "TODAY'S EYE CARE", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, WorkshopInk);
      FirstLevelUiFactory.SetRect(_routineDockTitle.rectTransform, new Vector2(0.045f, 0.54f), new Vector2(0.56f, 0.90f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _routineDockTitle.raycastTarget = false;
      _routineHintText = FirstLevelUiFactory.CreateText("Routine Step Summary", _routineDock, "READY FOR TODAY'S ROUTINE", 14f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, WorkshopInk);
      FirstLevelUiFactory.SetRect(_routineHintText.rectTransform, new Vector2(0.045f, 0.12f), new Vector2(0.56f, 0.53f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      for (var index = 0; index < 4; index++)
      {
        var x = 0.12f + index * 0.12f;
        var dot = FirstLevelUiFactory.CreateImage("Routine Step Dot", _routineDock, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(x, 0.18f), new Vector2(x, 0.18f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(16f, 16f));
        dot.raycastTarget = false;
        _routineDockDots.Add(dot);
        var label = FirstLevelUiFactory.CreateText("Routine Step Label", _routineDock, string.Empty, 13f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextMuted, true);
        FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(x - 0.05f, 0.02f), new Vector2(x + 0.05f, 0.15f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        label.raycastTarget = false;
        _routineDockLabels.Add(label);
      }

      _routinePrimaryButton = CreateWorkshopButton("Routine Primary Prompt", _routineDock, WorkshopMetal);
      FirstLevelUiFactory.SetRect((RectTransform)_routinePrimaryButton.transform, new Vector2(0.59f, 0.16f), new Vector2(0.955f, 0.84f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _routinePrimaryText = FirstLevelUiFactory.CreateText("Routine Primary Text", _routinePrimaryButton.transform, "STATION WORKING", 16f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopPaper, true);
      FirstLevelUiFactory.Stretch(_routinePrimaryText.rectTransform, new Vector2(12f, 4f), new Vector2(-12f, -4f));
      _routinePrimaryText.raycastTarget = false;
      RegisterButtonBinding(_routinePrimaryButton, () => StartCareSelected?.Invoke());

      if (_statusText != null) _statusText.gameObject.SetActive(true);
      if (_xpReady != null) _xpReady.gameObject.SetActive(false);
      RefreshRoutineDock(null);
    }

    private void BuildNavigation()
    {
      _navigationRoot = FirstLevelUiFactory.CreateObject("Station Navigation", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_navigationRoot, new Vector2(0.03f, 0.008f), new Vector2(0.97f, 0.069f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var labels = new[] { "STATION", "UPGRADES", "REPORTS" };
      var rotations = new[] { -0.8f, 0.55f, -0.3f };
      for (var index = 0; index < labels.Length; index++)
      {
        var min = new Vector2(index / 3f + 0.014f, 0.08f);
        var max = new Vector2((index + 1) / 3f - 0.014f, 0.92f);
        var selected = index == 0;
        var captured = index;
        var button = CreateWorkshopButton(labels[index] + " Tab", _navigationRoot, selected ? WorkshopWood : WorkshopOutline);
        var tab = button.targetGraphic as Image;
        FirstLevelUiFactory.SetRect((RectTransform)button.transform, min, max, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ((RectTransform)button.transform).localRotation = Quaternion.Euler(0f, 0f, rotations[index]);
        RegisterButtonBinding(button, () => NavigationSelected?.Invoke(captured));
        _navigationButtons.Add(button);
        _navigationTabs.Add(tab);
        var text = FirstLevelUiFactory.CreateText(labels[index] + " Label", tab.transform, labels[index], 16f, FontStyles.Bold, TextAlignmentOptions.Center,
          selected ? WorkshopPaper : WorkshopPaperDim);
        FirstLevelUiFactory.Stretch(text.rectTransform, new Vector2(4f, 3f), new Vector2(-4f, -3f));
        text.raycastTarget = false;
        _navigationLabels.Add(text);
        if (index == 1)
        {
          _upgradeOpportunityDot = FirstLevelUiFactory.CreateImage("Upgrade Opportunity", tab.transform, KeepBlinkingTheme.AccentWarm, FirstLevelUiFactory.CircleSprite);
          FirstLevelUiFactory.SetRect(_upgradeOpportunityDot.rectTransform, new Vector2(0.88f, 0.74f), new Vector2(0.88f, 0.74f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(18f, 18f));
          _upgradeOpportunityDot.raycastTarget = false;
          _upgradeOpportunityDot.gameObject.SetActive(false);
        }
      }
    }

    private void SetNavigationSelection(int selectedIndex)
    {
      for (var index = 0; index < _navigationTabs.Count; index++)
      {
        var selected = index == selectedIndex;
        _navigationTabs[index].color = selected ? WorkshopWood : WorkshopOutline;
        if (index < _navigationLabels.Count)
          _navigationLabels[index].color = selected ? WorkshopPaper : WorkshopPaperDim;
      }
    }

    private void RefreshRoutineDock(CareRecipeSaveData recipe)
    {
      if (_routineDockTitle == null) return;
      _routineDockTitle.text = recipe == null ? "TODAY'S EYE CARE" : RoutineTitle(recipe);
      if (_routineHintText != null)
      {
        _routineHintText.gameObject.SetActive(true);
        if (recipe == null || recipe.ActionCount <= 0)
          _routineHintText.text = "READY FOR TODAY'S ROUTINE";
        else if (recipe.recipeCompleted)
          _routineHintText.text = "ROUTINE COMPLETE";
        else
        {
          var visibleStep = Mathf.Clamp(recipe.currentActionIndex + 1, 1, recipe.ActionCount);
          var action = ShortActionLabel(recipe.CurrentAction).Replace("\n", " ");
          _routineHintText.text = $"STEP {visibleStep}/{recipe.ActionCount}  ·  {action}";
        }
      }
      for (var index = 0; index < _routineDockDots.Count; index++)
      {
        _routineDockDots[index].gameObject.SetActive(false);
        _routineDockLabels[index].gameObject.SetActive(false);
      }
    }

    private static string RoutineTitle(CareRecipeSaveData recipe)
    {
      if (recipe == null) return "TODAY'S EYE CARE";
      switch (recipe.routineId)
      {
        case CareRoutineId.FocusFlow: return "A · FOCUS FLOW";
        case CareRoutineId.PilotFlow: return recipe.recipeType == CareRecipeType.Inspection
          ? "STATION INSPECTION · PILOT FLOW"
          : "B · PILOT FLOW";
        case CareRoutineId.DeepReset: return "C · DEEP RESET";
        case CareRoutineId.FullCare: return "D · FULL CARE";
        default: return "TODAY'S EYE CARE";
      }
    }

    private void SetRoutinePrimary(string text)
    {
      var value = text ?? string.Empty;
      if (_routinePrimaryText != null) _routinePrimaryText.text = value;
      if (_routinePrimaryButton == null) return;
      var canStartCare = string.Equals(value, "START CARE", StringComparison.Ordinal);
      _routinePrimaryButton.interactable = canStartCare;
      if (_routinePrimaryButton.targetGraphic != null)
        _routinePrimaryButton.targetGraphic.raycastTarget = canStartCare;
    }

    private void RegisterButtonBinding(Button button, UnityAction action)
    {
      if (button == null || action == null) return;
      if (_ownedButtonBindings.TryGetValue(button, out var previous) && previous != null)
        button.onClick.RemoveListener(previous);
      _ownedButtonBindings[button] = action;
    }

    private void BindInputHandlers()
    {
      foreach (var pair in _ownedButtonBindings)
      {
        if (pair.Key == null || pair.Value == null) continue;
        pair.Key.onClick.RemoveListener(pair.Value);
        pair.Key.onClick.AddListener(pair.Value);
      }
      if (_routinePrimaryText != null) SetRoutinePrimary(_routinePrimaryText.text);
    }

    private void SetProductionAnimation(bool active)
    {
      _productionAnimating = active;
      if (_routinePrimaryButton != null) _routinePrimaryButton.interactable = !active;
      if (!active)
      {
        _renderedProductionStage = CareProductionStage.None;
        if (_productionBottle != null) _productionBottle.gameObject.SetActive(false);
        if (_cart != null) _cart.anchoredPosition = _productionCartHome;
        if (_fillerLevel != null) _fillerLevel.rectTransform.localScale = Vector3.one;
        if (_baseInputPipe != null) _baseInputPipe.color = KeepBlinkingTheme.WithAlpha(WorkshopPaperDim, 0.34f);
        SetRouteColor(_liquidTransportSegments, false, WorkshopMetalLight);
        SetRouteColor(_manualCarryMarkers, false, WorkshopPaperDim);
        SetRouteColor(_conveyorSegments, false, WorkshopMetal);
        if (_storageToCartRoute != null)
          _storageToCartRoute.color = KeepBlinkingTheme.WithAlpha(WorkshopPaperDim, 0.3f);
        if (_filterArt != null)
        {
          _filterArt.SetIntegratedBottleVisible(false);
          _filterArt.SetProductionVisual(FilterProductionVisualState.Idle, 0f);
        }
      }
    }

    private void UpdateProductionAnimation()
    {
      // Production v22 is rendered only from the persisted stage supplied by
      // ShowProductionStage. There is no independent visual-only bottle loop.
      if (_productionAnimating || _productionBottle == null) return;
      _productionBottle.gameObject.SetActive(false);
    }

    private void ApplyTransportModeVisuals(CareProductionTransportMode mode)
    {
      _transportMode = mode;
      var manual = mode == CareProductionTransportMode.ManualCarry;
      if (_manualCarryRoot != null) _manualCarryRoot.gameObject.SetActive(manual);
      if (_manualFilterHoseRoot != null) _manualFilterHoseRoot.gameObject.SetActive(manual);
      if (_basicConveyorRoot != null) _basicConveyorRoot.gameObject.SetActive(!manual);
      if (_fixedFilterPipeRoot != null) _fixedFilterPipeRoot.gameObject.SetActive(!manual);
    }

    private void SetProductionBottleFill(float fill, bool visible)
    {
      fill = Mathf.Clamp01(fill);
      if (_productionBottleLiquidMask != null)
        _productionBottleLiquidMask.sizeDelta = new Vector2(34f, 50f * fill);
      if (_productionBottleLiquid != null)
        _productionBottleLiquid.gameObject.SetActive(visible && fill > 0.001f);
      if (_productionBottleLiquidSurface != null)
      {
        _productionBottleLiquidSurface.gameObject.SetActive(visible && fill > 0.001f);
        var anchor = new Vector2(0.5f, 0.08f + 0.60f * fill);
        _productionBottleLiquidSurface.rectTransform.anchorMin = anchor;
        _productionBottleLiquidSurface.rectTransform.anchorMax = anchor;
        _productionBottleLiquidSurface.rectTransform.anchoredPosition = Vector2.zero;
      }
    }

    private static Vector2 EvaluatePolyline(Vector2[] points, float progress)
    {
      if (points == null || points.Length == 0) return Vector2.zero;
      if (points.Length == 1) return points[0];
      progress = Mathf.Clamp01(progress);
      var lengths = new float[points.Length - 1];
      var total = 0f;
      for (var index = 0; index < lengths.Length; index++)
      {
        lengths[index] = Vector2.Distance(points[index], points[index + 1]);
        total += lengths[index];
      }
      if (total <= 0.0001f) return points[points.Length - 1];
      var distance = total * progress;
      for (var index = 0; index < lengths.Length; index++)
      {
        if (distance <= lengths[index] || index == lengths.Length - 1)
          return Vector2.Lerp(points[index], points[index + 1], lengths[index] <= 0f ? 1f : distance / lengths[index]);
        distance -= lengths[index];
      }
      return points[points.Length - 1];
    }

    private static void SetRouteColor(List<Image> route, bool active, Color idle)
    {
      if (route == null) return;
      var color = active
        ? KeepBlinkingTheme.WithAlpha(WorkshopMint, 0.94f)
        : KeepBlinkingTheme.WithAlpha(idle, 0.34f);
      for (var index = 0; index < route.Count; index++)
        if (route[index] != null) route[index].color = color;
    }

    private static string ShortActionLabel(CareActionType action)
    {
      switch (action)
      {
        case CareActionType.ClosedEyeRest: return "CLOSED-EYE\nREST";
        case CareActionType.FocusShift: return "FOCUS\nSHIFT";
        case CareActionType.GuidedEyeCircles: return "GUIDED\nMOVEMENT";
        case CareActionType.PilotEyeRoutine: return "PILOT EYE\nROUTINE";
        default: return string.Empty;
      }
    }

    private static string ProductionStageText(CareProductionStage stage)
    {
      switch (stage)
      {
        case CareProductionStage.FilterProcessing: return "FILTERING";
        case CareProductionStage.TransferFilteredLiquid: return "TRANSFERRING";
        case CareProductionStage.FillerCreateBottle:
        case CareProductionStage.FillerFilling:
        case CareProductionStage.FillerFilled: return "FILLING";
        case CareProductionStage.TransferToPacker: return "TRANSFERRING";
        case CareProductionStage.PackerCapping: return "CAPPING";
        case CareProductionStage.PackerLabeling: return "LABELING";
        case CareProductionStage.PackerPackaging: return "PACKAGING";
        case CareProductionStage.TransferToStorage: return "TRANSFERRING";
        case CareProductionStage.WaitingForStorage: return "WAITING FOR STORAGE";
        default: return "READY";
      }
    }

    private void ApplyProductionStageStatus(CareProductionStage stage)
    {
      switch (stage)
      {
        case CareProductionStage.FilterProcessing:
          SetFactoryStatus(string.Empty, "FILTERING");
          break;
        case CareProductionStage.TransferFilteredLiquid:
          SetFactoryStatus(string.Empty, "TRANSFERRING");
          break;
        case CareProductionStage.FillerCreateBottle:
          SetFactoryStatus(string.Empty, "IDLE", "EMPTY BOTTLE");
          break;
        case CareProductionStage.FillerFilling:
        case CareProductionStage.FillerFilled:
          SetFactoryStatus(string.Empty, "IDLE", "FILLING");
          break;
        case CareProductionStage.TransferToPacker:
          SetFactoryStatus(string.Empty, "IDLE", "TRANSFERRING");
          break;
        case CareProductionStage.PackerCapping:
          SetFactoryStatus(string.Empty, "IDLE", "IDLE", "CAPPING");
          break;
        case CareProductionStage.PackerLabeling:
          SetFactoryStatus(string.Empty, "IDLE", "IDLE", "LABELING");
          break;
        case CareProductionStage.PackerPackaging:
          SetFactoryStatus(string.Empty, "IDLE", "IDLE", "PACKAGING");
          break;
        case CareProductionStage.TransferToStorage:
          SetFactoryStatus(string.Empty, "IDLE", "IDLE", "TRANSFERRING");
          break;
        case CareProductionStage.WaitingForStorage:
          SetFactoryStatus(string.Empty, "IDLE", "IDLE", "IDLE", "WAITING FOR STORAGE\nSTORAGE FULL");
          break;
        default:
          SetFactoryStatus(string.Empty);
          break;
      }
    }

    private void SetFactoryStatus(
      string global,
      string filter = "IDLE",
      string filler = "IDLE",
      string packer = "IDLE",
      string storage = "READY",
      string cart = "READY")
    {
      if (_statusText != null) _statusText.text = global ?? string.Empty;
      SetFactoryStatusLabel(_filterStatusText, filter);
      SetFactoryStatusLabel(_fillerStatusText, filler);
      SetFactoryStatusLabel(_packerStatusText, packer);
      SetFactoryStatusLabel(_storageStatusText, storage);
      SetFactoryStatusLabel(_cartStatusText, cart);
    }

    private static void SetFactoryStatusLabel(TextMeshProUGUI label, string value)
    {
      if (label == null) return;
      label.text = value ?? string.Empty;
      var waiting = value != null &&
                    (value.IndexOf("FULL", StringComparison.Ordinal) >= 0 ||
                     value.IndexOf("WAIT", StringComparison.Ordinal) >= 0);
      var inactive = string.Equals(value, "IDLE", StringComparison.Ordinal) ||
                     string.Equals(value, "READY", StringComparison.Ordinal) ||
                     string.IsNullOrWhiteSpace(value);
      var visualRoot = label.transform.parent != null &&
                       label.transform.parent.name.IndexOf("Status Tag", StringComparison.Ordinal) >= 0
        ? label.transform.parent.gameObject
        : label.gameObject;
      visualRoot.SetActive(!inactive);
      label.color = waiting
        ? WorkshopPaper
        : inactive ? WorkshopPaperDim : WorkshopMint;
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
        var dust = FirstLevelUiFactory.CreateImage("Legacy Particle Group", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.42f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dust.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), dustOffsets[i], new Vector2(42f + i * 8f, 42f + i * 8f));
        _dustGroups.Add(dust);
      }
      for (var i = 0; i < 4; i++)
      {
        var crack = FirstLevelUiFactory.CreateImage("Dry Crack", _incidentRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.72f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(crack.rectTransform, new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.58f), new Vector2(0.5f, 0.5f), new Vector2((i - 1) * 28f, (i % 2 == 0 ? 1f : -1f) * 10f), new Vector2(7f, 108f));
        crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -24f + i * 24f);
        crack.gameObject.SetActive(false);
        _dryCracks.Add(crack);
      }
      _incidentLabel = FirstLevelUiFactory.CreateText("Legacy Care Label", _incidentRoot, "CARE", 25f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
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
      for (var i = 0; i < 4; i++)
      {
        var dot = FirstLevelUiFactory.CreateImage("Recipe Step Dot", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.2f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.815f), new Vector2(0.5f, 0.5f), new Vector2((i - 1.5f) * 34f, 0f), new Vector2(14f, 14f));
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
      for (var i = 0; i < 6; i++)
      {
        var dot = FirstLevelUiFactory.CreateImage("Distance Guide Dot", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.5f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(dot.rectTransform, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(13f, 13f));
        _distanceGuideDots.Add(dot.rectTransform);

        var step = FirstLevelUiFactory.CreateImage("Focus Step", _actionRoot, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.18f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(step.rectTransform, new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.34f), new Vector2(0.5f, 0.5f), new Vector2((i - 2.5f) * 38f, 0f), new Vector2(18f, 18f));
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
      _pilotRoot = FirstLevelUiFactory.CreateObject("Pilot Eye Guide", _actionRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_pilotRoot, new Vector2(0.5f, 0.57f), new Vector2(0.5f, 0.57f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360f, 360f));
      var axisRotations = new[] { 90f, 0f, -45f, 45f };
      for (var index = 0; index < axisRotations.Length; index++)
      {
        var axis = FirstLevelUiFactory.CreateImage($"Pilot Axis {index + 1}", _pilotRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.18f), FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(axis.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(270f, 5f));
        axis.rectTransform.localRotation = Quaternion.Euler(0f, 0f, axisRotations[index]);
        axis.raycastTarget = false;
        _pilotAxes.Add(axis);
      }
      var endpointDirections = new[]
      {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new Vector2(-0.707f, 0.707f), new Vector2(0.707f, -0.707f),
        new Vector2(0.707f, 0.707f), new Vector2(-0.707f, -0.707f),
      };
      for (var index = 0; index < endpointDirections.Length; index++)
      {
        var endpoint = FirstLevelUiFactory.CreateImage($"Pilot Endpoint {index + 1}", _pilotRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.32f), FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(endpoint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f), endpointDirections[index] * 135f, new Vector2(17f, 17f));
        endpoint.raycastTarget = false;
        _pilotEndpoints.Add(endpoint);
      }
      var pilotBody = FirstLevelUiFactory.CreateImage("Worker Body", _pilotRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.94f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(pilotBody.rectTransform, new Vector2(0.5f, 0.24f), new Vector2(0.5f, 0.24f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(116f, 86f));
      var pilotHead = FirstLevelUiFactory.CreateImage("Worker Head", _pilotRoot,
        KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(pilotHead.rectTransform, new Vector2(0.5f, 0.53f), new Vector2(0.5f, 0.53f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(172f, 150f));
      var leftEye = FirstLevelUiFactory.CreateImage("Left Eye", pilotHead.rectTransform,
        KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(leftEye.rectTransform, new Vector2(0.32f, 0.54f), new Vector2(0.32f, 0.54f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 38f));
      var rightEye = FirstLevelUiFactory.CreateImage("Right Eye", pilotHead.rectTransform,
        KeepBlinkingTheme.SurfaceOverlay, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(rightEye.rectTransform, new Vector2(0.68f, 0.54f), new Vector2(0.68f, 0.54f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 38f));
      _pilotLeftPupil = FirstLevelUiFactory.CreateImage("Left Pupil", leftEye.rectTransform,
        KeepBlinkingTheme.BackgroundPrimary, FirstLevelUiFactory.CircleSprite).rectTransform;
      FirstLevelUiFactory.SetRect(_pilotLeftPupil, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
      _pilotRightPupil = FirstLevelUiFactory.CreateImage("Right Pupil", rightEye.rectTransform,
        KeepBlinkingTheme.BackgroundPrimary, FirstLevelUiFactory.CircleSprite).rectTransform;
      FirstLevelUiFactory.SetRect(_pilotRightPupil, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(20f, 20f));
      _pilotGuideDot = FirstLevelUiFactory.CreateImage("Pilot Guide Dot", _pilotRoot,
        KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite).rectTransform;
      FirstLevelUiFactory.SetRect(_pilotGuideDot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
      _pilotRoot.gameObject.SetActive(false);
      _actionPrompt = FirstLevelUiFactory.CreateText("Action Prompt", _actionRoot, "REST", 38f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_actionPrompt.rectTransform, new Vector2(0f, 0.12f), new Vector2(1f, 0.28f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _actionPurpose = FirstLevelUiFactory.CreateText("Action Purpose", _actionRoot, string.Empty, 18f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary, true);
      FirstLevelUiFactory.SetRect(_actionPurpose.rectTransform, new Vector2(0.08f, 0.28f), new Vector2(0.92f, 0.38f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _actionPurpose.raycastTarget = false;
      _fallbackButton = FirstLevelUiFactory.CreateButton("Collect Fallback", _actionRoot, "COLLECT", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_fallbackButton.transform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 92f));
      RegisterButtonBinding(_fallbackButton, () => FallbackCollectSelected?.Invoke());
      _returnFallbackButton = FirstLevelUiFactory.CreateButton("Return Fallback", _actionRoot, "CONTINUE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_returnFallbackButton.transform, new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.08f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 92f));
      RegisterButtonBinding(_returnFallbackButton, () => ReturnFallbackSelected?.Invoke());
      _returnFallbackButton.gameObject.SetActive(false);
      _changeStepButton = FirstLevelUiFactory.CreateButton("Change Care Step", _actionRoot, "CHANGE STEP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)_changeStepButton.transform, new Vector2(0.18f, 0.055f), new Vector2(0.18f, 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190f, 70f));
      RegisterButtonBinding(_changeStepButton, () => ChangeStepSelected?.Invoke());
      _changeStepButton.gameObject.SetActive(false);
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
      RegisterButtonBinding(use, () => UseRestSelected?.Invoke());
      var keep = FirstLevelUiFactory.CreateButton("Keep Step", _changeStepConfirmRoot, "KEEP STEP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)keep.transform, new Vector2(0.53f, 0.13f), new Vector2(0.92f, 0.36f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(keep, () => KeepStepSelected?.Invoke());
      SetPanelVisible(_changeStepConfirmRoot, false);
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
      var back = FirstLevelUiFactory.CreateButton("Back To Station", _upgradeRoot, "BACK TO STATION", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)back.transform, new Vector2(0.22f, 0.105f), new Vector2(0.78f, 0.195f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(back, () => UpgradeBackSelected?.Invoke());
    }

    private void BuildToast()
    {
      _toastText = FirstLevelUiFactory.CreateText(
        "Station Toast",
        _content,
        string.Empty,
        24f,
        FontStyles.Bold,
        TextAlignmentOptions.Center,
        KeepBlinkingTheme.AccentWarm,
        true);
      FirstLevelUiFactory.SetRect(_toastText.rectTransform, new Vector2(0.23f, 0.72f), new Vector2(0.77f, 0.81f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _toastText.raycastTarget = false;
      _toastText.gameObject.SetActive(false);
    }

    private void CreateUpgradeCard(CareStationUpgradeId id, float y)
    {
      var button = FirstLevelUiFactory.CreateButton(id.ToString(), _upgradeRoot, string.Empty, KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)button.transform, new Vector2(0.08f, y), new Vector2(0.92f, y + 0.14f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var cardText = FirstLevelUiFactory.CreateText("Card Text", button.transform, string.Empty, 22f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(cardText.rectTransform, new Vector2(0.055f, 0.08f), new Vector2(0.95f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(button, () => UpgradeSelected?.Invoke(id));
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
        RegisterButtonBinding(minus, () => AdjustSurveyScore(captured, -1));
        var value = FirstLevelUiFactory.CreateText("Score Value", panel.transform, "--", 32f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentPrimary);
        FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(0.67f, rowY - 0.02f), new Vector2(0.79f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        _surveyValues.Add(value);
        var plus = FirstLevelUiFactory.CreateButton("Increase Score", panel.transform, "+", KeepBlinkingTheme.SurfaceElevated);
        FirstLevelUiFactory.SetRect((RectTransform)plus.transform, new Vector2(0.80f, rowY - 0.02f), new Vector2(0.91f, rowY + 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        RegisterButtonBinding(plus, () => AdjustSurveyScore(captured, 1));
      }
      _surveyContinueButton = FirstLevelUiFactory.CreateButton("Continue Care Check", panel.transform, "CONTINUE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_surveyContinueButton.transform, new Vector2(0.12f, 0.055f), new Vector2(0.60f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(_surveyContinueButton, () =>
      {
        if (!_surveyDraft.HasAllResponses) return;
        var submitted = _surveyDraft.Clone();
        submitted.submitted = true;
        submitted.skipped = false;
        SubjectiveScoresSubmitted?.Invoke(_surveyIsPost, submitted);
      });
      var skip = FirstLevelUiFactory.CreateButton("Skip Care Check", panel.transform, "SKIP", KeepBlinkingTheme.SurfaceElevated);
      FirstLevelUiFactory.SetRect((RectTransform)skip.transform, new Vector2(0.64f, 0.055f), new Vector2(0.88f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(skip, () => SubjectiveScoresSkipped?.Invoke(_surveyIsPost));
      SetPanelVisible(_surveyRoot, false);
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
      RegisterButtonBinding(done, () => CareReportDoneSelected?.Invoke());
      SetPanelVisible(_reportRoot, false);
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
      for (var index = 0; index < 4; index++)
      {
        var icon = FirstLevelUiFactory.CreateImage("Completed Care Step", _completeRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
        FirstLevelUiFactory.SetRect(icon.rectTransform, new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.29f), new Vector2(0.5f, 0.5f), new Vector2((index - 1.5f) * 54f, 0f), new Vector2(24f, 24f));
        _completeStepIcons.Add(icon);
      }
      _endShiftButton = FirstLevelUiFactory.CreateButton("End Shift", _completeRoot, "END SHIFT", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)_endShiftButton.transform, new Vector2(0.22f, 0.07f), new Vector2(0.78f, 0.22f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      RegisterButtonBinding(_endShiftButton, () => EndShiftSelected?.Invoke());
    }

    private void BuildHud()
    {
      _hudRoot = FirstLevelUiFactory.CreateObject("Station HUD", _content).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_hudRoot);
      var topOutline = FirstLevelUiFactory.CreateImage("Resource Bar Dark Wood Edge", _hudRoot, WorkshopOutline, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(topOutline.rectTransform, new Vector2(0.035f, 0.918f), new Vector2(0.965f, 0.987f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      topOutline.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -0.18f);
      var top = FirstLevelUiFactory.CreateImage("Compact Workshop Resource Bar", topOutline.transform, WorkshopWood, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(top.rectTransform, new Vector2(6f, 6f), new Vector2(-6f, -6f));
      top.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 0.22f);
      _stationText = FirstLevelUiFactory.CreateText("Station Number Compatibility", top.transform, string.Empty, 1f, FontStyles.Bold, TextAlignmentOptions.Center, Color.clear);
      _stationText.gameObject.SetActive(false);
      CreateHudResourceCard(top.transform, "Coins Counter", "COINS", new Vector2(0.015f, 0.08f), new Vector2(0.325f, 0.92f), WorkshopPaper, out _fullBottleText);
      CreateHudResourceCard(top.transform, "Care Energy Counter", "CARE ENERGY", new Vector2(0.335f, 0.08f), new Vector2(0.665f, 0.92f), WorkshopMint, out _goldBottleText);
      CreateHudResourceCard(top.transform, "Storage Capacity", "STORAGE", new Vector2(0.675f, 0.08f), new Vector2(0.985f, 0.92f), WorkshopMetalLight, out _storageText);

      _xpReady = FirstLevelUiFactory.CreateText("Bottles Ready Compatibility", _hudRoot, string.Empty, 20f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(_xpReady.rectTransform, new Vector2(0.3f, 0.205f), new Vector2(0.7f, 0.235f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _statusText = FirstLevelUiFactory.CreateText("Primary Station Prompt", _hudRoot, string.Empty, 20f, FontStyles.Bold, TextAlignmentOptions.Center, WorkshopPaper, false);
      FirstLevelUiFactory.SetRect(_statusText.rectTransform, new Vector2(0.18f, 0.892f), new Vector2(0.82f, 0.917f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _statusText.gameObject.SetActive(false);
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

    private static void CreateHudResourceCard(
      Transform parent,
      string name,
      string label,
      Vector2 min,
      Vector2 max,
      Color accent,
      out TextMeshProUGUI value)
    {
      var root = FirstLevelUiFactory.CreateObject(name, parent).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, min, max, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var icon = FirstLevelUiFactory.CreateImage(name + " Hand Drawn Token", root, WorkshopOutline, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(icon.rectTransform, new Vector2(0.17f, 0.5f), new Vector2(0.17f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(42f, 42f));
      var iconInner = FirstLevelUiFactory.CreateImage(name + " Token Face", icon.transform, accent, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.SetRect(iconInner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(28f, 28f));
      var labelText = FirstLevelUiFactory.CreateText(name + " Label", root, label, 12f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, WorkshopPaperDim);
      FirstLevelUiFactory.SetRect(labelText.rectTransform, new Vector2(0.33f, 0.5f), new Vector2(0.98f, 0.92f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      value = FirstLevelUiFactory.CreateText(name + " Value", root, "0", 20f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, accent);
      FirstLevelUiFactory.SetRect(value.rectTransform, new Vector2(0.33f, 0.04f), new Vector2(0.98f, 0.56f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
    }

    private void RefreshResourceHud()
    {
      if (_fullBottleText == null) return;
      var stored = _stationSave == null ? 0 : CareStationStorageRules.Stored(_stationSave);
      var capacity = _stationSave == null ? 24 : Mathf.Max(1, _stationSave.storageHours);
      _storageFull = stored >= capacity;
      if (_filterArt != null && (_storageFull || !_productionAnimating))
        _filterArt.SetProductionVisual(FilterProductionVisualState.Idle, 0f);
      _stationText.text = $"STATION {(_stationSave == null ? 1 : Mathf.Max(1, _stationSave.stationLevel))}";
      _fullBottleText.text = (_stationSave == null ? 0 : Mathf.Max(0, _stationSave.coins)).ToString();
      _goldBottleText.text = (_stationSave == null ? 0 : Mathf.Max(0, _stationSave.careEnergy)).ToString();
      _storageText.text = $"{Mathf.Min(stored, capacity)} / {capacity}";
      if (_storageFill != null)
      {
        var fill = Mathf.Clamp01(stored / (float)capacity);
        _storageFill.fillAmount = fill;
        _storageFill.color = fill >= 0.85f ? KeepBlinkingTheme.AccentWarm : KeepBlinkingTheme.AccentPrimary;
      }
      if (_storageStatusText != null && !_productionAnimating)
        SetFactoryStatusLabel(_storageStatusText, _storageFull ? "STORAGE FULL" : "READY");
    }

    private static Vector3 CartScale(int capacity)
    {
      if (capacity >= 12) return new Vector3(1.12f, 1.08f, 1f);
      if (capacity >= 8) return new Vector3(1.08f, 1.06f, 1f);
      if (capacity >= 6) return new Vector3(1.04f, 1.03f, 1f);
      return Vector3.one;
    }

    private static string ResolveActionLabel(string prompt, string status)
    {
      if (!string.IsNullOrWhiteSpace(status))
      {
        if (!status.StartsWith("BOTTLES READY", StringComparison.Ordinal)) return status;
      }
      if (prompt == "REST") return "CLOSE YOUR EYES";
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

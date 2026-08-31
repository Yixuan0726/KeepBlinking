using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeepBlinking.Tests
{
  public sealed class CareStationUiInputPlayModeTests
  {
    private static readonly string[] PrimaryButtonNames =
    {
      "Routine Primary Prompt",
      "STATION Tab",
      "UPGRADES Tab",
      "REPORTS Tab",
    };

    private static readonly string[] HiddenPanelNames =
    {
      "Welcome Back",
      "Care Action",
      "Station Upgrade",
      "Shift Complete",
      "Care Check",
      "Care Report",
      "Change Step Confirmation",
      "EyeMovementGuidanceOverlay",
    };

    private readonly List<EventSystemState> _preexistingEventSystems = new List<EventSystemState>();
    private GameObject _eventSystemObject;
    private GameObject _root;
    private EventSystem _eventSystem;
    private CareStationView _view;
    private float _previousTimeScale;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      _previousTimeScale = Time.timeScale;

      foreach (var system in UnityEngine.Object.FindObjectsByType<EventSystem>(
                 FindObjectsInactive.Include,
                 FindObjectsSortMode.None))
      {
        _preexistingEventSystems.Add(new EventSystemState(system, system.enabled));
        system.enabled = false;
      }

      _eventSystemObject = new GameObject("[TEST] UI EventSystem");
      _eventSystem = _eventSystemObject.AddComponent<EventSystem>();
      _eventSystemObject.AddComponent<StandaloneInputModule>();
      EventSystem.current = _eventSystem;

      _root = new GameObject("[TEST] Care Station UI Input");
      _view = _root.AddComponent<CareStationView>();
      if (!CareStationWorkerArtCatalog.LoadFromResources().IsComplete)
      {
        for (var index = 0; index < 3; index++)
          LogAssert.Expect(LogType.Error,
            "Care Station Worker formal art is incomplete; retired graybox fallback is disabled.");
      }
      _view.Build();
      yield return SettleUi();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      Time.timeScale = _previousTimeScale;
      if (_root != null) UnityEngine.Object.DestroyImmediate(_root);
      if (_eventSystemObject != null) UnityEngine.Object.DestroyImmediate(_eventSystemObject);

      foreach (var state in _preexistingEventSystems)
        if (state.System != null) state.System.enabled = state.Enabled;
      _preexistingEventSystems.Clear();

      var restored = UnityEngine.Object.FindObjectsByType<EventSystem>(
          FindObjectsInactive.Include,
          FindObjectsSortMode.None)
        .FirstOrDefault(system => system != null && system.isActiveAndEnabled);
      if (restored != null) EventSystem.current = restored;
      yield return null;
    }

    [UnityTest]
    public IEnumerator StationLoad_AllFourPrimaryButtonsReceiveRealRaycastClicks()
    {
      _view.ShowStationWorking();
      var startCareCount = 0;
      var navigation = new List<int>();
      _view.StartCareSelected += () => startCareCount++;
      _view.NavigationSelected += navigation.Add;
      yield return SettleUi();

      AssertHealthyInputInfrastructure();
      ClickThroughTopRaycast("Routine Primary Prompt");
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      Assert.That(startCareCount, Is.EqualTo(1));
      Assert.That(navigation, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [UnityTest]
    public IEnumerator StorageTwentyFourOfTwentyFourWithoutPendingReward_StartCareStillClicks()
    {
      var save = FullStorageSave();
      var startCareCount = 0;
      _view.StartCareSelected += () => startCareCount++;
      _view.ShowStorageFullStation(save);
      yield return SettleUi();

      var button = FindButton("Routine Primary Prompt");
      Assert.That(button.interactable, Is.True);
      ClickThroughTopRaycast(button);

      Assert.That(startCareCount, Is.EqualTo(1));
      Assert.That(save.storedFullBottles, Is.EqualTo(24), "A View input test must not mutate stored bottles.");
      Assert.That(save.pendingIncidentXP, Is.Zero);
      Assert.That(save.pendingOfflineXP, Is.Zero);
    }

    [UnityTest]
    public IEnumerator StorageFull_AllThreeBottomNavigationButtonsRemainClickable()
    {
      var navigation = new List<int>();
      _view.NavigationSelected += navigation.Add;
      _view.ShowStorageFullStation(FullStorageSave());
      yield return SettleUi();

      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      Assert.That(navigation, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [UnityTest]
    public IEnumerator ProductionWaitingAndStorageFull_AllBottomNavigationButtonsRemainClickable()
    {
      var navigation = new List<int>();
      _view.NavigationSelected += navigation.Add;
      var save = new CareStationSaveData { storageHours = 24, careEnergy = 3 };

      _view.ShowProductionStage(CareProductionStage.FillerFilling, 0.5f, save);
      yield return SettleUi();
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      save.storedFullBottles = 24;
      _view.ShowProductionStage(CareProductionStage.WaitingForStorage, 1f, save);
      yield return SettleUi();
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      _view.ShowStorageFullStation(save);
      yield return SettleUi();
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      Assert.That(navigation, Is.EqualTo(new[] { 0, 1, 2, 0, 1, 2, 0, 1, 2 }));
    }

    [UnityTest]
    public IEnumerator FactoryDevices_HaveThreeDistinctNonOverlappingClickAreas()
    {
      _view.ShowStationWorking();
      var selected = new List<string>();
      _view.DeviceSelected += selected.Add;
      yield return SettleUi();

      var buttons = new[]
      {
        FindButton("Filter Device"),
        FindButton("Filler Device"),
        FindButton("Packer Device"),
      };
      Assert.That(buttons.Distinct().Count(), Is.EqualTo(3));
      for (var left = 0; left < buttons.Length; left++)
      {
        Assert.That(buttons[left].interactable, Is.True, buttons[left].name);
        for (var right = left + 1; right < buttons.Length; right++)
          Assert.That(WorldRect((RectTransform)buttons[left].transform).Overlaps(
            WorldRect((RectTransform)buttons[right].transform)), Is.False,
            $"{buttons[left].name} overlaps {buttons[right].name}.");
        ClickThroughTopRaycast(buttons[left]);
      }

      Assert.That(selected, Is.EqualTo(new[] { "FILTER", "FILLER", "PACKER" }));
      var energy = FindTransform("Care Energy Source");
      Assert.That(energy.GetComponent<Button>(), Is.Null,
        "Care Energy is an input resource indicator, not a fourth production device.");
    }

    [UnityTest]
    public IEnumerator HiddenModalsDisableCanvasGroupsAndGraphicsAndNeverWinRaycasts()
    {
      _view.ShowUpgrade(FullStorageSave());
      _view.ShowCareReport(FullStorageSave());
      _view.HideAllModals();
      _view.ShowStationWorking();
      yield return SettleUi();

      Assert.That(ReadInternalProperty<bool>("HasVisibleModal"), Is.False);
      foreach (var panelName in HiddenPanelNames)
      {
        var panel = FindTransform(panelName);
        Assert.That(panel.gameObject.activeSelf, Is.False, panelName);
        var group = panel.GetComponent<CanvasGroup>();
        Assert.That(group, Is.Not.Null, panelName + " must have explicit hidden raycast state.");
        Assert.That(group.interactable, Is.False, panelName);
        Assert.That(group.blocksRaycasts, Is.False, panelName);
        Assert.That(panel.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True,
          panelName + " left a Graphic raycast target enabled while hidden.");
      }

      foreach (var name in PrimaryButtonNames)
        AssertTopRaycastRoutesTo(FindButton(name));
    }

    [UnityTest]
    public IEnumerator RebuildAndFilterRefresh_PreserveOneWorkingBindingPerButton()
    {
      var startCareCount = 0;
      var navigation = new List<int>();
      _view.StartCareSelected += () => startCareCount++;
      _view.NavigationSelected += navigation.Add;

      _view.Build();
      InvokeInternal("RebindInputHandlers");
      _view.ApplyStation(new CareStationSaveData { stationLevel = 1, storageHours = 24 });
      _view.ApplyStation(new CareStationSaveData { stationLevel = 2, storageHours = 24 });
      _view.ApplyStation(new CareStationSaveData { stationLevel = 3, storageHours = 24 });
      _view.ShowStationWorking();
      yield return SettleUi();

      var filter = _root.GetComponentInChildren<CareStationFilterArtView>(true);
      Assert.That(filter, Is.Not.Null, "The real FILTER view must participate in the refresh regression.");
      Assert.That(filter.Level, Is.EqualTo(1));
      Assert.That(filter.HitRect, Is.Not.Null);
      Assert.That(filter.HitRect.raycastTarget, Is.False);
      Assert.That(filter.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True,
        "A FILTER level/sprite refresh introduced a Station-wide raycast target.");

      ClickThroughTopRaycast("Routine Primary Prompt");
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");

      Assert.That(startCareCount, Is.EqualTo(1));
      Assert.That(navigation, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [UnityTest]
    public IEnumerator PauseResumeOwnershipSynchronization_RecoversStationInput()
    {
      _view.ShowStationWorking();
      _view.PreviewFullscreenPilotDevelopment();
      yield return SettleUi();
      Assert.That(FindTransform("Guidance Input Shield").gameObject.activeInHierarchy, Is.True);

      SetPrivateField("_developmentGuidancePreviewUntil", -1f);
      InvokeInternal("SynchronizeUiInputOwnership", false);
      // The real controller restores its state presentation after releasing the
      // owner; mirror that without constructing a save-writing controller.
      _view.ShowStationWorking();
      yield return SettleUi();

      Assert.That(ReadInternalProperty<string>("UiInputLockDescription"), Is.EqualTo("owner=NONE"));
      Assert.That(ReadInternalProperty<bool>("HasVisibleModal"), Is.False);
      foreach (var name in PrimaryButtonNames)
        AssertTopRaycastRoutesTo(FindButton(name));
    }

    [UnityTest]
    public IEnumerator DesynchronizedGuidanceWithoutVisibilitySnapshot_RestoresCompleteStationShell()
    {
      _view.ShowStationWorking();
      _view.PreviewFullscreenPilotDevelopment();
      yield return SettleUi();

      SetPrivateField("_guidanceMode", false);
      SetPrivateField("_hudWasVisible", false);
      SetPrivateField("_transportWasVisible", false);
      SetPrivateField("_routineWasVisible", false);
      SetPrivateField("_navigationWasVisible", false);
      SetPrivateField("_developmentGuidancePreviewUntil", -1f);
      InvokeInternal("SynchronizeUiInputOwnership", false);
      yield return SettleUi();

      foreach (var name in new[] { "Station HUD", "Bottle Transport", "Care Routine Dock", "Station Navigation" })
        Assert.That(FindTransform(name).gameObject.activeInHierarchy, Is.True, name);
      Assert.That(ReadInternalProperty<string>("UiInputLockDescription"), Is.EqualTo("owner=NONE"));
      foreach (var name in PrimaryButtonNames)
        AssertTopRaycastRoutesTo(FindButton(name));
    }

    [UnityTest]
    public IEnumerator ExpectedGuidanceWithHiddenShield_ReleasesStaleModeAndRestoresFourPrimaryButtons()
    {
      _view.ShowStationWorking();
      var startCareCount = 0;
      var navigation = new List<int>();
      _view.StartCareSelected += () => startCareCount++;
      _view.NavigationSelected += navigation.Add;
      _view.PreviewFullscreenPilotDevelopment();
      yield return SettleUi();

      var contentGroup = FindTransform("Comfort Padded Content").GetComponent<CanvasGroup>();
      Assert.That(contentGroup, Is.Not.Null);
      Assert.That(contentGroup.interactable, Is.False);
      Assert.That(contentGroup.blocksRaycasts, Is.False);
      Assert.That(ReadPrivateField<bool>("_guidanceMode"), Is.True);

      InvokeMethodOnPrivateField("_eyeMovementGuidance", "HideImmediate");
      SetPrivateField("_developmentGuidancePreviewUntil", -1f);
      var shield = FindTransform("Guidance Input Shield");
      Assert.That(shield.gameObject.activeInHierarchy, Is.False);
      Assert.That(shield.GetComponent<Graphic>().raycastTarget, Is.False);

      // The controller may still report a guidance action for this frame. An
      // invisible shield cannot legitimately retain global input ownership.
      InvokeInternal("SynchronizeUiInputOwnership", true);
      yield return SettleUi();

      Assert.That(ReadPrivateField<bool>("_guidanceMode"), Is.False);
      Assert.That(contentGroup.interactable, Is.True);
      Assert.That(contentGroup.blocksRaycasts, Is.True);
      Assert.That(ReadInternalProperty<string>("UiInputLockDescription"), Is.EqualTo("owner=NONE"));

      ClickThroughTopRaycast("Routine Primary Prompt");
      ClickThroughTopRaycast("STATION Tab");
      ClickThroughTopRaycast("UPGRADES Tab");
      ClickThroughTopRaycast("REPORTS Tab");
      Assert.That(startCareCount, Is.EqualTo(1));
      Assert.That(navigation, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [UnityTest]
    public IEnumerator TimeScaleZeroThenOne_DoesNotLeaveGuidanceShieldOrInputLock()
    {
      _view.ShowStationWorking();
      Time.timeScale = 0f;
      _view.PreviewFullscreenPilotDevelopment();
      yield return SettleUi();

      // Exercise the real animated release path while scaled time is frozen.
      // The overlay must finish with unscaledDeltaTime and release its shield.
      InvokeInternal("ExitEyeMovementGuidance", false);
      var shield = FindTransform("Guidance Input Shield");
      for (var frame = 0; frame < 90 && shield.gameObject.activeInHierarchy; frame++)
        yield return null;
      Assert.That(shield.gameObject.activeInHierarchy, Is.False,
        "The guidance fade did not complete while Time.timeScale was zero.");

      Time.timeScale = 1f;
      _view.ShowStationWorking();
      yield return SettleUi();

      var shieldGraphic = shield.GetComponent<Graphic>();
      Assert.That(shield.gameObject.activeInHierarchy, Is.False);
      Assert.That(shieldGraphic.raycastTarget, Is.False);
      Assert.That(ReadInternalProperty<string>("UiInputLockDescription"), Is.EqualTo("owner=NONE"));
      foreach (var name in PrimaryButtonNames)
        AssertTopRaycastRoutesTo(FindButton(name));
    }

    [UnityTest]
    public IEnumerator LegacyStaleUiSave_LoadsAtStationWithoutInputLockOrEconomyLoss()
    {
      var directory = Path.Combine(
        Application.temporaryCachePath,
        "KeepBlinkingUiInputTests",
        Guid.NewGuid().ToString("N"));
      var path = Path.Combine(directory, "stale-ui-state.json");
      try
      {
        Directory.CreateDirectory(directory);
        var stale = FullStorageSave();
        stale.saveVersion = CareStationSaveService.CurrentVersion;
        stale.currentState = CareStationState.WaitStorageSpace;
        stale.activeCollectionPhase = CareStationCollectionPhase.Care;
        stale.pendingReturnPhase = CareStationCollectionPhase.Care;
        stale.offlineCollectionResolved = false;
        stale.returnedNeutralAfterOffline = false;
        File.WriteAllText(path, JsonUtility.ToJson(stale));

        var restored = new CareStationSaveService(path).Load(DateTime.UtcNow);
        Assert.That(restored.currentState, Is.EqualTo(CareStationState.StationWorking));
        Assert.That(restored.storedFullBottles, Is.EqualTo(24));
        Assert.That(restored.offlineProductionPausedByFullStorage, Is.True);
        Assert.That(restored.offlineCollectionResolved, Is.True);
        Assert.That(restored.returnedNeutralAfterOffline, Is.True);

        var startCareCount = 0;
        _view.StartCareSelected += () => startCareCount++;
        _view.ApplyStation(restored);
        _view.ShowStationWorking();
        yield return SettleUi();

        Assert.That(ReadInternalProperty<string>("UiInputLockDescription"), Is.EqualTo("owner=NONE"));
        ClickThroughTopRaycast("Routine Primary Prompt");
        Assert.That(startCareCount, Is.EqualTo(1));
        Assert.That(restored.storedFullBottles, Is.EqualTo(24));
      }
      finally
      {
        if (File.Exists(path)) File.Delete(path);
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
      }
    }

    [UnityTest]
    public IEnumerator NoVisibleModal_TransparentFullscreenGraphicCannotBeFirstRaycastResult()
    {
      _view.ShowStationWorking();
      yield return SettleUi();
      Assert.That(ReadInternalProperty<bool>("HasVisibleModal"), Is.False);

      foreach (var name in PrimaryButtonNames)
      {
        var button = FindButton(name);
        var results = Raycast(button);
        Assert.That(results, Is.Not.Empty, name);
        var topGraphic = results[0].gameObject.GetComponent<Graphic>();
        var topIsTransparentFullscreen = topGraphic != null &&
                                         topGraphic.canvasRenderer.GetInheritedAlpha() <= 0.001f &&
                                         CoversCanvas(topGraphic.rectTransform);
        Assert.That(topIsTransparentFullscreen, Is.False,
          $"{name} was intercepted by transparent fullscreen Graphic '{HierarchyPath(results[0].gameObject.transform)}'.");
        AssertTopRaycastRoutesTo(button, results);
      }
    }

    [UnityTest]
    public IEnumerator RepeatedRebind_OnePhysicalClickInvokesEachEventOnlyOnce()
    {
      var startCareCount = 0;
      var stationCount = 0;
      _view.StartCareSelected += () => startCareCount++;
      _view.NavigationSelected += index =>
      {
        if (index == 0) stationCount++;
      };
      _view.ShowStationWorking();
      InvokeInternal("RebindInputHandlers");
      InvokeInternal("RebindInputHandlers");
      InvokeInternal("RebindInputHandlers");
      yield return SettleUi();

      ClickThroughTopRaycast("Routine Primary Prompt");
      ClickThroughTopRaycast("STATION Tab");

      Assert.That(startCareCount, Is.EqualTo(1));
      Assert.That(stationCount, Is.EqualTo(1));
    }

    private void AssertHealthyInputInfrastructure()
    {
      Assert.That(EventSystem.current, Is.SameAs(_eventSystem));
      Assert.That(_eventSystem.isActiveAndEnabled, Is.True);
      Assert.That(_eventSystem.currentInputModule, Is.Not.Null);
      Assert.That(_eventSystem.currentInputModule.isActiveAndEnabled, Is.True);
      var canvas = _root.GetComponentInChildren<Canvas>(true);
      Assert.That(canvas, Is.Not.Null);
      Assert.That(canvas.isActiveAndEnabled, Is.True);
      var raycaster = canvas.GetComponent<GraphicRaycaster>();
      Assert.That(raycaster, Is.Not.Null);
      Assert.That(raycaster.isActiveAndEnabled, Is.True);
    }

    private void ClickThroughTopRaycast(string buttonName)
    {
      ClickThroughTopRaycast(FindButton(buttonName));
    }

    private void ClickThroughTopRaycast(Button expectedButton)
    {
      var results = Raycast(expectedButton);
      AssertTopRaycastRoutesTo(expectedButton, results);
      var data = PointerDataAt(expectedButton);
      var receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(results[0].gameObject);
      Assert.That(ExecuteEvents.Execute(receiver, data, ExecuteEvents.pointerClickHandler), Is.True,
        "The top RaycastAll result did not execute a pointerClick handler.");
    }

    private void AssertTopRaycastRoutesTo(Button expectedButton)
    {
      AssertTopRaycastRoutesTo(expectedButton, Raycast(expectedButton));
    }

    private static void AssertTopRaycastRoutesTo(Button expectedButton, IReadOnlyList<RaycastResult> results)
    {
      Assert.That(results, Is.Not.Empty, expectedButton.name);
      var receiver = ExecuteEvents.GetEventHandler<IPointerClickHandler>(results[0].gameObject);
      Assert.That(receiver, Is.SameAs(expectedButton.gameObject),
        $"Top raycast for {expectedButton.name} was '{HierarchyPath(results[0].gameObject.transform)}'.");
    }

    private List<RaycastResult> Raycast(Button button)
    {
      var data = PointerDataAt(button);
      var results = new List<RaycastResult>();
      _eventSystem.RaycastAll(data, results);
      return results;
    }

    private PointerEventData PointerDataAt(Button button)
    {
      var rect = (RectTransform)button.transform;
      var canvas = button.GetComponentInParent<Canvas>();
      var camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
      return new PointerEventData(_eventSystem)
      {
        button = PointerEventData.InputButton.Left,
        pointerId = -1,
        position = RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)),
      };
    }

    private Button FindButton(string name)
    {
      var button = _root.GetComponentsInChildren<Button>(true).FirstOrDefault(candidate => candidate.name == name);
      Assert.That(button, Is.Not.Null, name);
      return button;
    }

    private Transform FindTransform(string name)
    {
      var found = _root.GetComponentsInChildren<Transform>(true).FirstOrDefault(candidate => candidate.name == name);
      Assert.That(found, Is.Not.Null, name);
      return found;
    }

    private bool CoversCanvas(RectTransform rect)
    {
      var canvas = rect.GetComponentInParent<Canvas>();
      if (canvas == null) return false;
      var canvasRect = (RectTransform)canvas.transform;
      var corners = new Vector3[4];
      var canvasCorners = new Vector3[4];
      rect.GetWorldCorners(corners);
      canvasRect.GetWorldCorners(canvasCorners);
      var size = corners[2] - corners[0];
      var canvasSize = canvasCorners[2] - canvasCorners[0];
      return Mathf.Abs(size.x) >= Mathf.Abs(canvasSize.x) * 0.95f &&
             Mathf.Abs(size.y) >= Mathf.Abs(canvasSize.y) * 0.95f;
    }

    private static Rect WorldRect(RectTransform rect)
    {
      var corners = new Vector3[4];
      rect.GetWorldCorners(corners);
      return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private void InvokeInternal(string methodName, params object[] arguments)
    {
      var method = typeof(CareStationView).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null, methodName);
      method.Invoke(_view, arguments);
    }

    private T ReadInternalProperty<T>(string propertyName)
    {
      var property = typeof(CareStationView).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(property, Is.Not.Null, propertyName);
      return (T)property.GetValue(_view);
    }

    private void SetPrivateField(string fieldName, object value)
    {
      var field = typeof(CareStationView).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(field, Is.Not.Null, fieldName);
      field.SetValue(_view, value);
    }

    private T ReadPrivateField<T>(string fieldName)
    {
      var field = typeof(CareStationView).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(field, Is.Not.Null, fieldName);
      return (T)field.GetValue(_view);
    }

    private void InvokeMethodOnPrivateField(string fieldName, string methodName)
    {
      var ownerField = typeof(CareStationView).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(ownerField, Is.Not.Null, fieldName);
      var owner = ownerField.GetValue(_view);
      Assert.That(owner, Is.Not.Null, fieldName);
      var method = owner.GetType().GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null, methodName);
      method.Invoke(owner, null);
    }

    private static CareStationSaveData FullStorageSave()
    {
      return new CareStationSaveData
      {
        currentState = CareStationState.WaitIncidentSelection,
        stationLevel = 1,
        storageHours = 24,
        storedFullBottles = 24,
        storedGoldBottles = 0,
        pendingIncidentXP = 0,
        pendingOfflineXP = 0,
        careActionCompleted = false,
        offlineProductionPausedByFullStorage = true,
      };
    }

    private static IEnumerator SettleUi()
    {
      Canvas.ForceUpdateCanvases();
      yield return null;
      Canvas.ForceUpdateCanvases();
      yield return null;
    }

    private static string HierarchyPath(Transform transform)
    {
      var path = transform.name;
      while (transform.parent != null)
      {
        transform = transform.parent;
        path = transform.name + "/" + path;
      }
      return path;
    }

    private readonly struct EventSystemState
    {
      internal EventSystemState(EventSystem system, bool enabled)
      {
        System = system;
        Enabled = enabled;
      }

      internal EventSystem System { get; }
      internal bool Enabled { get; }
    }
  }
}

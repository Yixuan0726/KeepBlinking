using System;
using System.Collections;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeepBlinking.Tests.PlayMode
{
  public sealed class CareStationPhaseFourLayoutPlayModeTests
  {
    private GameObject _owner;
    private CareStationView _view;
    private int _previousWidth;
    private int _previousHeight;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      _previousWidth = Screen.width;
      _previousHeight = Screen.height;
      _owner = new GameObject("[TEST] Station Workshop Layout");
      _view = _owner.AddComponent<CareStationView>();
      _view.Build();
      _view.ApplyStation(Save());
      _view.ShowStationWorking();
      yield return SettleUi();
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      Screen.SetResolution(_previousWidth, _previousHeight, false);
      if (_owner != null) UnityEngine.Object.DestroyImmediate(_owner);
      yield return null;
    }

    [UnityTest]
    public IEnumerator FactoryTopology_UsesOneAuthoredFilterAndUniqueWorkshopDevicesWithoutWorkers()
    {
      var transforms = AllTransforms();
      Assert.That(transforms.Count(item => item.name == "Filter Device"), Is.EqualTo(1));
      Assert.That(transforms.Count(item => item.name == "Filler Device"), Is.EqualTo(1));
      Assert.That(transforms.Count(item => item.name == "Packer Device"), Is.EqualTo(1));
      var filterArt = _owner.GetComponentInChildren<CareStationFilterArtView>(true);
      Assert.That(filterArt, Is.Not.Null);
      Assert.That(filterArt.gameObject.activeSelf, Is.True);
      Assert.That(filterArt.Level, Is.EqualTo(1));
      Assert.That(transforms.Single(item => item.name == "BottleFillAnchor").gameObject.activeSelf, Is.False);
      Assert.That(transforms.Any(item => item.name == "Care Core Platform"), Is.False);

      var deviceButtons = _owner.GetComponentsInChildren<Button>(true)
        .Where(button => button.name == "Filter Device" || button.name == "Filler Device" || button.name == "Packer Device")
        .ToArray();
      Assert.That(deviceButtons.Select(button => button.name).Distinct(), Is.EquivalentTo(
        new[] { "Filter Device", "Filler Device", "Packer Device" }));
      var energy = transforms.Single(item => item.name == "Care Energy Source");
      Assert.That(energy.GetComponent<Button>(), Is.Null);
      Assert.That(energy.GetComponentsInChildren<Button>(true), Is.Empty);
      Assert.That(_owner.GetComponentsInChildren<CareStationWorkerArtView>(true), Is.Empty);
      yield return null;
    }

    [UnityTest]
    public IEnumerator LevelOne_HasSoftLiquidHoseManualCarryPathAndNoConveyor()
    {
      Assert.That(FindRect("L1 Manual Liquid Hose").gameObject.activeSelf, Is.True);
      Assert.That(FindRect("L1 Manual Bottle Carry Path").gameObject.activeSelf, Is.True);
      Assert.That(FindRect("L2 Fixed Liquid Pipe And Pump").gameObject.activeSelf, Is.False);
      Assert.That(FindRect("L2 Basic Bottle Conveyor").gameObject.activeSelf, Is.False);

      _view.ShowProductionStage(CareProductionStage.TransferFilteredLiquid, 0.5f, Save());
      yield return SettleUi();
      Assert.That(FindRect("Representative Production Bottle").gameObject.activeSelf, Is.False);
    }

    [UnityTest]
    public IEnumerator LevelTwoSwitchesToFixedPipeAndBasicConveyorWithoutChangingDevices()
    {
      var l2 = Save();
      l2.stationLevel = 2;
      l2.inspectionCompleted = true;
      l2.productionTransportMode = CareProductionTransportMode.BasicConveyor;
      l2.basicConveyorUnlockPresented = true;
      _view.ApplyStation(l2);
      yield return SettleUi();

      Assert.That(FindRect("L1 Manual Liquid Hose").gameObject.activeSelf, Is.False);
      Assert.That(FindRect("L1 Manual Bottle Carry Path").gameObject.activeSelf, Is.False);
      Assert.That(FindRect("L2 Fixed Liquid Pipe And Pump").gameObject.activeSelf, Is.True);
      Assert.That(FindRect("L2 Basic Bottle Conveyor").gameObject.activeSelf, Is.True);
      Assert.That(_owner.GetComponentInChildren<CareStationFilterArtView>(true).Level, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator BottleStartsAtFillerAndFollowsManualHandoffsToPackerAndStorage()
    {
      var bottle = FindRect("Representative Production Bottle");
      _view.ShowProductionStage(CareProductionStage.FilterProcessing, 0.5f, Save());
      yield return SettleUi();
      Assert.That(bottle.gameObject.activeSelf, Is.False);

      _view.ShowProductionStage(CareProductionStage.FillerCreateBottle, 0.5f, Save());
      yield return SettleUi();
      var filler = bottle.anchorMin;
      Assert.That(bottle.gameObject.activeSelf, Is.True);

      _view.ShowProductionStage(CareProductionStage.TransferToPacker, 0.5f, Save());
      yield return SettleUi();
      var manualCarry = bottle.anchorMin;
      _view.ShowProductionStage(CareProductionStage.PackerPackaging, 0.5f, Save());
      yield return SettleUi();
      var packer = bottle.anchorMin;
      _view.ShowProductionStage(CareProductionStage.TransferToStorage, 0.5f, Save());
      yield return SettleUi();
      var downstream = bottle.anchorMin;
      _view.ShowProductionStage(CareProductionStage.WaitingForStorage, 1f, FullSave());
      yield return SettleUi();
      var storage = bottle.anchorMin;

      Assert.That(manualCarry.x, Is.GreaterThan(filler.x));
      Assert.That(manualCarry.y, Is.LessThan(filler.y));
      Assert.That(packer.y, Is.LessThan(filler.y));
      Assert.That(downstream.x, Is.LessThan(packer.x));
      Assert.That(storage.x, Is.LessThan(packer.x));
      Assert.That(storage.y, Is.LessThanOrEqualTo(packer.y));
    }

    [UnityTest]
    public IEnumerator RequiredStatusesAppearOnlyBesideTheWorkingDevice()
    {
      var global = FindText("Primary Station Prompt");
      var cases = new[]
      {
        new StatusCase(CareProductionStage.FilterProcessing, "FILTER Status", "FILTERING"),
        new StatusCase(CareProductionStage.TransferFilteredLiquid, "FILTER Status", "TRANSFERRING"),
        new StatusCase(CareProductionStage.FillerFilling, "FILLER Status", "FILLING"),
        new StatusCase(CareProductionStage.PackerCapping, "PACKER Status", "CAPPING"),
        new StatusCase(CareProductionStage.PackerLabeling, "PACKER Status", "LABELING"),
        new StatusCase(CareProductionStage.PackerPackaging, "PACKER Status", "PACKAGING"),
      };

      foreach (var item in cases)
      {
        _view.ShowProductionStage(item.Stage, 0.5f, Save());
        yield return SettleUi();
        Assert.That(TextValue(global), Is.Empty, item.Stage.ToString());
        var status = FindText(item.TextName);
        Assert.That(TextValue(status), Is.EqualTo(item.Expected));
        Assert.That(status.gameObject.activeInHierarchy, Is.True);
        var otherStatuses = new[] { "FILTER Status", "FILLER Status", "PACKER Status" }
          .Where(name => name != item.TextName)
          .Select(FindText);
        Assert.That(otherStatuses.All(text => !text.gameObject.activeInHierarchy), Is.True);
      }

      _view.ShowProductionStage(CareProductionStage.WaitingForStorage, 1f, FullSave());
      yield return SettleUi();
      Assert.That(TextValue(FindText("Storage Status")), Does.Contain("WAITING FOR STORAGE"));
      Assert.That(TextValue(FindText("Storage Status")), Does.Contain("STORAGE FULL"));

      var selling = Save();
      selling.lastCartCoinsEarned = 5;
      _view.ApplyStation(selling);
      _view.ShowWelcome(new CareStationOfflineResult(TimeSpan.FromHours(1), 0, 0, 0));
      yield return SettleUi();
      Assert.That(TextValue(FindText("Cart Status")), Is.EqualTo("SELLING"));
    }

    [UnityTest]
    public IEnumerator RealPhoneViewKeepsFilterLargeAndWorkshopShellInsideSafeArea()
    {
      Screen.SetResolution(368, 797, false);
      yield return SettleUi();
      var safe = RectOf(FindRect("Safe Area"));
      foreach (var name in new[] { "Resource Bar Dark Wood Edge", "Station Stage", "Care Routine Dock", "Station Navigation" })
        AssertContains(safe, RectOf(FindRect(name)), name);

      var filterArt = RectOf(_owner.GetComponentInChildren<CareStationFilterArtView>(true).GetComponent<RectTransform>());
      Assert.That(filterArt.width, Is.InRange(105f, 125f));
      Assert.That(RectOf(FindRect("Filter Device")).Overlaps(RectOf(FindRect("Filler Device"))), Is.False);
      Assert.That(RectOf(FindRect("Filler Device")).Overlaps(RectOf(FindRect("Packer Device"))), Is.False);
      Assert.That(AllTransforms().Any(item => item.name == "Production Status Strip"), Is.False);
      Assert.That(AllTransforms().Any(item => item.name == "Factory Flow Heading"), Is.False);
      Assert.That(AllTransforms().Any(item => item.name == "Downstream Logistics Surface"), Is.False);
    }

    [UnityTest]
    public IEnumerator CommonPortraitRatiosKeepDevicesRoutineAndNavigationSeparated()
    {
      var resolutions = new[] { new Vector2Int(390, 844), new Vector2Int(430, 932), new Vector2Int(412, 915) };
      foreach (var resolution in resolutions)
      {
        Screen.SetResolution(resolution.x, resolution.y, false);
        yield return SettleUi();
        var safe = RectOf(FindRect("Safe Area"));
        var stage = RectOf(FindRect("Station Stage"));
        var routine = RectOf(FindRect("Care Routine Dock"));
        var navigation = RectOf(FindRect("Station Navigation"));
        AssertContains(safe, stage, resolution.ToString());
        AssertContains(safe, routine, resolution.ToString());
        AssertContains(safe, navigation, resolution.ToString());
        Assert.That(stage.yMin, Is.GreaterThanOrEqualTo(routine.yMax - 1.5f), resolution.ToString());
        Assert.That(routine.yMin, Is.GreaterThanOrEqualTo(navigation.yMax - 1.5f), resolution.ToString());
        Assert.That(RectOf(FindRect("Filter Device")).Overlaps(RectOf(FindRect("Filler Device"))), Is.False, resolution.ToString());
        Assert.That(RectOf(FindRect("Filler Device")).Overlaps(RectOf(FindRect("Packer Device"))), Is.False, resolution.ToString());
      }
    }

    private CareStationSaveData Save()
    {
      return new CareStationSaveData
      {
        stationLevel = 1,
        storageLevel = 1,
        cartLevel = 1,
        storageHours = 24,
        careEnergy = 12,
        coins = 8,
        productionTransportMode = CareProductionTransportMode.ManualCarry,
      };
    }

    private CareStationSaveData FullSave()
    {
      var save = Save();
      save.storedFullBottles = save.storageHours;
      save.offlineProductionPausedByFullStorage = true;
      return save;
    }

    private Transform[] AllTransforms() => _owner.GetComponentsInChildren<Transform>(true);

    private RectTransform FindRect(string name)
    {
      var rect = AllTransforms().OfType<RectTransform>().SingleOrDefault(item => item.name == name);
      Assert.That(rect, Is.Not.Null, name);
      return rect;
    }

    private Component FindText(string name)
    {
      var text = _owner.GetComponentsInChildren<Component>(true).SingleOrDefault(item =>
        item != null && item.name == name && item.GetType().FullName == "TMPro.TextMeshProUGUI");
      Assert.That(text, Is.Not.Null, name);
      return text;
    }

    private static string TextValue(Component text)
    {
      return text?.GetType().GetProperty("text")?.GetValue(text) as string ?? string.Empty;
    }

    private static Rect RectOf(RectTransform rect)
    {
      var corners = new Vector3[4];
      rect.GetWorldCorners(corners);
      return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static void AssertContains(Rect outer, Rect inner, string message)
    {
      const float tolerance = 1.5f;
      Assert.That(inner.xMin, Is.GreaterThanOrEqualTo(outer.xMin - tolerance), message);
      Assert.That(inner.yMin, Is.GreaterThanOrEqualTo(outer.yMin - tolerance), message);
      Assert.That(inner.xMax, Is.LessThanOrEqualTo(outer.xMax + tolerance), message);
      Assert.That(inner.yMax, Is.LessThanOrEqualTo(outer.yMax + tolerance), message);
    }

    private static IEnumerator SettleUi()
    {
      Canvas.ForceUpdateCanvases();
      yield return null;
      Canvas.ForceUpdateCanvases();
      yield return null;
    }

    private readonly struct StatusCase
    {
      public readonly CareProductionStage Stage;
      public readonly string TextName;
      public readonly string Expected;

      public StatusCase(CareProductionStage stage, string textName, string expected)
      {
        Stage = stage;
        TextName = textName;
        Expected = expected;
      }
    }
  }
}

using System.Collections;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests.PlayMode
{
  public sealed class CareEconomyPhaseOnePlayModeTests
  {
    [UnityTest]
    public IEnumerator GrayboxShowsNewResourcesAndProductionNamesWithoutIncidentCard()
    {
      var owner = new GameObject("Economy V21 View Test");
      var view = owner.AddComponent<CareStationView>();
      view.Build();
      view.ApplyStation(new CareStationSaveData
      {
        coins = 17,
        careEnergy = 24,
        storedFullBottles = 6,
        storageHours = 24,
      });
      view.ShowStationWorking();
      yield return null;

      var allText = owner.GetComponentsInChildren<Component>(true)
        .Where(component => component != null && component.GetType().FullName == "TMPro.TextMeshProUGUI")
        .Select(component => component.GetType().GetProperty("text")?.GetValue(component) as string ?? string.Empty)
        .ToArray();
      var combined = string.Join("\n", allText).ToUpperInvariant();

      Assert.That(combined, Does.Contain("FILTER"));
      Assert.That(combined, Does.Contain("FILLER"));
      Assert.That(combined, Does.Contain("PACKER"));
      Assert.That(combined, Does.Contain("COINS"));
      Assert.That(combined, Does.Contain("CARE ENERGY"));
      Assert.That(combined, Does.Contain("17"));
      Assert.That(combined, Does.Contain("24"));
      Assert.That(combined, Does.Contain("6 / 24"));
      Assert.That(combined, Does.Not.Contain("DUST"));
      Assert.That(combined, Does.Not.Contain("DRY SPOT"));
      Assert.That(combined, Does.Not.Contain("EYE GUNK"));
      Assert.That(combined, Does.Not.Contain("TANK"));
      Assert.That(combined, Does.Not.Contain("PRESS"));
      Assert.That(combined, Does.Not.Contain("GOLD"));
      Assert.That(owner.transform.Find("Eye Care Station Canvas/Comfort Padded Content/Station Stage/Station Incident"), Is.Null);

      Object.Destroy(owner);
      yield return null;
    }

    [UnityTest]
    public IEnumerator ProductionGrayboxHasOneFilterAndBottleStartsAtFiller()
    {
      var owner = new GameObject("Production V22 View Test");
      var view = owner.AddComponent<CareStationView>();
      view.Build();
      var save = new CareStationSaveData { careEnergy = 1, storageHours = 24 };
      view.ApplyStation(save);
      yield return null;

      var transforms = owner.GetComponentsInChildren<Transform>(true);
      Assert.That(owner.GetComponentsInChildren<CareStationFilterArtView>(true).Length, Is.EqualTo(1));
      Assert.That(transforms.Any(item => item.name == "Care Core Platform"), Is.False);
      Assert.That(transforms.Single(item => item.name == "BottleFillAnchor").gameObject.activeSelf, Is.False);
      var productionBottle = (RectTransform)transforms.Single(item => item.name == "Representative Production Bottle");

      view.ShowProductionStage(CareProductionStage.FilterProcessing, 0.5f, save);
      yield return null;
      Assert.That(productionBottle.gameObject.activeSelf, Is.False);

      view.ShowProductionStage(CareProductionStage.FillerCreateBottle, 0.5f, save);
      yield return null;
      Assert.That(productionBottle.gameObject.activeSelf, Is.True);
      Assert.That(productionBottle.anchorMin.x, Is.EqualTo(0.73f).Within(0.001f));
      Assert.That(productionBottle.anchorMin.y, Is.EqualTo(0.655f).Within(0.001f));

      view.ShowProductionStage(CareProductionStage.PackerPackaging, 0.5f, save);
      yield return null;
      Assert.That(productionBottle.anchorMin.x, Is.EqualTo(0.73f).Within(0.001f));
      Assert.That(productionBottle.anchorMin.y, Is.EqualTo(0.335f).Within(0.001f));
      Assert.That(transforms.Single(item => item.name == "Bottle Cap").gameObject.activeSelf, Is.True);
      Assert.That(transforms.Single(item => item.name == "Bottle Warm Paper Label").gameObject.activeSelf, Is.True);
      Assert.That(transforms.Single(item => item.name == "Bottle Paper Package").gameObject.activeSelf, Is.True);

      Object.Destroy(owner);
      yield return null;
    }
  }
}

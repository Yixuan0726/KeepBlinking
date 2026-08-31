using System;
using System.Collections;
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
  public sealed class FirstLevelUiFactoryInputRecoveryPlayModeTests
  {
    private const string InputSystemModuleName = "InputSystemUIInputModule";

    [UnityTest]
    public IEnumerator InactiveParentEventSystemRemainsInactiveAndIndependentActiveSystemIsSelected()
    {
      var previousCurrent = EventSystem.current;
      var inactiveParent = new GameObject("Inactive EventSystem Parent");
      var inactiveObject = new GameObject("Inactive Scene EventSystem");
      inactiveObject.transform.SetParent(inactiveParent.transform, false);
      var inactiveEventSystem = inactiveObject.AddComponent<EventSystem>();
      var inactiveModule = inactiveObject.AddComponent<StandaloneInputModule>();
      inactiveModule.enabled = false;
      inactiveParent.SetActive(false);

      var activeObject = new GameObject("Independent Active EventSystem");
      var activeEventSystem = activeObject.AddComponent<EventSystem>();
      var activeModule = activeObject.AddComponent<StandaloneInputModule>();
      var competingObject = new GameObject("Competing Active EventSystem");
      var competingEventSystem = competingObject.AddComponent<EventSystem>();
      competingObject.AddComponent<StandaloneInputModule>();

      try
      {
        var recovered = InvokeEnsureEventSystem(inactiveEventSystem);

        Assert.That(recovered, Is.Not.SameAs(inactiveEventSystem));
        Assert.That(recovered.gameObject.activeInHierarchy, Is.True);
        Assert.That(recovered.isActiveAndEnabled, Is.True);
        Assert.That(CompatibleModules(recovered).Any(module => module.isActiveAndEnabled), Is.True);
        Assert.That(inactiveParent.activeSelf, Is.False,
          "Input recovery must not reactivate an arbitrary inactive scene hierarchy.");
        Assert.That(inactiveObject.activeInHierarchy, Is.False);
        Assert.That(inactiveModule.enabled, Is.False,
          "The module under the inactive hierarchy must remain untouched.");
        Assert.That(activeEventSystem.enabled, Is.True);
        Assert.That(activeModule.enabled, Is.True);
        Assert.That(competingEventSystem.enabled, Is.True,
          "Recovery must not disable other scene-owned EventSystems.");
      }
      finally
      {
        UnityEngine.Object.Destroy(inactiveParent);
        UnityEngine.Object.Destroy(activeObject);
        UnityEngine.Object.Destroy(competingObject);
        RestoreCurrent(previousCurrent);
      }

      yield return null;
    }

    [UnityTest]
    public IEnumerator MissingModuleIsAddedOnceAndRepeatedRecoveryIsIdempotent()
    {
      var previousCurrent = EventSystem.current;
      var eventSystemObject = new GameObject("Module-less EventSystem");
      var eventSystem = eventSystemObject.AddComponent<EventSystem>();

      try
      {
        Assert.That(InvokeEnsureEventSystem(eventSystem), Is.SameAs(eventSystem));
        Assert.That(InvokeEnsureEventSystem(eventSystem), Is.SameAs(eventSystem));

        var compatibleModules = CompatibleModules(eventSystem);
        Assert.That(compatibleModules.Length, Is.EqualTo(1),
          "Recovery must not add a second compatible UI input module on repeated calls.");
        Assert.That(compatibleModules[0].isActiveAndEnabled, Is.True);
        AssertInputSystemActionsAreAssigned(compatibleModules[0]);
      }
      finally
      {
        UnityEngine.Object.Destroy(eventSystemObject);
        RestoreCurrent(previousCurrent);
      }

      yield return null;
    }

    [UnityTest]
    public IEnumerator DisabledCompatibleModuleIsEnabledOnceWithoutAddingDuplicate()
    {
      var previousCurrent = EventSystem.current;
      var eventSystemObject = new GameObject("Disabled Module EventSystem");
      var eventSystem = eventSystemObject.AddComponent<EventSystem>();
      var inputModule = eventSystemObject.AddComponent<StandaloneInputModule>();
      inputModule.enabled = false;

      try
      {
        Assert.That(InvokeEnsureEventSystem(eventSystem), Is.SameAs(eventSystem));
        Assert.That(InvokeEnsureEventSystem(eventSystem), Is.SameAs(eventSystem));

        Assert.That(inputModule.isActiveAndEnabled, Is.True);
        Assert.That(CompatibleModules(eventSystem), Is.EqualTo(new BaseInputModule[] { inputModule }));
      }
      finally
      {
        UnityEngine.Object.Destroy(eventSystemObject);
        RestoreCurrent(previousCurrent);
      }

      yield return null;
    }

    [UnityTest]
    public IEnumerator InputSystemModuleWithMissingActionsIsRepairedWithoutAddingDuplicate()
    {
      var previousCurrent = EventSystem.current;
      var eventSystemObject = new GameObject("Input System Actions EventSystem");
      var eventSystem = eventSystemObject.AddComponent<EventSystem>();
      var moduleType = AppDomain.CurrentDomain.GetAssemblies()
        .Select(assembly => assembly.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule"))
        .FirstOrDefault(type => type != null);
      Assert.That(moduleType, Is.Not.Null, "InputSystemUIInputModule must be available to the runtime assembly.");
      var module = (BaseInputModule)eventSystemObject.AddComponent(moduleType);

      try
      {
        InvokePublicMethod(module, "AssignDefaultActions");
        AssertInputSystemActionsAreAssigned(module);

        SetPublicPropertyToNull(module, "actionsAsset");
        SetPublicPropertyToNull(module, "point");
        SetPublicPropertyToNull(module, "leftClick");
        Assert.That(ReadPublicProperty(module, "actionsAsset"), Is.Null);
        Assert.That(ReadPublicProperty(module, "point"), Is.Null);
        Assert.That(ReadPublicProperty(module, "leftClick"), Is.Null);

        Assert.That(InvokeEnsureEventSystem(eventSystem), Is.SameAs(eventSystem));

        Assert.That(CompatibleModules(eventSystem), Is.EqualTo(new[] { module }),
          "Repair must reuse the existing InputSystemUIInputModule.");
        Assert.That(module.isActiveAndEnabled, Is.True);
        AssertInputSystemActionsAreAssigned(module);
      }
      finally
      {
        UnityEngine.Object.Destroy(eventSystemObject);
        RestoreCurrent(previousCurrent);
      }

      yield return null;
    }

    [UnityTest]
    public IEnumerator RebindInputHandlersRestoresDisabledCanvasRaycasterAndRootGroup()
    {
      var eventSystemsBefore = UnityEngine.Object.FindObjectsByType<EventSystem>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);
      var viewRoot = new GameObject("Minimal Care Station View");
      var view = viewRoot.AddComponent<CareStationView>();
      var canvasObject = new GameObject(
        "Disabled Station Canvas",
        typeof(RectTransform),
        typeof(Canvas),
        typeof(GraphicRaycaster),
        typeof(CanvasGroup));
      canvasObject.transform.SetParent(viewRoot.transform, false);
      var canvas = canvasObject.GetComponent<Canvas>();
      canvas.renderMode = RenderMode.ScreenSpaceOverlay;
      var raycaster = canvasObject.GetComponent<GraphicRaycaster>();
      var group = canvasObject.GetComponent<CanvasGroup>();
      var safeObject = new GameObject("Safe Area", typeof(RectTransform));
      safeObject.transform.SetParent(canvasObject.transform, false);

      SetPrivateField(view, "_safe", safeObject.GetComponent<RectTransform>());
      SetPrivateField(view, "_group", group);
      canvas.enabled = false;
      raycaster.enabled = false;
      group.alpha = 0f;
      group.interactable = false;
      group.blocksRaycasts = false;

      try
      {
        InvokeRebindInputHandlers(view);

        Assert.That(canvas.isActiveAndEnabled, Is.True);
        Assert.That(raycaster.isActiveAndEnabled, Is.True);
        Assert.That(group.alpha, Is.EqualTo(1f));
        Assert.That(group.interactable, Is.True);
        Assert.That(group.blocksRaycasts, Is.True);
        Assert.That(EventSystem.current, Is.Not.Null);
        Assert.That(EventSystem.current.isActiveAndEnabled, Is.True);
        Assert.That(CompatibleModules(EventSystem.current).Any(module => module.isActiveAndEnabled), Is.True);
      }
      finally
      {
        UnityEngine.Object.Destroy(viewRoot);
        DestroyEventSystemsCreatedAfter(eventSystemsBefore);
      }

      yield return null;
    }

    private static EventSystem InvokeEnsureEventSystem(EventSystem preferredEventSystem)
    {
      var factory = Type.GetType("KeepBlinking.Gameplay.FirstLevelUiFactory, KeepBlinking.Runtime", true);
      var method = factory.GetMethod(
        "EnsureEventSystem",
        BindingFlags.Static | BindingFlags.NonPublic,
        null,
        new[] { typeof(EventSystem) },
        null);
      Assert.That(method, Is.Not.Null);
      return (EventSystem)method.Invoke(null, new object[] { preferredEventSystem });
    }

    private static void InvokeRebindInputHandlers(CareStationView view)
    {
      var method = typeof(CareStationView).GetMethod(
        "RebindInputHandlers",
        BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null);
      method.Invoke(view, null);
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
      var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(field, Is.Not.Null);
      field.SetValue(target, value);
    }

    private static void InvokePublicMethod(object target, string methodName)
    {
      var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
      Assert.That(method, Is.Not.Null, methodName);
      method.Invoke(target, null);
    }

    private static object ReadPublicProperty(object target, string propertyName)
    {
      var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
      Assert.That(property, Is.Not.Null, propertyName);
      return property.GetValue(target);
    }

    private static void SetPublicPropertyToNull(object target, string propertyName)
    {
      var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
      Assert.That(property, Is.Not.Null, propertyName);
      Assert.That(property.CanWrite, Is.True, propertyName + " must be writable for the recovery test.");
      property.SetValue(target, null);
    }

    private static BaseInputModule[] CompatibleModules(EventSystem eventSystem)
    {
      return eventSystem.GetComponents<BaseInputModule>()
        .Where(module => module is StandaloneInputModule || module.GetType().Name == InputSystemModuleName)
        .ToArray();
    }

    private static void AssertInputSystemActionsAreAssigned(BaseInputModule module)
    {
      if (module.GetType().Name != InputSystemModuleName)
      {
        return;
      }

      foreach (var propertyName in new[] { "actionsAsset", "point", "leftClick", "move", "submit", "cancel" })
      {
        var property = module.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        Assert.That(property, Is.Not.Null, $"{InputSystemModuleName}.{propertyName} must exist.");
        Assert.That(property.GetValue(module), Is.Not.Null,
          $"{InputSystemModuleName}.{propertyName} must be assigned by recovery.");
      }
    }

    private static void DestroyEventSystemsCreatedAfter(EventSystem[] existingSystems)
    {
      var currentSystems = UnityEngine.Object.FindObjectsByType<EventSystem>(
        FindObjectsInactive.Include,
        FindObjectsSortMode.None);
      for (var index = 0; index < currentSystems.Length; index++)
      {
        if (!existingSystems.Contains(currentSystems[index]))
        {
          UnityEngine.Object.Destroy(currentSystems[index].gameObject);
        }
      }
    }

    private static void RestoreCurrent(EventSystem previousCurrent)
    {
      if (previousCurrent != null && previousCurrent.isActiveAndEnabled)
      {
        EventSystem.current = previousCurrent;
      }
    }
  }
}

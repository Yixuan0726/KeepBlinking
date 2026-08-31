#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// Development-only evidence collector for UI soft locks. It observes pointer
  /// input and reports Unity's real EventSystem raycast order; it never changes
  /// save data or gameplay state.
  /// </summary>
  internal sealed class CareStationUiInputDiagnostics : MonoBehaviour
  {
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>(64);
    private CareStationController _controller;
    private int _startupFrames = 3;

    internal void Bind(CareStationController controller)
    {
      _controller = controller;
    }

    private void Update()
    {
      if (_startupFrames > 0 && --_startupFrames == 0)
        DumpCurrentPointer("STARTUP UI INPUT SNAPSHOT");

      var mouse = Mouse.current;
      if (mouse != null && mouse.leftButton.wasPressedThisFrame)
      {
        Dump(mouse.position.ReadValue(), "POINTER CLICK");
      }

      var touchscreen = Touchscreen.current;
      if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
      {
        Dump(touchscreen.primaryTouch.position.ReadValue(), "PRIMARY TOUCH");
      }
    }

    internal void DumpCurrentPointer(string trigger = "DUMP UI INPUT")
    {
      var mouse = Mouse.current;
      var position = mouse != null ? mouse.position.ReadValue() : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
      Dump(position, trigger);
    }

    internal void Dump(Vector2 screenPosition, string trigger)
    {
      var report = new StringBuilder(8192);
      report.AppendLine($"[UI INPUT DUMP] trigger={trigger} pointer={screenPosition} screen={Screen.width}x{Screen.height} timeScale={Time.timeScale:0.###} state={(_controller != null ? _controller.State.ToString() : "NO_CONTROLLER")}");
      AppendEventSystem(report);
      AppendCanvases(report);
      AppendCanvasGroups(report);
      AppendRaycast(report, screenPosition);
      AppendLockOwners(report);
      Debug.Log(report.ToString(), this);
    }

    private void AppendEventSystem(StringBuilder report)
    {
      var current = EventSystem.current;
      if (current == null)
      {
        report.AppendLine("EVENT_SYSTEM current=NULL enabled=false active=false");
      }
      else
      {
        report.AppendLine($"EVENT_SYSTEM current={HierarchyPath(current.transform)} enabled={current.enabled} active={current.gameObject.activeInHierarchy} sendNavigationEvents={current.sendNavigationEvents} currentSelected={PathOrNull(current.currentSelectedGameObject)}");
        report.AppendLine($"INPUT_MODULE current={ModuleDescription(current.currentInputModule)}");
      }

      var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      for (var index = 0; index < systems.Length; index++)
      {
        var system = systems[index];
        var modules = system.GetComponents<BaseInputModule>();
        report.AppendLine($"EVENT_SYSTEM[{index}] path={HierarchyPath(system.transform)} enabled={system.enabled} activeSelf={system.gameObject.activeSelf} activeInHierarchy={system.gameObject.activeInHierarchy} modules={modules.Length}");
        for (var moduleIndex = 0; moduleIndex < modules.Length; moduleIndex++)
          report.AppendLine($"  MODULE[{moduleIndex}] {ModuleDescription(modules[moduleIndex])}");
      }
    }

    private static string ModuleDescription(BaseInputModule module)
    {
      if (module == null) return "NULL";
      var actions = module is InputSystemUIInputModule input
        ? $" actionsAsset={(input.actionsAsset != null ? input.actionsAsset.name : "NULL")} point={input.point != null} click={input.leftClick != null} scroll={input.scrollWheel != null} move={input.move != null} submit={input.submit != null} cancel={input.cancel != null}"
        : string.Empty;
      return $"type={module.GetType().FullName} path={HierarchyPath(module.transform)} enabled={module.enabled} active={module.gameObject.activeInHierarchy} supported={module.IsModuleSupported()} activeModule={EventSystem.current?.currentInputModule == module}{actions}";
    }

    private static void AppendCanvases(StringBuilder report)
    {
      var canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      report.AppendLine($"CANVASES count={canvases.Length}");
      for (var index = 0; index < canvases.Length; index++)
      {
        var canvas = canvases[index];
        var raycaster = canvas.GetComponent<GraphicRaycaster>();
        report.AppendLine(
          $"  CANVAS[{index}] path={HierarchyPath(canvas.transform)} active={canvas.gameObject.activeInHierarchy} enabled={canvas.enabled} renderMode={canvas.renderMode} sortingLayer={SortingLayer.IDToName(canvas.sortingLayerID)} sortingOrder={canvas.sortingOrder} overrideSorting={canvas.overrideSorting} raycaster={(raycaster == null ? "NULL" : $"enabled={raycaster.enabled} active={raycaster.gameObject.activeInHierarchy} blocking={raycaster.blockingObjects}")}");
      }
    }

    private static void AppendCanvasGroups(StringBuilder report)
    {
      var groups = FindObjectsByType<CanvasGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
      report.AppendLine($"ACTIVE_CANVAS_GROUPS count={groups.Length}");
      for (var index = 0; index < groups.Length; index++)
      {
        var group = groups[index];
        report.AppendLine($"  GROUP[{index}] path={HierarchyPath(group.transform)} interactable={group.interactable} blocksRaycasts={group.blocksRaycasts} ignoreParentGroups={group.ignoreParentGroups} alpha={group.alpha:0.###}");
      }
    }

    private void AppendRaycast(StringBuilder report, Vector2 screenPosition)
    {
      var eventSystem = EventSystem.current;
      if (eventSystem == null || !eventSystem.enabled || !eventSystem.gameObject.activeInHierarchy)
      {
        report.AppendLine("RAYCAST skipped=no-active-EventSystem");
        return;
      }

      _raycastResults.Clear();
      var eventData = new PointerEventData(eventSystem) { position = screenPosition };
      eventSystem.RaycastAll(eventData, _raycastResults);
      report.AppendLine($"RAYCAST_ALL count={_raycastResults.Count}");
      for (var index = 0; index < _raycastResults.Count; index++)
      {
        var result = _raycastResults[index];
        var graphic = result.gameObject != null ? result.gameObject.GetComponent<Graphic>() : null;
        var canvas = result.gameObject != null ? result.gameObject.GetComponentInParent<Canvas>() : null;
        report.AppendLine(
          $"  HIT[{index}] name={result.gameObject?.name ?? "NULL"} path={PathOrNull(result.gameObject)} module={result.module?.GetType().Name ?? "NULL"} canvas={CanvasDescription(canvas)} resultSortingOrder={result.sortingOrder} resultSortingLayer={result.sortingLayer} depth={result.depth} distance={result.distance:0.###} index={result.index:0.###} graphic={GraphicDescription(graphic)} groups={CanvasGroupChain(result.gameObject)}");
      }

      if (_raycastResults.Count == 0)
      {
        report.AppendLine("TOP_INTERCEPTOR none");
      }
      else
      {
        var top = _raycastResults[0];
        var graphic = top.gameObject != null ? top.gameObject.GetComponent<Graphic>() : null;
        var canvas = top.gameObject != null ? top.gameObject.GetComponentInParent<Canvas>() : null;
        report.AppendLine($"TOP_INTERCEPTOR name={top.gameObject?.name ?? "NULL"} path={PathOrNull(top.gameObject)} canvas={CanvasDescription(canvas)} graphic={GraphicDescription(graphic)} groups={CanvasGroupChain(top.gameObject)}");
      }
    }

    private void AppendLockOwners(StringBuilder report)
    {
      var reasons = new List<string>();
      report.AppendLine($"DECLARED_UI_LOCK {(_controller != null ? _controller.UiInputLockDescription : "owner=NO_CONTROLLER")}");
      var eventSystem = EventSystem.current;
      if (eventSystem == null) reasons.Add("Missing EventSystem owner=<scene>");
      else
      {
        if (!eventSystem.enabled || !eventSystem.gameObject.activeInHierarchy)
          reasons.Add($"Disabled EventSystem owner={HierarchyPath(eventSystem.transform)}");
        var module = eventSystem.currentInputModule;
        if (module == null) reasons.Add($"Missing current input module owner={HierarchyPath(eventSystem.transform)}");
        else if (!module.enabled || !module.gameObject.activeInHierarchy)
          reasons.Add($"Disabled input module owner={HierarchyPath(module.transform)} type={module.GetType().Name}");
      }

      var groups = FindObjectsByType<CanvasGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
      for (var index = 0; index < groups.Length; index++)
      {
        var group = groups[index];
        if (!group.interactable && group.alpha > 0.001f)
          reasons.Add($"Non-interactable CanvasGroup owner={HierarchyPath(group.transform)} blocksRaycasts={group.blocksRaycasts} alpha={group.alpha:0.###}");
        if (group.blocksRaycasts && group.alpha <= 0.001f)
          reasons.Add($"Invisible raycast-blocking CanvasGroup owner={HierarchyPath(group.transform)} interactable={group.interactable} alpha={group.alpha:0.###}");
      }

      var graphics = FindObjectsByType<Graphic>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
      for (var index = 0; index < graphics.Length; index++)
      {
        var graphic = graphics[index];
        if (!graphic.raycastTarget || graphic.canvasRenderer.GetInheritedAlpha() > 0.001f || !CoversScreen(graphic.rectTransform)) continue;
        reasons.Add($"Invisible full-screen Graphic owner={HierarchyPath(graphic.transform)} type={graphic.GetType().Name}");
      }

      report.AppendLine($"GLOBAL_UI_LOCK_REASONS count={reasons.Count}");
      if (reasons.Count == 0) report.AppendLine("  LOCK none detected by structural audit");
      for (var index = 0; index < reasons.Count; index++) report.AppendLine($"  LOCK[{index}] {reasons[index]}");
    }

    private static bool CoversScreen(RectTransform rect)
    {
      if (rect == null || Screen.width <= 0 || Screen.height <= 0) return false;
      var canvas = rect.GetComponentInParent<Canvas>();
      var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
      var corners = new Vector3[4];
      rect.GetWorldCorners(corners);
      var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
      var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
      for (var index = 0; index < corners.Length; index++)
      {
        var screen = RectTransformUtility.WorldToScreenPoint(camera, corners[index]);
        min = Vector2.Min(min, screen);
        max = Vector2.Max(max, screen);
      }
      return max.x - min.x >= Screen.width * 0.95f && max.y - min.y >= Screen.height * 0.95f;
    }

    private static string CanvasDescription(Canvas canvas)
    {
      return canvas == null
        ? "NULL"
        : $"{HierarchyPath(canvas.transform)}(mode={canvas.renderMode},sortingOrder={canvas.sortingOrder},layer={SortingLayer.IDToName(canvas.sortingLayerID)})";
    }

    private static string GraphicDescription(Graphic graphic)
    {
      if (graphic == null) return "NULL";
      return $"type={graphic.GetType().Name},enabled={graphic.enabled},active={graphic.gameObject.activeInHierarchy},raycastTarget={graphic.raycastTarget},rendererAlpha={graphic.canvasRenderer.GetAlpha():0.###},inheritedAlpha={graphic.canvasRenderer.GetInheritedAlpha():0.###},colorAlpha={graphic.color.a:0.###}";
    }

    private static string CanvasGroupChain(GameObject gameObject)
    {
      if (gameObject == null) return "[]";
      var groups = gameObject.GetComponentsInParent<CanvasGroup>(true);
      if (groups.Length == 0) return "[]";
      var result = new StringBuilder("[");
      for (var index = 0; index < groups.Length; index++)
      {
        if (index > 0) result.Append(" <- ");
        var group = groups[index];
        result.Append($"{HierarchyPath(group.transform)}(i={group.interactable},b={group.blocksRaycasts},a={group.alpha:0.###},ignore={group.ignoreParentGroups})");
      }
      result.Append(']');
      return result.ToString();
    }

    private static string PathOrNull(GameObject gameObject)
    {
      return gameObject == null ? "NULL" : HierarchyPath(gameObject.transform);
    }

    private static string HierarchyPath(Transform transform)
    {
      if (transform == null) return "NULL";
      var result = transform.name;
      while (transform.parent != null)
      {
        transform = transform.parent;
        result = transform.name + "/" + result;
      }
      return result;
    }
  }
}
#endif

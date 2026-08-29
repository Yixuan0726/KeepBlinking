using System;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  public enum CareStationWorkerFacing
  {
    Front,
    FrontRight,
    Right,
    BackRight,
    Back,
    BackLeft,
    Left,
    FrontLeft,
  }

  public enum CareStationWorkerExpression
  {
    Angry,
    Focused,
    Happy,
  }

  /// <summary>
  /// Runtime-only catalog for the approved no-accessory droplet Worker art.
  /// It deliberately never falls back to the retired graybox or role sprites.
  /// </summary>
  public sealed class CareStationWorkerArtCatalog
  {
    private const string Root = "CareStation/Worker/";
    private readonly Sprite[] _directionalBodies = new Sprite[8];

    public Sprite OpenEye { get; private set; }
    public Sprite HappyEye { get; private set; }
    public Sprite AngryBrows { get; private set; }
    public Sprite FocusedBrows { get; private set; }
    public Sprite AngryMouth { get; private set; }
    public Sprite FocusedMouth { get; private set; }
    public Sprite HappyMouth { get; private set; }
    public Sprite LeftArm { get; private set; }
    public Sprite RightArm { get; private set; }
    public Sprite LeftHand { get; private set; }
    public Sprite RightHand { get; private set; }
    public Sprite LeftLeg { get; private set; }
    public Sprite RightLeg { get; private set; }
    public Sprite LeftFoot { get; private set; }
    public Sprite RightFoot { get; private set; }

    public bool IsComplete
    {
      get
      {
        for (var i = 0; i < _directionalBodies.Length; i++)
          if (_directionalBodies[i] == null) return false;
        return OpenEye != null && HappyEye != null && AngryBrows != null && FocusedBrows != null &&
               AngryMouth != null && FocusedMouth != null && HappyMouth != null &&
               LeftArm != null && RightArm != null && LeftHand != null && RightHand != null &&
               LeftLeg != null && RightLeg != null && LeftFoot != null && RightFoot != null;
      }
    }

    public Sprite Body(CareStationWorkerFacing facing)
    {
      return _directionalBodies[Mathf.Clamp((int)facing, 0, _directionalBodies.Length - 1)];
    }

    public static CareStationWorkerArtCatalog LoadFromResources()
    {
      var catalog = new CareStationWorkerArtCatalog();
      var directionNames = new[]
      {
        "Front", "FrontRight", "Right", "BackRight",
        "Back", "BackLeft", "Left", "FrontLeft",
      };
      for (var i = 0; i < directionNames.Length; i++)
        catalog._directionalBodies[i] = Load("Worker_Body_" + directionNames[i]);

      catalog.OpenEye = Load("Worker_Eye_Open");
      catalog.HappyEye = Load("Worker_Eye_Happy");
      catalog.AngryBrows = Load("Worker_Brows_Angry");
      catalog.FocusedBrows = Load("Worker_Brows_Focused");
      catalog.AngryMouth = Load("Worker_Mouth_Angry");
      catalog.FocusedMouth = Load("Worker_Mouth_Focused");
      catalog.HappyMouth = Load("Worker_Mouth_Happy");
      catalog.LeftArm = Load("Worker_LeftArm");
      catalog.RightArm = Load("Worker_RightArm");
      catalog.LeftHand = Load("Worker_LeftHand");
      catalog.RightHand = Load("Worker_RightHand");
      catalog.LeftLeg = Load("Worker_LeftLeg");
      catalog.RightLeg = Load("Worker_RightLeg");
      catalog.LeftFoot = Load("Worker_LeftFoot");
      catalog.RightFoot = Load("Worker_RightFoot");
      return catalog;
    }

    private static Sprite Load(string name)
    {
      return Resources.Load<Sprite>(Root + name);
    }
  }

  public static class CareStationWorkerVisualRules
  {
    // The approved art direction defines three visible station tiers. Worker L4
    // keeps the L3 three-character presentation; economic crewCount remains the
    // independent production authority and is never modified here.
    public static int VisibleCountForLevel(int workerLevel)
    {
      return Mathf.Clamp(workerLevel, 1, 3);
    }

    public static CareStationWorkerExpression ExpressionForLevel(int workerLevel)
    {
      if (workerLevel <= 1) return CareStationWorkerExpression.Angry;
      if (workerLevel == 2) return CareStationWorkerExpression.Focused;
      return CareStationWorkerExpression.Happy;
    }

    public static CareStationWorkerFacing FacingForMovement(
      Vector2 movement,
      CareStationWorkerFacing lastFacing,
      float deadZone = 0.001f)
    {
      if (movement.sqrMagnitude <= deadZone * deadZone) return lastFacing;
      var degrees = Mathf.Repeat(Mathf.Atan2(movement.y, movement.x) * Mathf.Rad2Deg + 360f, 360f);
      if (degrees < 22.5f || degrees >= 337.5f) return CareStationWorkerFacing.Right;
      if (degrees < 67.5f) return CareStationWorkerFacing.BackRight;
      if (degrees < 112.5f) return CareStationWorkerFacing.Back;
      if (degrees < 157.5f) return CareStationWorkerFacing.BackLeft;
      if (degrees < 202.5f) return CareStationWorkerFacing.Left;
      if (degrees < 247.5f) return CareStationWorkerFacing.FrontLeft;
      if (degrees < 292.5f) return CareStationWorkerFacing.Front;
      return CareStationWorkerFacing.FrontRight;
    }

    public static bool FaceVisible(CareStationWorkerFacing facing)
    {
      return facing != CareStationWorkerFacing.Back &&
             facing != CareStationWorkerFacing.BackLeft &&
             facing != CareStationWorkerFacing.BackRight;
    }

    public static bool IsSide(CareStationWorkerFacing facing)
    {
      return facing == CareStationWorkerFacing.Left || facing == CareStationWorkerFacing.Right;
    }
  }
}

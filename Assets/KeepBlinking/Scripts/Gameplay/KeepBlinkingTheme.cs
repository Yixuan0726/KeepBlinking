using UnityEngine;

namespace KeepBlinking.Gameplay
{
  internal static class KeepBlinkingTheme
  {
    internal readonly struct ModuleProtocol
    {
      public readonly string Level;
      public readonly string TitleZh;
      public readonly string TitleEn;
      public readonly string TagZh;
      public readonly string TagEn;
      public readonly string DescriptionZh;
      public readonly string DescriptionEn;
      public readonly string DeltaZh;
      public readonly string DeltaEn;
      public readonly Color AccentColor;
      public readonly int UnlockDay;
      public readonly string Rarity;

      public ModuleProtocol(
        string level,
        string titleZh,
        string titleEn,
        string tagZh,
        string tagEn,
        string descriptionZh,
        string descriptionEn,
        string deltaZh,
        string deltaEn,
        Color accentColor,
        int unlockDay,
        string rarity)
      {
        Level = level;
        TitleZh = titleZh;
        TitleEn = titleEn;
        TagZh = tagZh;
        TagEn = tagEn;
        DescriptionZh = descriptionZh;
        DescriptionEn = descriptionEn;
        DeltaZh = deltaZh;
        DeltaEn = deltaEn;
        AccentColor = accentColor;
        UnlockDay = unlockDay;
        Rarity = rarity;
      }
    }

    public static readonly Color BackgroundPrimary = new Color32(0x17, 0x23, 0x21, 0xFF);
    public static readonly Color BackgroundSecondary = new Color32(0x19, 0x28, 0x25, 0xFF);
    public static readonly Color BackgroundTertiary = new Color32(0x14, 0x20, 0x1E, 0xFF);
    public static readonly Color SurfaceBase = new Color32(0x20, 0x29, 0x28, 0xFF);
    public static readonly Color SurfaceElevated = new Color32(0x29, 0x36, 0x33, 0xFF);
    public static readonly Color BorderSubtle = new Color32(0x53, 0x61, 0x5B, 0xFF);
    public static readonly Color BorderReadable = new Color32(0x7C, 0x8E, 0x83, 0xFF);
    public static readonly Color BorderFocus = new Color32(0x9E, 0xB7, 0xAA, 0xFF);
    public static readonly Color TextPrimary = new Color32(0xF2, 0xF4, 0xEA, 0xFF);
    public static readonly Color TextSecondary = new Color32(0xCA, 0xD6, 0xCC, 0xFF);
    public static readonly Color TextMuted = new Color32(0x96, 0xA6, 0x9C, 0xFF);
    public static readonly Color AccentPrimary = new Color32(0x9F, 0xCB, 0xB4, 0xFF);
    public static readonly Color AccentSoft = new Color32(0xC8, 0xD7, 0xCB, 0xFF);
    public static readonly Color AccentWarm = new Color32(0xCB, 0xBF, 0x9B, 0xFF);
    public static readonly Color WarningSoft = new Color32(0xE7, 0x8E, 0x78, 0xFF);
    public static readonly Color DangerMuted = new Color32(0xD4, 0x6D, 0x62, 0xFF);
    public static readonly Color BackdropClosedEye = new Color32(0x03, 0x05, 0x06, 0xFF);

    public static readonly Color SurfaceOverlay = new Color(32f / 255f, 41f / 255f, 40f / 255f, 0.94f);
    public static readonly Color SurfaceScrim = new Color(5f / 255f, 8f / 255f, 9f / 255f, 0.78f);
    public static readonly Color SurfaceShadow = new Color(0f, 0f, 0f, 0.32f);
    public static readonly Color PanelGlow = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.10f);
    public static readonly Color GridTint = new Color(36f / 255f, 51f / 255f, 47f / 255f, 0.24f);
    public static readonly Color RingTint = new Color(36f / 255f, 51f / 255f, 47f / 255f, 0.10f);
    public static readonly Color DustTint = new Color(226f / 255f, 229f / 255f, 212f / 255f, 0.045f);

    public static readonly Color OrbitSignal = new Color(226f / 255f, 229f / 255f, 212f / 255f, 0.98f);
    public static readonly Color OrbitSignalHover = new Color(159f / 255f, 203f / 255f, 180f / 255f, 1.00f);
    public static readonly Color ConvertedSignal = new Color(177f / 255f, 210f / 255f, 191f / 255f, 1.00f);
    public static readonly Color GazeIdle = new Color(200f / 255f, 215f / 255f, 203f / 255f, 0.45f);
    public static readonly Color GazeHover = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.72f);
    public static readonly Color CalibrationSignal = new Color(242f / 255f, 244f / 255f, 234f / 255f, 1.00f);
    public static readonly Color CalibrationBackplate = new Color(32f / 255f, 41f / 255f, 40f / 255f, 0.98f);
    public static readonly Color CalibrationOuter = new Color(228f / 255f, 198f / 255f, 109f / 255f, 0.96f);
    public static readonly Color CalibrationCore = new Color(250f / 255f, 248f / 255f, 230f / 255f, 1.00f);
    public static readonly Color CrisisSignal = new Color(231f / 255f, 142f / 255f, 120f / 255f, 0.98f);
    public static readonly Color ProgressBack = new Color(36f / 255f, 51f / 255f, 47f / 255f, 0.72f);
    public static readonly Color ProgressFill = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.92f);
    public static readonly Color ProgressGlow = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.28f);

    public static readonly ModuleProtocol[] ModuleProtocols =
    {
      new ModuleProtocol(
        "D1",
        "Blink Relief",
        "Blink Relief",
        "Blink",
        "Blink",
        "Natural blinks settle nearby signals without asking for perfect timing.",
        "Natural blinks settle nearby signals without asking for perfect timing.",
        "Next wave slower",
        "Next wave slower",
        AccentPrimary,
        1,
        "Core"),
      new ModuleProtocol(
        "D1",
        "Full Rest",
        "Full Rest",
        "Close",
        "Close",
        "A longer closed-eye rest builds a calmer, stronger release.",
        "A longer closed-eye rest builds a calmer, stronger release.",
        "Rest builds wave",
        "Rest builds wave",
        AccentSoft,
        1,
        "Core"),
      new ModuleProtocol(
        "D2",
        "Gentle Reopen",
        "Gentle Reopen",
        "Reopen",
        "Reopen",
        "After reopening, the field stays calm while brightness returns.",
        "After reopening, the field stays calm while brightness returns.",
        "2s calm shield",
        "2s calm shield",
        AccentSoft,
        2,
        "Core"),
      new ModuleProtocol(
        "D1",
        "Distance Reset",
        "Distance Reset",
        "Distance",
        "Distance",
        "A successful pull-away collects the field and delays new pressure.",
        "A successful pull-away collects the field and delays new pressure.",
        "Next wave calmer",
        "Next wave calmer",
        AccentWarm,
        1,
        "Core"),
      new ModuleProtocol(
        "D2",
        "Quiet Field",
        "Quiet Field",
        "Calm",
        "Calm",
        "Closed-eye rest keeps the field quiet instead of rushing the release.",
        "Closed-eye rest keeps the field quiet instead of rushing the release.",
        "Rest pauses motion",
        "Rest pauses motion",
        AccentSoft,
        2,
        "Core"),
      new ModuleProtocol(
        "D1",
        "Safe Spacing",
        "Safe Spacing",
        "Safety",
        "Safety",
        "When gaze pressure builds, the system reduces visual load first.",
        "When gaze pressure builds, the system reduces visual load first.",
        "Fewer samples next",
        "Fewer samples next",
        WarningSoft,
        1,
        "Core"),
      new ModuleProtocol(
        "D3",
        "Clinical Notes",
        "Clinical Notes",
        "Protocol",
        "Protocol",
        "Daily notes widen future module choices.",
        "Daily notes widen future module choices.",
        "Richer future pool",
        "Richer future pool",
        AccentPrimary,
        3,
        "Economy"),
      new ModuleProtocol(
        "D3",
        "Rest Bank",
        "Rest Bank",
        "Close+Eco",
        "Close+Eco",
        "Complete rests store value for future choices.",
        "Complete rests store value for future choices.",
        "Full rest banks value",
        "Full rest banks value",
        AccentSoft,
        3,
        "Economy"),
      new ModuleProtocol(
        "D4",
        "Blink Drift",
        "Blink Drift",
        "Blink+Calm",
        "Blink+Calm",
        "Natural blinking softens movement so the field is easier to scan.",
        "Natural blinking softens movement so the field is easier to scan.",
        "Blink chain slows",
        "Blink chain slows",
        AccentPrimary,
        4,
        "Synergy"),
      new ModuleProtocol(
        "D4",
        "Wider Rest Wave",
        "Wider Rest Wave",
        "Close",
        "Close",
        "Staying closed gently strengthens the next clearing wave.",
        "Staying closed gently strengthens the next clearing wave.",
        "Long rest powers wave",
        "Long rest powers wave",
        AccentSoft,
        4,
        "Synergy"),
      new ModuleProtocol(
        "D5",
        "Baseline Recovery",
        "Baseline Recovery",
        "Dist+Safe",
        "Dist+Safe",
        "Returning to baseline distance steadies the next observation field.",
        "Returning to baseline distance steadies the next observation field.",
        "Lower next density",
        "Lower next density",
        AccentWarm,
        5,
        "Synergy"),
      new ModuleProtocol(
        "D5",
        "Reopen Clarity",
        "Reopen Clarity",
        "Reopen",
        "Reopen",
        "After reopening, targets stay larger and calmer for a short window.",
        "After reopening, targets stay larger and calmer for a short window.",
        "Clearer after reopen",
        "Clearer after reopen",
        AccentSoft,
        5,
        "Synergy"),
      new ModuleProtocol(
        "D7",
        "Low-Stim Protocol",
        "Low-Stim Protocol",
        "Safety",
        "Safety",
        "Intrusive samples become slower, fewer, and less visually sharp.",
        "Intrusive samples become slower, fewer, and less visually sharp.",
        "Lower visual load",
        "Lower visual load",
        WarningSoft,
        7,
        "Rare"),
      new ModuleProtocol(
        "D7",
        "Rest Dividend",
        "Rest Dividend",
        "Close+Eco",
        "Close+Eco",
        "Complete rests improve later module quality without longer play.",
        "Complete rests improve later module quality without longer play.",
        "Better rest offers",
        "Better rest offers",
        AccentSoft,
        7,
        "Rare"),
      new ModuleProtocol(
        "D10",
        "Protective Cutoff",
        "Protective Cutoff",
        "Safety",
        "Safety",
        "Sustained gaze triggers protection before the field becomes stressful.",
        "Sustained gaze triggers protection before the field becomes stressful.",
        "Pressure pauses",
        "Pressure pauses",
        WarningSoft,
        10,
        "Rare"),
      new ModuleProtocol(
        "D10",
        "20-20-20",
        "20-20-20",
        "Close",
        "Close",
        "Longer protocol runs add a real rest interval with stronger rewards.",
        "Longer protocol runs add a real rest interval with stronger rewards.",
        "Rest interval reward",
        "Rest interval reward",
        AccentWarm,
        10,
        "Rare"),
      new ModuleProtocol(
        "D12",
        "System Sync",
        "System Sync",
        "System",
        "System",
        "Blink, close, reopen, and distance resets reinforce each other.",
        "Blink, close, reopen, and distance resets reinforce each other.",
        "Actions reinforce",
        "Actions reinforce",
        AccentPrimary,
        12,
        "Mythic"),
      new ModuleProtocol(
        "D14",
        "Autopilot",
        "Autopilot",
        "Calm",
        "Calm",
        "If fatigue builds, the system lowers density and enlarges targets.",
        "If fatigue builds, the system lowers density and enlarges targets.",
        "Fatigue eases field",
        "Fatigue eases field",
        AccentSoft,
        14,
        "Mythic"),
    };

    public static Color WithAlpha(Color color, float alpha)
    {
      return new Color(color.r, color.g, color.b, alpha);
    }
  }
}

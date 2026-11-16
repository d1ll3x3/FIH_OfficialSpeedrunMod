using BepInEx.Configuration;

namespace OfficialSpeedrunMod
{
    // OfficialSpeedrunMod.cfg, next to the dll: the restart binds, the teleport hotkeys and the
    // best time of every level. The menu edits these entries directly, so a rebind or a new
    // best is saved as it happens.
    public static class ModConfig
    {
        private const string PbSection = "PersonalBests";
        private const string HotkeySection = "Hotkeys";
        private const string BindHelp =
            "Keys and gamepad buttons held together, joined by +: R, Ctrl+Alpha1, Pad.L3+Pad.R3, Shift+Pad.A. " +
            "Keys are Unity KeyCode names, or Ctrl, Shift, Alt; gamepad buttons are Pad.A, Pad.B, Pad.X, Pad.Y, " +
            "Pad.LB, Pad.RB, Pad.LT, Pad.RT, Pad.L3, Pad.R3, Pad.Start, Pad.Select, Pad.Up, Pad.Down, Pad.Left, Pad.Right.";

        private static ConfigFile _file;

        public static ConfigEntry<string> Restart { get; private set; }

        public static void Init(ConfigFile file)
        {
            _file = file;
            // Every gamepad button already does something in the game, so its default is a
            // combo of the two that matter least (Ping and the fly boost).
            Restart = file.Bind("General", "Restart", "R, Pad.L3+Pad.R3",
                "Takes you back to the last spawn, restarts the level timer and ends the active NPC challenge. " +
                "One or more binds, separated by commas. " + BindHelp + " Set in the phone app (Hotkeys) too.");
        }

        // One per teleport target, bound when first needed like the best times.
        public static ConfigEntry<string> TeleportHotkey(TeleportTarget target)
            => _file.Bind(HotkeySection, target.Id, "", $"Teleports to {target.Name}. " + BindHelp + " Empty = none.");

        // Seconds, or 0 when the level has no best time yet. One entry per map and level id,
        // bound when first needed: entries the mod never asked for stay in the file untouched.
        private static ConfigEntry<double> PbEntry(string mapId, int levelId, string levelName)
            => _file.Bind(PbSection, $"{mapId}.{levelId}", 0d, $"Best time of {levelName}, in seconds (0 = none).");

        public static bool TryGetPb(string mapId, int levelId, string levelName, out double time)
        {
            time = PbEntry(mapId, levelId, levelName).Value;
            return time > 0d;
        }

        // Saves the time when it beats the best one. Returns whether it did.
        public static bool RecordTime(string mapId, int levelId, string levelName, double time)
        {
            var entry = PbEntry(mapId, levelId, levelName);
            if (entry.Value > 0d && entry.Value <= time) return false;
            entry.Value = time;
            return true;
        }

        public static void ResetPb(string mapId, int levelId, string levelName)
            => PbEntry(mapId, levelId, levelName).Value = 0d;
    }
}

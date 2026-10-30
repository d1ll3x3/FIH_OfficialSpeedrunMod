using BepInEx.Configuration;

namespace OfficialSpeedrunMod
{
    // OfficialSpeedrunMod.cfg, next to the dll: the restart key, the teleport hotkeys and the
    // best time of every level. The menu edits these entries directly, so a rebind or a new
    // best is saved as it happens.
    public static class ModConfig
    {
        private const string PbSection = "PersonalBests";
        private const string HotkeySection = "Hotkeys";
        private const string HotkeyHelp =
            "A key (Unity KeyCode name), optionally with Ctrl, Shift and Alt: R, Alpha1, Ctrl+F2, Ctrl+Shift+K. Empty = none.";

        private static ConfigFile _file;

        public static ConfigEntry<string> RestartKey { get; private set; }

        public static void Init(ConfigFile file)
        {
            _file = file;
            RestartKey = file.Bind("General", "RestartKey", "R",
                "Takes you back to the last spawn and restarts the level timer and the active NPC challenge. " + HotkeyHelp);
        }

        // One per teleport target, bound when first needed like the best times. Level targets
        // are keyed by level id, NPCs by name (spaces aren't safe in a config key).
        public static ConfigEntry<string> TeleportHotkey(TeleportTarget target)
        {
            string key = target.StartsLevel >= 0 ? $"Level.{target.StartsLevel}" : "Npc." + target.Name.Replace(' ', '_');
            return _file.Bind(HotkeySection, key, "", $"Teleports to {target.Name}. " + HotkeyHelp);
        }

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
    }
}

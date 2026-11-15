using EHS;
using UnityEngine;

namespace OfficialSpeedrunMod
{
    // The last spawn and the restart key. The spawn is the last place the menu or a teleport
    // hotkey took you (a level or an NPC). The restart key takes you back there and starts
    // the attempt over: the level timer back to 0, and the NPC challenge in progress ended.
    public static class Restart
    {
        private static TeleportTarget _spawn;
        // The map the spawn belongs to: its coordinates mean nothing on another one.
        private static string _spawnMap = "";

        public static TeleportTarget Spawn => _spawn;

        // A teleport from the menu or a hotkey: it becomes the spawn.
        public static void GoTo(TeleportTarget target)
        {
            _spawn = target;
            _spawnMap = LevelTimer.MapId();
            if (TeleportTargets.Teleport(target))
                LevelTimer.Reset(target.StartsLevel);
        }

        // The restart key (see Hotkeys).
        public static void Go()
        {
            if (_spawn == null || _spawnMap != LevelTimer.MapId())
            {
                SpeedrunPlugin.Logger.LogInfo("Restart: no spawn on this map yet. Teleport from the menu first.");
                return;
            }
            if (!TeleportTargets.Teleport(_spawn)) return;
            LevelTimer.Reset(_spawn.StartsLevel);

            // Ended the way the NPC's dialogue quits it, so the game hides its timer and plays
            // the exit sound.
            var challenge = PlayerRef.LocalPlayer?.GameRefs?.NpcChallengeManager?.ActiveChallenge;
            if (challenge != null && challenge.IsActive)
            {
                challenge.QuitChallenge();
                SpeedrunPlugin.Logger.LogInfo("Restart: NPC challenge ended.");
            }
        }
    }
}

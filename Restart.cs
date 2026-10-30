using EHS;
using EHS.Interactables;
using UnityEngine;

namespace OfficialSpeedrunMod
{
    // The last spawn and the restart key. The spawn is the last place the menu or a teleport
    // hotkey took you (a level or an NPC). The restart key takes you back there and starts
    // the attempt over: the level timer back to 0, and the NPC challenge in progress
    // restarted at 0 and held there until you move.
    public static class Restart
    {
        private static TeleportTarget _spawn;
        // The map the spawn belongs to: its coordinates mean nothing on another one.
        private static string _spawnMap = "";
        private static bool _holdChallenge;

        public static TeleportTarget Spawn => _spawn;

        // A teleport from the menu or a hotkey: it becomes the spawn.
        public static void GoTo(TeleportTarget target)
        {
            _spawn = target;
            _spawnMap = LevelTimer.MapId();
            if (TeleportTargets.Teleport(target))
                LevelTimer.Reset(target.StartsLevel, -1);
        }

        public static void Update() => HoldChallenge();

        // Also after every Update: the challenge adds its frame time in its own Update, which
        // can run after this mod's.
        public static void LateUpdate() => HoldChallenge();

        // The restart key (see Hotkeys).
        public static void Go()
        {
            if (_spawn == null || _spawnMap != LevelTimer.MapId())
            {
                SpeedrunPlugin.Logger.LogInfo("Restart: no spawn on this map yet. Teleport from the menu first.");
                return;
            }
            if (!TeleportTargets.Teleport(_spawn)) return;
            LevelTimer.Reset(_spawn.StartsLevel, -1);

            var challenge = PlayerRef.LocalPlayer?.GameRefs?.NpcChallengeManager?.ActiveChallenge;
            if (challenge != null && challenge.IsActive && challenge.RestartChallenge())
            {
                _holdChallenge = true;
                SpeedrunPlugin.Logger.LogInfo("Restart: NPC challenge restarted.");
            }
        }

        // Keeps the restarted challenge at 0 until the player moves. If the spawn is outside
        // the challenge's area the game cancels it on its own, and the hold ends with it.
        private static void HoldChallenge()
        {
            if (!_holdChallenge) return;
            var player = PlayerRef.LocalPlayer;
            NpcChallengeController challenge = player?.GameRefs?.NpcChallengeManager?.ActiveChallenge;
            if (challenge == null || !challenge.IsActive || GameplayInput.Started(player))
            {
                _holdChallenge = false;
                return;
            }
            challenge.elapsedTime = 0d;
        }
    }
}

using System;
using EHS;
using HarmonyLib;

namespace OfficialSpeedrunMod
{
    // While the level timer is on, reaching the end ends the level, not the run: the game's
    // end (the tube's animation, the credits and the finished run) never starts, so practising
    // the last levels never finishes the save's run. With the timer off, the game ends as
    // usual.
    //
    // Harmony-patched because the end can't be undone once it starts. GameEndManager.EndGame
    // is what starts it; the end zone's GameEndZoneChecker.TryEndGame is patched too, in case
    // it does more before calling it (a small private method may also be inlined into its
    // caller, so EndGame is the one that counts). Both have their own body (not folded with
    // another method) and take no arguments.
    public static class GameEndBlock
    {
        public static void Patch(Harmony harmony)
        {
            harmony.PatchAll(typeof(ZoneCheck));
            harmony.PatchAll(typeof(EndGame));
        }

        [HarmonyPatch(typeof(GameEndZoneChecker), "TryEndGame")]
        private static class ZoneCheck
        {
            private static bool Prefix() => Allow(nameof(GameEndZoneChecker));
        }

        [HarmonyPatch(typeof(GameEndManager), "EndGame")]
        private static class EndGame
        {
            private static bool Prefix() => Allow(nameof(GameEndManager));
        }

        private static bool Allow(string source)
        {
            try
            {
                if (!LevelTimer.IsOn) return true;
                LevelTimer.OnGameEndReached();
                SpeedrunPlugin.Logger.LogInfo($"Game end blocked ({source}): the level timer is on, so the level ends instead of the run.");
                return false;
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error blocking the game end, letting it happen: {ex}");
                return true;
            }
        }
    }
}

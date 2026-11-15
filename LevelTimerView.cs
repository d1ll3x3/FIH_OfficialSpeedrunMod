using EHS;

namespace OfficialSpeedrunMod
{
    // The level timer on screen: the game's own NPC challenge timer (NpcChallengeTimerView),
    // in its place and with its time and best time format, driven through the same
    // UpdateView its controller calls. A running challenge, or its result, owns the view:
    // the controller updates and hides it then, and the level timer waits for it to finish.
    public static class LevelTimerView
    {
        // Whether the view is showing a level time (so hiding it is this mod's to do).
        private static bool _shown;

        // How long the game shows a finished challenge's result.
        private const float DefaultResultDuration = 3f;

        public static void Show(PlayerRef player, double time, bool hasBest, double best)
        {
            var controller = Controller(player);
            var view = controller?.timerView;
            var container = view?.containerT;
            if (container == null || ChallengeOwns(controller))
            {
                _shown = false;
                return;
            }
            if (!container.gameObject.activeSelf) container.gameObject.SetActive(true);
            view.UpdateView(time, hasBest, best);
            _shown = true;
        }

        public static void Hide(PlayerRef player)
        {
            if (!_shown) return;
            _shown = false;
            var controller = Controller(player);
            var container = controller?.timerView?.containerT;
            if (container == null || ChallengeOwns(controller)) return;
            container.gameObject.SetActive(false);
        }

        public static float ResultDuration(PlayerRef player)
        {
            var controller = Controller(player);
            return controller != null ? controller.completionDisplayDuration : DefaultResultDuration;
        }

        private static NpcChallengeTimerController Controller(PlayerRef player)
            => player?.GameRefs?.GameUIManager?.npcChallengeTimerController;

        private static bool ChallengeOwns(NpcChallengeTimerController controller)
            => controller.activeChallenge != null || controller.resultChallenge != null;
    }
}

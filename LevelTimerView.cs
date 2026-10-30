using System;
using EHS;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // The level timer on screen, as a copy of the game's own run timer (TimerController's
    // container and its TextMeshPro text) placed right under it, so it has the game's font,
    // size and outline. Its text is the game's split format (TimerUtil). The copy belongs to
    // the game's UI, so a scene change destroys it and it is made again.
    public static class LevelTimerView
    {
        private const string Name = "OfficialSpeedrunMod LevelTimer";
        // How long to wait before trying again when the copy could not be made.
        private const float RetryInterval = 2f;

        private static GameObject _root;
        private static TextMeshProUGUI _text;
        private static bool _loggedMissing;
        private static float _nextAttempt;

        public static void Show(PlayerRef player, string text)
        {
            if (_root == null && (Time.unscaledTime < _nextAttempt || !Create(player))) return;
            if (!_root.activeSelf) _root.SetActive(true);
            _text.text = text;
        }

        public static void Hide()
        {
            if (_root != null && _root.activeSelf) _root.SetActive(false);
        }

        private static bool Create(PlayerRef player)
        {
            _nextAttempt = Time.unscaledTime + RetryInterval;
            var source = player.GameRefs?.GameUIManager?.timerController?.containerT?.TryCast<RectTransform>();
            if (source == null)
            {
                if (!_loggedMissing) SpeedrunPlugin.Logger.LogWarning("Level timer: the game's timer UI was not found yet.");
                _loggedMissing = true;
                return false;
            }

            try
            {
                _root = Object.Instantiate(source.gameObject, source.parent, false);
                _root.name = Name;
                _text = _root.GetComponentInChildren<TextMeshProUGUI>(true);
                if (_text == null)
                {
                    SpeedrunPlugin.Logger.LogWarning("Level timer: the game's timer has no text to copy.");
                    Object.Destroy(_root);
                    _root = null;
                    return false;
                }
                // The game hides its timer by switching the text off; the copy is always on.
                _text.gameObject.SetActive(true);
                _text.richText = true;
                _text.overflowMode = TextOverflowModes.Overflow;

                var rt = _root.GetComponent<RectTransform>();
                rt.anchoredPosition = source.anchoredPosition - new Vector2(0f, source.rect.height);
                _loggedMissing = false;
                SpeedrunPlugin.Logger.LogInfo("Level timer: added under the game's timer.");
                return true;
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Level timer: could not copy the game's timer: {ex}");
                if (_root != null) Object.Destroy(_root);
                _root = null;
                return false;
            }
        }
    }
}

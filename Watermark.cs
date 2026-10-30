using System;
using EHS;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // "OSM" in the top right corner, so a recording shows the run was made with this mod. A
    // copy of the game's timer text (its font and outline), on the HUD's own canvas, so it
    // looks like part of the game. A scene change destroys it with the HUD and it is made
    // again.
    public static class Watermark
    {
        private const string Name = "OfficialSpeedrunMod Watermark";
        private const string Text = "OSM";
        // Relative to the timer's text size.
        private const float SizeScale = 0.6f;
        // From the canvas' top right corner, in canvas units.
        private static readonly Vector2 Margin = new Vector2(-28f, -18f);
        // How long to wait before trying again when the copy could not be made.
        private const float RetryInterval = 2f;

        private static GameObject _root;
        private static float _nextAttempt;

        public static void Update()
        {
            var player = PlayerRef.LocalPlayer;
            if (player?.Rb == null || _root != null || Time.unscaledTime < _nextAttempt) return;
            _nextAttempt = Time.unscaledTime + RetryInterval;

            var source = player.GameRefs?.GameUIManager?.timerController?.timerView?.timerText;
            var canvas = source?.canvas?.rootCanvas;
            if (source == null || canvas == null) return;

            try
            {
                _root = Object.Instantiate(source.gameObject, canvas.transform, false);
                _root.name = Name;
                _root.SetActive(true);

                var text = _root.GetComponent<TextMeshProUGUI>();
                text.text = Text;
                text.fontSize = source.fontSize * SizeScale;
                text.alignment = TextAlignmentOptions.TopRight;
                text.overflowMode = TextOverflowModes.Overflow;

                var rt = _root.GetComponent<RectTransform>();
                rt.anchorMin = Vector2.one;
                rt.anchorMax = Vector2.one;
                rt.pivot = Vector2.one;
                rt.anchoredPosition = Margin;
                SpeedrunPlugin.Logger.LogInfo("Watermark: added to the game's HUD.");
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Watermark: could not copy the game's timer text: {ex}");
                if (_root != null) Object.Destroy(_root);
                _root = null;
            }
        }
    }
}

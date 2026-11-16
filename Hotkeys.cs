using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace OfficialSpeedrunMod
{
    // The keys and gamepad buttons a bind can use, by name. Keys are Unity's KeyCode names,
    // with Ctrl, Shift and Alt standing for either side's key. Gamepad buttons are "Pad."
    // and their Xbox name, read through the Input System, so they are the same buttons on
    // every gamepad (Pad.A is Cross on PlayStation).
    public static class Controls
    {
        private const string PadPrefix = "Pad.";

        private static readonly (string name, KeyCode[] keys)[] Modifiers =
        {
            ("Ctrl", new[] { KeyCode.LeftControl, KeyCode.RightControl }),
            ("Shift", new[] { KeyCode.LeftShift, KeyCode.RightShift }),
            ("Alt", new[] { KeyCode.LeftAlt, KeyCode.RightAlt, KeyCode.AltGr }),
        };

        private static readonly (string name, Func<Gamepad, ButtonControl> button)[] PadButtons =
        {
            (PadPrefix + "A", g => g.buttonSouth),
            (PadPrefix + "B", g => g.buttonEast),
            (PadPrefix + "X", g => g.buttonWest),
            (PadPrefix + "Y", g => g.buttonNorth),
            (PadPrefix + "LB", g => g.leftShoulder),
            (PadPrefix + "RB", g => g.rightShoulder),
            (PadPrefix + "LT", g => g.leftTrigger),
            (PadPrefix + "RT", g => g.rightTrigger),
            (PadPrefix + "L3", g => g.leftStickButton),
            (PadPrefix + "R3", g => g.rightStickButton),
            (PadPrefix + "Start", g => g.startButton),
            (PadPrefix + "Select", g => g.selectButton),
            (PadPrefix + "Up", g => g.dpad.up),
            (PadPrefix + "Down", g => g.dpad.down),
            (PadPrefix + "Left", g => g.dpad.left),
            (PadPrefix + "Right", g => g.dpad.right),
        };

        // Every keyboard key but the modifiers (named above). Mouse buttons can't be bound
        // (the click that starts a capture would be), and joysticks are read as gamepads.
        private static readonly KeyCode[] Keys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Distinct()
            .Where(k => k != KeyCode.None && !(k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) && k < KeyCode.JoystickButton0
                        && !Modifiers.Any(m => m.keys.Contains(k)))
            .ToArray();

        // How a control is held, and how it went down this frame, by its canonical name.
        // Looked up ignoring case; each keeps its name as written here.
        private static readonly Dictionary<string, (string name, Func<bool> held, Func<bool> down)> ByName = Build();

        private static Dictionary<string, (string, Func<bool>, Func<bool>)> Build()
        {
            var byName = new Dictionary<string, (string, Func<bool>, Func<bool>)>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, keys) in Modifiers)
                byName[name] = (name, () => keys.Any(Input.GetKey), () => keys.Any(Input.GetKeyDown));
            foreach (var (name, button) in PadButtons)
                byName[name] = (name, () => Gamepad.current is Gamepad g && button(g).isPressed,
                                () => Gamepad.current is Gamepad g && button(g).wasPressedThisFrame);
            foreach (var key in Keys)
                byName[key.ToString()] = (key.ToString(), () => Input.GetKey(key), () => Input.GetKeyDown(key));
            return byName;
        }

        public static string GamepadNames => string.Join(", ", PadButtons.Select(b => b.name));

        // The name as the binds store it, or null when there is no such control.
        // KeyCode's other names for the same key (LeftApple for LeftCommand) are accepted too.
        public static string Canonical(string name)
        {
            if (ByName.TryGetValue(name, out var control)) return control.name;
            return Enum.TryParse(name, true, out KeyCode key) && Keys.Contains(key) ? key.ToString() : null;
        }

        public static bool IsHeld(string name) => ByName.TryGetValue(name, out var c) && c.held();
        public static bool WentDown(string name) => ByName.TryGetValue(name, out var c) && c.down();

        // Something went down this frame: only then can a bind fire.
        public static bool AnyWentDown()
            => Input.anyKeyDown || (Gamepad.current is Gamepad g && PadButtons.Any(b => b.button(g).wasPressedThisFrame));

        // Every control held now, modifiers by their name (for the capture in the phone app).
        public static IEnumerable<string> HeldNow()
        {
            foreach (var (name, keys) in Modifiers)
                if (keys.Any(Input.GetKey)) yield return name;
            if (Input.anyKey)
                foreach (var key in Keys)
                    if (Input.GetKey(key)) yield return key.ToString();
            if (Gamepad.current is Gamepad g)
                foreach (var (name, button) in PadButtons)
                    if (button(g).isPressed) yield return name;
        }

        // How the phone shows it: "Pad.L3" as "L3", "Alpha1" as "1".
        public static string Display(string name)
        {
            if (name.StartsWith(PadPrefix)) return name.Substring(PadPrefix.Length);
            if (name.StartsWith("Alpha") && name.Length == 6) return name.Substring(5);
            return name;
        }
    }

    // Keys and gamepad buttons held together, stored as their names joined by "+": "R",
    // "Ctrl+Alpha1", "Pad.L3+Pad.R3", "Shift+Pad.A". A list of binds is joined by ", ".
    public sealed class Bind
    {
        public readonly string[] Parts;

        public Bind(IEnumerable<string> parts) => Parts = parts.ToArray();

        public bool IsNone => Parts.Length == 0;

        // Null when a name is unknown: a typo disables that bind only, and says so.
        public static Bind Parse(string text)
        {
            var parts = new List<string>();
            foreach (string part in (text ?? "").Split('+').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                string name = Controls.Canonical(part);
                if (name == null)
                {
                    SpeedrunPlugin.Logger.LogWarning($"Bind \"{text}\": unknown key or button \"{part}\". Keys are Unity KeyCode names " +
                                                     $"or Ctrl, Shift, Alt; gamepad buttons are {Controls.GamepadNames}.");
                    return null;
                }
                if (!parts.Contains(name)) parts.Add(name);
            }
            return new Bind(parts);
        }

        public static List<Bind> ParseList(string text)
            => (text ?? "").Split(',').Select(Parse).Where(b => b != null && !b.IsNone).ToList();

        public static string JoinList(IEnumerable<Bind> binds) => string.Join(", ", binds.Select(b => b.ToString()));

        // Fires once: all its parts held, one of them just pressed.
        public bool WasPressed() => !IsNone && Parts.All(Controls.IsHeld) && Parts.Any(Controls.WentDown);

        public override string ToString() => string.Join("+", Parts);

        public string Display => string.Join(" + ", Parts.Select(Controls.Display));
    }

    // The restart binds and the teleport hotkeys, read while playing (not with the phone
    // open). Of the binds pressed, the one with the most keys and buttons wins, so R and
    // Shift+R can each do their own thing.
    public static class Hotkeys
    {
        // Parsed again only when the config text changes, so a typo is reported once.
        private static readonly Dictionary<string, List<Bind>> Parsed = new Dictionary<string, List<Bind>>();

        public static List<Bind> Binds(ConfigEntry<string> entry)
        {
            string text = entry.Value ?? "";
            if (!Parsed.TryGetValue(text, out var binds)) Parsed[text] = binds = Bind.ParseList(text);
            return binds;
        }

        public static void Update(bool menuOpen)
        {
            if (menuOpen || !Application.isFocused || !Controls.AnyWentDown()) return;

            Action best = null;
            int bestParts = 0;
            void Consider(ConfigEntry<string> entry, Action action)
            {
                foreach (var bind in Binds(entry))
                {
                    if (bind.Parts.Length > bestParts && bind.WasPressed())
                    {
                        best = action;
                        bestParts = bind.Parts.Length;
                    }
                }
            }

            Consider(ModConfig.Restart, Restart.Go);
            if (TeleportTargets.IsPlayerReady() && TeleportTargets.IsStoryMap())
            {
                // The lists are only rebuilt when the phone app opens; a first hotkey before
                // that needs them built.
                TeleportTargets.Refresh();
                foreach (var target in TeleportTargets.Levels.Concat(TeleportTargets.Npcs))
                    Consider(ModConfig.TeleportHotkey(target), () => Restart.GoTo(target));
            }
            best?.Invoke();
        }
    }
}

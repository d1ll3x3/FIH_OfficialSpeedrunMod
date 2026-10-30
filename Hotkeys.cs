using System;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace OfficialSpeedrunMod
{
    // A key with optional Ctrl, Shift and Alt, stored as text: "R", "Ctrl+Alpha1",
    // "Ctrl+Shift+F2". Key names are Unity's KeyCode names; empty is no key.
    public readonly struct Hotkey
    {
        [Flags]
        public enum Mods { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

        public readonly Mods Modifiers;
        public readonly KeyCode Key;

        public Hotkey(Mods modifiers, KeyCode key)
        {
            Modifiers = modifiers;
            Key = key;
        }

        public bool IsNone => Key == KeyCode.None;

        // Unknown text reads as no key, so a typo in the config disables one hotkey only.
        public static Hotkey Parse(string text)
        {
            var mods = Mods.None;
            var key = KeyCode.None;
            foreach (string part in (text ?? "").Split('+').Select(p => p.Trim()).Where(p => p.Length > 0))
            {
                if (Enum.TryParse(part, true, out Mods mod) && mod != Mods.None) mods |= mod;
                else if (!Enum.TryParse(part, true, out key)) return default;
            }
            return new Hotkey(mods, key);
        }

        public override string ToString()
        {
            if (IsNone) return "";
            var held = Modifiers;
            string mods = string.Join("+", new[] { Mods.Ctrl, Mods.Shift, Mods.Alt }
                .Where(m => (held & m) != 0).Select(m => m.ToString()));
            return mods.Length > 0 ? mods + "+" + Key : Key.ToString();
        }

        public static Mods HeldModifiers()
        {
            var mods = Mods.None;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) mods |= Mods.Ctrl;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) mods |= Mods.Shift;
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt) || Input.GetKey(KeyCode.AltGr)) mods |= Mods.Alt;
            return mods;
        }

        public static bool IsModifier(KeyCode key)
            => key == KeyCode.LeftControl || key == KeyCode.RightControl || key == KeyCode.LeftShift ||
               key == KeyCode.RightShift || key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.AltGr;

        // Its key went down this frame with at least its modifiers held.
        public bool WasPressed(Mods held) => !IsNone && (held & Modifiers) == Modifiers && Input.GetKeyDown(Key);

        public int ModifierCount => ((Modifiers & Mods.Ctrl) != 0 ? 1 : 0) + ((Modifiers & Mods.Shift) != 0 ? 1 : 0)
                                    + ((Modifiers & Mods.Alt) != 0 ? 1 : 0);
    }

    // The restart key and the teleport hotkeys, read while playing (not with the menu open).
    // Of the hotkeys pressed, the one with the most modifiers wins, so R and Shift+R can
    // each do their own thing.
    public static class Hotkeys
    {
        public static void Update(bool menuOpen)
        {
            if (menuOpen || !Application.isFocused || !Input.anyKeyDown) return;
            var held = Hotkey.HeldModifiers();

            Action best = null;
            int bestMods = -1;
            void Consider(ConfigEntry<string> entry, Action action)
            {
                var hotkey = Hotkey.Parse(entry.Value);
                if (hotkey.ModifierCount > bestMods && hotkey.WasPressed(held))
                {
                    best = action;
                    bestMods = hotkey.ModifierCount;
                }
            }

            Consider(ModConfig.RestartKey, Restart.Go);
            if (TeleportTargets.IsPlayerReady() && TeleportTargets.IsStoryMap())
            {
                // The lists are only rebuilt when the menu opens; a first hotkey before that
                // needs them built.
                TeleportTargets.Refresh();
                foreach (var target in TeleportTargets.Levels.Concat(TeleportTargets.Npcs))
                    Consider(ModConfig.TeleportHotkey(target), () => Restart.GoTo(target));
            }
            best?.Invoke();
        }

        // The next key pressed, with the modifiers held, for binding one. None while only
        // modifiers are down; Escape cancels (returns true with no key).
        public static bool TryCapture(out Hotkey hotkey)
        {
            hotkey = default;
            if (!Input.anyKeyDown) return false;
            foreach (KeyCode key in AllKeys)
            {
                if (!Input.GetKeyDown(key)) continue;
                if (key == KeyCode.Escape) return true;
                hotkey = new Hotkey(Hotkey.HeldModifiers(), key);
                return true;
            }
            return false;
        }

        // Every keyboard key but the modifiers. Mouse buttons can't be bound (the click that
        // started the capture would be).
        private static readonly KeyCode[] AllKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(k => k != KeyCode.None && !(k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6) &&
                        k < KeyCode.JoystickButton0 && !Hotkey.IsModifier(k))
            .Distinct()
            .ToArray();
    }
}

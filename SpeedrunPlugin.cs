using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;
using System.IO;

namespace OfficialSpeedrunMod
{
    [BepInPlugin("com.flippingishard.officialspeedrunmod", "FIH Official Speedrun Mod", "1.0.0")]
    public class SpeedrunPlugin : BasePlugin
    {
        internal static ManualLogSource Logger { get; private set; }

        public override void Load()
        {
            Logger = Log;
            try
            {
                // Next to the dll instead of BepInEx\config, so the mod is one folder that
                // keeps its best times when it is copied to another install.
                string folder = Path.GetDirectoryName(typeof(SpeedrunPlugin).Assembly.Location) ?? Paths.ConfigPath;
                ModConfig.Init(new ConfigFile(Path.Combine(folder, "OfficialSpeedrunMod.cfg"), true));

                ClassInjector.RegisterTypeInIl2Cpp<SpeedrunBehaviour>();

                var go = new GameObject("OfficialSpeedrunMod");
                GameObject.DontDestroyOnLoad(go);
                go.AddComponent<SpeedrunBehaviour>();

                Logger.LogInfo($"Official Speedrun Mod loaded. B (or LB + RB) opens the teleport menu, {ModConfig.RestartKey.Value} restarts.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading the mod: {ex}");
            }
        }
    }

    // Drives the teleport menu, the restart key and the level timer. Must have the IntPtr
    // constructor for IL2CPP interop.
    public class SpeedrunBehaviour : MonoBehaviour
    {
        public SpeedrunBehaviour(IntPtr ptr) : base(ptr) { }

        private TeleportMenu _menu;

        void Awake()
        {
            _menu = new TeleportMenu();
        }

        void Update()
        {
            try
            {
                AchievementBlocker.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error blocking achievements: {ex}");
            }

            try
            {
                _menu.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error in the teleport menu: {ex}");
            }

            try
            {
                Hotkeys.Update(_menu.IsOpen);
                Restart.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error in the hotkeys: {ex}");
            }

            try
            {
                LevelTimer.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error in the level timer: {ex}");
            }

            try
            {
                Watermark.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error in the watermark: {ex}");
            }
        }

        void LateUpdate()
        {
            try
            {
                Restart.LateUpdate();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error holding the NPC challenge: {ex}");
            }
        }

        void OnGUI()
        {
            _menu.Draw();
        }
    }
}

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;
using System.IO;

namespace OfficialSpeedrunMod
{
    [BepInPlugin("com.flippingishard.officialspeedrunmod", "FIH Official Speedrun Mod", "1.1.1")]
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
                GameEndBlock.Patch(new Harmony("com.flippingishard.officialspeedrunmod"));

                ClassInjector.RegisterTypeInIl2Cpp<SpeedrunBehaviour>();

                var go = new GameObject("OfficialSpeedrunMod");
                GameObject.DontDestroyOnLoad(go);
                go.AddComponent<SpeedrunBehaviour>();

                Logger.LogInfo($"Official Speedrun Mod loaded. Its menu is the Speedrun app in the phone (Tab); {ModConfig.RestartKey.Value} restarts.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error loading the mod: {ex}");
            }
        }
    }

    // Drives the phone app, the hotkeys, the level timer and the watermark. Must have the
    // IntPtr constructor for IL2CPP interop.
    public class SpeedrunBehaviour : MonoBehaviour
    {
        public SpeedrunBehaviour(IntPtr ptr) : base(ptr) { }

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
                PhoneApp.Update();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error in the phone app: {ex}");
            }

            try
            {
                Hotkeys.Update(PhoneApp.IsOpen);
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
    }
}

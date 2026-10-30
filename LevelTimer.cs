using System;
using System.Collections.Generic;
using EHS;
using EHS.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // Times each level on its own, the way the game times the run.
    //
    // The game's splits come from its ChangeLevelProgress zones: entering one calls
    // LevelsInfoManager.SetActiveLevelInfo with the run's playtime, and that playtime is
    // real time (realtimeSinceStartup) that only stops while the game is frozen. So a level
    // starts when the player enters one of its zones and ends when they enter a zone of a
    // later level, and its clock is real time paused while GameManager.IsGameFrozen.
    //
    // The zones are polled through the field OnTriggerEntered sets (playerInside) instead of
    // hooking it: the game skips SetActiveLevelInfo when the level is already the active one,
    // which is exactly the case after a restart, and no game method gets detoured.
    //
    // After a teleport, and when a level loads, the zones the player is put into belong to
    // the spawn: until the player moves (the same inputs that start the game's own timer)
    // entering them starts nothing, so a spawn just before a level times only that level.
    // The first level of a new run, and a level the menu teleported into, start on that
    // first move instead, like the game's timer does; a level target placed just before its
    // level waits for the level's zone like any other spawn.
    public static class LevelTimer
    {
        private enum State { Idle, Armed, Running }

        private static ChangeLevelProgressBase[] _zones = Array.Empty<ChangeLevelProgressBase>();
        private static bool[] _wasInside = Array.Empty<bool>();
        private static IntPtr _zonesBuiltFor;

        private static State _state;
        private static bool _awaitingMove;
        private static int _spawnLevel = -1;
        private static int _armedLevel = -1;

        private static LevelInfoSO _level;
        private static double _elapsed;
        private static double _lastRealtime;

        // The level whose name is shown under the time: the one running, or the one a level
        // spawn waits for. Its name is looked up when it changes.
        private static int _shownLevel = -1;
        private static int _namedLevel = -1;
        private static string _levelName = "";

        private static bool _wasGameEnded;

        /// <summary>
        /// Back to 0 at a spawn. <paramref name="startsLevel"/> is the level the spawn is the
        /// start of (a menu level teleport), which then starts on the first move if the player
        /// is in it; -1 waits for the next level's zone. <paramref name="spawnLevel"/> is the level the spawn stands
        /// in, if known.
        /// </summary>
        public static void Reset(int startsLevel, int spawnLevel)
        {
            _state = startsLevel >= 0 ? State.Armed : State.Idle;
            _armedLevel = startsLevel;
            _shownLevel = startsLevel;
            _spawnLevel = spawnLevel;
            _awaitingMove = true;
            _level = null;
            _elapsed = 0d;
        }

        public static void Update()
        {
            var player = PlayerRef.LocalPlayer;
            var levels = player?.GameRefs?.LevelsInfoManager;
            if (player?.Rb == null || levels == null)
            {
                LevelTimerView.Hide();
                return;
            }

            if (levels.Pointer != _zonesBuiltFor) OnLevelLoaded(levels, player);

            Tick();

            bool moved = _awaitingMove && GameplayInput.Started(player);
            foreach (var info in EnteredLevels())
                OnLevelEntered(info);

            if (moved) OnFirstMove(levels, player);

            // The run's end is the last level's end.
            bool ended = GameManager.IsGameEnded;
            if (ended && !_wasGameEnded && _state == State.Running) Finish();
            _wasGameEnded = ended;

            Show(player);
        }

        private static void OnLevelLoaded(LevelsInfoManager levels, PlayerRef player)
        {
            _zonesBuiltFor = levels.Pointer;
            _zones = Object.FindObjectsOfType<ChangeLevelProgressBase>(true);
            _wasInside = new bool[_zones.Length];
            for (int i = 0; i < _zones.Length; i++) _wasInside[i] = IsInside(_zones[i]);
            Reset(-1, levels.ActiveLevelInfo != null ? levels.ActiveLevelInfo.Id : -1);
            _wasGameEnded = GameManager.IsGameEnded;
            // Level ids repeat across maps.
            _namedLevel = -1;
            _levelName = "";
        }

        // Levels whose zone the player entered since last frame.
        private static List<LevelInfoSO> EnteredLevels()
        {
            var entered = new List<LevelInfoSO>();
            for (int i = 0; i < _zones.Length; i++)
            {
                var zone = _zones[i];
                if (zone == null) continue;
                bool inside = IsInside(zone);
                if (inside && !_wasInside[i])
                {
                    var info = zone.levelInfoHolder?.LevelInfo;
                    if (info != null) entered.Add(info);
                }
                _wasInside[i] = inside;
            }
            return entered;
        }

        // Compared as a plain reference: PlayerRef's own == needs FishNet, which this mod
        // doesn't reference. The game clears the field when the player leaves.
        private static bool IsInside(ChangeLevelProgressBase zone) => (object)zone.playerInside != null;

        private static void OnLevelEntered(LevelInfoSO info)
        {
            if (_awaitingMove)
            {
                _spawnLevel = info.Id;
                return;
            }

            switch (_state)
            {
                case State.Idle:
                    if (info.Id != _spawnLevel) Start(info);
                    break;
                case State.Running:
                    // A zone of a lower level is the player falling back: the level goes on.
                    if (info.Id > _level.Id)
                    {
                        Finish();
                        Start(info);
                    }
                    break;
            }
        }

        private static void OnFirstMove(LevelsInfoManager levels, PlayerRef player)
        {
            _awaitingMove = false;

            if (_state == State.Armed)
            {
                // Outside the level, its zone starts it, as for any spawn.
                if (IsInsideLevel(_armedLevel)) Start(FindLevel(levels, _armedLevel));
                else _state = State.Idle;
                return;
            }

            // A new run: the game's timer is still at 0 and starts on this same move.
            double playtime = player.PlayerProgressHandler != null ? player.PlayerProgressHandler.Playtime : -1d;
            if (_state == State.Idle && playtime >= 0d && playtime < 0.05d)
                Start(FindLevel(levels, _spawnLevel) ?? LevelInside());
        }

        // The level of a zone the player is in, for a run that loads with the player already
        // inside the first level's zone (no entry to see).
        private static LevelInfoSO LevelInside()
        {
            foreach (var zone in _zones)
                if (zone != null && IsInside(zone)) return zone.levelInfoHolder?.LevelInfo;
            return null;
        }

        private static bool IsInsideLevel(int id)
        {
            foreach (var zone in _zones)
            {
                var info = zone != null && IsInside(zone) ? zone.levelInfoHolder?.LevelInfo : null;
                if (info != null && info.Id == id) return true;
            }
            return false;
        }

        private static LevelInfoSO FindLevel(LevelsInfoManager levels, int id)
        {
            var infos = levels.LevelsInfo?.TryCast<Il2CppSystem.Collections.Generic.List<LevelInfoSO>>();
            for (int i = 0; infos != null && i < infos.Count; i++)
                if (infos[i] != null && infos[i].Id == id) return infos[i];
            return null;
        }

        private static void Start(LevelInfoSO info)
        {
            if (info == null)
            {
                _state = State.Idle;
                return;
            }
            _state = State.Running;
            _level = info;
            _shownLevel = info.Id;
            _elapsed = 0d;
            _lastRealtime = Time.realtimeSinceStartupAsDouble;
        }

        // The game's playtime clock: real time, paused while the game is frozen.
        private static void Tick()
        {
            if (_state != State.Running) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!GameManager.IsGameFrozen) _elapsed += now - _lastRealtime;
            _lastRealtime = now;
        }

        private static void Finish()
        {
            string mapId = MapId();
            string name = TeleportTargets.LevelName(_level);
            bool newPb = ModConfig.RecordTime(mapId, _level.Id, name, _elapsed);
            SpeedrunPlugin.Logger.LogInfo($"Level {name}: {_elapsed:0.000}s{(newPb ? " (new PB)" : "")}");

            _state = State.Idle;
            _spawnLevel = _level.Id;
            _level = null;
        }

        // The game's own time format with centiseconds (its run timer and splits stop at
        // seconds). Monospaced adds TextMeshPro tags, for the on-screen timer only.
        public static string Format(double time, bool monospaced)
            => TimerUtil.GetTimeParts(time).ToStringCentiseconds(monospaced);

        public static string MapId() => PlayerRef.LocalPlayer?.GameRefs?.WorldSaveManager?.GetCurrentProgress()?.MapId ?? "";

        // The time, like the game's timer, and the level under it: 0 while waiting for a level,
        // frozen on the last level's time when the run ends.
        private static void Show(PlayerRef player)
        {
            if (_shownLevel != _namedLevel)
            {
                _namedLevel = _shownLevel;
                var info = FindLevel(player.GameRefs.LevelsInfoManager, _shownLevel);
                _levelName = info != null ? TeleportTargets.LevelName(info) : "";
            }
            string text = Format(_elapsed, true);
            if (_levelName.Length > 0) text += $"\n<size=55%>{_levelName}</size>";
            LevelTimerView.Show(player, text);
        }
    }

    // The inputs that start the game's run timer (TimerFreezeController
    // .DidReceiveTimerStartingGameplayInput): moving, jumping or interacting, each only when
    // the player can actually do it.
    public static class GameplayInput
    {
        public static bool Started(PlayerRef player)
        {
            var input = player.InputHandler;
            var passer = player.PlayerInputPasser;
            if (input == null || passer == null) return false;
            if (input.MoveInput.sqrMagnitude > 0.01f && passer.CanMove()) return true;
            if (input.WasPressed(PlayerButtons.ButtonType.Jump, false) && passer.CanJump()) return true;
            return input.WasPressed(PlayerButtons.ButtonType.Interact, false) && passer.CanInputsPass();
        }
    }
}

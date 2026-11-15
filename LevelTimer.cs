using System;
using System.Collections.Generic;
using System.Linq;
using EHS;
using EHS.Player;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // Times the level of the spawn you used, the way the game times the run.
    //
    // The game's splits come from its ChangeLevelProgress zones: entering one calls
    // LevelsInfoManager.SetActiveLevelInfo with the run's playtime, and that playtime is
    // real time (realtimeSinceStartup) that only stops while the game is frozen. So the level
    // starts when the player enters one of its zones and ends when they enter a zone of a
    // later level, and its clock is real time paused while GameManager.IsGameFrozen.
    //
    // The zones are polled through the field OnTriggerEntered sets (playerInside) instead of
    // hooking it: the game skips SetActiveLevelInfo when the level is already the active one,
    // which is exactly the case after a restart, and no game method gets detoured.
    //
    // Only the spawn's level is timed, once per spawn: picking a level in the menu (or its
    // hotkey, or the restart key) arms it, and reaching the next level ends it. Nothing else
    // starts it, so passing through other levels never touches their best times. Zones the
    // teleport itself puts the player into start nothing; if the spawn is already inside the
    // level, it starts on the first move (the inputs that start the game's own timer).
    //
    // Some levels have a fake area trigger (ChangeAreaNotifierFakeOnCollision): it only shows
    // another area's name, like the fake "The End" before the boss door, and is not a
    // progress zone. It counts as the level whose zone starts highest below it, the one it
    // stands in (there, The Server Room), so that level runs from the fake trigger to the
    // real next one and the level before it ends there.
    //
    // While the timer is on, the end of the game ends the level instead of the run (see
    // GameEndBlock).
    public static class LevelTimer
    {
        private enum State { Off, Waiting, Running, Finished }

        private static ChangeLevelProgressBase[] _zones = Array.Empty<ChangeLevelProgressBase>();
        private static bool[] _wasInside = Array.Empty<bool>();
        private static IntPtr _zonesBuiltFor;
        // The fake area triggers and the level each one counts as.
        private static (TriggerDetectorLocalPlayer detector, int level)[] _fakes = Array.Empty<(TriggerDetectorLocalPlayer, int)>();
        private static bool[] _wasInsideFake = Array.Empty<bool>();

        private static State _state;
        private static int _levelId = -1;
        private static bool _awaitingMove;
        private static double _elapsed;
        private static double _lastRealtime;
        // A finished level's time stays on screen until then, like a challenge's result.
        private static float _resultUntil;

        // The timed level's name, for its best time entry, looked up when the level changes.
        private static int _namedLevel = -1;
        private static string _levelName = "";

        /// <summary>
        /// Back to 0 at a spawn, armed for <paramref name="level"/>: the level the spawn is
        /// the start of, or -1 (an NPC spawn) to turn the timer off.
        /// </summary>
        public static void Reset(int level)
        {
            _state = level >= 0 ? State.Waiting : State.Off;
            _levelId = level;
            _awaitingMove = true;
            _elapsed = 0d;
        }

        public static bool IsOn => _state != State.Off;

        // The end of the game reached while the timer is on: the level ends there.
        public static void OnGameEndReached()
        {
            var player = PlayerRef.LocalPlayer;
            var levels = player?.GameRefs?.LevelsInfoManager;
            if (_state == State.Running && levels != null) Finish(player, levels);
        }

        public static void Update()
        {
            var player = PlayerRef.LocalPlayer;
            var levels = player?.GameRefs?.LevelsInfoManager;
            if (player?.Rb == null || levels == null)
            {
                LevelTimerView.Hide(player);
                return;
            }

            if (levels.Pointer != _zonesBuiltFor) OnLevelLoaded(levels);

            // Followed even while off, so arming the timer doesn't see old entries as new.
            var entered = EnteredLevels();
            if (_state == State.Off)
            {
                LevelTimerView.Hide(player);
                return;
            }

            Tick();

            switch (_state)
            {
                case State.Waiting:
                    if (_awaitingMove && GameplayInput.Started(player))
                    {
                        _awaitingMove = false;
                        if (IsInsideLevel(_levelId)) Start();
                    }
                    else if (!_awaitingMove && entered.Contains(_levelId))
                    {
                        Start();
                    }
                    break;
                case State.Running:
                    if (entered.Any(id => id > _levelId)) Finish(player, levels);
                    break;
            }

            Show(player, levels);
        }

        private static void OnLevelLoaded(LevelsInfoManager levels)
        {
            _zonesBuiltFor = levels.Pointer;
            _zones = Object.FindObjectsOfType<ChangeLevelProgressBase>(true);
            _wasInside = new bool[_zones.Length];
            for (int i = 0; i < _zones.Length; i++) _wasInside[i] = IsInside(_zones[i]);
            _fakes = FindFakeAreas();
            _wasInsideFake = new bool[_fakes.Length];
            for (int i = 0; i < _fakes.Length; i++) _wasInsideFake[i] = IsInside(_fakes[i].detector);
            Reset(-1);
            // Level ids repeat across maps.
            _namedLevel = -1;
        }

        // Ids of the levels whose zone the player entered since last frame.
        private static List<int> EnteredLevels()
        {
            var entered = new List<int>();
            for (int i = 0; i < _zones.Length; i++)
            {
                var zone = _zones[i];
                if (zone == null) continue;
                bool inside = IsInside(zone);
                if (inside && !_wasInside[i])
                {
                    var info = zone.levelInfoHolder?.LevelInfo;
                    if (info != null) entered.Add(info.Id);
                }
                _wasInside[i] = inside;
            }
            for (int i = 0; i < _fakes.Length; i++)
            {
                var (detector, level) = _fakes[i];
                if (detector == null) continue;
                bool inside = IsInside(detector);
                if (inside && !_wasInsideFake[i]) entered.Add(level);
                _wasInsideFake[i] = inside;
            }
            return entered;
        }

        // Each fake area trigger, with the level of the zone whose trigger starts highest below
        // it. One with no zone below counts for nothing.
        private static (TriggerDetectorLocalPlayer, int)[] FindFakeAreas()
        {
            var zoneStarts = new List<(float bottom, int level, string name)>();
            foreach (var zone in _zones)
            {
                var info = zone != null ? zone.levelInfoHolder?.LevelInfo : null;
                if (info == null || !TryGetBounds(zone.triggerDetectorComponent, out var bounds)) continue;
                zoneStarts.Add((bounds.min.y, info.Id, TeleportTargets.LevelName(info)));
            }

            var fakes = new List<(TriggerDetectorLocalPlayer, int)>();
            foreach (var fake in Object.FindObjectsOfType<ChangeAreaNotifierFakeOnCollision>(true))
            {
                var detector = fake.triggerDetectorComponent;
                if (detector == null) continue;
                float y = TryGetBounds(detector, out var bounds) ? bounds.center.y : fake.transform.position.y;
                var below = zoneStarts.Where(z => z.bottom <= y).OrderByDescending(z => z.bottom).ToList();
                if (below.Count == 0) continue;
                fakes.Add((detector, below[0].level));
                SpeedrunPlugin.Logger.LogInfo(
                    $"Level timer: fake area trigger '{fake.gameObject.name}' at y {y:0.#} counts as {below[0].name}.");
            }
            return fakes.ToArray();
        }

        private static bool TryGetBounds(Component detector, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            if (detector == null) return false;
            foreach (var col in detector.GetComponentsInChildren<Collider>(true))
            {
                if (!has) bounds = col.bounds;
                else bounds.Encapsulate(col.bounds);
                has = true;
            }
            return has;
        }

        private static bool IsInside(TriggerDetectorLocalPlayer detector) => !detector.IsEmpty();

        // Compared as a plain reference: PlayerRef's own == needs FishNet, which this mod
        // doesn't reference. The game clears the field when the player leaves.
        private static bool IsInside(ChangeLevelProgressBase zone) => (object)zone.playerInside != null;

        private static bool IsInsideLevel(int id)
        {
            foreach (var zone in _zones)
            {
                var info = zone != null && IsInside(zone) ? zone.levelInfoHolder?.LevelInfo : null;
                if (info != null && info.Id == id) return true;
            }
            foreach (var (detector, level) in _fakes)
                if (level == id && detector != null && IsInside(detector)) return true;
            return false;
        }

        private static LevelInfoSO FindLevel(LevelsInfoManager levels, int id)
        {
            var infos = levels.LevelsInfo?.TryCast<Il2CppSystem.Collections.Generic.List<LevelInfoSO>>();
            for (int i = 0; infos != null && i < infos.Count; i++)
                if (infos[i] != null && infos[i].Id == id) return infos[i];
            return null;
        }

        private static void Start()
        {
            _state = State.Running;
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

        private static void Finish(PlayerRef player, LevelsInfoManager levels)
        {
            string name = LevelName(levels);
            bool newPb = ModConfig.RecordTime(MapId(), _levelId, name, _elapsed);
            SpeedrunPlugin.Logger.LogInfo($"Level {name}: {_elapsed:0.000}s{(newPb ? " (new PB)" : "")}");
            _state = State.Finished;
            _resultUntil = Time.unscaledTime + LevelTimerView.ResultDuration(player);
        }

        private static string LevelName(LevelsInfoManager levels)
        {
            if (_namedLevel != _levelId)
            {
                _namedLevel = _levelId;
                var info = FindLevel(levels, _levelId);
                _levelName = info != null ? TeleportTargets.LevelName(info) : $"Level {_levelId}";
            }
            return _levelName;
        }

        // The game's own time format with centiseconds (its run timer and splits stop at
        // seconds). Monospaced adds TextMeshPro tags, which IMGUI would print.
        public static string Format(double time, bool monospaced)
            => TimerUtil.GetTimeParts(time).ToStringCentiseconds(monospaced);

        public static string MapId() => PlayerRef.LocalPlayer?.GameRefs?.WorldSaveManager?.GetCurrentProgress()?.MapId ?? "";

        // On the game's challenge timer, with the level's best time: 0 while waiting, the
        // running time, then the result for as long as a challenge's result stays up.
        private static void Show(PlayerRef player, LevelsInfoManager levels)
        {
            if (_state == State.Finished && Time.unscaledTime >= _resultUntil)
            {
                LevelTimerView.Hide(player);
                return;
            }
            bool hasPb = ModConfig.TryGetPb(MapId(), _levelId, LevelName(levels), out double pb);
            LevelTimerView.Show(player, _elapsed, hasPb, pb);
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

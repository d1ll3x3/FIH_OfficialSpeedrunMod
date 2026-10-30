using System;
using System.Collections.Generic;
using System.Linq;
using EHS;
using EHS.Interactables;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // One teleport target: where the phone lands (rigidbody position and rotation) and the
    // camera's orbit yaw it lands with. StartsLevel is the id of the level it is the start
    // of (a level target), or -1.
    public sealed class TeleportTarget
    {
        public readonly string Name;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float CameraYaw;
        public readonly int StartsLevel;

        public TeleportTarget(string name, Vector3 position, Quaternion rotation, float cameraYaw, int startsLevel = -1)
        {
            Name = name;
            Position = position;
            Rotation = rotation;
            CameraYaw = cameraYaw;
            StartsLevel = startsLevel;
        }
    }

    // Copied from FIH Ghost's WorldIndex.cs (https://github.com/d1ll3x3/FIH_Ghost), so the
    // teleport menu lands exactly where that mod's Levels and NPCs tabs do.
    //
    // An area starts where its progress zone starts: each area has ChangeLevelProgress zones
    // (by height or along a spline) that tell the game how far the player got, and their
    // start point is on the path the level is climbed from. A boss area is the exception:
    // landing inside it skips the door that starts the fight (the game then forces a
    // fail-safe boss respawn), so its target, and that of any NPC inside it, is the end of
    // the previous area's path, in front of that door. That point is above the door's roof,
    // so the target is the first floor under it. Every computed target is put on the floor
    // below it, so the phone doesn't land falling. The spots the level data gets wrong are
    // placed by hand (FixedTargets).
    public static class TeleportTargets
    {
        // A zone's path from its start to its end, and the volume its trigger covers.
        private struct Zone
        {
            public int Area;
            public Vector3 Start, StartAhead, End, EndBehind;
            public Bounds Bounds;
            public bool HasBounds;
        }

        // The phone rests this far above the floor it lands on: lying flat it is ~0.31 m
        // thick (its BoundingBox), so its centre sits just above the floor.
        private const float RestHeight = 0.2f;
        // How far above a target the floor search starts, and how far down it looks.
        private const float SearchAbove = 1.5f;
        private const float SearchDepth = 12f;
        // Under a roof the floor can be further down than in the open.
        private const float RoofSearchDepth = 30f;
        // How far in front of an NPC the phone lands.
        private const float NpcDistance = 2.5f;
        // Everything but the local player (layer 6), so the search never hits the phone
        // itself wherever it stands.
        private const int GroundMask = ~(1 << 6);

        public const string StoryMapId = "OfficialMap_TrashlandsStory";

        // The story map's spots the level data gets wrong, placed by hand in the game with
        // the phone resting where a run of that level starts: rigidbody position and
        // rotation, and the camera's orbit yaw. Levels 2 to 9 were placed on 30-oct-2026 just
        // before the level, so its timer starts as you walk in (see LevelTimer); the others
        // on 25-oct-2026. L10 is in front of the boss door, under its roof; landing past it
        // starts the fight's fail-safe.
        private static readonly Dictionary<string, (Vector3 pos, Quaternion rot, float cameraYaw)> FixedTargets =
            new Dictionary<string, (Vector3, Quaternion, float)>
        {
            ["area:2"] = (new Vector3(18.724f, -13.059f, 264.634f), new Quaternion(0f, 0.06909f, 0f, 0.99761f), 5.2f),
            ["area:3"] = (new Vector3(-4.956f, 20.956f, 92.231f), new Quaternion(0f, -0.03929f, 0f, 0.99923f), -4.5f),
            ["area:4"] = (new Vector3(2.669f, 295.231f, -99.07f), new Quaternion(0.97126f, 0f, 0.23803f, 0f), 152.4f),
            ["area:5"] = (new Vector3(-65.883f, 809.284f, -124.641f), new Quaternion(0f, -0.03899f, 0f, -0.99924f), -176f),
            ["area:6"] = (new Vector3(-68.264f, 1284.841f, 141.761f), new Quaternion(0.00002f, 0.12312f, -0.00002f, -0.99239f), -11f),
            ["area:7"] = (new Vector3(-132.599f, 2172.341f, -181.957f), new Quaternion(0f, -0.92913f, 0f, 0.36975f), -136.6f),
            ["area:8"] = (new Vector3(-16.755f, 2491.311f, -95.9f), new Quaternion(0f, -0.9622f, 0f, -0.27233f), -32.3f),
            ["area:9"] = (new Vector3(140.339f, 3039.361f, -291.387f), new Quaternion(0f, -0.1188f, 0f, 0.99292f), 165.9f),
            ["area:10"] = (new Vector3(-171.424f, 3511.64f, -79.893f), new Quaternion(0.71287f, 0f, 0.7013f, 0f), -76f),
            ["area:11"] = (new Vector3(-314.231f, 3583.05f, 195.891f), new Quaternion(0f, -0.71351f, 0f, 0.70064f), 140.2f),
            ["npc:GingerbreadFred"] = (new Vector3(-58.053f, 560.423f, -98.186f), new Quaternion(-0.1226f, 0.69639f, 0.6964f, 0.12259f), 166.4f),
            ["npc:Webby"] =(new Vector3(-88.997f, 986.947f, 176.111f), new Quaternion(-0.25796f, 0.28094f, 0.91968f, 0.0933f), -57.7f),
        };
        // The story map's official level names, by level id (1 is the first level). The mod
        // is in English whatever language the game is in, so these replace the game's
        // localized names.
        private static readonly string[] StoryLevelNames =
        {
            "Storm Drain", "Cardboard Fort", "The Playground", "Candyland", "Toy Chest", "The Grid",
            "The Fridge", "Blanket Fort Kingdom", "Recycling Core", "The Server Room", "The End",
        };
        // NPCs are listed bottom to top, the order they are met in, except where height gets
        // it wrong: the first of each pair goes right before the second.
        private static readonly (string first, string second)[] NpcOrderFixes = { ("Muddy", "Plonk") };
        // Interactable NPCs left out of the teleport list (not characters to visit).
        private static readonly string[] HiddenNpcs = { "Microwaver", "Phone", "Narrator" };

        private static TeleportTarget[] _levels = Array.Empty<TeleportTarget>();
        private static TeleportTarget[] _npcs = Array.Empty<TeleportTarget>();
        private static IntPtr _builtFor;

        public static TeleportTarget[] Levels => _levels;
        public static TeleportTarget[] Npcs => _npcs;

        // Checked through its Rigidbody: comparing PlayerRef itself needs FishNet, which this
        // mod doesn't reference.
        public static bool IsPlayerReady() => PlayerRef.LocalPlayer?.Rb != null;

        // The targets only fit the story map's geometry.
        public static bool IsStoryMap()
        {
            var progress = PlayerRef.LocalPlayer?.GameRefs?.WorldSaveManager?.GetCurrentProgress();
            return progress != null && progress.MapId == StoryMapId;
        }

        /// <summary>
        /// Rebuilds the targets when the level (its LevelsInfoManager) changed, or when forced
        /// (a list built while the level was still spawning its objects could miss some).
        /// </summary>
        public static void Refresh(bool force = false)
        {
            try
            {
                var player = PlayerRef.LocalPlayer;
                var levels = player?.GameRefs?.LevelsInfoManager;
                if (levels == null) return;
                if (!force && levels.Pointer == _builtFor) return;
                _builtFor = levels.Pointer;

                bool story = IsStoryMap();
                var zones = FindZones();
                var infos = levels.LevelsInfo?.TryCast<Il2CppSystem.Collections.Generic.List<LevelInfoSO>>();
                var areas = new List<(int id, string name, LevelInfoSO info)>();
                for (int i = 0; infos != null && i < infos.Count; i++)
                    areas.Add((infos[i].Id, CleanAreaName(infos[i].name), infos[i]));

                var doors = new Dictionary<int, (Vector3 pos, Quaternion rot)>();
                var levelTargets = new List<TeleportTarget>();
                foreach (var (id, name, info) in areas)
                {
                    var own = zones.Where(z => z.Area == id).ToList();
                    bool isBoss = name.IndexOf("Boss", StringComparison.OrdinalIgnoreCase) >= 0;
                    var before = zones.Where(z => z.Area < id).ToList();
                    bool has = false;
                    Vector3 pos = default;
                    Quaternion rot = Quaternion.identity;
                    float? cameraYaw = null;
                    if (isBoss && before.Count > 0)
                    {
                        // Highest end among the areas below: the end of the climb up to the door.
                        var last = before.OrderByDescending(z => z.End.y).First();
                        doors[id] = (UnderRoof(last.End), Facing(last.EndBehind, last.End));
                        (pos, rot) = doors[id];
                        has = true;
                    }
                    else if (own.Count > 0)
                    {
                        // Lowest start: areas are climbed upwards, so that is where it begins.
                        var first = own.OrderBy(z => z.Start.y).First();
                        pos = OnFloor(first.Start);
                        rot = Facing(first.Start, first.StartAhead);
                        has = true;
                    }
                    if (story && FixedTargets.TryGetValue("area:" + id, out var fixedTarget))
                    {
                        (pos, rot, float yaw) = fixedTarget;
                        cameraYaw = yaw;
                        has = true;
                    }
                    if (has) levelTargets.Add(new TeleportTarget($"{id}. {LevelName(info)}", pos, rot, cameraYaw ?? rot.eulerAngles.y, id));
                }
                _levels = levelTargets.ToArray();
                _npcs = FindNpcs(zones, doors, story);
                SpeedrunPlugin.Logger.LogInfo($"Teleports: {_levels.Length} levels, {_npcs.Length} NPCs");
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error building the teleport targets: {ex}");
            }
        }

        // The level's name in the menu, the best times and the config: the official name on
        // the story map, the asset's name ("LevelInfo_L5_ToyChest" -> "L5 ToyChest") elsewhere.
        public static string LevelName(LevelInfoSO info)
        {
            if (IsStoryMap() && info.Id >= 1 && info.Id <= StoryLevelNames.Length)
                return StoryLevelNames[info.Id - 1];
            return CleanAreaName(info.name);
        }

        // Same steps as the game's own checkpoint load: through its PlayerTeleportController
        // (which keeps the NetworkTransform in sync), velocities cleared, the camera snapped
        // to the new spot and turned to the target's yaw.
        public static bool Teleport(TeleportTarget target)
        {
            try
            {
                var player = PlayerRef.LocalPlayer;
                var teleport = player?.PlayerTeleportController;
                var rb = player?.Rb;
                if (teleport == null || rb == null)
                {
                    SpeedrunPlugin.Logger.LogWarning($"Could not teleport to {target.Name}: player not ready");
                    return false;
                }
                teleport.TeleportTo(target.Position, target.Rotation);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;

                player.GameRefs?.CameraManager?.SyncPosAfterTeleport(target.Position);
                var orbital = Orbital(player);
                if (orbital != null)
                {
                    var axis = orbital.HorizontalAxis;
                    axis.Value = Mathf.Repeat(target.CameraYaw + 180f, 360f) - 180f;
                    orbital.HorizontalAxis = axis;
                }
                SpeedrunPlugin.Logger.LogInfo($"Teleported to {target.Name}");
                return true;
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Error teleporting to {target.Name}: {ex}");
                return false;
            }
        }

        internal static Unity.Cinemachine.CinemachineOrbitalFollow Orbital(PlayerRef player)
            => player.GameRefs?.CameraManager?.MainCinemachineCamera?.GetComponent<Unity.Cinemachine.CinemachineOrbitalFollow>();

        private static List<Zone> FindZones()
        {
            var zones = new List<Zone>();
            foreach (var zone in Object.FindObjectsOfType<ChangeLevelProgressByHeight>(true))
            {
                int id = ZoneArea(zone);
                if (id < 0 || zone.startPosT == null) continue;
                Vector3 start = zone.startPosT.position;
                Vector3 end = zone.finishPosT != null ? zone.finishPosT.position : start;
                zones.Add(WithBounds(zone, new Zone { Area = id, Start = start, StartAhead = end, End = end, EndBehind = start }));
            }
            foreach (var zone in Object.FindObjectsOfType<ChangeLevelProgressBySpline>(true))
            {
                int id = ZoneArea(zone);
                if (id < 0) continue;
                var path = zone.cachedPositions;
                if (path != null && path.Length > 0)
                {
                    int n = path.Length;
                    zones.Add(WithBounds(zone, new Zone
                    {
                        Area = id, Start = path[0], StartAhead = path[Math.Min(5, n - 1)],
                        End = path[n - 1], EndBehind = path[Math.Max(0, n - 6)],
                    }));
                    continue;
                }
                // The zone caches its path lazily; evaluate the spline itself until it has.
                var spline = zone.splineContainer;
                if (spline == null) continue;
                zones.Add(WithBounds(zone, new Zone
                {
                    Area = id, Start = ToVector(spline.EvaluatePosition(0f)), StartAhead = ToVector(spline.EvaluatePosition(0.02f)),
                    End = ToVector(spline.EvaluatePosition(1f)), EndBehind = ToVector(spline.EvaluatePosition(0.98f)),
                }));
            }
            return zones;
        }

        // The volume of the zone's trigger, to tell which area an NPC stands in.
        private static Zone WithBounds(ChangeLevelProgressBase zone, Zone z)
        {
            var detector = zone.triggerDetectorComponent;
            if (detector == null) return z;
            foreach (var col in detector.GetComponentsInChildren<Collider>(true))
            {
                if (!z.HasBounds) z.Bounds = col.bounds;
                else z.Bounds.Encapsulate(col.bounds);
                z.HasBounds = true;
            }
            return z;
        }

        private static int ZoneArea(ChangeLevelProgressBase zone)
        {
            var info = zone.levelInfoHolder?.LevelInfo;
            return info != null ? info.Id : -1;
        }

        private static TeleportTarget[] FindNpcs(List<Zone> zones, Dictionary<int, (Vector3 pos, Quaternion rot)> doors, bool story)
        {
            var npcs = new List<TeleportTarget>();
            foreach (var npc in Object.FindObjectsOfType<InteractableNpc>(true))
            {
                var t = npc.transform;
                string name = CleanNpcName(npc.gameObject.name);
                if (HiddenNpcs.Any(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase))) continue;

                Vector3 front = t.forward;
                front.y = 0;
                if (front.sqrMagnitude < 0.01f) front = Vector3.forward;
                front.Normalize();
                Vector3 pos = OnFloor(t.position + front * NpcDistance);
                // Facing the NPC, ready to talk.
                Quaternion rot = Quaternion.LookRotation(-front);
                float? cameraYaw = null;
                // Inside a boss area: in front of its door, like the boss itself.
                foreach (var z in zones)
                {
                    if (!z.HasBounds || !z.Bounds.Contains(t.position) || !doors.TryGetValue(z.Area, out var door)) continue;
                    (pos, rot) = door;
                    break;
                }
                if (story && FixedTargets.TryGetValue("npc:" + name, out var fixedTarget))
                {
                    (pos, rot, float yaw) = fixedTarget;
                    cameraYaw = yaw;
                }
                npcs.Add(new TeleportTarget(name, pos, rot, cameraYaw ?? rot.eulerAngles.y));
            }
            return InNpcOrder(npcs, n => n.Name, n => n.Position.y).ToArray();
        }

        // Bottom to top, with NpcOrderFixes applied.
        internal static List<T> InNpcOrder<T>(IEnumerable<T> npcs, Func<T, string> name, Func<T, float> height)
        {
            var list = npcs.OrderBy(height).ToList();
            foreach (var (first, second) in NpcOrderFixes)
            {
                int a = list.FindIndex(n => string.Equals(name(n), first, StringComparison.OrdinalIgnoreCase));
                int b = list.FindIndex(n => string.Equals(name(n), second, StringComparison.OrdinalIgnoreCase));
                if (a < 0 || b < 0 || a < b) continue;
                var npc = list[a];
                list.RemoveAt(a);
                list.Insert(b, npc);
            }
            return list;
        }

        // The floor under (or just above) a point, with the phone resting on it; the point
        // itself when there is none within reach.
        private static Vector3 OnFloor(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * SearchAbove, Vector3.down, out RaycastHit hit,
                       SearchAbove + SearchDepth, GroundMask, QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * RestHeight
                : point;
        }

        // The first floor under a roof: the search goes on from just below the first surface
        // it hits. Started inside the roof, it can't hit the roof's underside (colliders are
        // only hit from outside), so the next hit is the floor beneath.
        private static Vector3 UnderRoof(Vector3 point)
        {
            if (!Physics.Raycast(point + Vector3.up * SearchAbove, Vector3.down, out RaycastHit roof,
                    SearchAbove + SearchDepth, GroundMask, QueryTriggerInteraction.Ignore))
                return point;
            if (Physics.Raycast(roof.point + Vector3.down * 0.05f, Vector3.down, out RaycastHit floor,
                    RoofSearchDepth, GroundMask, QueryTriggerInteraction.Ignore))
                return floor.point + Vector3.up * RestHeight;
            return roof.point + Vector3.up * RestHeight;
        }

        private static Quaternion Facing(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            dir.y = 0;
            return dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
        }

        private static Vector3 ToVector(Unity.Mathematics.float3 v) => new Vector3(v.x, v.y, v.z);

        // "LevelInfo_L5_ToyChest" -> "L5 ToyChest".
        private static string CleanAreaName(string assetName)
        {
            string name = assetName ?? "";
            if (name.StartsWith("LevelInfo_")) name = name.Substring("LevelInfo_".Length);
            return name.Replace('_', ' ');
        }

        // "InteractableNPC_MsElephant(Clone)" -> "MsElephant".
        internal static string CleanNpcName(string goName)
        {
            string name = (goName ?? "").Replace("(Clone)", "").Trim();
            const string prefix = "InteractableNPC";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(prefix.Length);
            return name.Trim('_', ' ', '-').Replace('_', ' ');
        }
    }
}

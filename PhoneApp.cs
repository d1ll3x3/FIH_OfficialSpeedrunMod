using System;
using System.Collections.Generic;
using System.Linq;
using EHS;
using EHS.Interactables;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace OfficialSpeedrunMod
{
    // The mod's menu, as a "Speedrun" app in the game's phone (FlipOS, opened with Tab). It is
    // built from copies of the phone's own pieces, so it looks, navigates and animates like
    // the vanilla apps (keyboard and gamepad alike).
    //  - the entry is a copy of the Stats entry in the phone's main menu;
    //  - its submenu (Levels, NPCs, Best times, Hotkeys) is a copy of the main menu screen;
    //  - every entry gets its own animated icon (PhoneIcons) instead of the Stats one;
    //  - each section is a copy of the Stats screen, a scrolling list of name + value rows
    //    (copies of its plain stat rows, like "Distance travelled") split by its separator.
    //
    // How the phone works (Cpp2IL ISIL of the PhoneOs* classes): PhoneOsCanvas.OpenScreen
    // hides the screen on top of its stack and shows the new one; back (GoToPreviousScreen)
    // pops it, and leaves the phone from the first screen. A list item's ClickItem invokes
    // OnItemClicked and then opens its openScreenOnClick. A scroll list keeps a few slots
    // under itemsSlotsRoot: on every selection it unparents all its rows (SetParent(null)) and
    // hides them, then parents the rows around the selection to the slots. A row's
    // SetSelectedState recolors its recolorGraphics with defaultColor or selectedColor, which
    // the theme sets at runtime (ApplyTheme) and a copy doesn't get. The canvas
    // initializes the screens it has once (InternalInit), so the copies, made later, are
    // initialized here the same way and added to its allScreens.
    //
    // Lists of components are copied (ToArray) before anything in them is destroyed: walking
    // the game's own array while destroying crashed the game natively.
    public static class PhoneApp
    {
        private const string Name = "OSM_";
        // Each row's own texts (see Row).
        private const string LabelName = Name + "Label";
        private const string ValueName = Name + "Value";

        private static IntPtr _builtFor;
        private static PhoneOsCanvas _canvas;
        private static GameObject _rowTemplate;
        private static GameObject _separatorTemplate;
        // The theme's row colors, from the live Stats rows (see Row).
        private static Color _defaultColor;
        private static Color _selectedColor;
        // Each section's rows: the scroll list takes them out of the screen, so they are kept
        // here to be removed when the section is filled again.
        private static readonly Dictionary<Section, List<GameObject>> _sectionRows = new Dictionary<Section, List<GameObject>>();
        private static PhoneOsScreenListBase _menu;
        private static readonly Dictionary<Section, PhoneOsScreenScrollList> _sections = new Dictionary<Section, PhoneOsScreenScrollList>();

        private enum Section { Levels, Npcs, BestTimes, Hotkeys }

        private static readonly (Section, string, PhoneIcons.Kind)[] Titles =
        {
            (Section.Levels, "Levels", PhoneIcons.Kind.Route),
            (Section.Npcs, "NPCs", PhoneIcons.Kind.Chat),
            (Section.BestTimes, "Best times", PhoneIcons.Kind.Trophy),
            (Section.Hotkeys, "Hotkeys", PhoneIcons.Kind.Gamepad),
        };

        // Picked in the phone: done once the phone has closed, so the teleport isn't undone
        // by the phone's own exit.
        private static TeleportTarget _pendingTeleport;

        // The best time row clicked once: a second click resets it.
        private static string _confirmReset;

        public static bool IsOpen => PlayerRef.LocalPlayer?.PlayerPhoneOs?.IsPhoneOsActive ?? false;

        public static void Update()
        {
            var phone = PlayerRef.LocalPlayer?.PlayerPhoneOs;
            var canvas = phone?.phoneOsCanvas;
            if (canvas == null) return;

            if (canvas.Pointer != _builtFor && canvas.isInitialized) Build(canvas);
            PhoneIcons.Update();
            if (_captureRow != null) Capture(phone);

            if (_pendingTeleport != null && !phone.IsPhoneOsActive)
            {
                var target = _pendingTeleport;
                _pendingTeleport = null;
                Restart.GoTo(target);
            }
        }

        // ── Building ────────────────────────────────────────────────────

        private static void Build(PhoneOsCanvas canvas)
        {
            _builtFor = canvas.Pointer;
            _canvas = canvas;
            _sections.Clear();
            _sectionRows.Clear();
            var created = new List<GameObject>();
            try
            {
                var main = canvas.firstScreen?.TryCast<PhoneOsScreenListBase>();
                var stats = canvas.statsScreen;
                var statsEntry = main == null || stats == null ? null : main.items?.ToArray()
                    .FirstOrDefault(i => i != null && i.openScreenOnClick != null && i.openScreenOnClick.Pointer == stats.Pointer);
                if (statsEntry == null)
                {
                    SpeedrunPlugin.Logger.LogWarning("Phone app: the phone's main menu or its Stats entry was not found; the Speedrun app is not added.");
                    return;
                }
                if (!TakeTemplates(stats))
                {
                    SpeedrunPlugin.Logger.LogWarning("Phone app: the Stats screen has no plain stat row or separator to copy; the Speedrun app is not added.");
                    return;
                }

                // The sections first: the submenu's entries open them.
                foreach (var (section, title, _) in Titles)
                    _sections[section] = CopyStatsScreen(stats, section, title, created);

                _menu = CopyMenuScreen(main, statsEntry, created);

                // Last, the app's entry in the main menu, right after Stats.
                var entryGo = Object.Instantiate(statsEntry.gameObject, statsEntry.transform.parent, false);
                created.Add(entryGo);
                entryGo.name = Name + "Entry";
                entryGo.transform.SetSiblingIndex(statsEntry.transform.GetSiblingIndex() + 1);
                var entry = entryGo.GetComponent<PhoneOsListItemBase>();
                SetLabel(entryGo, "Speedrun");
                PhoneIcons.Apply(entryGo, PhoneIcons.Kind.Stopwatch);
                entry.openScreenOnClick = _menu;
                entry.OnItemClicked = Safe("opening the app", () => TeleportTargets.Refresh(force: true));
                int at = main.items.IndexOf(statsEntry);
                main.items.Insert(at + 1, entry);
                // Again, so its scroll bar is sized for one more entry.
                main.Init(canvas);

                SpeedrunPlugin.Logger.LogInfo("Phone app: Speedrun added to the phone (Tab).");
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Phone app: could not build the Speedrun app, leaving the phone as it is: {ex}");
                foreach (var go in created)
                    if (go != null) Object.Destroy(go);
                _sections.Clear();
                _menu = null;
            }
        }

        // The submenu: a copy of the main menu with a copy of the Stats entry per section.
        private static PhoneOsScreenListBase CopyMenuScreen(PhoneOsScreenListBase main, PhoneOsListItemBase statsEntry, List<GameObject> created)
        {
            var go = Object.Instantiate(main.gameObject, main.transform.parent, false);
            created.Add(go);
            go.name = Name + "Menu";
            var screen = go.GetComponent<PhoneOsScreenListBase>();

            // The copy of the Stats entry is the template; every other entry goes.
            PhoneOsListItemBase template = null;
            foreach (var item in go.GetComponentsInChildren<PhoneOsListItemBase>(true).ToArray())
            {
                if (template == null && item.gameObject.name == statsEntry.gameObject.name) template = item;
                else Object.DestroyImmediate(item.gameObject);
            }

            // All copied before any gets its icon: a copy made after would take the icon along.
            var copies = Titles.Select((_, i) => i == 0 ? template.gameObject
                : Object.Instantiate(template.gameObject, template.transform.parent, false)).ToList();
            var entries = new List<PhoneOsListItemBase>();
            foreach (var (section, label, icon) in Titles)
            {
                var itemGo = copies[entries.Count];
                itemGo.name = Name + label;
                itemGo.transform.SetAsLastSibling();
                SetLabel(itemGo, label);
                PhoneIcons.Apply(itemGo, icon);
                var item = itemGo.GetComponent<PhoneOsListItemBase>();
                var target = section;
                item.openScreenOnClick = _sections[section];
                item.OnItemClicked = Safe($"opening {label}", () => Fill(target));
                entries.Add(item);
            }

            UseItems(screen, entries);
            return screen;
        }

        // A section: a copy of the Stats screen as a plain scrolling list, every stat item gone.
        private static PhoneOsScreenScrollList CopyStatsScreen(PhoneOsScreenStats stats, Section section, string title, List<GameObject> created)
        {
            var go = Object.Instantiate(stats.gameObject, stats.transform.parent, false);
            created.Add(go);
            go.name = Name + section;
            SetTitle(go, title);
            var copy = go.GetComponent<PhoneOsScreenStats>();
            var scrollBar = copy.scrollBar;
            var slotsRoot = copy.itemsSlotsRoot;
            Object.DestroyImmediate(copy);
            var screen = go.AddComponent<PhoneOsScreenScrollList>();
            screen.scrollBar = scrollBar;
            screen.itemsSlotsRoot = slotsRoot;

            foreach (var item in go.GetComponentsInChildren<PhoneOsListItemBase>(true).ToArray())
                Object.DestroyImmediate(item.gameObject);

            UseItems(screen, new List<PhoneOsListItemBase>());
            _canvas.allScreens.Add(screen);
            return screen;
        }

        // From the live Stats rows (its items, wherever the scroll list has put them): a stat
        // row with a name and a value (the main stats are progress bars with no name), the
        // separator line, and the colors the theme gave them.
        private static bool TakeTemplates(PhoneOsScreenStats stats)
        {
            var items = stats.items?.ToArray().Where(i => i != null).ToList() ?? new List<PhoneOsListItemBase>();
            var row = items.Select(i => i.TryCast<PhoneOsListItemStat>()).FirstOrDefault(r => r != null && LabelText(r) != null);
            var separator = items.Where(i => i.TryCast<PhoneOsListItemStat>() == null)
                .Select(i => i.TryCast<PhoneOsListItemSimpleButton>()).FirstOrDefault(b => b != null);
            if (row == null || separator == null) return false;

            _defaultColor = row.defaultColor;
            _selectedColor = row.selectedColor;
            if (_rowTemplate == null) _rowTemplate = KeepAsTemplate(Object.Instantiate(row.gameObject));
            if (_separatorTemplate == null) _separatorTemplate = KeepAsTemplate(Object.Instantiate(separator.gameObject));
            return true;
        }

        // Kept out of every screen, so no screen counts it as one of its rows.
        private static GameObject KeepAsTemplate(GameObject go)
        {
            go.transform.SetParent(null, false);
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go;
        }

        // Gives a screen its rows and initializes it as the canvas does its own (InternalInit:
        // Init, then hidden).
        private static void UseItems(PhoneOsScreenListBase screen, List<PhoneOsListItemBase> rows)
        {
            var items = new Il2CppSystem.Collections.Generic.List<PhoneOsListItemBase>();
            foreach (var row in rows) items.Add(row);
            screen.cacheWithGetComponent = false;
            screen.items = items;
            screen.firstSelection = rows.FirstOrDefault(r => r.CanBeSelected);
            screen.selectedItem = null;
            screen.gameObject.SetActive(true);
            screen.Init(_canvas);
            screen.SetActiveState(false);
        }

        // ── Sections ────────────────────────────────────────────────────

        // Rebuilt every time a section is opened, before the phone shows it. A section changed
        // while it is shown is rebuilt and shown again with row `select` selected.
        private static void Fill(Section section, int select = -1)
        {
            if (!_sections.TryGetValue(section, out var screen) || screen == null || _rowTemplate == null || _separatorTemplate == null) return;
            _confirmReset = null;
            CancelCapture();
            if (_sectionRows.TryGetValue(section, out var old))
                foreach (var go in old)
                    if (go != null) Object.DestroyImmediate(go);

            var rows = new List<PhoneOsListItemBase>();
            Transform parent = screen.transform;
            try
            {
                switch (section)
                {
                    case Section.Levels:
                        AddTeleportRows(rows, parent, TeleportTargets.Levels);
                        break;
                    case Section.Npcs:
                        AddTeleportRows(rows, parent, TeleportTargets.Npcs);
                        break;
                    case Section.BestTimes:
                        AddBestTimeRows(rows, parent);
                        break;
                    case Section.Hotkeys:
                        AddHotkeyRows(rows, parent);
                        break;
                }
            }
            catch
            {
                // Rows the scroll list never got would stay piled up on the screen.
                foreach (var row in rows)
                    if (row != null) Object.DestroyImmediate(row.gameObject);
                UseItems(screen, new List<PhoneOsListItemBase>());
                throw;
            }
            _sectionRows[section] = rows.Select(r => r.gameObject).ToList();
            UseItems(screen, rows);
            if (select < 0) return;
            // Shown again as the phone shows a screen: SetActiveState(true) selects firstSelection.
            var first = screen.firstSelection;
            screen.firstSelection = rows.Skip(select).FirstOrDefault(r => r.CanBeSelected) ?? first;
            screen.SetActiveState(true);
            screen.firstSelection = first;
        }

        private static void AddTeleportRows(List<PhoneOsListItemBase> rows, Transform parent, TeleportTarget[] targets)
        {
            if (!TeleportTargets.IsStoryMap())
            {
                rows.Add(Row(parent, "Teleports are for the story map", "", null));
                return;
            }
            foreach (var target in targets)
            {
                var t = target;
                rows.Add(Row(parent, t.Name, "", _ => Teleport(t)));
            }
        }

        private static void AddBestTimeRows(List<PhoneOsListItemBase> rows, Transform parent)
        {
            string mapId = LevelTimer.MapId();
            var infos = PlayerRef.LocalPlayer?.GameRefs?.LevelsInfoManager?.LevelsInfo
                ?.TryCast<Il2CppSystem.Collections.Generic.List<LevelInfoSO>>();
            var levels = new List<LevelInfoSO>();
            for (int i = 0; infos != null && i < infos.Count; i++)
                if (infos[i] != null) levels.Add(infos[i]);
            levels.Sort((a, b) => a.Id.CompareTo(b.Id));

            foreach (var info in levels)
            {
                int id = info.Id;
                string name = TeleportTargets.LevelName(info);
                string label = $"{id}. {name}";
                bool has = ModConfig.TryGetPb(mapId, id, name, out double time);
                rows.Add(Row(parent, label, has ? LevelTimer.Format(time, false) : "--:--", row =>
                {
                    if (!ModConfig.TryGetPb(mapId, id, name, out _)) return;
                    if (_confirmReset != label)
                    {
                        _confirmReset = label;
                        SetValue(row, "Again to reset");
                        return;
                    }
                    ModConfig.ResetPb(mapId, id, name);
                    _confirmReset = null;
                    SetValue(row, "--:--");
                    SpeedrunPlugin.Logger.LogInfo($"Best time of {label} reset.");
                }));
            }

            // The game keeps each challenge's best time in the save.
            rows.Add(Separator(parent));
            var npcs = new List<(float height, string id, bool has, double time)>();
            foreach (var npc in Object.FindObjectsOfType<InteractableNpc>(true))
            {
                var challenge = npc.useChallengeController ? npc.challengeController : null;
                if (challenge == null) continue;
                npcs.Add((npc.transform.position.y, TeleportTargets.CleanNpcName(npc.gameObject.name),
                    challenge.HasBestTime, challenge.BestTime));
            }
            foreach (var (_, id, has, time) in TeleportTargets.InNpcOrder(npcs, n => n.id, n => n.height))
                rows.Add(Row(parent, TeleportTargets.NpcName(id), has ? LevelTimer.Format(time, false) : "--:--", null));
        }

        // Every restart bind, then a row to add one, then a hotkey per teleport. Picking a bind
        // row captures a new bind for it (see Capture).
        private static void AddHotkeyRows(List<PhoneOsListItemBase> rows, Transform parent)
        {
            var restart = Hotkeys.Binds(ModConfig.Restart);
            for (int i = 0; i < restart.Count; i++)
            {
                int index = rows.Count, slot = i;
                rows.Add(Row(parent, "Restart", restart[i].Display, row => StartCapture(row, bind =>
                {
                    var binds = Hotkeys.Binds(ModConfig.Restart).ToList();
                    if (bind == null) binds.RemoveAt(slot);
                    else binds[slot] = bind;
                    SaveRestart(binds);
                    Fill(Section.Hotkeys, index);
                })));
            }
            int addIndex = rows.Count;
            rows.Add(Row(parent, "Add restart bind", "+", row => StartCapture(row, bind =>
            {
                if (bind == null) return;
                SaveRestart(Hotkeys.Binds(ModConfig.Restart).Append(bind));
                Fill(Section.Hotkeys, addIndex);
            })));

            if (!TeleportTargets.IsStoryMap()) return;
            foreach (var group in new[] { TeleportTargets.Levels, TeleportTargets.Npcs })
            {
                rows.Add(Separator(parent));
                foreach (var target in group)
                {
                    var entry = ModConfig.TeleportHotkey(target);
                    int index = rows.Count;
                    var binds = Hotkeys.Binds(entry);
                    rows.Add(Row(parent, target.Name, binds.Count > 0 ? binds[0].Display : Unbound, row => StartCapture(row, bind =>
                    {
                        entry.Value = bind?.ToString() ?? "";
                        SpeedrunPlugin.Logger.LogInfo($"Hotkey of {target.Name}: {(entry.Value.Length > 0 ? entry.Value : "none")}.");
                        Fill(Section.Hotkeys, index);
                    })));
                }
            }
        }

        private const string Unbound = "--";

        // A bind already there isn't added twice.
        private static void SaveRestart(IEnumerable<Bind> binds)
        {
            var unique = new List<Bind>();
            foreach (var bind in binds)
                if (!unique.Any(b => b.ToString() == bind.ToString())) unique.Add(bind);
            ModConfig.Restart.Value = Bind.JoinList(unique);
            SpeedrunPlugin.Logger.LogInfo($"Restart binds: {(unique.Count > 0 ? ModConfig.Restart.Value : "none")}.");
        }

        private static void Teleport(TeleportTarget target)
        {
            _pendingTeleport = target;
            PlayerRef.LocalPlayer?.PlayerPhoneOs?.TryToExitPhoneOsMode();
        }

        // ── Rows ────────────────────────────────────────────────────────

        private static PhoneOsListItemStat Row(Transform parent, string label, string value, Action<PhoneOsListItemStat> onClick)
        {
            var go = Object.Instantiate(_rowTemplate, parent, false);
            go.name = Name + "Row";
            go.hideFlags = HideFlags.None;
            go.SetActive(true);
            var row = go.GetComponent<PhoneOsListItemStat>();
            row.isSeparator = false;
            if (row.progressFillImage != null) row.progressFillImage.gameObject.SetActive(false);

            // The row gets texts of its own, copies of its name and value texts with nothing of
            // the game's on them: its localization writes the name back (to the stat's) every
            // time the scroll list shows the row again. The originals are hidden.
            var labelSource = LabelText(row);
            var valueSource = row.valueText;
            var labelText = OwnText(row, labelSource, LabelName);
            if (valueSource != null) OwnText(row, valueSource, ValueName);
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true).ToArray())
                if (!text.gameObject.name.StartsWith(Name)) text.gameObject.SetActive(false);
            labelText.text = label;
            SetValue(row, value);
            Theme(row);
            row.SetSelectedState(false);
            row.SetLockedState(false);
            row.SetInteractableState(true);
            if (onClick != null) row.OnItemClicked = Safe($"picking {label}", () => onClick(row));
            return row;
        }

        // The Stats screen's separator line, which the selection skips.
        private static PhoneOsListItemBase Separator(Transform parent)
        {
            var go = Object.Instantiate(_separatorTemplate, parent, false);
            go.name = Name + "Separator";
            go.hideFlags = HideFlags.None;
            go.SetActive(true);
            var item = go.GetComponent<PhoneOsListItemSimpleButton>();
            item.isSeparator = true;
            Theme(item);
            return item;
        }

        // The colors ApplyTheme gives the game's rows; without them a copy recolors its texts
        // and lines to transparent as soon as it is selected or deselected.
        // A separator only gets the colors: it has no selected background (selectedBg), which
        // SetSelectedState shows or hides without checking, so it is never selected, in the
        // game or here.
        private static void Theme(PhoneOsListItemSimpleButton item)
        {
            item.defaultColor = _defaultColor;
            item.selectedColor = _selectedColor;
        }

        // The stat row's name text: its text that isn't the value (null for a main stat).
        private static TMP_Text LabelText(PhoneOsListItemStat row)
        {
            TMP_Text label = null;
            foreach (var text in row.GetComponentsInChildren<TMP_Text>(true).ToArray())
            {
                if (row.valueText != null && text.Pointer == row.valueText.Pointer) continue;
                if (text.gameObject.name == "StatLabelText") return text;
                label ??= text;
            }
            return label;
        }

        // A plain copy of one of the row's texts, recolored with the row when it is selected,
        // like the original.
        private static TMP_Text OwnText(PhoneOsListItemStat row, TMP_Text source, string name)
        {
            var go = Object.Instantiate(source.gameObject, source.transform.parent, false);
            go.name = name;
            foreach (var uiText in go.GetComponents<UIText>().ToArray())
                Object.DestroyImmediate(uiText);
            var text = go.GetComponent<TMP_Text>();
            var recolor = row.recolorGraphics;
            if (recolor != null)
            {
                recolor.Remove(source);
                recolor.Add(text);
            }
            return text;
        }

        private static void SetValue(PhoneOsListItemStat row, string value) => SetOwnText(row, ValueName, value);

        private static void SetOwnText(PhoneOsListItemStat row, string name, string value)
        {
            var text = row?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.name == name);
            if (text != null) text.text = value;
        }

        private static string GetOwnText(PhoneOsListItemStat row, string name)
            => row?.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault(t => t.gameObject.name == name)?.text ?? "";

        // ── Bind capture ────────────────────────────────────────────────

        // Picking a bind row waits for keys and gamepad buttons held together: everything
        // pressed until all of it is let go is the new bind. It first waits for the key or
        // button that picked the row to be let go. Esc cancels, Delete removes the bind, and
        // ten seconds with nothing pressed cancel (for a gamepad alone). The phone's own
        // navigation (its InputActionSOs, read in PhoneOsCanvas.Update) is switched off
        // meanwhile, so the buttons pressed don't also move the selection or leave the screen.
        private const float CaptureTimeout = 10f;

        private static PhoneOsListItemStat _captureRow;
        private static Action<Bind> _captureDone;
        private static string _captureLabel;
        private static string _captureValue;
        private static bool _captureArmed;
        private static float _captureIdleSince;
        private static readonly List<string> _captured = new List<string>();
        private static readonly List<InputAction> _pausedNavigation = new List<InputAction>();

        // `done` gets the new bind, or null to remove it.
        private static void StartCapture(PhoneOsListItemStat row, Action<Bind> done)
        {
            CancelCapture();
            _captureRow = row;
            _captureDone = done;
            _captureLabel = GetOwnText(row, LabelName);
            _captureValue = GetOwnText(row, ValueName);
            _captureArmed = false;
            _captured.Clear();
            SetOwnText(row, LabelName, "Esc cancels, Del removes");
            SetValue(row, "Press...");
            PauseNavigation(true);
        }

        private static void Capture(PlayerPhoneOs phone)
        {
            if (!phone.IsPhoneOsActive || _captureRow == null || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelCapture();
                return;
            }
            if (Input.GetKeyDown(KeyCode.Delete))
            {
                EndCapture(null);
                return;
            }

            var held = Controls.HeldNow().ToList();
            if (!_captureArmed)
            {
                if (held.Count > 0) return;
                _captureArmed = true;
                _captureIdleSince = Time.unscaledTime;
                return;
            }
            if (held.Count > 0)
            {
                foreach (var name in held)
                    if (!_captured.Contains(name)) _captured.Add(name);
                SetValue(_captureRow, new Bind(_captured).Display);
                return;
            }
            if (_captured.Count > 0) EndCapture(new Bind(_captured));
            else if (Time.unscaledTime - _captureIdleSince > CaptureTimeout) CancelCapture();
        }

        private static void EndCapture(Bind bind)
        {
            var done = _captureDone;
            CancelCapture();
            done(bind);
        }

        private static void CancelCapture()
        {
            if (_captureRow == null) return;
            SetOwnText(_captureRow, LabelName, _captureLabel);
            SetValue(_captureRow, _captureValue);
            _captureRow = null;
            _captureDone = null;
            _captured.Clear();
            PauseNavigation(false);
        }

        // Only the actions it switched off are switched back on: the canvas switches them all
        // off itself when the phone closes.
        private static void PauseNavigation(bool pause)
        {
            if (!pause)
            {
                foreach (var action in _pausedNavigation)
                    if (action != null) action.Enable();
                _pausedNavigation.Clear();
                return;
            }
            if (_canvas == null) return;
            foreach (var so in new[] { _canvas.up, _canvas.down, _canvas.left, _canvas.right, _canvas.back, _canvas.select })
            {
                var action = so?.InputAction;
                if (action == null || !action.enabled) continue;
                action.Disable();
                _pausedNavigation.Add(action);
            }
        }

        // The copy's first text; its localization is removed so the game doesn't write its own
        // name back.
        private static void SetLabel(GameObject go, string label)
        {
            RemoveLocalization(go);
            var text = go.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
            if (text != null) text.text = label;
        }

        // The screen's title (the copy of "Stats").
        private static void SetTitle(GameObject screen, string title)
        {
            foreach (var text in screen.GetComponentsInChildren<TMP_Text>(true).ToArray())
            {
                if (!text.gameObject.name.StartsWith("PhoneOsUI_TitleText")) continue;
                RemoveLocalization(text.gameObject);
                text.text = title;
            }
        }

        private static void RemoveLocalization(GameObject go)
        {
            foreach (var localized in go.GetComponentsInChildren<LocalizedUIText>(true).ToArray())
                Object.DestroyImmediate(localized);
            foreach (var localized in go.GetComponentsInChildren<LocalizedCompositeUIText>(true).ToArray())
                Object.DestroyImmediate(localized);
        }

        // The game calls these back from its own code: an error is logged, never thrown into it.
        private static Il2CppSystem.Action Safe(string what, Action action) => (Action)(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogError($"Phone app: error {what}: {ex}");
            }
        });
    }
}

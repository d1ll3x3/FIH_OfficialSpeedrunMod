using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;
using EHS;
using EHS.Interactables;

namespace OfficialSpeedrunMod
{
    // Three tabs. Teleports is the trainer's Teleports tab on its own: two columns, levels and
    // NPCs, of buttons, under the current spawn (see Restart). Best times lists this mod's
    // best time of every level and the game's own best time of every NPC challenge. Hotkeys
    // binds the restart key and a key or combo to each teleport (see Hotkeys). B (or LB + RB
    // on a gamepad) opens and closes it; a click, or A on the focused button, closes it and
    // teleports there.
    public class TeleportMenu
    {
        private const KeyCode ToggleKey = KeyCode.B;

        // Layout, in unscaled units (the whole menu is scaled to the screen via GUI.matrix)
        private const float W        = 760f;
        private const float PAD      = 22f;
        private const float HEADER_H = 66f;
        private const float CAPTION_H = 24f;
        private const float CONTROLS_H = 44f;
        private const float ROW_H    = 28f;
        private const float GAP      = 20f;
        private const float HINT_H   = 24f;
        private const float BUTTON_H = 36f;
        // Room for the "Load into a level first." line when there are no columns.
        private const int MIN_ROWS   = 2;
        private const float TAB_W    = 110f;

        private const int TabTeleports = 0;
        private const int TabBestTimes = 1;
        private const int TabHotkeys   = 2;

        private bool _isVisible;
        private int _tab = TabTeleports;
        private Vector2 _panelPos;
        private bool _needsCentering = true;
        private float _uiScale = 1f;
        private float _height;
        private bool _dragging;
        private Vector2 _dragOffset;

        private GUIStyle _styleTitle;
        private GUIStyle _styleSubtitle;
        private GUIStyle _styleCaption;
        private GUIStyle _styleLabel;
        private GUIStyle _styleButton;
        private GUIStyle _styleTime;
        private bool _stylesReady;
        private bool _clickHandledThisFrame;

        // Gamepad focus: column (0 levels, 1 NPCs) and row within it.
        private int _column;
        private int _row;
        private bool _wasCombo;
        private bool _wasNavUp, _wasNavDown, _wasNavLeft, _wasNavRight;

        // The hotkey waiting for its key (restart or a teleport's), or null.
        private ConfigEntry<string> _capturing;

        // The Best times tab, read when the menu opens: one row per level of the loaded map and
        // per NPC challenge.
        private readonly List<(string name, bool has, double time)> _levelTimes = new List<(string, bool, double)>();
        private readonly List<(string name, bool has, double time)> _challengeTimes = new List<(string, bool, double)>();

        // Carried out once the menu is closed and the player unfrozen: the freeze would pin a
        // teleported player back.
        private TeleportTarget _pendingTeleport;

        // The player is frozen while the menu is open so no input source can move it.
        private Rigidbody _frozenRb;
        private Vector3 _frozenPosition;
        private Quaternion _frozenRotation;
        private Vector3 _frozenLinVel;
        private Vector3 _frozenAngVel;
        private bool _frozenKinematic;
        private readonly List<MonoBehaviour> _disabledScripts = new List<MonoBehaviour>();

        public void Update()
        {
            if (_capturing != null)
            {
                ApplyFreeze();
                CaptureHotkey();
                return;
            }

            // A keyboard B and the gamepad combo both toggle it; the combo fires once per press.
            var gp = Gamepad.current;
            bool combo = gp != null && gp.leftShoulder.isPressed && gp.rightShoulder.isPressed;
            bool toggle = Application.isFocused && (Input.GetKeyDown(ToggleKey) || (combo && !_wasCombo));
            _wasCombo = combo;

            if (toggle)
            {
                if (_isVisible) Close();
                else Open();
            }
            else if (_isVisible && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
            }

            if (_isVisible)
            {
                ApplyFreeze();
                if (gp != null && _tab == TabTeleports) HandleGamepad(gp);
            }

            var pending = _pendingTeleport;
            _pendingTeleport = null;
            if (pending != null) Restart.GoTo(pending);
        }

        public bool IsOpen => _isVisible;

        private void Open()
        {
            // Rebuilt on opening: a list built while the level was still spawning its objects
            // could be missing some.
            TeleportTargets.Refresh(force: true);
            RefreshBestTimes();
            _isVisible = true;
            _needsCentering = true;
            _column = 0;
            _row = 0;

            DisableGameScripts();
            SetInputDevicesEnabled(false);
            BeginFreeze();
        }

        private void Close()
        {
            _isVisible = false;
            _dragging = false;
            _capturing = null;

            EndFreeze();
            SetInputDevicesEnabled(true);
            EnableGameScripts();

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Pick(TeleportTarget target)
        {
            _pendingTeleport = target;
            Close();
        }

        // Legacy Input, through Hotkeys.TryCapture: the menu disables the Input System's
        // keyboard device. Esc cancels.
        private void CaptureHotkey()
        {
            if (!Hotkeys.TryCapture(out Hotkey hotkey)) return;
            if (!hotkey.IsNone)
            {
                _capturing.Value = hotkey.ToString();
                SpeedrunPlugin.Logger.LogInfo($"Hotkey {_capturing.Definition.Key} bound to {_capturing.Value}.");
            }
            _capturing = null;
        }

        private void RefreshBestTimes()
        {
            _levelTimes.Clear();
            string mapId = LevelTimer.MapId();
            var infos = PlayerRef.LocalPlayer?.GameRefs?.LevelsInfoManager?.LevelsInfo
                ?.TryCast<Il2CppSystem.Collections.Generic.List<LevelInfoSO>>();
            var levels = new List<LevelInfoSO>();
            for (int i = 0; infos != null && i < infos.Count; i++)
                if (infos[i] != null) levels.Add(infos[i]);
            levels.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var info in levels)
            {
                string name = TeleportTargets.LevelName(info);
                bool has = ModConfig.TryGetPb(mapId, info.Id, name, out double time);
                _levelTimes.Add(($"{info.Id}. {name}", has, time));
            }

            // The game keeps each challenge's best time in the save; bottom to top, like the
            // NPC teleports.
            _challengeTimes.Clear();
            var npcs = new List<(float height, string name, bool has, double time)>();
            foreach (var npc in UnityEngine.Object.FindObjectsOfType<InteractableNpc>(true))
            {
                var challenge = npc.useChallengeController ? npc.challengeController : null;
                if (challenge == null) continue;
                npcs.Add((npc.transform.position.y, TeleportTargets.CleanNpcName(npc.gameObject.name),
                    challenge.HasBestTime, challenge.BestTime));
            }
            foreach (var (_, name, has, time) in TeleportTargets.InNpcOrder(npcs, n => n.name, n => n.height))
                _challengeTimes.Add((name, has, time));
        }

        private static TeleportTarget[] Column(int column)
            => column == 0 ? TeleportTargets.Levels : TeleportTargets.Npcs;

        private static bool CanTeleport() => TeleportTargets.IsPlayerReady() && TeleportTargets.IsStoryMap();

        // ── Input ───────────────────────────────────────────────────────────

        private void HandleGamepad(Gamepad gp)
        {
            bool navUp    = gp.dpad.up.isPressed    || gp.leftStick.y.ReadValue() >  0.7f;
            bool navDown  = gp.dpad.down.isPressed  || gp.leftStick.y.ReadValue() < -0.7f;
            bool navLeft  = gp.dpad.left.isPressed  || gp.leftStick.x.ReadValue() < -0.7f;
            bool navRight = gp.dpad.right.isPressed || gp.leftStick.x.ReadValue() >  0.7f;

            int count = Column(_column).Length;
            if (navUp   && !_wasNavUp   && count > 0) _row = (_row - 1 + count) % count;
            if (navDown && !_wasNavDown && count > 0) _row = (_row + 1) % count;
            if ((navLeft && !_wasNavLeft) || (navRight && !_wasNavRight))
            {
                _column = 1 - _column;
                _row = Mathf.Min(_row, Mathf.Max(0, Column(_column).Length - 1));
            }
            _wasNavUp    = navUp;
            _wasNavDown  = navDown;
            _wasNavLeft  = navLeft;
            _wasNavRight = navRight;

            var targets = Column(_column);
            if (gp.buttonSouth.wasPressedThisFrame && _row < targets.Length && CanTeleport())
                Pick(targets[_row]);
            else if (gp.buttonEast.wasPressedThisFrame)
                Close();
        }

        // ── Freeze ──────────────────────────────────────────────────────────

        private void BeginFreeze()
        {
            _frozenRb = PlayerRef.LocalPlayer?.Rb;
            if (_frozenRb == null) return;
            _frozenPosition = _frozenRb.position;
            _frozenRotation = _frozenRb.rotation;
            _frozenLinVel = _frozenRb.linearVelocity;
            _frozenAngVel = _frozenRb.angularVelocity;
            _frozenKinematic = _frozenRb.isKinematic;
            _frozenRb.isKinematic = true;
        }

        // The body is kinematic while frozen, so only its position and rotation are pinned.
        private void ApplyFreeze()
        {
            if (_frozenRb == null) return;
            _frozenRb.position = _frozenPosition;
            _frozenRb.rotation = _frozenRotation;
            _frozenRb.transform.SetPositionAndRotation(_frozenPosition, _frozenRotation);
        }

        private void EndFreeze()
        {
            if (_frozenRb != null)
            {
                _frozenRb.isKinematic = _frozenKinematic;
                _frozenRb.linearVelocity = _frozenLinVel;
                _frozenRb.angularVelocity = _frozenAngVel;
            }
            _frozenRb = null;
        }

        private static void SetInputDevicesEnabled(bool enabled)
        {
            try
            {
                foreach (InputDevice device in new InputDevice[] { Keyboard.current, Mouse.current })
                {
                    if (device == null) continue;
                    if (enabled) InputSystem.EnableDevice(device);
                    else InputSystem.DisableDevice(device);
                }
            }
            catch (Exception ex)
            {
                SpeedrunPlugin.Logger.LogWarning($"Could not {(enabled ? "enable" : "disable")} the keyboard and mouse: {ex.Message}");
            }
        }

        // Input and pause scripts, and the player's own ones, so the game ignores the menu's
        // input. Camera scripts are never touched: disabling the Cinemachine camera made it cut
        // to another one in the level.
        private void DisableGameScripts()
        {
            _disabledScripts.Clear();
            foreach (var comp in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (comp == null || !comp.enabled || IsOwn(comp)) continue;
                string ns = comp.GetType().Namespace ?? "";
                string name = comp.GetType().Name.ToLower();
                if (ns.StartsWith("UnityEngine.InputSystem") || name.Contains("input") || name.Contains("pause"))
                {
                    comp.enabled = false;
                    _disabledScripts.Add(comp);
                }
            }

            var rb = PlayerRef.LocalPlayer?.Rb;
            if (rb == null) return;
            foreach (var comp in rb.gameObject.GetComponents<MonoBehaviour>())
            {
                if (comp == null || !comp.enabled || IsOwn(comp)) continue;
                if ((comp.GetType().Namespace ?? "").StartsWith("UnityEngine")) continue;
                comp.enabled = false;
                if (!_disabledScripts.Contains(comp)) _disabledScripts.Add(comp);
            }
        }

        private void EnableGameScripts()
        {
            foreach (var comp in _disabledScripts)
                if (comp != null) comp.enabled = true;
            _disabledScripts.Clear();
        }

        private static bool IsOwn(MonoBehaviour comp) => comp.GetType().Namespace == typeof(TeleportMenu).Namespace;

        // ── Drawing ─────────────────────────────────────────────────────────

        public void Draw()
        {
            if (!_isVisible) return;

            if (Event.current.type == EventType.Repaint)
                _clickHandledThisFrame = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            InitStyles();

            bool canTeleport = CanTeleport();
            // Not Mathf.Max(params int[]): that overload is stripped from the interop build.
            int rows;
            float controls = 0f;
            if (_tab == TabBestTimes)
                rows = Math.Max(MIN_ROWS, Math.Max(_levelTimes.Count, _challengeTimes.Count));
            else
            {
                rows = canTeleport ? Math.Max(MIN_ROWS, Math.Max(TeleportTargets.Levels.Length, TeleportTargets.Npcs.Length)) : MIN_ROWS;
                controls = CONTROLS_H;
            }
            _height = HEADER_H + controls + 14f + CAPTION_H + ROW_H * rows + 12f + HINT_H + BUTTON_H + PAD;

            // Scale with the resolution (designed at 1080p) and always fit on screen.
            _uiScale = Math.Min(Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f),
                                Math.Min(Screen.height * 0.94f / _height, Screen.width * 0.96f / W));
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_uiScale, _uiScale, 1f));

            float viewW = Screen.width / _uiScale;
            float viewH = Screen.height / _uiScale;
            if (_needsCentering)
            {
                _panelPos = new Vector2((viewW - W) * 0.5f, (viewH - _height) * 0.5f);
                _needsCentering = false;
            }

            Theme.FillRect(new Rect(0f, 0f, viewW, viewH), Theme.Backdrop);
            if (Event.current.type == EventType.Repaint)
                HandleDrag(viewW, viewH);
            DrawPanel(canTeleport);

            GUI.matrix = oldMatrix;
        }

        private void InitStyles()
        {
            if (_stylesReady) return;
            _styleTitle    = Theme.MakeLabel(20, FontStyle.Bold);
            _styleSubtitle = Theme.MakeLabel(12);
            _styleCaption  = Theme.MakeLabel(11, FontStyle.Bold);
            _styleLabel    = Theme.MakeLabel(14);
            _styleButton   = Theme.MakeLabel(13, FontStyle.Bold, TextAnchor.MiddleCenter);
            _styleTime     = Theme.MakeLabel(13, FontStyle.Bold, TextAnchor.MiddleRight);
            _stylesReady = true;
        }

        private void DrawPanel(bool canTeleport)
        {
            float px = _panelPos.x;
            float py = _panelPos.y;
            bool gamepadActive = Gamepad.current != null;

            Theme.DrawPanel(new Rect(px, py, W, _height), Theme.Panel);

            // ── Header ───────────────────────────────────────────────────
            Theme.Fill(new Rect(px + PAD, py + 18f, 4f, 30f), Theme.Accent, true);
            string title = _tab == TabBestTimes ? "Best times" : _tab == TabHotkeys ? "Hotkeys" : "Teleports";
            string subtitle = _tab == TabBestTimes ? "Best time on every level and NPC challenge"
                : _tab == TabHotkeys ? "Keys and combos for the restart and each teleport"
                : "Click a level or an NPC to go there";
            float tabX = CloseRect().x - 12f - TAB_W * 3f - 12f;
            Theme.Label(new Rect(px + PAD + 14f, py + 12f, tabX - px - PAD - 22f, 26f), title, _styleTitle, Theme.Text);
            Theme.Label(new Rect(px + PAD + 14f, py + 37f, tabX - px - PAD - 22f, 18f), subtitle, _styleSubtitle, Theme.TextDim);
            if (Button(new Rect(tabX, py + 18f, TAB_W, 30f), "Teleports", _tab == TabTeleports, Theme.Accent))
                _tab = TabTeleports;
            if (Button(new Rect(tabX + TAB_W + 6f, py + 18f, TAB_W, 30f), "Best times", _tab == TabBestTimes, Theme.Pb))
                _tab = TabBestTimes;
            if (Button(new Rect(tabX + (TAB_W + 6f) * 2f, py + 18f, TAB_W, 30f), "Hotkeys", _tab == TabHotkeys, Theme.Npc))
                _tab = TabHotkeys;
            if (Button(CloseRect(), "X", false, Theme.Danger))
                Close();
            Theme.FillRect(new Rect(px, py + HEADER_H, W, 1f), Theme.Divider);

            if (_tab == TabBestTimes)
            {
                DrawBestTimes(px, py);
                return;
            }
            if (_tab == TabHotkeys)
            {
                DrawHotkeys(px, py, canTeleport);
                return;
            }

            // ── Spawn ────────────────────────────────────────────────────
            string spawn = Restart.Spawn != null ? Restart.Spawn.Name : "none yet";
            string restart = ModConfig.RestartKey.Value.Length > 0 ? $"  ·  {ModConfig.RestartKey.Value} takes you back" : "";
            Theme.Label(new Rect(px + PAD, py + HEADER_H + 8f, W - PAD * 2f, ROW_H), $"Spawn: {spawn}{restart}",
                _styleLabel, Theme.TextDim);
            Theme.FillRect(new Rect(px, py + HEADER_H + CONTROLS_H, W, 1f), Theme.Divider);

            // ── Columns ──────────────────────────────────────────────────
            float y = py + HEADER_H + CONTROLS_H + 14f;
            float colW = (W - PAD * 2f - GAP) * 0.5f;
            if (!canTeleport)
            {
                Theme.Label(new Rect(px + PAD, y, W - PAD * 2f, 24f),
                    TeleportTargets.IsPlayerReady() ? "Teleports are for the story map." : "Load into a level first.",
                    _styleLabel, Theme.TextDim);
            }
            else
            {
                DrawColumn(0, "LEVELS", px + PAD, y, colW, gamepadActive);
                DrawColumn(1, "NPCS", px + PAD + colW + GAP, y, colW, gamepadActive);
            }

            DrawFooter(py, gamepadActive
                ? "D-Pad navigate  ·  A teleport  ·  B close"
                : "The menu closes and you land there  ·  B or Esc closes");
        }

        private void DrawBestTimes(float px, float py)
        {
            float y = py + HEADER_H + 14f;
            float colW = (W - PAD * 2f - GAP) * 0.5f;
            DrawTimes("LEVELS", Theme.Accent, _levelTimes, px + PAD, y, colW);
            DrawTimes("NPC CHALLENGES", Theme.Npc, _challengeTimes, px + PAD + colW + GAP, y, colW);
            DrawFooter(py, "Level times are saved by this mod; challenge times are the game's own");
        }

        private void DrawHotkeys(float px, float py, bool canTeleport)
        {
            DrawHotkey(ModConfig.RestartKey, "Restart: back to the spawn", Theme.Accent, px + PAD, py + HEADER_H + 8f, W - PAD * 2f);
            Theme.FillRect(new Rect(px, py + HEADER_H + CONTROLS_H, W, 1f), Theme.Divider);

            float y = py + HEADER_H + CONTROLS_H + 14f;
            float colW = (W - PAD * 2f - GAP) * 0.5f;
            if (!canTeleport)
            {
                Theme.Label(new Rect(px + PAD, y, W - PAD * 2f, 24f),
                    TeleportTargets.IsPlayerReady() ? "Teleports are for the story map." : "Load into a level first.",
                    _styleLabel, Theme.TextDim);
            }
            else
            {
                DrawHotkeyColumn("LEVELS", Theme.Accent, TeleportTargets.Levels, px + PAD, y, colW);
                DrawHotkeyColumn("NPCS", Theme.Npc, TeleportTargets.Npcs, px + PAD + colW + GAP, y, colW);
            }
            DrawFooter(py, _capturing != null
                ? "Press the key, holding Ctrl, Shift or Alt for a combo  ·  Esc cancels"
                : "Click a key to change it  ·  × clears it  ·  They work while playing");
        }

        private void DrawHotkeyColumn(string caption, Color color, TeleportTarget[] targets, float x, float y, float w)
        {
            Theme.Label(new Rect(x, y, w, 18f), caption, _styleCaption, color);
            y += CAPTION_H;
            foreach (var target in targets)
            {
                DrawHotkey(ModConfig.TeleportHotkey(target), target.Name, color, x, y, w);
                y += ROW_H;
            }
        }

        // A hotkey row: the name, the key (click to rebind) and × to clear it.
        private void DrawHotkey(ConfigEntry<string> entry, string name, Color color, float x, float y, float w)
        {
            const float KeyW = 136f, ClearW = 24f;
            Theme.Label(new Rect(x, y, w - KeyW - ClearW - 12f, ROW_H - 4f), name, _styleLabel, Theme.Text);
            bool capturing = _capturing == entry;
            string key = capturing ? "Press a key…" : entry.Value.Length > 0 ? entry.Value : "—";
            if (Button(new Rect(x + w - KeyW - ClearW - 4f, y, KeyW, ROW_H - 4f), key, capturing, color))
                _capturing = entry;
            if (Button(new Rect(x + w - ClearW, y, ClearW, ROW_H - 4f), "×", false, Theme.Danger))
                entry.Value = "";
        }

        private void DrawFooter(float py, string hint)
        {
            float px = _panelPos.x;
            float by = py + _height - PAD - BUTTON_H;
            Theme.Label(new Rect(px + PAD, by - HINT_H, W - PAD * 2f, HINT_H), hint, _styleSubtitle, Theme.TextFaint);
            if (Button(new Rect(px + W - PAD - 130f, by, 130f, BUTTON_H), "Close", false, Theme.Accent))
                Close();
        }

        private void DrawColumn(int column, string caption, float x, float y, float w, bool gamepadActive)
        {
            Color color = column == 0 ? Theme.Accent : Theme.Npc;
            Theme.Label(new Rect(x, y, w, 18f), caption, _styleCaption, color);
            y += CAPTION_H;
            var targets = Column(column);
            for (int i = 0; i < targets.Length; i++)
            {
                bool selected = gamepadActive && _column == column && _row == i;
                if (Button(new Rect(x, y, w, ROW_H - 4f), targets[i].Name, selected, color))
                    Pick(targets[i]);
                y += ROW_H;
            }
        }

        private void DrawTimes(string caption, Color color, List<(string name, bool has, double time)> rows,
            float x, float y, float w)
        {
            Theme.Label(new Rect(x, y, w, 18f), caption, _styleCaption, color);
            y += CAPTION_H;
            if (rows.Count == 0)
                Theme.Label(new Rect(x, y, w, ROW_H - 4f), "Load into a level first.", _styleLabel, Theme.TextDim);
            foreach (var (name, has, time) in rows)
            {
                var row = new Rect(x, y, w, ROW_H - 4f);
                Theme.Fill(row, Theme.Chip, true);
                Theme.Label(new Rect(row.x + 10f, row.y, row.width - 120f, row.height), name, _styleLabel, Theme.Text);
                Theme.Label(new Rect(row.xMax - 110f, row.y, 100f, row.height), has ? LevelTimer.Format(time, false) : "--:--",
                    _styleTime, has ? Theme.Pb : Theme.TextFaint);
                y += ROW_H;
            }
        }

        // Rounded button: a subtle surface that takes the hover color on mouse-over. Selected
        // (gamepad focus) draws an accent ring.
        private bool Button(Rect r, string text, bool selected, Color hoverColor)
        {
            bool hover = r.Contains(MouseGui());
            if (selected)
                Theme.Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Theme.Accent, true);
            Color bg = hover ? new Color(hoverColor.r, hoverColor.g, hoverColor.b, 0.22f) : Theme.Chip;
            Color fg = hover ? hoverColor : Theme.Text;
            Theme.DrawChip(r, text, _styleButton, bg, fg);
            return Clicked(r);
        }

        private void HandleDrag(float viewW, float viewH)
        {
            Vector2 mouse = MouseGui();
            var header = new Rect(_panelPos.x, _panelPos.y, W, HEADER_H);
            if (!_dragging && Input.GetMouseButtonDown(0) && header.Contains(mouse) && !CloseRect().Contains(mouse))
            {
                _dragging = true;
                _dragOffset = mouse - _panelPos;
            }
            if (_dragging)
            {
                if (Input.GetMouseButton(0)) _panelPos = mouse - _dragOffset;
                else _dragging = false;
            }

            _panelPos.x = Mathf.Clamp(_panelPos.x, 0f, Mathf.Max(0f, viewW - W));
            _panelPos.y = Mathf.Clamp(_panelPos.y, 0f, Mathf.Max(0f, viewH - _height));
        }

        private Rect CloseRect() => new Rect(_panelPos.x + W - PAD - 30f, _panelPos.y + 18f, 30f, 30f);

        // Mouse in menu space. Raw Input instead of IMGUI events: the keyboard and mouse
        // devices of the Input System are disabled while the menu is open.
        private Vector2 MouseGui()
        {
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x, Screen.height - m.y) / _uiScale;
        }

        // IMGUI calls Draw several times a frame; a click counts once, on the Repaint pass.
        private bool Clicked(Rect r)
        {
            if (_clickHandledThisFrame || Event.current.type != EventType.Repaint) return false;
            if (!Input.GetMouseButtonDown(0) || !r.Contains(MouseGui())) return false;
            _clickHandledThisFrame = true;
            return true;
        }
    }
}

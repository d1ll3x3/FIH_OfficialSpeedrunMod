# FIH Official Speedrun Mod

A BepInEx (IL2CPP) plugin for Flipping is Hard for practising levels: the trainer's
teleports (every level start and every NPC of the story map), a restart key, a timer for
each level and the best time of every level.

## Controls

| Action              | Keyboard / mouse | Gamepad      |
|---------------------|------------------|--------------|
| Open / close menu   | B                | LB + RB      |
| Teleport            | Click a button   | D-Pad + A    |
| Close               | B, Esc or Close  | B            |
| Back to the spawn   | R (Hotkeys tab)  |              |

The player is frozen while the menu is open. Picking a target closes the menu and lands
you there with the camera turned the way the level is climbed.

## Spawn and restart

The spawn is the last place the menu or a teleport hotkey took you. On the story map,
levels 2 to 9 land just before the level, so its timer starts as you walk in. The restart
key (R by default) takes you back there, puts the level timer back to 0 and, if an NPC
challenge is running, restarts it at 0 and holds it there until you move. A challenge
whose area the spawn is outside of is cancelled by the game itself.

## Hotkeys

The menu's **Hotkeys** tab binds the restart key and a key to every teleport. Click a key
and press the new one, holding Ctrl, Shift or Alt for a combo (`Ctrl+Alpha1`); Esc
cancels and × clears it. They work while playing, not with the menu open. When several
match, the one with the most modifiers wins, so `R` and `Shift+R` can do different things.
They are saved in `OfficialSpeedrunMod.cfg` (`RestartKey` and the `Hotkeys` section).

## Level timer

Shown under the game's timer, as a copy of it: the level's time, and the level under it.
Times use the game's own clock (real time, paused only while the game is frozen) and
format, with centiseconds.

- A level starts when you enter it: the same zones the game's splits use. Entering the
  next level ends it and starts that one.
- A level the menu teleported you into, and the first level of a new run, start on your
  first move (move, jump or interact), like the game's timer. A level target moved to
  just before its level waits for the level's zone instead.
- The zone a spawn puts you in starts nothing: set the spawn just before a level and its
  timer starts as you walk in.

The best time of every level is saved in `OfficialSpeedrunMod.cfg`, next to the dll
(section `PersonalBests`, one entry per map and level). The menu's **Best times** tab lists
them, with the game's own best time of every NPC challenge.

## Watermark

`OSM` in the top right corner, in the game's HUD font, so recordings show the mod was on.

## Achievements

Teleporting is an advantage, so achievements (and the stats behind them) can't be earned
while this mod is loaded. It uses the game's own achievement block, the one Crowd Control
runs use, with no achievement allowed. Remove the mod to earn them again.

## Install

Copy `OfficialSpeedrunMod.dll` to `Flipping is Hard\BepInEx\plugins\OfficialSpeedrunMod\`.
Requires BepInEx 6 (IL2CPP).

## Build

Copy the game's interop assemblies listed in `OfficialSpeedrunMod.csproj` from
`Flipping is Hard\BepInEx\interop\` into `lib\`, then run `build.bat` (builds and copies
the DLL into the game's plugins folder).

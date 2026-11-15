# FIH Official Speedrun Mod

A BepInEx (IL2CPP) plugin for Flipping is Hard for practising levels: the trainer's
teleports (every level start and every NPC of the story map), a restart key, a timer for
each level and the best time of every level.

## The Speedrun app

The mod's menu is a **Speedrun** app in the game's phone (Tab), right after Stats. It is
made from the phone's own screens, so it looks, navigates and animates like the vanilla
apps, with the keyboard or a gamepad:

- **Levels** and **NPCs**: pick one to close the phone and land there, with the camera
  turned the way the level is climbed.
- **Best times**: your best time on every level, and the game's own best time on every NPC
  challenge. Pick a level twice to reset its time.

The restart key (R by default) takes you back to the spawn while playing.

## Spawn and restart

The spawn is the last place the app or a teleport hotkey took you. On the story map,
levels 1 to 10 land just before the level, so its timer starts as you walk in. The restart
key (R by default) takes you back there, puts the level timer back to 0 and ends the NPC
challenge in progress, the same way quitting it from the NPC does.

## Hotkeys

The restart key and a key for every teleport are set in `OfficialSpeedrunMod.cfg`: a Unity
key name, optionally with Ctrl, Shift and Alt (`R`, `Alpha1`, `Ctrl+F2`). They work while
playing, not with the phone open. When several
match, the one with the most modifiers wins, so `R` and `Shift+R` can do different things.
They are `RestartKey` and the `Hotkeys` section.

## Level timer

Times the level of the spawn you used: pick a level in the app (or with its hotkey) and
that level is timed once, from entering it until you reach the next one. The restart key
arms it again; an NPC teleport or loading a world turns it off. Passing through other
levels never starts their timer or touches their best times.

- The level starts when you enter it, through the same zones the game's splits use. If the
  spawn is already inside it, it starts on your first move (move, jump or interact), like
  the game's timer.
- It is shown on the game's NPC challenge timer, in its place and format, with the level's
  best time; the finished time stays up as long as a challenge's result does. A running
  NPC challenge takes the timer back until it ends.
- Times use the game's own clock: real time, paused only while the game is frozen.
- A fake area trigger, which only shows another area's name (the fake "The End" before the
  boss door), counts as the level it stands in: Recycling Core ends there, and The Server
  Room runs from it to the real "The End". The BepInEx log names the level each fake
  trigger counts as when a world loads.
- While the timer is on, the end of the game ends the level instead of the run: the end
  zone stops the timer and the game's ending doesn't start, so practising never finishes
  the save's run. With the timer off, the game ends as usual.

The best time of every level is saved in `OfficialSpeedrunMod.cfg`, next to the dll
(section `PersonalBests`, one entry per map and level). The app's **Best times** section lists
them, with the game's own best time of every NPC challenge; picking a level twice puts its
time back to none.

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

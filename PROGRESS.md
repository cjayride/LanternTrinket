# Lantern Trinket — heat tooltip hotkey

## Goal
Show the Hip Lantern heat/light keys from Hip Lantern's live config, not a hardcoded LeftAlt hint.

## Status
Completed. 1.0.9 built.

## Cause
The tooltip already tried to read `toggleLanternHeatShortcut`, but lookup ran once. If Hip Lantern was not loaded yet, it kept the hardcoded fallback `H + LeftAlt`. KeyboardShortcut.ToString() also printed `H + LeftAlt` instead of `Alt + H` / `Shift + H`.

## Changes
- Retry Hip Lantern field lookup until the type exists
- Read ConfigEntry.Value each hover; fall back to Hip Lantern ConfigFile
- Format keys as Shift/Alt/Ctrl + main key
- Version 1.0.9

## Output
`D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\bin\Release\net481\Cjayride.LanternTrinket.dll`

## Next
If heat still is not LeftAlt in-game, change Hip Lantern `Toggle lantern heat shortcut` (server-locked in current packs).

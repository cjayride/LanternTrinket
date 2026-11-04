# Lantern Trinket — Valheim 1.0.21 compatibility and performance

## Goal
Confirm the mod against current Valheim, then cut per-frame work that hurts load time, lag, and FPS.

## Status
Completed. 1.1.1 built (HarmonyX TypeByName spam fixed).

## Compatibility
- Local Valheim: Unity `6000.0.75f1`, `assembly_valheim.dll` dated 28 Oct 2026 (Patch 1.0.21).
- `ItemDrop.ItemData.GetTooltip` still has the six-parameter static signature this mod patches.
- Hip Lantern no longer stores `HipLanternState`; light is read from `LanternItem.IsLightEnabled` (with customData / variant fallback).
- SeneaL `Inv.Slots` and EpicLoot `GetMagicEquipment` / `EquipmentEffectCache.Reset` still exist.

## Changes
- Dropped `Update` polling and `LateUpdate` inventory scans after slot setup.
- Cached SeneaL slot cell, SFX prefab, tooltip shortcut text, and EpicLoot hide flag.
- Harmony postfix on `SetLightEnabled` instead of checking light every frame.
- Version 1.1.0.

## Output
- DLL: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\bin\Release\net481\Cjayride.LanternTrinket.dll`
- Zip: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\dist\cjayride-LanternTrinket-1.1.0.zip`

## Next
In-game check: equip hip lantern, confirm Charm slot, light toggle still gates EpicLoot, tooltip keys still match Hip Lantern config.

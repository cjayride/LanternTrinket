# Lantern Trinket — usage directions

## Goal
Show how to use the hip lantern on its tooltip/overlay: light key (required for EpicLoot) and heat key.

## Status
Completed.

## Changes
- `src/Plugin.cs`: append usage lines to `GetTooltip`; equip/toggle HUD messages include keys; version 1.0.7
- Package README, CHANGELOG, manifest, csproj version

## Notes
Hip Lantern heat shortcut in config is `H + LeftAlt`, not Shift+H. Overlay reads the live Hip Lantern config values.

## Next
Build and confirm DLL path.

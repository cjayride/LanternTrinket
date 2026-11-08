# Lantern Trinket — Charm slot arrows on login

## Goal
Stop arrows (and other stacked items) from occupying the SeneaL Charm cell together with the hip lantern after login.

## Status
Completed. 1.1.2 built.

## Cause
`WispSlot.Move` returned early when the lantern was already on the Charm cell, so any other item stacked on that cell (often ammo) stayed there. Login restore plus the SlotFor/IsCharm patches then drew both in Charm.

## Changes
- Evict every non-lantern item stacked on the Charm cell into a free backpack slot.
- Ammo is never treated as a lantern and is not unequipped; it is only moved off Charm.
- Recache Charm coordinates if inventory size changes.
- Version 1.1.2.

## Output
- DLL: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\bin\Release\net481\Cjayride.LanternTrinket.dll`
- Zip: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\dist\cjayride-LanternTrinket-1.1.2.zip`

## Next
In-game: log in with hip lantern equipped and arrows selected; Charm should show only the lantern, arrows stay in quiver/backpack.

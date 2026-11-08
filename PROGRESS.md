# Lantern Trinket 1.1.3 — save corruption with SeneaL UI

## Goal
Fix items landing in the wrong equipment slots after login (potion in Head, backpack in Charm), items unequipped, and items dropped on the ground. Prevent it from recurring, and heal saves already damaged by 1.1.2.

Acceptance:
- Mod never writes `m_gridPos` into SeneaL's reserved rows.
- Lantern moves through SeneaL's own supported API.
- Items stuck in equipment doll cells are moved back on login.
- 1.1.3 DLL builds, is packaged, and is installed in the Gale profiles.

## Status
Completed (code + build + packaging + install). In-game verification pending: the user must log in once; the repair pass runs then.

## Cause
SeneaL UI 1.2.8 renders the equipment doll, food, action, key, wallet, and quiver rows as cells of the player's **single inventory grid** (doll row = `height - Tail - 5`; cells Head 0, Cape 1, Chest 2, Belt 3, Legs 4, Trinket 5, Quiver 6, Charm/Wisp 7).
`WispSlot.Move`/`EmptyBackpack` in 1.1.2 scanned `y = 0..m_height` for empty cells top-down and wrote `item.m_gridPos` by hand. That placed items in the doll row (potion -> Head cell, backpack -> Charm cell) and in the quiver tail rows, where SeneaL's `Migrate`/`SpillTail` called `Humanoid.DropItem` (items on the ground). No exception, so the log stayed clean.

## Changes
`src\Plugin.cs` (single-file mod):
- Deleted `EmptyBackpack`/`CanPlace`/`Vacate`/`EvictStacked` and all raw grid writes. `Move` now calls SeneaL `Slots.Relocate(inventory, item)` — the same call SeneaL's own equip postfix uses. It swaps occupants, never displaces worn gear, and fires `Inventory.Changed`.
- Guards on `Move`: SeneaL `Slots.Ready` + `IsPlayer`, our `IsCharm`/`SlotFor` postfixes applied (SeneaL maps ItemType 18 to Belt; only our postfixes make it Charm), and `Slots.Shown(slot)` (warns once in log if the chosen slot is disabled in SeneaL config).
- Cape safety (checked against `Cjayride.SenealCapeOnLogin` 1.0.4): the repair pass never unequips an item SeneaL maps to a doll slot (`Slots.SlotFor(item) >= 0` — cape=1, backpack=8, quiver=6, rings, etc.). Only slotless items (potions, food — the 1.1.2 corruption signature) are unequipped+evicted. The cape mod itself only calls `Slots.Relocate` + `Changed` (verified: `Changed` just fires `m_onChanged`, it never sweeps the grid; `Migrate` is called by SeneaL only from its own load/`Epi` paths), and Relocate is idempotent for an item already in its slot — the repair leaving a cape in its Cape cell means Fits passes and both mods agree.
- New one-time **Repair pass** on login (max 6 runs, 2 s apart, from `LateUpdate` after world load; also runs after each lantern `Move`). It touches **only** doll-row cells (`EqRow` + `IsEquipCell`), and only items SeneaL's `Fits` rejects:
  - stacked squatters (two items, one cell) -> `Slots.Evict` into the main grid (stays equipped; ammo keeps its arrows),
  - gear with its own slot (backpack/quiver/rings/neck/finger) -> `Slots.Relocate` into that slot, stays equipped,
  - other equipped items -> `UnequipItem(false)` + `Evict` (worn ammo never unequipped),
  - non-equipped items -> `Evict`,
  - the hip lantern itself is never touched.
  One log warning summarises how many items were healed.
- `LateUpdate` order: `EnsureWispPatch()` before `WispSlot.Apply()` so `Relocate`'s `SlotFor` sees the Charm mapping.
- Added `Plugin.Log` (`ManualLogSource`) because `BaseUnityPlugin.Logger` is protected.
- Version 1.1.3 (csproj, manifest.json, CHANGELOG).

## Commands / results
- `dotnet build -c Release --nologo` -> Build succeeded, 0 errors (NU1900 offline vulnerability warnings are benign).
- `[Reflection.AssemblyName]::GetAssemblyName(...)` -> `Cjayride.LanternTrinket, Version=1.1.3.0`.
- Zip entries verified flat: CHANGELOG.md, Cjayride.LanternTrinket.dll, icon.png, manifest.json, README.md.
- Install verified in both Gale profiles (client 4.4.41 + dedicated server 4.4.41): version 1.1.3.0, timestamp 2026-11-09 05:34.
- Valheim process not running during install.

## Important decisions
- Never write `m_gridPos` outside the SeneaL API again.
- Repair only touches doll cells; food/action/key/wallet/quiver rows belong to SeneaL and BetterArchery.
- No direct `.fch` surgery (format decode abandoned); repair happens in-game through SeneaL's own functions.
- Save + deployed DLLs backed up first (see Output).

## Output
- DLL: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\bin\Release\net481\Cjayride.LanternTrinket.dll` (1.1.3.0)
- Zip: `D:\AI-Agent-Projects\cjaycraft\Cjayride.LanternTrinket\dist\cjayride-LanternTrinket-1.1.3.zip`
- Installed (1.1.3.0):
  - `...\com.kesomannen.gale\valheim\profiles\cjayride-beta_test_cjaycraft_ultimate_modpack-4.4.41\BepInEx\plugins\cjayride-LanternTrinket\Cjayride.LanternTrinket.dll`
  - same path under `..._dedicated_server-4.4.41`
- Backups: `D:\AI-Agent-Projects\cjaycraft\troubleshooting\backups\Cjayride.LanternTrinket-1.1.2-{client,server}4.4.41.dll.bak`, character `cjayride-20261109-052626.fch`.
- Modpack manifests (client + server) now pin `cjayride-LanternTrinket-1.1.3`; both parse as valid JSON (pack version 4.4.43).

## Next
1. User logs in (client profile). On world load, watch for the one-time log line "Moved N item(s) out of the SeneaL equipment doll cells...".
2. Verify: head slot empty, backpack back in its own slot, lantern in Charm, no new ground drops.
3. Items already spawned on the ground in the world stay there (world data, not player data) — pick them up.
4. Optional: restore `cjayride_backup_auto-20261109-002044.fch` (~4 h older) only if the repair pass leaves anything wrong.

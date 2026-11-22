# 1.1.5

- Hip lantern default SeneaL cell is **Key** (Charm stays for Wishbone + Wisplight).
- Fixes Hip Lantern **Slot type** 24 (vanilla Trinket) stealing the necklace and breaking **H** toggle: forced custom hip type `56`, inventory rewrite, equip hook order with Hip Lantern.
- Unequipping moves the lantern out of the key cell into the main bag.
- Dragging onto a key cell uses the same equip countdown as right-click (not instant pouch drop).

# 1.1.4

- Hip lantern sits in a SeneaL **key** cell by default (still equipped on the hip). Charm is free for Wishbone + Wisplight.

# 1.1.3

- Fixed: logging in could put items in the wrong equipment slots (a potion in the head slot, a backpack in the charm slot) and leave items on the ground. The mod wrote inventory grid positions by hand, and SeneaL UI draws the equipment doll, the food, action, key, wallet and quiver rows as cells of that same grid.
- The lantern now moves through SeneaL's own `Slots.Relocate`, the same call SeneaL uses when it equips something, so nothing else is displaced.
- Saves damaged by 1.1.2 are repaired on login: items sitting in equipment doll cells they do not belong in go back to the normal inventory rows. Gear with a slot of its own (cape, backpack, quiver, rings, neck, finger) is moved into that slot and stays equipped; worn ammo is never taken off; the hip lantern is never evicted. Worn gear that SeneaL has a slot for is never unequipped, so slot owners such as SeneaL Cape On Login keep control of their items.
- The mod no longer scans the whole inventory height for empty cells.
- Warns in the log when the chosen SeneaL slot is switched off in the SeneaL UI config.

# 1.1.2

- Logging in no longer leaves arrows stacked in the Charm cell with the hip lantern.

# 1.1.1

- Stop HarmonyX warning spam when Hip Lantern types are not loaded yet.

# 1.1.0

- Works with Valheim 1.0.21 / Unity 6 and current Hip Lantern light state APIs.
- Removed per-frame inventory scans; light changes and EpicLoot gating use Hip Lantern events and a cached flag.
- Slot setup retries until SeneaL and Hip Lantern exist, then stops running every frame.

# 1.0.9

- Light and heat tooltip keys come from Hip Lantern's live config, shown as Shift/Alt/Ctrl + key.

# 1.0.8

- Hovering the hip lantern no longer stalls for a second while the tooltip loads.

# 1.0.7

- The lantern tooltip and equip message show how to turn on light and heat, including that light is required for EpicLoot magic.

# 1.0.6

- Logging in without a hip lantern no longer says Lantern off.

# 1.0.5

- The Charm cell keeps the hip lantern model. Turning the light on plays the cue again.

# 1.0.4

- SeneaL slot chooses the equipment cell. Charm is the wisplight and wishbone cell. Belt, Trinket, Head, Cape, Chest, and Legs are the other choices.

# 1.0.3

- The lantern goes in the wisplight slot, not the belt.
- Equipping it leaves the light off until H.
- A side message says Lantern on or Lantern off.

# 1.0.2

- The lantern equips in the wisplight slot and swaps with the wisplight.
- Turning the light on no longer plays the fire burst. A short message and a quiet cue mark that the enchants are on.

# 1.0.1

- The lantern is forced into the trinket slot even when Hip Lantern's slot type is still 56.

# 1.0.0

- The hip lantern equips in the trinket slot, swapping with the wishbone.
- EpicLoot effects on the lantern count only while the light is on.

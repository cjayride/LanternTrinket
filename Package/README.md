# Lantern Trinket

EpicLoot effects on the hip lantern count only while its light is on.

`SeneaL slot` chooses the equipment cell. Charm is the wisplight and wishbone cell. The lantern swaps with whatever is already in that cell, and the hip model stays on the character. Equipping it leaves the light off until H. Turning the light off keeps the lantern equipped and drops its EpicLoot effects until the light is on again. A side message says Lantern on or Lantern off, and turning the light on plays a quiet cue instead of the fire burst.

`EnchantsOnlyWhileLit` defaults to on.

## Install

Copy `Cjayride.LanternTrinket.dll` into `BepInEx/plugins` on the client and the server. Hip Lantern, SeneaL UI, and EpicLoot need to be installed. `Make Lantern enchantable by EpicLoot` should be true. Leave Hip Lantern's utility slot off so the hip model stays attached.

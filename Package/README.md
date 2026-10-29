# 🏮 Lantern Trinket

**Bring Epic Loot enchantments to your hip lantern — only while its light is on.**

Lantern Trinket integrates **Hip Lantern**, **SeneaL UI**, and **EpicLoot** so your equipped hip lantern can provide its magical effects without requiring the light to remain on all the time.

## ✨ Features

### 🔮 EpicLoot Enchantments

- EpicLoot effects only activate while the lantern's light is on.
- Turning the light off disables the lantern's EpicLoot effects without unequipping it.
- The `EnchantsOnlyWhileLit` configuration option controls this behavior and defaults to `true`.

### 🎒 Equipment Slot Integration

- **SeneaL slot** selects which equipment cell the lantern uses.
- **Charm** is the wisplight and wishbone equipment cell.
- Other available cells include Belt, Trinket, Head, Cape, Chest, and Legs.
- Equipping the lantern swaps it with the item already occupying the selected cell.
- The hip lantern model remains attached to your character.

### 💡 Light Controls & Feedback

- Equipping the lantern leaves its light off until you press **H**.
- A side message displays **Lantern on** or **Lantern off** when the light state changes.
- Turning the light on plays a quiet audio cue instead of the default fire burst.
- The lantern tooltip and equip message display the current Hip Lantern controls for light and heat.
- **Light must be on for EpicLoot magic to work.**

**Default controls:**

| Action | Key |
|---|---|
| Toggle light | `H` |
| Toggle heat | `LeftAlt + H` |

The heat shortcut can be changed in Hip Lantern's configuration.

## 📦 Requirements

- **BepInEx**
- **Hip Lantern**
- **SeneaL UI**
- **EpicLoot**

## 📥 Installation

1. Install all required dependencies.
2. Copy `Cjayride.LanternTrinket.dll` into your `BepInEx/plugins` folder on **both the client and server**.
3. In EpicLoot's configuration, enable **Make Lantern enchantable by EpicLoot**.
4. Leave Hip Lantern's utility slot disabled so the hip lantern model stays attached to your character.
5. Launch the game and configure the
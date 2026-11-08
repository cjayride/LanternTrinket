using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Cjayride.LanternTrinket
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("shudnal.HipLantern", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("randyknapp.mods.epicloot", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "cjayride.LanternTrinket";
        public const string PluginName = "Lantern Trinket";
        public const string PluginVersion = "1.1.3";

        public static ConfigEntry<bool> EnchantsOnlyWhileLit;
        public static ConfigEntry<string> SeneaLSlot;
        static Harmony _harmony;
        internal static BepInEx.Logging.ManualLogSource Log;
        static MethodInfo _resetCache;
        static MethodInfo _setLight;
        static MethodInfo _setState;
        static readonly object[] _one = new object[1];
        static readonly object[] _two = new object[2];
        static bool _wispPatched;
        internal static bool WispPatched => _wispPatched;
        static bool _haveLight;
        static bool _sawLight;
        static bool _setupFinished;
        static int _setupTicks;
        static float _nextSetup;
        static int _repairRuns;
        static float _nextRepair;
        static Player _repairPlayer;
        internal static bool HideLanternMagic;

        private void Awake()
        {
            EnchantsOnlyWhileLit = Config.Bind("General", "EnchantsOnlyWhileLit", true,
                "EpicLoot effects on the hip lantern count only while its light is on.");
            SeneaLSlot = Config.Bind("General", "SeneaL slot", "Charm",
                new ConfigDescription(
                    "SeneaL UI equipment slot the hip lantern equips into. Charm is the wisplight and wishbone cell.",
                    new AcceptableValueList<string>("Charm", "Belt", "Trinket", "Head", "Cape", "Chest", "Legs")));
            _harmony = new Harmony(PluginGuid);
            Log = Logger;
            _harmony.PatchAll();
            TryPatchMods();
            Logger.LogInfo("Hip lantern slot comes from the SeneaL slot config. Enchants count only while the light is on.");
        }

        private void LateUpdate()
        {
            if (_setupFinished)
            {
                RepairTick();
                return;
            }

            if (_wispPatched && WispSlot.Ready)
            {
                _setupFinished = true;
                return;
            }

            if (Time.unscaledTime < _nextSetup)
            {
                return;
            }

            _nextSetup = Time.unscaledTime + 0.5f;
            _setupTicks++;
            Mods.Retry();
            TryPatchMods();
            // EnsureWispPatch first: WispSlot.Move needs the IsCharm/SlotFor
            // postfixes, and WispSlot.Apply only runs its equip once per session.
            EnsureWispPatch();
            WispSlot.Apply();
            if (_setupTicks >= 20)
            {
                _setupFinished = true;
            }
        }

        // A corrupted save shows up on login, and a lantern is not always equipped
        // then, so the doll-cell repair also runs on its own for a short window
        // after entering a world. SeneaL's Slots.Ready gates it until the mod's
        // rows exist, and each pass is a single inventory walk.
        void RepairTick()
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                return;
            }

            if (player != _repairPlayer)
            {
                _repairPlayer = player;
                _repairRuns = 0;
                _nextRepair = 0f;
            }

            if (_repairRuns >= 6 || Time.unscaledTime < _nextRepair)
            {
                return;
            }

            _nextRepair = Time.unscaledTime + 2f;
            _repairRuns++;
            WispSlot.RepairNow();
        }

        static void TryPatchMods()
        {
            LightToggled.Apply(_harmony);
            QuietEnchantCue.Apply(_harmony);
            HideDarkLanternMagic.Apply(_harmony);
        }

        void EnsureWispPatch()
        {
            if (_wispPatched || _harmony == null)
            {
                return;
            }

            MethodInfo isCharm = Mods.Method("SeneaLUI.Inv.Slots", "IsCharm");
            MethodInfo slotFor = Mods.Method("SeneaLUI.Inv.Slots", "SlotFor");
            if (isCharm == null || slotFor == null)
            {
                return;
            }

            _harmony.Patch(isCharm, postfix: new HarmonyMethod(typeof(CountLanternAsCharm), nameof(CountLanternAsCharm.Postfix)));
            _harmony.Patch(slotFor, postfix: new HarmonyMethod(typeof(CountLanternAsCharm), nameof(CountLanternAsCharm.Slot)));
            _wispPatched = true;
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData lantern = player == null ? null : Lit.EquippedLantern(player);
            if (lantern != null)
            {
                WispSlot.Move(player, lantern);
            }

            Logger.LogInfo("Hip lantern uses the SeneaL " + (SeneaLSlot != null ? SeneaLSlot.Value : "Charm") + " slot.");
        }

        internal static void OnLightChanged(Player player, ItemDrop.ItemData item, bool on)
        {
            if (player == null || !Lit.IsLantern(item))
            {
                return;
            }

            HideLanternMagic = EnchantsOnlyWhileLit != null && EnchantsOnlyWhileLit.Value && !on;
            if (EnchantsOnlyWhileLit == null || !EnchantsOnlyWhileLit.Value)
            {
                return;
            }

            if (!_haveLight)
            {
                _haveLight = true;
                _sawLight = on;
                return;
            }

            if (on == _sawLight)
            {
                return;
            }

            _sawLight = on;
            HideLanternMagic = !on;
            Lit.ResetMagicTotals(player);
            player.Message(MessageHud.MessageType.TopLeft, on ? "Lantern on (EpicLoot active)" : "Lantern off — press " + Keys.Light() + " for light");
        }

        internal static void StartDark(Player player, ItemDrop.ItemData item)
        {
            if (!Lit.IsLantern(item))
            {
                return;
            }

            if (_setLight == null)
            {
                _setLight = Mods.Method("HipLantern.LanternItem", "SetLightEnabled");
            }

            if (_setState == null)
            {
                _setState = Mods.Method("HipLantern.VisEquipmentExtension", "SetLanternState");
            }

            try
            {
                _two[0] = item;
                _two[1] = false;
                _setLight?.Invoke(null, _two);
                VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
                if (vis != null)
                {
                    _two[0] = vis;
                    _two[1] = item;
                    _setState?.Invoke(null, _two);
                }
            }
            catch (Exception)
            {
            }

            _haveLight = true;
            _sawLight = false;
            HideLanternMagic = EnchantsOnlyWhileLit == null || EnchantsOnlyWhileLit.Value;
            if (player != null)
            {
                player.Message(MessageHud.MessageType.TopLeft, Keys.EquipHint());
            }
        }

        internal static void ResetMagicTotals(Player player)
        {
            if (_resetCache == null)
            {
                Type cache = Mods.Type("EpicLoot.EquipmentEffectCache");
                _resetCache = cache == null ? null : AccessTools.Method(cache, "Reset");
            }

            try
            {
                _one[0] = player;
                _resetCache?.Invoke(null, _one);
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class Mods
    {
        static readonly Dictionary<string, Type> Found = new Dictionary<string, Type>();
        static readonly HashSet<string> Missing = new HashSet<string>();

        public static void Retry()
        {
            Missing.Clear();
        }

        public static Type Type(string fullName)
        {
            if (Found.TryGetValue(fullName, out Type hit))
            {
                return hit;
            }

            if (Missing.Contains(fullName))
            {
                return null;
            }

            Type type = Find(fullName);
            if (type != null)
            {
                Found[fullName] = type;
                return type;
            }

            Missing.Add(fullName);
            return null;
        }

        public static MethodInfo Method(string typeName, string methodName)
        {
            Type type = Type(typeName);
            return type == null ? null : AccessTools.Method(type, methodName);
        }

        static Type Find(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = null;
                try
                {
                    type = assemblies[i].GetType(fullName, false);
                }
                catch (Exception)
                {
                }

                if (type != null)
                {
                    return type;
                }
            }

            int dot = fullName.LastIndexOf('.');
            string simple = dot >= 0 ? fullName.Substring(dot + 1) : fullName;
            string ns = dot >= 0 ? fullName.Substring(0, dot) : null;
            for (int i = 0; i < assemblies.Length; i++)
            {
                string name = assemblies[i].GetName().Name;
                if (name == null
                    || (name.IndexOf("HipLantern", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("SeneaL", StringComparison.OrdinalIgnoreCase) < 0
                        && name.IndexOf("EpicLoot", StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null)
                    {
                        continue;
                    }

                    if (type.FullName == fullName || (type.Name == simple && type.Namespace == ns))
                    {
                        return type;
                    }
                }
            }

            return null;
        }
    }

    internal static class WispSlot
    {
        static FieldInfo _useUtility;
        static MethodInfo _apply;
        static MethodInfo _slotCell;
        static bool _layoutReady;
        static MethodInfo _seneaReady;
        static MethodInfo _relocate;
        static MethodInfo _isPlayer;
        static MethodInfo _shown;
        static MethodInfo _evict;
        static MethodInfo _fits;
        static MethodInfo _isEquipCell;
        static MethodInfo _eqRow;
        static MethodInfo _slotFor;
        static readonly object[] _one = new object[1];
        static readonly object[] _two = new object[2];
        static readonly object[] _three = new object[3];
        static readonly object[] _evictArgs = new object[2];
        static readonly object[] _fitsArgs = new object[4];
        static bool _warnedOff;
        static bool _healReported;

        public static bool Ready => _layoutReady && _slotCell != null;

        public static bool CharmChosen()
        {
            return string.Equals(ChosenName(), "Charm", StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply()
        {
            if (_layoutReady)
            {
                return;
            }

            Lookup();
            if (_useUtility == null)
            {
                return;
            }

            SetConfig(_useUtility, false);
            _layoutReady = true;
            try
            {
                _apply?.Invoke(null, null);
            }
            catch (Exception)
            {
            }

            Player player = Player.m_localPlayer;
            ItemDrop.ItemData item = WornLantern(player);
            if (player != null && item != null)
            {
                Move(player, item);
                if (!player.IsItemEquiped(item))
                {
                    player.EquipItem(item, false);
                }
            }
        }

        public static void Move(Player player, ItemDrop.ItemData item)
        {
            if (player == null || !Lit.IsLantern(item) || item.m_shared == null)
            {
                return;
            }

            Lookup();
            Inventory inventory = player.GetInventory();
            if (inventory == null || _relocate == null)
            {
                return;
            }

            // SeneaL UI draws the equipment doll, the food, action, key, wallet and
            // quiver rows as cells of the player's own inventory grid. The doll row
            // is Slots.EqRow = height - Tail - 5, and its cells are Head 0, Cape 1,
            // Chest 2, Belt 3, Legs 4, Trinket 5, Quiver 6, Charm 7. Writing
            // m_gridPos by hand puts items into those cells, so the game shows a
            // potion in the Head slot and it is saved that way. Anything SeneaL
            // finds in the quiver tail rows is dropped on the ground.
            // Slots.Relocate is the only supported way into a doll cell: it moves
            // the occupant to a legal main-grid cell, keeps worn equipment where
            // it is, and calls Inventory.Changed itself.
            // Slots.Relocate puts an item in Slots.SlotFor(item)'s cell. SeneaL maps
            // a utility item (ItemType 18) to the Belt cell; only this mod's
            // IsCharm/SlotFor postfixes make it say Charm. Move must not run before
            // those patches exist, and not before SeneaL has built its rows.
            if (!Plugin.WispPatched)
            {
                return;
            }

            if (!SeneaReady(inventory) || !SeneaPlayer(inventory))
            {
                return;
            }

            int slot = SlotId();
            if (!SlotShown(slot))
            {
                WarnSlotOff(slot);
                return;
            }

            try
            {
                _two[0] = inventory;
                _two[1] = item;
                _relocate.Invoke(null, _two);
            }
            catch (Exception)
            {
            }
            finally
            {
                _two[0] = null;
                _two[1] = null;
            }

            Repair(inventory);
        }

        public static void RepairNow()
        {
            Player player = Player.m_localPlayer;
            if (player == null || !Plugin.WispPatched)
            {
                return;
            }

            Lookup();
            Inventory inventory = player.GetInventory();
            if (inventory == null || !SeneaReady(inventory) || !SeneaPlayer(inventory))
            {
                return;
            }

            Repair(inventory);
        }

        // 1.1.2 and earlier wrote m_gridPos straight into SeneaL's reserved rows, so
        // a save can already hold a potion in the Head cell, a backpack in the Charm
        // cell, or two items stacked on one doll cell. SeneaL's own rule for those
        // cells is Slots.Fits; an item that does not fit belongs in the main grid.
        // Slots.Evict is SeneaL's supported way to move such an item there, and it
        // never unequips anything, so ammo stays worn.
        static void Repair(Inventory inventory)
        {
            if (_evict == null || _fits == null || _isEquipCell == null || _slotFor == null)
            {
                return;
            }

            List<ItemDrop.ItemData> items = inventory.m_inventory;
            if (items == null)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            int width = inventory.m_width;
            int eqRow = EqRow(inventory);
            int healed = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item == null || item.m_shared == null)
                {
                    continue;
                }

                // The lantern is this mod's item; Move decides where it sits.
                // Repair must never evict it, not even when SeneaL's Fits() calls
                // the cell illegal (the Belt slot is one such case).
                if (Lit.IsLantern(item))
                {
                    continue;
                }

                int x = item.m_gridPos.x;
                int y = item.m_gridPos.y;
                if (x < 0 || y < 0 || x >= width)
                {
                    continue;
                }

                // Only the equipment doll cells are repaired. The food, action, key
                // and wallet rows and the quiver rows belong to SeneaL and to the
                // mods that own them, and moving their contents is not ours to do.
                if (y != eqRow)
                {
                    continue;
                }

                if (!CallSlot(inventory, x, y))
                {
                    continue;
                }

                if (Fits(inventory, x, y, item))
                {
                    continue;
                }

                if (item.m_equipped)
                {
                    // Two items on one doll cell: 1.1.2 stacked them there. The
                    // lantern keeps the cell (it is skipped above), everything else
                    // goes to the main grid. SeneaL's Evict leaves m_equipped alone,
                    // so worn ammo stays worn.
                    if (CountAt(inventory, x, y) > 1)
                    {
                        Evict(inventory, item);
                        if (item.m_gridPos.x != x || item.m_gridPos.y != y)
                        {
                            healed++;
                        }
                    }

                    // Gear with a slot of its own (backpack, quiver, rings, neck,
                    // finger) belongs in that cell, worn. Relocate is SeneaL's own
                    // move: it swaps a free cell's occupant and does nothing for an
                    // item SeneaL has no slot for (potions, for example).
                    Relocate(inventory, item);
                    if (item.m_gridPos.x != x || item.m_gridPos.y != y)
                    {
                        if (!Fits(inventory, item.m_gridPos.x, item.m_gridPos.y, item))
                        {
                            continue;
                        }

                        healed++;
                        continue;
                    }

                    // Anything SeneaL gives a doll slot to (cape, backpack, quiver,
                    // rings, neck, finger, armour, utility) stays worn: if Relocate
                    // could not place it, its owner mod (for example SeneaL Cape On
                    // Login) is responsible for it, not this repair. Only slotless
                    // items — potions, food, tools — are taken off and evicted.
                    // That is the 1.1.2 corruption signature: a potion in the head.
                    if (IsAmmo(item) || SlotFor(item) >= 0)
                    {
                        continue;
                    }

                    if (player != null)
                    {
                        player.UnequipItem(item, false);
                    }

                    // SeneaL's UnequipItem postfix evicts the item into the grid.
                    // It is skipped when Suspend is on, so evict here as well.
                    Evict(inventory, item);
                    if (item.m_gridPos.x != x || item.m_gridPos.y != y)
                    {
                        healed++;
                    }

                    continue;
                }

                Evict(inventory, item);
                if (item.m_gridPos.x != x || item.m_gridPos.y != y)
                {
                    healed++;
                }
            }

            if (healed > 0 && !_healReported)
            {
                _healReported = true;
                Plugin.Log.LogWarning("Moved " + healed + " item(s) out of the SeneaL equipment doll cells. These "
                    + "were placed there by Lantern Trinket 1.1.2, which wrote grid positions directly. Gear with a "
                    + "slot of its own (backpack, quiver) is back in that slot; anything else is in the inventory "
                    + "and can be equipped again.");
            }
        }

        // SeneaL's own answer to "does this item own a doll cell?". A cape (Shoulder)
        // is 1, a backpack 8, a quiver 6, rings 9/10. -1 means slotless: a potion,
        // food, a tool. Unbound means unknown, so answer as if it has a slot and
        // leave the item alone.
        static int SlotFor(ItemDrop.ItemData item)
        {
            if (_slotFor == null)
            {
                return int.MaxValue;
            }

            try
            {
                _one[0] = item;
                return (int)_slotFor.Invoke(null, _one);
            }
            catch (Exception)
            {
                return int.MaxValue;
            }
        }

        static int EqRow(Inventory inventory)
        {
            if (_eqRow == null)
            {
                return int.MinValue;
            }

            try
            {
                _one[0] = inventory;
                return (int)_eqRow.Invoke(null, _one);
            }
            catch (Exception)
            {
                return int.MinValue;
            }
        }

        static int CountAt(Inventory inventory, int x, int y)
        {
            List<ItemDrop.ItemData> items = inventory.m_inventory;
            if (items == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_gridPos.x == x && item.m_gridPos.y == y)
                {
                    count++;
                }
            }

            return count;
        }

        static void Relocate(Inventory inventory, ItemDrop.ItemData item)
        {
            if (_relocate == null)
            {
                return;
            }

            try
            {
                _two[0] = inventory;
                _two[1] = item;
                _relocate.Invoke(null, _two);
            }
            catch (Exception)
            {
            }
            finally
            {
                _two[0] = null;
                _two[1] = null;
            }
        }

        static bool IsAmmo(ItemDrop.ItemData item)
        {
            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.Ammo || type == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
        }

        static void Evict(Inventory inventory, ItemDrop.ItemData item)
        {
            try
            {
                _evictArgs[0] = inventory;
                _evictArgs[1] = item;
                _evict.Invoke(null, _evictArgs);
            }
            catch (Exception)
            {
            }
            finally
            {
                _evictArgs[0] = null;
                _evictArgs[1] = null;
            }
        }

        static bool CallSlot(Inventory inventory, int x, int y)
        {
            try
            {
                _three[0] = inventory;
                _three[1] = x;
                _three[2] = y;
                return (bool)_isEquipCell.Invoke(null, _three);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _three[0] = null;
            }
        }

        static bool Fits(Inventory inventory, int x, int y, ItemDrop.ItemData item)
        {
            if (_fits == null)
            {
                return true;
            }

            try
            {
                _fitsArgs[0] = inventory;
                _fitsArgs[1] = item;
                _fitsArgs[2] = x;
                _fitsArgs[3] = y;
                return (bool)_fits.Invoke(null, _fitsArgs);
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                _fitsArgs[0] = null;
                _fitsArgs[1] = null;
            }
        }

        static bool SlotShown(int slot)
        {
            if (_shown == null)
            {
                return true;
            }

            try
            {
                return (bool)_shown.Invoke(null, new object[] { slot });
            }
            catch (Exception)
            {
                return true;
            }
        }

        static void WarnSlotOff(int slot)
        {
            if (_warnedOff)
            {
                return;
            }

            _warnedOff = true;
            Plugin.Log.LogWarning("The SeneaL " + ChosenName() + " slot is switched off in the SeneaL UI config, "
                + "so the hip lantern stays in the normal inventory rows. Pick a slot SeneaL shows, "
                + "or enable that slot in the SeneaL UI config.");
        }

        static bool SeneaReady(Inventory inventory)
        {
            if (_seneaReady == null)
            {
                return false;
            }

            try
            {
                _one[0] = inventory;
                return (bool)_seneaReady.Invoke(null, _one);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static bool SeneaPlayer(Inventory inventory)
        {
            if (_isPlayer == null)
            {
                return false;
            }

            try
            {
                _one[0] = inventory;
                return (bool)_isPlayer.Invoke(null, _one);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string ChosenName()
        {
            string name = Plugin.SeneaLSlot != null ? Plugin.SeneaLSlot.Value : "Charm";
            return string.IsNullOrEmpty(name) ? "Charm" : name;
        }

        public static int SlotId()
        {
            switch (ChosenName().ToLowerInvariant())
            {
                case "belt": return 3;
                case "trinket": return 5;
                case "head": return 0;
                case "cape": return 1;
                case "chest": return 2;
                case "legs": return 4;
                default: return 7;
            }
        }

        static ItemDrop.ItemData WornLantern(Player player)
        {
            if (player == null)
            {
                return null;
            }

            ItemDrop.ItemData equipped = Lit.EquippedLantern(player);
            if (equipped != null)
            {
                return equipped;
            }

            Inventory inventory = player.GetInventory();
            List<ItemDrop.ItemData> items = inventory == null ? null : inventory.m_inventory;
            if (items == null)
            {
                return null;
            }

            ItemDrop.ItemData any = null;
            for (int i = 0; i < items.Count; i++)
            {
                if (!Lit.IsLantern(items[i]))
                {
                    continue;
                }

                any = items[i];
                if (items[i].m_equipped)
                {
                    return items[i];
                }
            }

            return any;
        }

        static void SetConfig(FieldInfo field, object next)
        {
            if (field == null)
            {
                return;
            }

            object entry = field.GetValue(null);
            PropertyInfo value = entry == null ? null : entry.GetType().GetProperty("Value");
            if (value == null || Equals(value.GetValue(entry, null), next))
            {
                return;
            }

            value.SetValue(entry, next, null);
        }

        static void Lookup()
        {
            Type slots = Mods.Type("SeneaLUI.Inv.Slots");
            if (_slotCell == null && slots != null)
            {
                _slotCell = AccessTools.Method(slots, "SlotCell");
                _seneaReady = AccessTools.Method(slots, "Ready");
                _relocate = AccessTools.Method(slots, "Relocate");
                _isPlayer = AccessTools.Method(slots, "IsPlayer");
                _shown = AccessTools.Method(slots, "Shown");
                _evict = AccessTools.Method(slots, "Evict");
                _fits = AccessTools.Method(slots, "Fits");
                _isEquipCell = AccessTools.Method(slots, "IsEquipCell");
                _eqRow = AccessTools.Method(slots, "EqRow");
                _slotFor = AccessTools.Method(slots, "SlotFor");
            }

            if (_useUtility != null && _apply != null && _slotCell != null)
            {
                return;
            }

            Type plugin = Mods.Type("HipLantern.HipLantern");
            Type lantern = Mods.Type("HipLantern.LanternItem");
            if (_useUtility == null && plugin != null)
            {
                _useUtility = AccessTools.Field(plugin, "itemSlotUtility");
            }

            if (_apply == null && lantern != null)
            {
                _apply = AccessTools.Method(lantern, "PatchLanternItemOnConfigChange");
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
    internal static class EquipIntoWisp
    {
        static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || !(__instance is Player player))
            {
                return;
            }

            WispSlot.Move(player, item);
            Plugin.StartDark(player, item);
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
    internal static class UnequipLantern
    {
        static void Postfix(Humanoid __instance, ItemDrop.ItemData item)
        {
            if (!(__instance is Player) || !Lit.IsLantern(item))
            {
                return;
            }

            Plugin.HideLanternMagic = false;
        }
    }

    internal static class LightToggled
    {
        static bool _applied;

        public static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
            {
                return;
            }

            MethodInfo method = Mods.Method("HipLantern.LanternItem", "SetLightEnabled");
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(LightToggled), nameof(Postfix)));
            _applied = true;
        }

        static void Postfix(ItemDrop.ItemData item)
        {
            Player player = Player.m_localPlayer;
            if (player == null || item == null)
            {
                return;
            }

            Plugin.OnLightChanged(player, item, Lit.LightOn(item));
        }
    }

    internal static class CountLanternAsCharm
    {
        public static void Postfix(ItemDrop.ItemData item, ref bool __result)
        {
            if (WispSlot.CharmChosen() && Lit.IsLantern(item))
            {
                __result = true;
            }
        }

        public static void Slot(ItemDrop.ItemData item, ref int __result)
        {
            if (Lit.IsLantern(item))
            {
                __result = WispSlot.SlotId();
            }
        }
    }

    internal static class QuietEnchantCue
    {
        static readonly string[] CuePrefabs =
        {
            "sfx_Potion_eitr_Start",
            "sfx_Potion_stamina_Start",
            "sfx_gui_craft"
        };

        static GameObject _cuePrefab;
        static bool _applied;

        public static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
            {
                return;
            }

            MethodInfo method = Mods.Method("HipLantern.LanternLightController", "EmitSwitchEffect");
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuietEnchantCue), nameof(Prefix)));
            _applied = true;
        }

        static bool Prefix(Component __instance, int variant)
        {
            if (variant != 0 && variant != 2)
            {
                return true;
            }

            Play(__instance.transform.position);
            return false;
        }

        static void Play(Vector3 position)
        {
            if (_cuePrefab == null)
            {
                if (ZNetScene.instance == null)
                {
                    return;
                }

                for (int i = 0; i < CuePrefabs.Length; i++)
                {
                    _cuePrefab = ZNetScene.instance.GetPrefab(CuePrefabs[i]);
                    if (_cuePrefab != null)
                    {
                        break;
                    }
                }
            }

            if (_cuePrefab == null)
            {
                return;
            }

            GameObject cue = UnityEngine.Object.Instantiate(_cuePrefab, position, Quaternion.identity);
            ZSFX sfx = cue.GetComponentInChildren<ZSFX>();
            if (sfx != null)
            {
                sfx.SetVolumeModifier(0.35f);
            }
        }
    }

    internal static class Lit
    {
        static MethodInfo _isLantern;
        static MethodInfo _lightOn;
        static MethodInfo _equipped;
        static readonly object[] _one = new object[1];
        static bool _lookedUp;

        public static bool IsLantern(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return false;
            }

            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            if (type == ItemDrop.ItemData.ItemType.Ammo || type == ItemDrop.ItemData.ItemType.AmmoNonEquipable)
            {
                return false;
            }

            if (item.m_shared.m_name == "$item_hiplantern")
            {
                return true;
            }

            if (item.m_dropPrefab != null && item.m_dropPrefab.name == "HipLantern")
            {
                return true;
            }

            Lookup();
            if (_isLantern == null)
            {
                return false;
            }

            try
            {
                _one[0] = item;
                return (bool)_isLantern.Invoke(null, _one);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool LightOn(ItemDrop.ItemData item)
        {
            Lookup();
            if (_lightOn != null && item != null)
            {
                try
                {
                    _one[0] = item;
                    return (bool)_lightOn.Invoke(null, _one);
                }
                catch (Exception)
                {
                }
            }

            if (item?.m_customData != null)
            {
                foreach (KeyValuePair<string, string> pair in item.m_customData)
                {
                    string json = pair.Value;
                    if (string.IsNullOrEmpty(json) || json.IndexOf("lightEnabled", StringComparison.Ordinal) < 0)
                    {
                        continue;
                    }

                    return json.IndexOf("\"lightEnabled\":false", StringComparison.Ordinal) < 0
                        && json.IndexOf("\"lightEnabled\": false", StringComparison.Ordinal) < 0;
                }
            }

            return item == null || item.m_variant != 1;
        }

        public static ItemDrop.ItemData EquippedLantern(Player player)
        {
            if (player == null)
            {
                return null;
            }

            Lookup();
            if (_equipped != null)
            {
                try
                {
                    _one[0] = player;
                    if (_equipped.Invoke(null, _one) is ItemDrop.ItemData fromHip)
                    {
                        return fromHip;
                    }
                }
                catch (Exception)
                {
                }
            }

            Inventory inventory = player.GetInventory();
            List<ItemDrop.ItemData> items = inventory == null ? null : inventory.m_inventory;
            if (items == null)
            {
                return null;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData item = items[i];
                if (item != null && item.m_equipped && IsLantern(item))
                {
                    return item;
                }
            }

            return null;
        }

        public static void ResetMagicTotals(Player player)
        {
            Plugin.ResetMagicTotals(player);
        }

        static void Lookup()
        {
            if (_lookedUp)
            {
                return;
            }

            Type lantern = Mods.Type("HipLantern.LanternItem");
            if (lantern == null)
            {
                return;
            }

            _lookedUp = true;
            _isLantern = AccessTools.Method(lantern, "IsLanternItem", new[] { typeof(ItemDrop.ItemData) })
                ?? AccessTools.Method(lantern, "IsLanternItem");
            _lightOn = AccessTools.Method(lantern, "IsLightEnabled", new[] { typeof(ItemDrop.ItemData) })
                ?? AccessTools.Method(lantern, "IsLightEnabled");
            _equipped = AccessTools.Method(lantern, "GetEquippedLantern", new[] { typeof(Player) })
                ?? AccessTools.Method(lantern, "GetEquippedLantern");
        }
    }

    internal static class Keys
    {
        const string LightHint = "required for EpicLoot magic";
        static Type _hipType;
        static FieldInfo _lightField;
        static FieldInfo _heatField;
        static PropertyInfo _lightValue;
        static PropertyInfo _heatValue;
        static string _lightText;
        static string _heatText;
        static object _lightCached;
        static object _heatCached;
        static object _fileLight;
        static object _fileHeat;
        static bool _fileLookedUp;

        public static string Light()
        {
            return Read(ref _lightField, ref _lightValue, ref _lightCached, ref _lightText, "Toggle lantern shortcut", "H");
        }

        public static string Heat()
        {
            return Read(ref _heatField, ref _heatValue, ref _heatCached, ref _heatText, "Toggle lantern heat shortcut", "LeftShift + H");
        }

        public static string EquipHint()
        {
            return "Lantern off — " + Light() + ": light (" + LightHint + "), " + Heat() + ": heat";
        }

        public static void AppendTooltip(ItemDrop.ItemData item, ref string text)
        {
            if (!Lit.IsLantern(item) || string.IsNullOrEmpty(text) || text.IndexOf(LightHint, StringComparison.Ordinal) >= 0)
            {
                return;
            }

            text += "\n\n<color=#ffc078>Press " + Light() + " to turn on light (" + LightHint + ")</color>\n<color=#ff9e7a>Press " + Heat() + " to turn on heat</color>";
        }

        static string Read(ref FieldInfo field, ref PropertyInfo value, ref object cached, ref string text, string configName, string fallback)
        {
            Lookup();
            object shortcut = FromEntry(ref field, ref value);
            if (shortcut == null)
            {
                shortcut = FromConfigFile(configName);
            }

            if (ReferenceEquals(shortcut, cached) && !string.IsNullOrEmpty(text))
            {
                return text;
            }

            cached = shortcut;
            text = Format(shortcut);
            return string.IsNullOrEmpty(text) ? fallback : text;
        }

        static object FromEntry(ref FieldInfo field, ref PropertyInfo value)
        {
            object entry = field == null ? null : field.GetValue(null);
            if (entry == null)
            {
                return null;
            }

            if (value == null)
            {
                value = entry.GetType().GetProperty("Value");
            }

            return value == null ? null : value.GetValue(entry, null);
        }

        static object FromConfigFile(string configName)
        {
            if (string.Equals(configName, "Toggle lantern shortcut", StringComparison.Ordinal) && _fileLight != null)
            {
                return _fileLight;
            }

            if (string.Equals(configName, "Toggle lantern heat shortcut", StringComparison.Ordinal) && _fileHeat != null)
            {
                return _fileHeat;
            }

            if (_fileLookedUp || _hipType == null)
            {
                return configName.IndexOf("heat", StringComparison.OrdinalIgnoreCase) >= 0 ? _fileHeat : _fileLight;
            }

            FieldInfo instanceField = AccessTools.Field(_hipType, "instance");
            object instance = instanceField == null ? null : instanceField.GetValue(null);
            ConfigFile config = null;
            try
            {
                config = (instance as BaseUnityPlugin)?.Config;
            }
            catch (Exception)
            {
            }

            if (config == null)
            {
                return null;
            }

            _fileLookedUp = true;
            foreach (KeyValuePair<ConfigDefinition, ConfigEntryBase> pair in config)
            {
                if (pair.Key == null || pair.Value == null)
                {
                    continue;
                }

                if (string.Equals(pair.Key.Key, "Toggle lantern shortcut", StringComparison.Ordinal))
                {
                    _fileLight = pair.Value.BoxedValue;
                }
                else if (string.Equals(pair.Key.Key, "Toggle lantern heat shortcut", StringComparison.Ordinal))
                {
                    _fileHeat = pair.Value.BoxedValue;
                }
            }

            return configName.IndexOf("heat", StringComparison.OrdinalIgnoreCase) >= 0 ? _fileHeat : _fileLight;
        }

        static string Format(object shortcut)
        {
            if (shortcut == null)
            {
                return null;
            }

            if (shortcut is KeyboardShortcut keys)
            {
                if (keys.MainKey == KeyCode.None)
                {
                    return null;
                }

                string main = KeyName(keys.MainKey);
                string first = null;
                int extra = 0;
                foreach (KeyCode modifier in keys.Modifiers)
                {
                    string name = KeyName(modifier);
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (first == null)
                    {
                        first = name;
                    }

                    extra++;
                    if (extra > 1)
                    {
                        break;
                    }
                }

                if (first == null)
                {
                    return main;
                }

                if (extra == 1)
                {
                    return first + " + " + main;
                }

                List<string> parts = new List<string>(4);
                foreach (KeyCode modifier in keys.Modifiers)
                {
                    string name = KeyName(modifier);
                    if (!string.IsNullOrEmpty(name))
                    {
                        parts.Add(name);
                    }
                }

                parts.Add(main);
                return string.Join(" + ", parts.ToArray());
            }

            string text = shortcut.ToString();
            return string.IsNullOrEmpty(text) || text == "None" ? null : text.Replace("LeftShift", "Shift").Replace("RightShift", "Shift").Replace("LeftAlt", "Alt").Replace("RightAlt", "Alt").Replace("LeftControl", "Ctrl").Replace("RightControl", "Ctrl");
        }

        static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.AltGr:
                    return "Alt";
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                default:
                    return key.ToString();
            }
        }

        static void Lookup()
        {
            if (_lightField != null && _heatField != null)
            {
                return;
            }

            _hipType = Mods.Type("HipLantern.HipLantern");
            if (_hipType == null)
            {
                return;
            }

            _lightField = AccessTools.Field(_hipType, "toggleLanternShortcut");
            _heatField = AccessTools.Field(_hipType, "toggleLanternHeatShortcut");
        }
    }

    [HarmonyPatch]
    internal static class LanternTooltip
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
                new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) });
        }

        [HarmonyPriority(Priority.Last)]
        static void Postfix(ItemDrop.ItemData item, ref string __result)
        {
            Keys.AppendTooltip(item, ref __result);
        }
    }

    internal static class HideDarkLanternMagic
    {
        static bool _applied;

        public static void Apply(Harmony harmony)
        {
            if (_applied || harmony == null)
            {
                return;
            }

            MethodInfo method = Mods.Method("EpicLoot.PlayerExtensions", "GetMagicEquipment");
            if (method == null)
            {
                return;
            }

            harmony.Patch(method, postfix: new HarmonyMethod(typeof(HideDarkLanternMagic), nameof(Postfix)));
            _applied = true;
        }

        static void Postfix(List<ItemDrop.ItemData> __result)
        {
            if (!Plugin.HideLanternMagic || Plugin.EnchantsOnlyWhileLit == null || !Plugin.EnchantsOnlyWhileLit.Value || __result == null)
            {
                return;
            }

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                if (Lit.IsLantern(__result[i]))
                {
                    __result.RemoveAt(i);
                }
            }
        }
    }
}

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
        public const string PluginVersion = "1.1.2";

        public static ConfigEntry<bool> EnchantsOnlyWhileLit;
        public static ConfigEntry<string> SeneaLSlot;
        static Harmony _harmony;
        static MethodInfo _resetCache;
        static MethodInfo _setLight;
        static MethodInfo _setState;
        static readonly object[] _one = new object[1];
        static readonly object[] _two = new object[2];
        static bool _wispPatched;
        static bool _haveLight;
        static bool _sawLight;
        static bool _setupFinished;
        static int _setupTicks;
        static float _nextSetup;
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
            _harmony.PatchAll();
            TryPatchMods();
            Logger.LogInfo("Hip lantern slot comes from the SeneaL slot config. Enchants count only while the light is on.");
        }

        private void LateUpdate()
        {
            if (_setupFinished)
            {
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
            WispSlot.Apply();
            EnsureWispPatch();
            if (_setupTicks >= 20)
            {
                _setupFinished = true;
            }
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
        static FieldInfo _cellX;
        static FieldInfo _cellY;
        static bool _ready;
        static int _slotX = int.MinValue;
        static int _slotY = int.MinValue;
        static int _cachedSlotId = int.MinValue;
        static int _cachedWidth;
        static int _cachedHeight;

        public static bool Ready => _ready && _slotCell != null;

        public static bool CharmChosen()
        {
            return string.Equals(ChosenName(), "Charm", StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply()
        {
            if (_ready)
            {
                return;
            }

            Lookup();
            if (_useUtility == null)
            {
                return;
            }

            SetConfig(_useUtility, false);
            _ready = true;
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
            if (inventory == null || _slotCell == null)
            {
                return;
            }

            int x = _slotX;
            int y = _slotY;
            int slot = SlotId();
            if (x == int.MinValue || slot != _cachedSlotId || inventory.m_width != _cachedWidth || inventory.m_height != _cachedHeight)
            {
                object cell;
                try
                {
                    cell = InvokeSlotCell(inventory, slot);
                }
                catch (Exception)
                {
                    return;
                }

                if (cell == null)
                {
                    return;
                }

                if (_cellX == null)
                {
                    Type type = cell.GetType();
                    _cellX = type.GetField("x");
                    _cellY = type.GetField("y");
                }

                if (_cellX == null || _cellY == null)
                {
                    return;
                }

                x = (int)_cellX.GetValue(cell);
                y = (int)_cellY.GetValue(cell);
                _slotX = x;
                _slotY = y;
                _cachedSlotId = slot;
                _cachedWidth = inventory.m_width;
                _cachedHeight = inventory.m_height;
            }

            if (x < 0)
            {
                return;
            }

            if (item.m_gridPos.x != x || item.m_gridPos.y != y)
            {
                ItemDrop.ItemData other = inventory.GetItemAt(x, y);
                if (other != null && other != item)
                {
                    Vacate(player, inventory, other, item, item.m_gridPos, x, y);
                }

                item.m_gridPos = new Vector2i(x, y);
            }

            EvictStacked(player, inventory, item, x, y);
            inventory.Changed();
        }

        static void EvictStacked(Player player, Inventory inventory, ItemDrop.ItemData keep, int x, int y)
        {
            List<ItemDrop.ItemData> items = inventory.m_inventory;
            if (items == null)
            {
                return;
            }

            for (int i = 0; i < items.Count; i++)
            {
                ItemDrop.ItemData other = items[i];
                if (other == null || other == keep || other.m_gridPos.x != x || other.m_gridPos.y != y)
                {
                    continue;
                }

                Vacate(player, inventory, other, keep, new Vector2i(-1, -1), x, y);
            }
        }

        static void Vacate(Player player, Inventory inventory, ItemDrop.ItemData other, ItemDrop.ItemData keep, Vector2i prefer, int slotX, int slotY)
        {
            if (other == null)
            {
                return;
            }

            bool ammo = IsAmmo(other);
            if (other.m_equipped && !ammo)
            {
                player.UnequipItem(other, false);
            }

            Vector2i dest = prefer;
            if (ammo || !CanPlace(inventory, dest, keep, slotX, slotY))
            {
                dest = EmptyBackpack(inventory, keep, slotX, slotY);
            }

            if (dest.x < 0)
            {
                return;
            }

            other.m_gridPos = dest;
        }

        static bool IsAmmo(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return false;
            }

            ItemDrop.ItemData.ItemType type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.Ammo || type == ItemDrop.ItemData.ItemType.AmmoNonEquipable;
        }

        static bool CanPlace(Inventory inventory, Vector2i pos, ItemDrop.ItemData ignore, int slotX, int slotY)
        {
            if (pos.x < 0 || pos.y < 0 || pos.x >= inventory.m_width || pos.y >= inventory.m_height)
            {
                return false;
            }

            if (pos.x == slotX && pos.y == slotY)
            {
                return false;
            }

            ItemDrop.ItemData occupant = inventory.GetItemAt(pos.x, pos.y);
            return occupant == null || occupant == ignore;
        }

        static Vector2i EmptyBackpack(Inventory inventory, ItemDrop.ItemData ignore, int slotX, int slotY)
        {
            int backpack = Math.Min(4, inventory.m_height);
            for (int y = 0; y < backpack; y++)
            {
                for (int x = 0; x < inventory.m_width; x++)
                {
                    if (CanPlace(inventory, new Vector2i(x, y), ignore, slotX, slotY))
                    {
                        return new Vector2i(x, y);
                    }
                }
            }

            for (int y = backpack; y < inventory.m_height; y++)
            {
                for (int x = 0; x < inventory.m_width; x++)
                {
                    if (CanPlace(inventory, new Vector2i(x, y), ignore, slotX, slotY))
                    {
                        return new Vector2i(x, y);
                    }
                }
            }

            return new Vector2i(-1, -1);
        }

        static object InvokeSlotCell(Inventory inventory, int slot)
        {
            object[] args = { inventory, slot };
            return _slotCell.Invoke(null, args);
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
            if (_slotCell != null && _useUtility != null)
            {
                return;
            }

            Type plugin = Mods.Type("HipLantern.HipLantern");
            Type lantern = Mods.Type("HipLantern.LanternItem");
            Type slots = Mods.Type("SeneaLUI.Inv.Slots");
            if (_useUtility == null && plugin != null)
            {
                _useUtility = AccessTools.Field(plugin, "itemSlotUtility");
            }

            if (_apply == null && lantern != null)
            {
                _apply = AccessTools.Method(lantern, "PatchLanternItemOnConfigChange");
            }

            if (_slotCell == null && slots != null)
            {
                _slotCell = AccessTools.Method(slots, "SlotCell");
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

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
        public const string PluginVersion = "1.0.7";

        public static ConfigEntry<bool> EnchantsOnlyWhileLit;
        public static ConfigEntry<string> SeneaLSlot;
        static Harmony _harmony;
        static MethodInfo _resetCache;
        static MethodInfo _setLight;
        static MethodInfo _setState;
        static bool _wispPatched;
        static bool _sawLight;
        static bool _haveLight;

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
            Logger.LogInfo("Hip lantern slot comes from the SeneaL slot config. Enchants count only while the light is on.");
        }

        private void LateUpdate()
        {
            WispSlot.Apply();
            EnsureWispPatch();
            Player player = Player.m_localPlayer;
            ItemDrop.ItemData lantern = player == null ? null : Lit.EquippedLantern(player);
            if (lantern != null)
            {
                WispSlot.Move(player, lantern);
            }
        }

        void EnsureWispPatch()
        {
            if (_wispPatched || _harmony == null)
            {
                return;
            }

            MethodInfo isCharm = AccessTools.Method("SeneaLUI.Inv.Slots:IsCharm");
            MethodInfo slotFor = AccessTools.Method("SeneaLUI.Inv.Slots:SlotFor");
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

        private void Update()
        {
            if (EnchantsOnlyWhileLit == null || !EnchantsOnlyWhileLit.Value)
            {
                return;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _haveLight = false;
                return;
            }

            ItemDrop.ItemData lantern = Lit.EquippedLantern(player);
            if (lantern == null)
            {
                _haveLight = false;
                _sawLight = false;
                return;
            }

            bool on = Lit.LightOn(lantern);
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
                _setLight = AccessTools.Method("HipLantern.LanternItem:SetLightEnabled");
            }

            if (_setState == null)
            {
                _setState = AccessTools.Method("HipLantern.VisEquipmentExtension:SetLanternState");
            }

            try
            {
                _setLight?.Invoke(null, new object[] { item, false });
                VisEquipment vis = player != null ? player.GetComponent<VisEquipment>() : null;
                if (vis != null)
                {
                    _setState?.Invoke(null, new object[] { vis, item });
                }
            }
            catch (Exception)
            {
            }

            _haveLight = true;
            _sawLight = false;
            if (player != null)
            {
                player.Message(MessageHud.MessageType.TopLeft, Keys.EquipHint());
            }
        }

        internal static void ResetMagicTotals(Player player)
        {
            if (_resetCache == null)
            {
                Type cache = AccessTools.TypeByName("EpicLoot.EquipmentEffectCache");
                _resetCache = cache == null ? null : AccessTools.Method(cache, "Reset");
            }

            try
            {
                _resetCache?.Invoke(null, new object[] { player });
            }
            catch (Exception)
            {
            }
        }
    }

    internal static class WispSlot
    {
        static FieldInfo _useUtility;
        static FieldInfo _slotType;
        static MethodInfo _apply;
        static MethodInfo _slotCell;
        static bool _lookedUp;
        static bool _synced;

        public static bool CharmChosen()
        {
            return string.Equals(ChosenName(), "Charm", StringComparison.OrdinalIgnoreCase);
        }

        public static void Apply()
        {
            Lookup();
            if (_synced || _useUtility == null)
            {
                return;
            }

            SetConfig(_useUtility, false);
            _synced = true;
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
                if (player.IsItemEquiped(item))
                {
                    player.UnequipItem(item, false);
                }

                player.EquipItem(item, false);
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

            object cell = null;
            try
            {
                cell = _slotCell.Invoke(null, new object[] { inventory, SlotId() });
            }
            catch (Exception)
            {
                return;
            }

            int x = (int)cell.GetType().GetField("x").GetValue(cell);
            int y = (int)cell.GetType().GetField("y").GetValue(cell);
            if (x < 0 || (item.m_gridPos.x == x && item.m_gridPos.y == y))
            {
                return;
            }

            ItemDrop.ItemData other = inventory.GetItemAt(x, y);
            if (other != null && other != item)
            {
                if (other.m_equipped)
                {
                    player.UnequipItem(other, false);
                }

                other.m_gridPos = item.m_gridPos;
            }

            item.m_gridPos = new Vector2i(x, y);
            inventory.Changed();
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

            List<ItemDrop.ItemData> items = player.GetInventory()?.GetAllItems();
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
            if (_lookedUp)
            {
                return;
            }

            _lookedUp = true;
            Type plugin = AccessTools.TypeByName("HipLantern.HipLantern");
            Type lantern = AccessTools.TypeByName("HipLantern.LanternItem");
            Type slots = AccessTools.TypeByName("SeneaLUI.Inv.Slots");
            _useUtility = plugin == null ? null : AccessTools.Field(plugin, "itemSlotUtility");
            _slotType = plugin == null ? null : AccessTools.Field(plugin, "itemSlotType");
            _apply = lantern == null ? null : AccessTools.Method(lantern, "PatchLanternItemOnConfigChange");
            _slotCell = slots == null ? null : AccessTools.Method(slots, "SlotCell");
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

    [HarmonyPatch]
    internal static class QuietEnchantCue
    {
        static readonly string[] CuePrefabs =
        {
            "sfx_Potion_eitr_Start",
            "sfx_Potion_stamina_Start",
            "sfx_gui_craft"
        };

        static bool Prepare()
        {
            return AccessTools.Method("HipLantern.LanternLightController:EmitSwitchEffect") != null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method("HipLantern.LanternLightController:EmitSwitchEffect");
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
            if (ZNetScene.instance == null)
            {
                return;
            }

            for (int i = 0; i < CuePrefabs.Length; i++)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(CuePrefabs[i]);
                if (prefab == null)
                {
                    continue;
                }

                GameObject cue = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
                ZSFX sfx = cue.GetComponentInChildren<ZSFX>();
                if (sfx != null)
                {
                    sfx.SetVolumeModifier(0.35f);
                }

                return;
            }
        }
    }

    internal static class Lit
    {
        public static bool IsLantern(ItemDrop.ItemData item)
        {
            if (item?.m_shared == null)
            {
                return false;
            }

            if (item.m_shared.m_name == "$item_hiplantern")
            {
                return true;
            }

            return item.m_dropPrefab != null && item.m_dropPrefab.name == "HipLantern";
        }

        public static bool LightOn(ItemDrop.ItemData item)
        {
            if (item?.m_customData != null && item.m_customData.TryGetValue("HipLanternState", out string json) && !string.IsNullOrEmpty(json))
            {
                return json.IndexOf("\"lightEnabled\":false", StringComparison.Ordinal) < 0
                    && json.IndexOf("\"lightEnabled\": false", StringComparison.Ordinal) < 0;
            }

            return item == null || item.m_variant != 1;
        }

        public static ItemDrop.ItemData EquippedLantern(Player player)
        {
            List<ItemDrop.ItemData> equipped = player.GetInventory()?.GetEquippedItems();
            if (equipped == null)
            {
                return null;
            }

            for (int i = 0; i < equipped.Count; i++)
            {
                if (IsLantern(equipped[i]))
                {
                    return equipped[i];
                }
            }

            return null;
        }

        public static void ResetMagicTotals(Player player)
        {
            Plugin.ResetMagicTotals(player);
        }
    }

    internal static class Keys
    {
        const string LightHint = "required for EpicLoot magic";

        public static string Light()
        {
            return Read("toggleLanternShortcut", "H");
        }

        public static string Heat()
        {
            return Read("toggleLanternHeatShortcut", "H + LeftAlt");
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

        static string Read(string fieldName, string fallback)
        {
            Type type = AccessTools.TypeByName("HipLantern.HipLantern");
            FieldInfo field = type == null ? null : AccessTools.Field(type, fieldName);
            object entry = field == null ? null : field.GetValue(null);
            PropertyInfo value = entry == null ? null : entry.GetType().GetProperty("Value");
            object shortcut = value == null ? null : value.GetValue(entry, null);
            string text = shortcut == null ? null : shortcut.ToString();
            return string.IsNullOrEmpty(text) ? fallback : text;
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

    [HarmonyPatch]
    internal static class HideDarkLanternMagic
    {
        static bool Prepare()
        {
            return AccessTools.Method("EpicLoot.PlayerExtensions:GetMagicEquipment") != null;
        }

        static MethodBase TargetMethod()
        {
            return AccessTools.Method("EpicLoot.PlayerExtensions:GetMagicEquipment");
        }

        static void Postfix(List<ItemDrop.ItemData> __result)
        {
            if (Plugin.EnchantsOnlyWhileLit == null || !Plugin.EnchantsOnlyWhileLit.Value || __result == null)
            {
                return;
            }

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                ItemDrop.ItemData item = __result[i];
                if (Lit.IsLantern(item) && !Lit.LightOn(item))
                {
                    __result.RemoveAt(i);
                }
            }
        }
    }
}

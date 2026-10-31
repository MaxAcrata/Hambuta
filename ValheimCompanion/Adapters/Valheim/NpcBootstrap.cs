using System;
using System.Text;
using Jotunn.Managers;
using UnityEngine;
using ValheimCompanion.Core;
using Object = UnityEngine.Object;

namespace ValheimCompanion.Adapters.Valheim
{
    /// <summary>
    /// Spawns and configures the companion NPC.
    /// Prefabs are provisional per Stage 2 (Skeleton_Friendly / Skeleton) — swap after Stage 4 evidence.
    /// </summary>
    internal static class NpcBootstrap
    {
        public const string PrimaryPrefab = "Skeleton_Friendly";
        public const string FallbackPrefab = "Skeleton";

        public sealed class SpawnResult
        {
            public bool Success;
            public string Error;
            public GameObject Go;
            public string PrefabUsed;
        }

        public static SpawnResult Spawn(Player player, bool useFallback)
        {
            var result = new SpawnResult();

            if (player == null)
            {
                result.Error = "player is null (not in world?)";
                return result;
            }

            string prefabName = useFallback ? FallbackPrefab : PrimaryPrefab;
            Plugin.Log.LogMessage($"[NpcBootstrap] resolve prefab '{prefabName}' (fallback={useFallback})");

            GameObject prefab = PrefabManager.Instance.GetPrefab(prefabName);
            if (prefab == null)
            {
                result.Error = $"prefab '{prefabName}' not found in PrefabManager";
                Plugin.Log.LogWarning($"[NpcBootstrap] {result.Error}");
                return result;
            }

            ZNetView prefabView = prefab.GetComponent<ZNetView>();
            if (prefabView == null)
            {
                result.Error = $"prefab '{prefabName}' has no ZNetView";
                Plugin.Log.LogWarning($"[NpcBootstrap] {result.Error}");
                return result;
            }

            Vector3 pos = player.transform.position + player.transform.forward * 2f + Vector3.up;
            GameObject go;
            try
            {
                go = Object.Instantiate(prefab, pos, Quaternion.identity);
            }
            catch (Exception ex)
            {
                result.Error = $"Instantiate threw: {ex.Message}";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                return result;
            }

            if (go == null)
            {
                result.Error = "Instantiate returned null";
                return result;
            }

            result.Go = go;
            result.PrefabUsed = prefabName;
            Plugin.Log.LogMessage($"[NpcBootstrap] instantiated '{prefabName}' at {pos}");

            // 2b. network identity
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                result.Error = "instance has no valid ZNetView/network identity";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3a. character
            Character character = go.GetComponent<Character>();
            if (character == null)
            {
                result.Error = "no Character component";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3b. monster AI
            MonsterAI monsterAI = go.GetComponent<MonsterAI>();
            if (monsterAI == null && go.GetComponent<Humanoid>() != null)
            {
                Plugin.Log.LogMessage("[NpcBootstrap] no MonsterAI, attempting AddComponent<MonsterAI>()");
                try
                {
                    monsterAI = go.AddComponent<MonsterAI>();
                }
                catch (Exception ex)
                {
                    result.Error = $"AddComponent<MonsterAI> threw: {ex.Message}";
                    Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                    CleanupFailed(go);
                    return result;
                }
            }

            if (monsterAI == null)
            {
                result.Error = "no MonsterAI and could not add one";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3c. base AI
            BaseAI baseAI = character.GetBaseAI();
            if (baseAI == null)
            {
                result.Error = "Character.GetBaseAI() is null";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3d. faction
            try
            {
                character.m_faction = Character.Faction.Players;
                Plugin.Log.LogMessage("[NpcBootstrap] faction set to Players");
            }
            catch (Exception ex)
            {
                result.Error = $"set faction threw: {ex.Message}";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3e. tameable
            Tameable tameable = go.GetComponent<Tameable>();
            if (tameable == null)
            {
                Plugin.Log.LogMessage("[NpcBootstrap] no Tameable, attempting AddComponent<Tameable>()");
                try
                {
                    tameable = go.AddComponent<Tameable>();
                }
                catch (Exception ex)
                {
                    result.Error = $"AddComponent<Tameable> threw: {ex.Message}";
                    Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                    CleanupFailed(go);
                    return result;
                }
            }

            if (tameable == null)
            {
                result.Error = "no Tameable and could not add one";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            tameable.m_commandable = true;

            // 3f. tame — prefer public MakeTame()
            try
            {
                monsterAI.MakeTame();
                Plugin.Log.LogMessage("[NpcBootstrap] MonsterAI.MakeTame() called");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] MakeTame threw ({ex.Message}), trying Character.SetTamed");
                try
                {
                    character.SetTamed(true);
                }
                catch (Exception ex2)
                {
                    result.Error = $"tame failed: {ex2.Message}";
                    Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                    CleanupFailed(go);
                    return result;
                }
            }

            // 3g. merchant neutralize (if present)
            Trader trader = go.GetComponent<Trader>();
            if (trader != null)
            {
                Plugin.Log.LogWarning("[NpcBootstrap] Trader present — merchant Interact may steal E-key (Stage 2 caveat)");
            }

            // 3h. verify
            bool tamed = character.IsTamed();
            var faction = character.GetFaction();
            ZDOID id = character.GetZDOID();
            Plugin.Log.LogMessage(
                $"[NpcBootstrap] verify: IsTamed={tamed}, Faction={faction}, commandable={tameable.m_commandable}, ZDOID={id}");

            if (!tamed)
            {
                result.Error = "verify failed: IsTamed()==false after MakeTame";
                Plugin.Log.LogError($"[NpcBootstrap] {result.Error}");
                CleanupFailed(go);
                return result;
            }

            // 3i. archer loadout (bow + ammo). Non-fatal: spawn still succeeds without a bow.
            try
            {
                EquipArcherLoadout(go);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] archer loadout failed: {ex.Message}");
            }

            result.Success = true;
            Plugin.Log.LogMessage($"[NpcBootstrap] SUCCESS prefab={result.PrefabUsed} ZDOID={id}");
            return result;
        }

        /// <summary>Ammo stack granted with the bow (clamped to the ammo item's max stack).</summary>
        private const int ArcherAmmoStack = 100;

        /// <summary>
        /// Gives and equips a bow + matching ammo so the companion fights as an archer.
        /// Bow/ammo prefab names live in asset bundles, not in assembly literals, so items are
        /// resolved from ZNetScene at spawn time (cheapest first) instead of being hardcoded.
        /// </summary>
        private static void EquipArcherLoadout(GameObject go)
        {
            Humanoid humanoid = go.GetComponent<Humanoid>();
            if (humanoid == null)
            {
                Plugin.Log.LogWarning("[NpcBootstrap] archer loadout: no Humanoid component, skipping");
                return;
            }

            // GiveDefaultItems() runs later from Start() and would re-add weapon/shield/ammo items:
            // a shield or ammo replaces the bow in m_leftItem/m_ammoItem and a second weapon gives
            // EquipBestWeapon an alternative to the bow. Strip those pools here; armor stays.
            humanoid.m_defaultItems = FilterOutCompetingItems(humanoid.m_defaultItems);
            humanoid.m_randomSets = FilterOutCompetingSets(humanoid.m_randomSets);
            humanoid.m_randomItems = FilterOutCompetingRandomItems(humanoid.m_randomItems);
            humanoid.m_randomWeapon = Array.Empty<GameObject>();
            humanoid.m_randomShield = Array.Empty<GameObject>();

            GameObject bowPrefab = FindItemPrefab(ItemDrop.ItemData.ItemType.Bow, requiredAmmoType: null);
            if (bowPrefab == null)
            {
                Plugin.Log.LogWarning("[NpcBootstrap] archer loadout: no ItemType.Bow prefab found — companion spawns without bow");
                return;
            }

            string ammoType = bowPrefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_ammoType;
            GameObject ammoPrefab = string.IsNullOrEmpty(ammoType)
                ? null
                : FindItemPrefab(ItemDrop.ItemData.ItemType.Ammo, ammoType);

            ItemDrop.ItemData bow = humanoid.PickupPrefab(bowPrefab, 0, autoequip: false);
            if (bow == null)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] archer loadout: failed to give bow '{bowPrefab.name}'");
                return;
            }

            humanoid.EquipItem(bow, triggerEquipEffects: false);
            Plugin.Log.LogMessage($"[NpcBootstrap] archer loadout: bow '{bowPrefab.name}' (ammo type '{ammoType}')");

            if (ammoPrefab == null)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] archer loadout: no ammo prefab for type '{ammoType}' — bow without ammo");
                return;
            }

            ItemDrop.ItemData ammo = humanoid.PickupPrefab(ammoPrefab, ArcherAmmoStack, autoequip: false);
            if (ammo == null)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] archer loadout: failed to give ammo '{ammoPrefab.name}'");
                return;
            }

            humanoid.EquipItem(ammo, triggerEquipEffects: false);
            Plugin.Log.LogMessage($"[NpcBootstrap] archer loadout: ammo '{ammoPrefab.name}' x{ammo.m_stack}");
        }

        /// <summary>Cheapest item prefab of the given type; when <paramref name="requiredAmmoType"/> is set, ammo type must match.</summary>
        private static GameObject FindItemPrefab(ItemDrop.ItemData.ItemType type, string requiredAmmoType)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null)
            {
                return null;
            }

            GameObject best = null;
            int bestValue = int.MaxValue;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                best = ConsiderItemPrefab(prefab, type, requiredAmmoType, best, ref bestValue);
            }

            foreach (GameObject prefab in scene.m_nonNetViewPrefabs)
            {
                best = ConsiderItemPrefab(prefab, type, requiredAmmoType, best, ref bestValue);
            }

            return best;
        }

        private static GameObject ConsiderItemPrefab(GameObject prefab, ItemDrop.ItemData.ItemType type,
            string requiredAmmoType, GameObject best, ref int bestValue)
        {
            if (prefab == null)
            {
                return best;
            }

            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            if (drop == null)
            {
                return best;
            }

            ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
            if (shared.m_itemType != type)
            {
                return best;
            }

            if (requiredAmmoType != null)
            {
                if (shared.m_ammoType != requiredAmmoType)
                {
                    return best;
                }
            }
            else if (type == ItemDrop.ItemData.ItemType.Bow && string.IsNullOrEmpty(shared.m_ammoType))
            {
                // A bow with no ammo type cannot shoot — skip it.
                return best;
            }

            if (shared.m_value >= bestValue)
            {
                return best;
            }

            bestValue = shared.m_value;
            return prefab;
        }

        private static bool CompetesWithBow(GameObject prefab)
        {
            ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
            if (drop == null)
            {
                return false;
            }

            switch (drop.m_itemData.m_shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Torch:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return true;
                default:
                    return false;
            }
        }

        private static GameObject[] FilterOutCompetingItems(GameObject[] items)
        {
            if (items == null || items.Length == 0)
            {
                return Array.Empty<GameObject>();
            }

            var kept = new System.Collections.Generic.List<GameObject>(items.Length);
            foreach (GameObject prefab in items)
            {
                if (prefab == null || !CompetesWithBow(prefab))
                {
                    kept.Add(prefab);
                }
            }

            return kept.ToArray();
        }

        private static Humanoid.ItemSet[] FilterOutCompetingSets(Humanoid.ItemSet[] sets)
        {
            if (sets == null || sets.Length == 0)
            {
                return Array.Empty<Humanoid.ItemSet>();
            }

            foreach (Humanoid.ItemSet set in sets)
            {
                if (set != null)
                {
                    set.m_items = FilterOutCompetingItems(set.m_items);
                }
            }

            return sets;
        }

        private static Humanoid.RandomItem[] FilterOutCompetingRandomItems(Humanoid.RandomItem[] items)
        {
            if (items == null || items.Length == 0)
            {
                return Array.Empty<Humanoid.RandomItem>();
            }

            var kept = new System.Collections.Generic.List<Humanoid.RandomItem>(items.Length);
            foreach (Humanoid.RandomItem item in items)
            {
                if (item == null || item.m_prefab == null || !CompetesWithBow(item.m_prefab))
                {
                    kept.Add(item);
                }
            }

            return kept.ToArray();
        }

        public static string DescribeComponents(GameObject go)
        {
            if (go == null) return "(null)";
            var sb = new StringBuilder();
            Component[] comps = go.GetComponents<Component>();
            sb.AppendLine($"components ({comps.Length}):");
            foreach (Component c in comps)
            {
                string typeName = c == null ? "(null)" : c.GetType().FullName;
                sb.Append("  - ").AppendLine(typeName);
            }

            return sb.ToString();
        }

        private static void CleanupFailed(GameObject go)
        {
            try
            {
                if (go != null) Object.Destroy(go);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NpcBootstrap] cleanup failed: {ex.Message}");
            }
        }
    }
}

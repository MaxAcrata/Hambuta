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

            result.Success = true;
            Plugin.Log.LogMessage($"[NpcBootstrap] SUCCESS prefab={result.PrefabUsed} ZDOID={id}");
            return result;
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

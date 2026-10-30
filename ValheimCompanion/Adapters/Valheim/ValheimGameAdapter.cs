using System;
using System.Globalization;
using UnityEngine;
using ValheimCompanion.Core;

namespace ValheimCompanion.Adapters.Valheim
{
    /// <summary>Only place allowed to use Valheim types.</summary>
    internal sealed class ValheimGameAdapter : IGameAdapter
    {
        // Stage 5: ONE move method — MonsterAI.SetFollowTarget(markerGO)
        // Follow stop radius is 3 m (BaseAI.Follow); success threshold 3.5 m.
        private const float SuccessDistance = 3.5f;
        private const float PollInterval = 0.5f;
        private const float TimeoutSeconds = 30f;
        private const float TimeoutCapSeconds = 60f;
        private const int SuccessPollsRequired = 2;

        /// <summary>ZDO string key marking NPCs spawned/owned by this mod.</summary>
        public const string CompanionZdoTag = "vc_companion";

        private GameObject _companionGo;
        private ZDOID _companionId = ZDOID.None;
        private string _prefabUsed;
        private string _lastError;

        private MoveJobStatus _moveStatus = MoveJobStatus.None;
        private Vector3 _moveTarget;
        private float _moveDistance = float.NaN;
        private GameObject _markerGo;
        private float _moveElapsed;
        private float _pollTimer;
        private int _successStreak;
        private string _beforeBlock = "";
        private string _afterBlock = "";
        private float _beforeDistance = float.NaN;

        // Clean start: 0 bots at world load — purge companion-like leftovers once.
        private const float PurgeDelaySeconds = 5f;
        private bool _purgeArmed;
        private bool _purgeDone;
        private float _purgeCountdown;

        public SpawnStatus SpawnCompanion(bool useFallback)
        {
            if (HasCompanion)
            {
                _lastError = "already have a living companion";
                Plugin.Log.LogWarning($"[Adapter] spawn refused: {_lastError}");
                return SpawnStatus.Failed;
            }

            // World reload leaves persistent NPCs behind while we lose the GameObject ref.
            if (TryRebindExisting(log: true))
            {
                Plugin.Log.LogMessage("[Adapter] re-bound to existing companion instead of spawning");
                return SpawnStatus.Success;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _lastError = "Player.m_localPlayer is null (not in world?)";
                Plugin.Log.LogWarning($"[Adapter] spawn refused: {_lastError}");
                return SpawnStatus.Failed;
            }

            NpcBootstrap.SpawnResult result = NpcBootstrap.Spawn(player, useFallback);
            if (!result.Success)
            {
                _lastError = result.Error;
                return SpawnStatus.Failed;
            }

            _companionGo = result.Go;
            _prefabUsed = result.PrefabUsed;
            Character ch = _companionGo.GetComponent<Character>();
            _companionId = ch != null ? ch.GetZDOID() : ZDOID.None;
            MarkAsOurs(_companionGo);
            _lastError = null;
            Plugin.Log.LogMessage($"[Adapter] companion registered ZDOID={_companionId}");
            return SpawnStatus.Success;
        }

        /// <summary>
        /// Re-attach to a living companion tagged by this mod (survives world reload).
        /// </summary>
        public bool TryRebindExisting(bool log)
        {
            if (HasCompanion) return true;

            Character found = FindTaggedCompanion(preferNear: Player.m_localPlayer);
            if (found == null)
            {
                if (log) Plugin.Log.LogMessage("[Adapter] rebind: no tagged companion in world");
                return false;
            }

            _companionGo = found.gameObject;
            _prefabUsed = found.gameObject.name.Replace("(Clone)", "").Trim();
            _companionId = found.GetZDOID();
            _lastError = null;
            if (log)
            {
                Plugin.Log.LogMessage(
                    $"[Adapter] rebind: '{_prefabUsed}' ZDOID={_companionId} pos={Fmt(found.transform.position)}");
            }

            return true;
        }

        /// <summary>Collect all live Characters (engine list first, then scene scan).</summary>
        internal static System.Collections.Generic.List<Character> AllCharacters()
        {
            var result = new System.Collections.Generic.List<Character>();
            try
            {
                foreach (Character c in Character.GetAllCharacters())
                {
                    if (c != null) result.Add(c);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Adapter] GetAllCharacters failed: {ex.Message}");
            }

            if (result.Count == 0)
            {
                try
                {
                    Character[] scene = UnityEngine.Object.FindObjectsByType<Character>(
                        FindObjectsSortMode.None);
                    foreach (Character c in scene)
                    {
                        if (c != null) result.Add(c);
                    }

                    Plugin.Log.LogMessage($"[Adapter] scene scan found {scene.Length} Characters (engine list was empty)");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Adapter] FindObjectsOfType failed: {ex.Message}");
                }
            }

            return result;
        }

        public int CountTaggedCompanions()
        {
            int n = 0;
            foreach (Character c in AllCharacters())
            {
                if (LooksLikeCompanion(c)) n++;
            }

            return n;
        }

        /// <summary>Destroy companion-like NPCs. Optionally keep the currently bound one.</summary>
        public int CleanupTagged(bool keepCurrent)
        {
            int destroyed = 0;
            GameObject keep = keepCurrent ? _companionGo : null;
            foreach (Character c in AllCharacters())
            {
                if (!LooksLikeCompanion(c)) continue;
                if (keep != null && c.gameObject == keep) continue;
                try
                {
                    Plugin.Log.LogMessage(
                        $"[Adapter] cleanup destroy name={c.gameObject.name} m_name={c.m_name} ZDOID={c.GetZDOID()} tagged={IsTaggedOurs(c)}");
                    UnityEngine.Object.Destroy(c.gameObject);
                    destroyed++;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Adapter] cleanup failed: {ex.Message}");
                }
            }

            return destroyed;
        }

        /// <summary>Human-readable dump of every character — diagnostic.</summary>
        public string ScanCharacters()
        {
            var sb = new System.Text.StringBuilder();
            var list = AllCharacters();
            sb.AppendLine($"Characters count={list.Count}");
            int i = 0;
            foreach (Character c in list)
            {
                if (c == null) continue;
                ZNetView nv = c.GetComponent<ZNetView>();
                string tag = "(none)";
                if (nv != null && nv.IsValid())
                {
                    string t = nv.GetZDO().GetString(CompanionZdoTag);
                    if (!string.IsNullOrEmpty(t)) tag = t;
                }

                bool tamed = false;
                try { tamed = c.IsTamed(); } catch { /* ignore */ }

                sb.AppendLine(
                    $"  [{i}] go='{c.gameObject.name}' m_name='{c.m_name}' " +
                    $"faction={c.GetFaction()} tamed={tamed} dead={c.IsDead()} " +
                    $"zdoid={c.GetZDOID()} tag={tag} match={LooksLikeCompanion(c)}");
                i++;
            }

            return sb.ToString();
        }

        /// <summary>
        /// True for our companions: ZDO tag (new) OR legacy heuristic
        /// (tamed + Players + Skeleton_Friendly/Skeleton prefab name).
        /// </summary>
        private static bool LooksLikeCompanion(Character c)
        {
            if (c == null || c.IsDead()) return false;

            if (IsTaggedOurs(c)) return true;

            // Legacy (pre-tag) spawns from 0.2.0/0.3.0
            try
            {
                if (!c.IsTamed()) return false;
                if (c.GetFaction() != Character.Faction.Players) return false;

                string goName = c.gameObject.name ?? "";
                bool skeletonLike =
                    goName.IndexOf("Skeleton_Friendly", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    goName.IndexOf("Skeleton", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!skeletonLike) return false;

                // Must look like our spawn (has MonsterAI + Tameable path)
                return c.GetComponent<MonsterAI>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsTaggedOurs(Character c)
        {
            if (c == null || c.IsDead()) return false;
            ZNetView nview = c.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid()) return false;
            return nview.GetZDO().GetString(CompanionZdoTag) == Plugin.PluginGuid;
        }

        private static Character FindTaggedCompanion(Player preferNear)
        {
            Character best = null;
            float bestDist = float.MaxValue;
            foreach (Character c in AllCharacters())
            {
                if (!LooksLikeCompanion(c)) continue;
                if (preferNear != null)
                {
                    float d = Vector3.Distance(c.transform.position, preferNear.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
                else if (best == null)
                {
                    best = c;
                }
            }

            return best;
        }

        private static void MarkAsOurs(GameObject go)
        {
            if (go == null) return;
            ZNetView nview = go.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
            {
                Plugin.Log.LogWarning("[Adapter] cannot mark companion ZDO (no ownership)");
                return;
            }

            nview.GetZDO().Set(CompanionZdoTag, Plugin.PluginGuid);
            Plugin.Log.LogMessage($"[Adapter] tagged ZDO {CompanionZdoTag}={Plugin.PluginGuid}");
        }

        public bool HasCompanion
        {
            get
            {
                if (_companionGo == null) return false;
                Character ch = _companionGo.GetComponent<Character>();
                return ch != null && !ch.IsDead();
            }
        }

        public bool TryGetState(out CompanionEntity state)
        {
            state = null;
            if (_companionGo == null && !TryRebindExisting(log: false))
            {
                _lastError = "no companion instance";
                return false;
            }

            if (_companionGo == null)
            {
                _lastError = "no companion instance";
                return false;
            }

            Character ch = _companionGo.GetComponent<Character>();
            if (ch == null)
            {
                _lastError = "companion has no Character (destroyed?)";
                _companionGo = null;
                return false;
            }

            ZNetView nview = _companionGo.GetComponent<ZNetView>();
            MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();

            float dist = float.NaN;
            string follow = "(none)";
            if (monsterAI != null)
            {
                GameObject ft = monsterAI.GetFollowTarget();
                if (ft != null) follow = ft.name;
            }

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                dist = Vector3.Distance(_companionGo.transform.position, player.transform.position);
            }

            state = new CompanionEntity
            {
                PrefabName = _prefabUsed ?? _companionGo.name,
                DisplayName = ch.m_name,
                Zdoid = ch.GetZDOID().ToString(),
                Position = _companionGo.transform.position,
                Health = ch.GetHealth(),
                MaxHealth = ch.GetMaxHealth(),
                IsTamed = ch.IsTamed(),
                IsDead = ch.IsDead(),
                IsValid = nview != null && nview.IsValid(),
                IsOwner = nview != null && nview.IsOwner(),
                IsOnGround = ch.IsOnGround(),
                IsSwimming = ch.IsSwimming(),
                Faction = ch.GetFaction().ToString(),
                DistanceToPlayer = dist,
                FollowTarget = follow
            };
            return true;
        }

        public MoveJobStatus RequestMove(Vector3 destination)
        {
            if (_companionGo == null)
            {
                TryRebindExisting(log: true);
            }

            if (!HasCompanion)
            {
                _lastError = "no companion";
                Plugin.Log.LogWarning($"[Move] refused: {_lastError}");
                return MoveJobStatus.Aborted;
            }

            if (_moveStatus == MoveJobStatus.Pending || _moveStatus == MoveJobStatus.Running)
            {
                AbortMove("new request supersedes previous");
            }

            MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
            if (monsterAI == null)
            {
                _lastError = "no MonsterAI on companion";
                Plugin.Log.LogError($"[Move] {_lastError}");
                return MoveJobStatus.Aborted;
            }

            Character ch = _companionGo.GetComponent<Character>();
            _beforeDistance = Vector3.Distance(_companionGo.transform.position, destination);
            _beforeBlock = Snapshot("BEFORE", ch, destination, _beforeDistance);

            // Cleanup any previous marker
            DestroyMarker();

            _markerGo = new GameObject("VC_MoveMarker");
            _markerGo.transform.position = destination;

            _moveTarget = destination;
            _moveDistance = _beforeDistance;
            _moveElapsed = 0f;
            _pollTimer = 0f;
            _successStreak = 0;
            _afterBlock = "";

            // THE one move method (Stage 5)
            try
            {
                monsterAI.SetFollowTarget(_markerGo);
                Plugin.Log.LogMessage($"[Move] SetFollowTarget(marker) -> {Fmt(destination)}");
                Plugin.Log.LogMessage(_beforeBlock);
                _moveStatus = MoveJobStatus.Running;
            }
            catch (Exception ex)
            {
                _lastError = $"SetFollowTarget threw: {ex.Message}";
                Plugin.Log.LogError($"[Move] {_lastError}");
                DestroyMarker();
                return MoveJobStatus.Aborted;
            }

            return _moveStatus;
        }

        public MoveJobStatus MoveStatus => _moveStatus;

        public float MoveDistanceToTarget => _moveDistance;

        public void Tick(float dt)
        {
            TickCleanStartPurge(dt);

            if (_companionGo == null) return;

            if (_moveStatus != MoveJobStatus.Pending && _moveStatus != MoveJobStatus.Running)
                return;

            _moveElapsed += dt;
            _pollTimer += dt;

            if (_pollTimer < PollInterval) return;
            _pollTimer = 0f;

            if (_companionGo == null)
            {
                FinishMove(MoveJobStatus.Aborted, "companion destroyed");
                return;
            }

            _moveDistance = Vector3.Distance(_companionGo.transform.position, _moveTarget);
            Plugin.Log.LogMessage($"[Move] poll t={_moveElapsed:0.0}s dist={_moveDistance:0.00} m");

            if (_moveDistance <= SuccessDistance)
            {
                _successStreak++;
                Plugin.Log.LogMessage($"[Move] success poll {_successStreak}/{SuccessPollsRequired}");
                if (_successStreak >= SuccessPollsRequired)
                {
                    FinishMove(MoveJobStatus.Succeeded, $"dist={_moveDistance:0.00} <= {SuccessDistance}");
                    return;
                }
            }
            else
            {
                _successStreak = 0;
            }

            float timeout = Mathf.Min(TimeoutSeconds, TimeoutCapSeconds);
            if (_moveElapsed >= timeout)
            {
                FinishMove(MoveJobStatus.TimedOut,
                    $"timeout after {_moveElapsed:0.0}s, final dist={_moveDistance:0.00} m");
            }
        }

        /// <summary>
        /// Clean start: once per world load, destroy companion-like leftovers so the
        /// game never opens with orphan bots (they live on in the world save).
        /// Skipped if a companion is already bound (user spawned before the purge).
        /// </summary>
        private void TickCleanStartPurge(float dt)
        {
            if (!InWorld)
            {
                _purgeArmed = false;
                _purgeDone = false;
                return;
            }

            if (_purgeDone) return;

            if (!_purgeArmed)
            {
                _purgeArmed = true;
                _purgeCountdown = PurgeDelaySeconds;
                Plugin.Log.LogMessage(
                    $"[Adapter] clean start: companion purge in {PurgeDelaySeconds:0}s");
                return;
            }

            if (HasCompanion)
            {
                _purgeDone = true;
                Plugin.Log.LogMessage(
                    "[Adapter] clean start: purge skipped (companion already spawned)");
                return;
            }

            _purgeCountdown -= dt;
            if (_purgeCountdown > 0f) return;

            _purgeDone = true;
            int n = CleanupTagged(keepCurrent: false);
            Plugin.Log.LogMessage($"[Adapter] clean start: purged {n} companion-like leftover(s)");
        }

        public void DespawnCompanion()
        {
            AbortMove("despawn");
            if (_companionGo != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_companionGo);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[Adapter] despawn failed: {ex.Message}");
                }
            }

            _companionGo = null;
            _companionId = ZDOID.None;
            _moveStatus = MoveJobStatus.None;
        }

        public string LastError => _lastError;

        public GameObject CompanionGo => _companionGo;

        public string DescribeComponents() => NpcBootstrap.DescribeComponents(_companionGo);

        public string LastMoveReport => _beforeBlock + _afterBlock;

        // ---- Voice/IPC verbs (0.5.0). All reuse existing SetFollowTarget / StopMoving / RequestMove. ----

        /// <summary>FOLLOW_ME — set player as the follow target (same API as move marker).</summary>
        public MoveJobStatus RequestFollowPlayer()
        {
            if (_companionGo == null) TryRebindExisting(log: true);
            if (!HasCompanion)
            {
                _lastError = "no companion";
                Plugin.Log.LogWarning($"[Voice] follow refused: {_lastError}");
                return MoveJobStatus.Aborted;
            }

            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _lastError = "not in world (no local player)";
                Plugin.Log.LogWarning($"[Voice] follow refused: {_lastError}");
                return MoveJobStatus.Aborted;
            }

            // supersede any point-move
            if (_moveStatus == MoveJobStatus.Pending || _moveStatus == MoveJobStatus.Running)
            {
                AbortMove("follow-player supersedes move");
            }
            else
            {
                DestroyMarker();
            }

            MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
            if (monsterAI == null)
            {
                _lastError = "no MonsterAI on companion";
                return MoveJobStatus.Aborted;
            }

            try
            {
                monsterAI.SetFollowTarget(player.gameObject);
                ZNetView nview = _companionGo.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid() && nview.IsOwner())
                {
                    nview.GetZDO().Set(ZDOVars.s_follow, player.GetPlayerName());
                }

                Plugin.Log.LogMessage($"[Voice] FOLLOW_ME -> SetFollowTarget(player '{player.GetPlayerName()}')");
                return MoveJobStatus.Succeeded;
            }
            catch (Exception ex)
            {
                _lastError = $"FOLLOW_ME threw: {ex.Message}";
                Plugin.Log.LogError($"[Voice] {_lastError}");
                return MoveJobStatus.Aborted;
            }
        }

        /// <summary>COME_HERE — move to the local player position (resolved game-side, never from IPC).</summary>
        public MoveJobStatus RequestMoveToPlayer()
        {
            if (_companionGo == null) TryRebindExisting(log: true);
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                _lastError = "not in world (no local player)";
                Plugin.Log.LogWarning($"[Voice] move-here refused: {_lastError}");
                return MoveJobStatus.Aborted;
            }

            Plugin.Log.LogMessage($"[Voice] COME_HERE -> RequestMove(player pos {Fmt(player.transform.position)})");
            return RequestMove(player.transform.position);
        }

        /// <summary>STOP — clear follow target, stop moving, clear ZDO s_follow (existing cleanup).</summary>
        public MoveJobStatus RequestStop()
        {
            if (_companionGo == null) TryRebindExisting(log: true);
            if (!HasCompanion)
            {
                _lastError = "no companion";
                Plugin.Log.LogWarning($"[Voice] stop refused: {_lastError}");
                return MoveJobStatus.Aborted;
            }

            try
            {
                if (_moveStatus == MoveJobStatus.Pending || _moveStatus == MoveJobStatus.Running)
                {
                    AbortMove("STOP requested");
                }
                else
                {
                    CleanupMoveHardware();
                }

                _moveStatus = MoveJobStatus.None;
                Plugin.Log.LogMessage("[Voice] STOP -> follow cleared + StopMoving");
                return MoveJobStatus.Succeeded;
            }
            catch (Exception ex)
            {
                _lastError = $"STOP threw: {ex.Message}";
                Plugin.Log.LogError($"[Voice] {_lastError}");
                return MoveJobStatus.Aborted;
            }
        }

        /// <summary>True when the companion is following the local player.</summary>
        public bool IsFollowingPlayer()
        {
            if (_companionGo == null) return false;
            MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
            if (monsterAI == null) return false;
            GameObject ft = monsterAI.GetFollowTarget();
            if (ft == null) return false;
            Player player = Player.m_localPlayer;
            return player != null && ft == player.gameObject;
        }

        /// <summary>True when no follow target and no active move job.</summary>
        public bool IsStopped()
        {
            if (_companionGo == null) return false;
            MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
            bool noFollow = monsterAI == null || monsterAI.GetFollowTarget() == null;
            bool noMove = _moveStatus != MoveJobStatus.Pending && _moveStatus != MoveJobStatus.Running;
            return noFollow && noMove;
        }

        public string FollowTargetName
        {
            get
            {
                if (_companionGo == null) return "";
                MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
                GameObject ft = monsterAI != null ? monsterAI.GetFollowTarget() : null;
                return ft != null ? ft.name : "";
            }
        }

        public bool InWorld => Player.m_localPlayer != null;

        public float DistanceToPlayer
        {
            get
            {
                if (_companionGo == null) return float.NaN;
                Player player = Player.m_localPlayer;
                if (player == null) return float.NaN;
                return Vector3.Distance(_companionGo.transform.position, player.transform.position);
            }
        }

        private void FinishMove(MoveJobStatus status, string reason)
        {
            Character ch = _companionGo != null ? _companionGo.GetComponent<Character>() : null;
            _afterBlock = Snapshot($"AFTER ({status}: {reason})", ch, _moveTarget, _moveDistance);
            Plugin.Log.LogMessage(_afterBlock);
            CleanupMoveHardware();
            _moveStatus = status;
        }

        private void AbortMove(string reason)
        {
            if (_moveStatus == MoveJobStatus.Pending || _moveStatus == MoveJobStatus.Running)
            {
                Plugin.Log.LogWarning($"[Move] aborted: {reason}");
                Character ch = _companionGo != null ? _companionGo.GetComponent<Character>() : null;
                _afterBlock = Snapshot($"ABORTED ({reason})", ch, _moveTarget, _moveDistance);
                Plugin.Log.LogMessage(_afterBlock);
                _moveStatus = MoveJobStatus.Aborted;
            }

            CleanupMoveHardware();
        }

        private void CleanupMoveHardware()
        {
            try
            {
                if (_companionGo != null)
                {
                    MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
                    BaseAI baseAI = _companionGo.GetComponent<BaseAI>();
                    if (monsterAI != null)
                    {
                        monsterAI.SetFollowTarget(null);
                        Plugin.Log.LogMessage("[Move] SetFollowTarget(null)");
                    }

                    baseAI?.StopMoving();

                    // clear persisted follow name so UpdateSavedFollowTarget does not re-attach
                    ZNetView nview = _companionGo.GetComponent<ZNetView>();
                    if (nview != null && nview.IsValid() && nview.IsOwner())
                    {
                        nview.GetZDO().Set(ZDOVars.s_follow, "");
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Move] cleanup: {ex.Message}");
            }

            DestroyMarker();
        }

        private void DestroyMarker()
        {
            if (_markerGo != null)
            {
                try
                {
                    UnityEngine.Object.Destroy(_markerGo);
                }
                catch
                {
                    // ignore
                }

                _markerGo = null;
            }
        }

        private string Snapshot(string label, Character ch, Vector3 dest, float dist)
        {
            string pos = ch != null ? Fmt(ch.transform.position) : "(no character)";
            string alerted = "(n/a)";
            string swimming = "(n/a)";
            string follow = "(none)";
            if (_companionGo != null)
            {
                MonsterAI monsterAI = _companionGo.GetComponent<MonsterAI>();
                if (monsterAI != null)
                {
                    alerted = monsterAI.IsAlerted().ToString();
                    GameObject ft = monsterAI.GetFollowTarget();
                    if (ft != null) follow = ft.name;
                }

                if (ch != null) swimming = ch.IsSwimming().ToString();
            }

            return
                $"[Move] {label}: pos={pos} dest={Fmt(dest)} distToDest={FmtDist(dist)} " +
                $"IsAlerted={alerted} IsSwimming={swimming} follow={follow}";
        }

        internal static string Fmt(Vector3 v)
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", v.x, v.y, v.z);
        }

        internal static string FmtDist(float d)
        {
            return float.IsNaN(d) ? "n/a" : d.ToString("0.00", CultureInfo.InvariantCulture) + " m";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using Jotunn.Entities;
using UnityEngine;
using ValheimCompanion.Core;

namespace ValheimCompanion.Commands
{
    /// <summary>
    /// Single Jötunn command `vc` (names cannot contain spaces).
    /// Subcommands: spawn [fallback] | state | status | move here|x y z | inspect | despawn | help
    /// </summary>
    internal sealed class VcCommand : ConsoleCommand
    {
        private readonly CompanionController _controller;
        private readonly Adapters.Valheim.ValheimGameAdapter _adapter;

        public VcCommand(CompanionController controller, Adapters.Valheim.ValheimGameAdapter adapter)
        {
            _controller = controller;
            _adapter = adapter;
        }

        public override string Name => "vc";

        public override string Help =>
            "ValheimCompanion diagnostics. " +
            "Usage: vc spawn [fallback] | vc state | vc status | vc move here|x y z | " +
            "vc inspect | vc scan | vc adopt | vc cleanup [keep] | vc despawn | vc help";

        public override bool IsCheat => false;

        public override void Run(string[] args, Terminal context)
        {
            try
            {
                RunCore(args, context);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[vc] exception: {ex}");
                Out(context, $"<color=#8b0000>vc error: {ex.Message}</color>");
            }
        }

        public override List<string> CommandOptionList()
        {
            return new List<string>
            {
                "spawn", "fallback", "state", "status", "move", "here", "inspect", "scan",
                "adopt", "cleanup", "keep", "despawn", "help"
            };
        }

        private void RunCore(string[] args, Terminal context)
        {
            // Jötunn passes args WITHOUT the command name (Skip(1)).
            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "help";

            switch (sub)
            {
                case "spawn":
                    bool useFallback = args.Length > 1 &&
                                       args[1].ToLowerInvariant().StartsWith("fallback");
                    Spawn(context, useFallback);
                    break;

                case "state":
                    State(context);
                    break;

                case "status":
                    Status(context);
                    break;

                case "move":
                    Move(context, args);
                    break;

                case "inspect":
                    Inspect(context, args);
                    break;

                case "despawn":
                    _controller.Despawn();
                    Out(context, "companion despawned (if any)");
                    break;

                case "adopt":
                    if (_adapter.TryRebindExisting(log: true))
                    {
                        Out(context, "<color=#00ff00>re-bound to existing companion</color>");
                        State(context);
                    }
                    else
                    {
                        Out(context, "<color=#8b0000>no companion-like NPC found in world</color>");
                        Out(context, _adapter.ScanCharacters());
                    }
                    break;

                case "cleanup":
                    // default: destroy ALL companion-like NPCs (including pre-tag orphans)
                    // vc cleanup keep -> keep the currently bound one
                    bool keep = args.Length > 1 && args[1].ToLowerInvariant() == "keep";
                    int n = _adapter.CleanupTagged(keepCurrent: keep);
                    Out(context, $"cleanup destroyed {n} companion-like NPC(s) (keepCurrent={keep})");
                    Out(context, $"still present: {_adapter.CountTaggedCompanions()}");
                    break;

                case "scan":
                    Out(context, _adapter.ScanCharacters());
                    break;

                case "help":
                default:
                    Out(context, Help);
                    break;
            }
        }

        private void Spawn(Terminal context, bool useFallback)
        {
            Out(context, $"spawning companion (fallback={useFallback})...");
            SpawnStatus st = _controller.Spawn(useFallback);
            if (st == SpawnStatus.Success)
            {
                Out(context, "<color=#00ff00>spawn OK</color>");
                State(context);
            }
            else
            {
                string err = _adapter.LastError ?? "unknown";
                Out(context, $"<color=#8b0000>spawn FAILED: {err}</color>");
            }
        }

        private void State(Terminal context)
        {
            if (!_controller.TryGetState(out CompanionEntity s))
            {
                Out(context, $"<color=#8b0000>no state: {_adapter.LastError ?? "no companion"}</color>");
                return;
            }

            // 10-field block (plan Stage 4) + extras used by Stage 5
            Out(context, "--- companion state ---");
            Out(context, $"1 prefab          : {s.PrefabName}");
            Out(context, $"2 name            : {s.DisplayName}");
            Out(context, $"3 ZDOID           : {s.Zdoid}");
            Out(context, $"4 position        : {Fmt(s.Position)}");
            Out(context, $"5 health          : {s.Health:0.##} / {s.MaxHealth:0.##}");
            Out(context, $"6 IsTamed         : {s.IsTamed}");
            Out(context, $"7 s_follow        : {s.FollowTarget}");
            Out(context, $"8 OnGround/Swim   : {s.IsOnGround} / {s.IsSwimming}");
            Out(context, $"9 IsValid/Owner   : {s.IsValid} / {s.IsOwner}");
            Out(context, $"10 dist to player : {Adapters.Valheim.ValheimGameAdapter.FmtDist(s.DistanceToPlayer)}");
            Out(context, $"   faction        : {s.Faction}");
            Out(context, $"   IsDead         : {s.IsDead}");
        }

        private void Status(Terminal context)
        {
            _adapter.TryRebindExisting(log: false);
            Out(context, $"move-job : {_controller.MoveStatus}");
            Out(context, $"distance : {Adapters.Valheim.ValheimGameAdapter.FmtDist(_controller.MoveDistanceToTarget)}");
            Out(context, $"tagged   : {_adapter.CountTaggedCompanions()} in world");
            if (_controller.TryGetState(out CompanionEntity s))
            {
                Out(context, $"bound    : {s.PrefabName} ZDOID={s.Zdoid}");
                Out(context, $"pos      : {Fmt(s.Position)}");
                Out(context, $"dist ply : {Adapters.Valheim.ValheimGameAdapter.FmtDist(s.DistanceToPlayer)}");
                Out(context, $"follow   : {s.FollowTarget}");
            }
            else
            {
                Out(context, "bound    : (none — vc adopt or vc spawn)");
            }

            if (_adapter.LastMoveReport != null && _adapter.LastMoveReport.Length > 0)
            {
                Out(context, "--- last move report ---");
                Out(context, _adapter.LastMoveReport);
            }
        }

        private void Move(Terminal context, string[] args)
        {
            // Stage 5 will implement the single move method. Stage 4 records the request only.
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                Out(context, "<color=#8b0000>not in world</color>");
                return;
            }

            Vector3 dest;
            if (args.Length >= 2 && args[1].ToLowerInvariant() == "here")
            {
                dest = player.transform.position;
            }
            else if (args.Length >= 4 &&
                     float.TryParse(args[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                     float.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
                     float.TryParse(args[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                dest = new Vector3(x, y, z);
            }
            else
            {
                Out(context, "usage: vc move here | vc move <x> <y> <z>");
                return;
            }

            if (!_adapter.HasCompanion)
            {
                Out(context, "<color=#8b0000>no companion — vc spawn first</color>");
                return;
            }

            MoveJobStatus st = _controller.RequestMove(dest);
            Out(context, $"move request -> {Fmt(dest)} status={st}");
            if (st == MoveJobStatus.Aborted)
            {
                Out(context, $"<color=#8b0000>move aborted: {_adapter.LastError}</color>");
            }
            else
            {
                Out(context, "polling every 0.5s; success dist <= 3.5 m (2 polls); timeout 30s. Watch log / vc status.");
            }
        }

        private void Inspect(Terminal context, string[] args)
        {
            if (args.Length >= 2)
            {
                GameObject prefab = Jotunn.Managers.PrefabManager.Instance.GetPrefab(args[1]);
                if (prefab == null)
                {
                    Out(context, $"<color=#8b0000>prefab '{args[1]}' not found</color>");
                    return;
                }

                Out(context, $"--- prefab '{args[1]}' ---");
                Out(context, Adapters.Valheim.NpcBootstrap.DescribeComponents(prefab));
                return;
            }

            if (_adapter.CompanionGo != null)
            {
                Out(context, "--- live companion ---");
                Out(context, _adapter.DescribeComponents());
            }
            else
            {
                Out(context, "usage: vc inspect <prefabName> | vc inspect (live companion)");
            }
        }

        private static void Out(Terminal context, string line)
        {
            Plugin.Log.LogMessage($"[vc] {line}");
            context?.AddString(line);
        }

        private static string Fmt(Vector3 v)
        {
            return Adapters.Valheim.ValheimGameAdapter.Fmt(v);
        }
    }
}

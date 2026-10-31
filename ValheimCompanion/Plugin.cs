using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using UnityEngine;
using ValheimCompanion.Adapters.Valheim;
using ValheimCompanion.Commands;
using ValheimCompanion.Core;
using ValheimCompanion.Core.Ipc;
using ValheimCompanion.Ipc;

namespace ValheimCompanion
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheimcompanion.prototype";
        public const string PluginName = "ValheimCompanion";
        public const string PluginVersion = "0.5.4";

        internal static ManualLogSource Log;

        private CompanionController _controller;
        private ValheimGameAdapter _adapter;
        private IpcServer _ipc;
        private MicCommand _micCommand;

        private ConfigEntry<bool> _ipcEnabled;
        private ConfigEntry<int> _ipcPort;

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage($"{PluginName} {PluginVersion} Awake");
            Log.LogMessage($"  BepInEx: {typeof(BaseUnityPlugin).Assembly.GetName().Version}");
            Log.LogMessage($"  Harmony: {typeof(Harmony).Assembly.GetName().Version}");
            Log.LogMessage($"  Jotunn: {typeof(Main).Assembly.GetName().Version}");
            Log.LogMessage($"  Unity: {Application.unityVersion}");

            _ipcEnabled = Config.Bind("Voice", "IpcEnabled", true,
                "Expose localhost IPC for the optional Python voice module");
            _ipcPort = Config.Bind("Voice", "IpcPort", 8765,
                "Loopback port for the voice IPC bridge (127.0.0.1 only)");

            _adapter = new ValheimGameAdapter();
            _controller = new CompanionController(_adapter, new NullBrain());

            CommandManager.Instance.AddConsoleCommand(new VcCommand(_controller, _adapter));
            _micCommand = new MicCommand(VoiceRootPath());
            CommandManager.Instance.AddConsoleCommand(_micCommand);

            if (_ipcEnabled.Value)
            {
                _ipc = new IpcServer("127.0.0.1", _ipcPort.Value, ExecuteIpcCommand, BuildHealthJson);
                if (!_ipc.Start())
                {
                    Log.LogWarning("[IPC] voice bridge not started");
                    _ipc = null;
                }
            }
            else
            {
                Log.LogMessage("[IPC] disabled via config (Voice.IpcEnabled=false)");
            }

            PrefabManager.OnVanillaPrefabsAvailable += () =>
                Log.LogMessage($"{PluginName}: OnVanillaPrefabsAvailable");
            PrefabManager.OnPrefabsRegistered += () =>
                Log.LogMessage($"{PluginName}: OnPrefabsRegistered");

            Log.LogMessage($"{PluginName}: Awake complete (voice IPC + vc commands)");
        }

        private void Update()
        {
            _controller?.Tick(Time.deltaTime);
            _ipc?.DrainMainThread();
        }

        private void OnDestroy()
        {
            _micCommand?.StopOnExit();
            _ipc?.Dispose();
            _ipc = null;
        }

        /// <summary>Voice module folder: &lt;repo&gt;\voice next to the project, or beside the plugin.</summary>
        private static string VoiceRootPath()
        {
            // Prefer the development tree path used on this machine; fall back to a folder
            // next to the game install so the command still works if only the plugin is deployed.
            string[] candidates =
            {
                System.IO.Path.Combine("D:\\Projects\\Valheim bot", "voice"),
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "",
                    "..", "..", "..", "..", "voice"),
                System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "",
                    "..", "..", "..", "voice"),
            };
            foreach (string c in candidates)
            {
                try
                {
                    string full = System.IO.Path.GetFullPath(c);
                    if (System.IO.Directory.Exists(full)) return full;
                }
                catch
                {
                    // ignore
                }
            }

            return System.IO.Path.Combine("D:\\Projects\\Valheim bot", "voice");
        }

        // ---- main-thread IPC execution (only place that touches game API from the bridge) ----

        private IpcCommandResult ExecuteIpcCommand(IpcCommandKind kind)
        {
            switch (kind)
            {
                case IpcCommandKind.Status:
                    return ExecStatus();
                case IpcCommandKind.FollowMe:
                    return ExecFollowMe();
                case IpcCommandKind.ComeHere:
                    return ExecComeHere();
                case IpcCommandKind.Stop:
                    return ExecStop();
                default:
                    return IpcCommandResult.Error("unknown command");
            }
        }

        private IpcCommandResult ExecStatus()
        {
            if (!_adapter.InWorld)
                return IpcCommandResult.Fail("not in world");
            if (!_controller.TryGetState(out var s))
                return IpcCommandResult.Fail(_adapter.LastError ?? "no companion — use vc spawn");

            string stateJson = IpcJson.StateBlock(
                s.PrefabName, s.DisplayName, s.Zdoid,
                s.Position.x, s.Position.y, s.Position.z,
                s.Health, s.MaxHealth,
                s.IsTamed, s.IsDead, s.IsValid, s.IsOwner,
                s.Faction, s.DistanceToPlayer, s.FollowTarget);
            return IpcCommandResult.Ok("status", stateJson);
        }

        private IpcCommandResult ExecFollowMe()
        {
            if (!_adapter.InWorld)
                return IpcCommandResult.Fail("not in world");
            MoveJobStatus st = _adapter.RequestFollowPlayer();
            if (st == MoveJobStatus.Aborted)
                return IpcCommandResult.Fail(_adapter.LastError ?? "follow failed");
            // Verification is done by the client polling /health (follow_target).
            return IpcCommandResult.Ok("follow target set to local player");
        }

        private IpcCommandResult ExecComeHere()
        {
            if (!_adapter.InWorld)
                return IpcCommandResult.Fail("not in world");
            MoveJobStatus st = _adapter.RequestMoveToPlayer();
            if (st == MoveJobStatus.Aborted)
                return IpcCommandResult.Fail(_adapter.LastError ?? "move refused");
            // Long-running move job is verified by the client polling /health (move_status).
            return IpcCommandResult.Ok("move to player started");
        }

        private IpcCommandResult ExecStop()
        {
            if (!_adapter.InWorld)
                return IpcCommandResult.Fail("not in world");
            MoveJobStatus st = _adapter.RequestStop();
            if (st == MoveJobStatus.Aborted)
                return IpcCommandResult.Fail(_adapter.LastError ?? "stop failed");
            return IpcCommandResult.Ok("follow cleared, movement stopped");
        }

        private string BuildHealthJson()
        {
            string moveStatus;
            switch (_controller.MoveStatus)
            {
                case MoveJobStatus.Pending: moveStatus = "pending"; break;
                case MoveJobStatus.Running: moveStatus = "running"; break;
                case MoveJobStatus.Succeeded: moveStatus = "succeeded"; break;
                case MoveJobStatus.TimedOut: moveStatus = "timeout"; break;
                case MoveJobStatus.Aborted: moveStatus = "aborted"; break;
                default: moveStatus = "none"; break;
            }

            return IpcJson.Health(
                ok: true,
                version: PluginVersion,
                inWorld: _adapter.InWorld,
                hasCompanion: _adapter.HasCompanion,
                moveStatus: moveStatus,
                moveDistance: _controller.MoveDistanceToTarget,
                followTarget: _adapter.FollowTargetName,
                distToPlayer: _adapter.DistanceToPlayer,
                lastError: _adapter.LastError ?? "");
        }
    }
}

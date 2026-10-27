using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn;
using Jotunn.Managers;
using UnityEngine;
using ValheimCompanion.Adapters.Valheim;
using ValheimCompanion.Commands;
using ValheimCompanion.Core;

namespace ValheimCompanion
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(Main.ModGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.valheimcompanion.prototype";
        public const string PluginName = "ValheimCompanion";
        public const string PluginVersion = "0.4.1";

        internal static ManualLogSource Log;

        private CompanionController _controller;
        private ValheimGameAdapter _adapter;

        private void Awake()
        {
            Log = Logger;
            Log.LogMessage($"{PluginName} {PluginVersion} Awake");
            Log.LogMessage($"  BepInEx: {typeof(BaseUnityPlugin).Assembly.GetName().Version}");
            Log.LogMessage($"  Harmony: {typeof(Harmony).Assembly.GetName().Version}");
            Log.LogMessage($"  Jotunn: {typeof(Main).Assembly.GetName().Version}");
            Log.LogMessage($"  Unity: {Application.unityVersion}");

            _adapter = new ValheimGameAdapter();
            _controller = new CompanionController(_adapter, new NullBrain());

            CommandManager.Instance.AddConsoleCommand(new VcCommand(_controller, _adapter));

            PrefabManager.OnVanillaPrefabsAvailable += () =>
                Log.LogMessage($"{PluginName}: OnVanillaPrefabsAvailable");
            PrefabManager.OnPrefabsRegistered += () =>
                Log.LogMessage($"{PluginName}: OnPrefabsRegistered");

            Log.LogMessage($"{PluginName}: Awake complete (Stage 4 — vc spawn/state/status)");
        }

        private void Update()
        {
            _controller?.Tick(Time.deltaTime);
        }
    }
}

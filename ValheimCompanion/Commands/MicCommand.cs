using System;
using System.Diagnostics;
using System.IO;
using Jotunn.Entities;

namespace ValheimCompanion.Commands
{
    /// <summary>
    /// Console command `mic` — start/stop the optional Python voice module (push-to-talk).
    /// Does not affect game mechanics; process is external to the mod.
    /// </summary>
    internal sealed class MicCommand : ConsoleCommand
    {
        private readonly string _voiceRoot;
        private Process _voiceProcess;

        public MicCommand(string voiceRoot)
        {
            _voiceRoot = voiceRoot;
        }

        public override string Name => "mic";

        public override string Help =>
            "Voice module. Usage: mic | mic stop | mic status. After `mic`: hold CTRL and speak.";

        public override bool IsCheat => false;

        public override void Run(string[] args, Terminal context)
        {
            string sub = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "start";
            switch (sub)
            {
                case "start":
                case "on":
                    Start(context);
                    break;
                case "stop":
                case "off":
                    Stop(context);
                    break;
                case "status":
                    Status(context);
                    break;
                case "help":
                    Out(context, Help);
                    break;
                default:
                    // bare `mic` already mapped to start; anything else is help
                    Out(context, Help);
                    break;
            }
        }

        private void Start(Terminal context)
        {
            if (_voiceProcess != null && !_voiceProcess.HasExited)
            {
                Out(context, "voice module already running (pid " + _voiceProcess.Id + ")");
                return;
            }

            if (string.IsNullOrEmpty(_voiceRoot) || !Directory.Exists(_voiceRoot))
            {
                Out(context, "<color=#8b0000>voice folder not found: " + _voiceRoot + "</color>");
                return;
            }

            string python = Path.Combine(_voiceRoot, ".venv", "Scripts", "python.exe");
            string script = Path.Combine(_voiceRoot, "run.py");
            if (!File.Exists(python))
            {
                Out(context, "<color=#8b0000>venv python missing: " + python + "</color>");
                Out(context, "Create it: python -m venv .venv && .venv\\Scripts\\pip install -r requirements.txt");
                return;
            }

            if (!File.Exists(script))
            {
                Out(context, "<color=#8b0000>run.py missing: " + script + "</color>");
                return;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + script + "\"",
                    WorkingDirectory = _voiceRoot,
                    UseShellExecute = true,
                    CreateNoWindow = false
                };
                _voiceProcess = Process.Start(psi);
                if (_voiceProcess == null)
                {
                    Out(context, "<color=#8b0000>failed to start voice process</color>");
                    return;
                }

                Out(context, "<color=#00ff00>voice module started</color> (pid " + _voiceProcess.Id + ")");
                Out(context, "Вернитесь в игру. Зажмите CTRL и говорите, отпустите — команда выполнится.");
                Out(context, "Окно Python можно свернуть; при выходе из игры оно закроется само. Стоп: mic stop (или Ctrl+C в окне).");
            }
            catch (Exception ex)
            {
                Out(context, "<color=#8b0000>mic start failed: " + ex.Message + "</color>");
                Plugin.Log.LogError("[mic] start: " + ex);
            }
        }

        private void Stop(Terminal context)
        {
            if (_voiceProcess == null)
            {
                Out(context, "no voice process recorded (was it started with `mic`?)");
                return;
            }

            try
            {
                if (!_voiceProcess.HasExited)
                {
                    _voiceProcess.Kill();
                    _voiceProcess.WaitForExit(3000);
                    Out(context, "voice module stopped (pid " + _voiceProcess.Id + ")");
                }
                else
                {
                    Out(context, "voice process already exited (code " + _voiceProcess.ExitCode + ")");
                }
            }
            catch (Exception ex)
            {
                Out(context, "<color=#8b0000>mic stop failed: " + ex.Message + "</color>");
            }
            finally
            {
                _voiceProcess = null;
            }
        }

        private void Status(Terminal context)
        {
            if (_voiceProcess == null)
            {
                Out(context, "voice: not started from console");
                return;
            }

            bool alive = !_voiceProcess.HasExited;
            Out(context, alive
                ? "voice: running (pid " + _voiceProcess.Id + ")"
                : "voice: exited (code " + _voiceProcess.ExitCode + ")");
        }

        /// <summary>Kill the voice module (and its console window) when the game exits.</summary>
        public void StopOnExit()
        {
            if (_voiceProcess == null) return;

            try
            {
                if (!_voiceProcess.HasExited)
                {
                    _voiceProcess.Kill();
                    _voiceProcess.WaitForExit(2000);
                    Plugin.Log.LogMessage(
                        "[mic] voice module stopped (game exit, pid " + _voiceProcess.Id + ")");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[mic] stop on game exit failed: " + ex.Message);
            }
            finally
            {
                _voiceProcess = null;
            }
        }

        private static void Out(Terminal context, string line)
        {
            Plugin.Log.LogMessage("[mic] " + line);
            context?.AddString(line);
        }
    }
}

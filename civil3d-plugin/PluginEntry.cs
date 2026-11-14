using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;

[assembly: ExtensionApplication(typeof(Civil3DMcp.PluginEntry))]
[assembly: CommandClass(typeof(Civil3DMcp.Commands))]

namespace Civil3DMcp
{
    /// <summary>Runs when Civil 3D loads the DLL (NETLOAD or autoloader bundle).</summary>
    public class PluginEntry : IExtensionApplication
    {
        public const int Port = 8765;
        internal static Listener Server;

        public void Initialize()
        {
            MainThread.Init();          // must run on Civil 3D's main thread
            Router.Init();
            Server = new Listener(Port);
            Server.Start();
            Say($"\n[Civil3D MCP] Ready. Listening on 127.0.0.1:{Port}. {Router.Count} tools loaded. Commands: MCPSTATUS, MCPSTOP, MCPSTART\n");
        }

        public void Terminate() => Server?.Stop();

        internal static void Say(string msg)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(msg); } catch { }
        }
    }

    public class Commands
    {
        [CommandMethod("MCPSTATUS")]
        public void Status() => PluginEntry.Say(
            $"\n[Civil3D MCP] {(PluginEntry.Server?.Running == true ? "RUNNING" : "STOPPED")} on port {PluginEntry.Port}, {Router.Count} tools.\n");

        [CommandMethod("MCPSTOP")]
        public void Stop() { PluginEntry.Server?.Stop(); Status(); }

        [CommandMethod("MCPSTART")]
        public void Start()
        {
            if (PluginEntry.Server?.Running != true)
            {
                PluginEntry.Server = new Listener(PluginEntry.Port);
                PluginEntry.Server.Start();
            }
            Status();
        }
    }
}

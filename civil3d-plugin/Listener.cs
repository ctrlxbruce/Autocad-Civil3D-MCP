using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;

namespace Civil3DMcp
{
    /// <summary>
    /// Tiny local TCP server. One JSON request per line, one JSON reply per line.
    /// Request:  {"id":1,"token":"...","tool":"drafting.line","args":{...}}
    /// Reply:    {"id":1,"ok":true,"result":{...}}  or  {"id":1,"ok":false,"error":"..."}
    /// Only listens on 127.0.0.1, so nothing outside your PC can reach it. Every request must carry the token
    /// from %LOCALAPPDATA%\Civil3DMcp\token-&lt;port&gt; (see Security); a line that isn't a JSON request, or a wrong
    /// token, closes the connection.
    /// </summary>
    public class Listener
    {
        private readonly int _port;
        private TcpListener _tcp;
        private Thread _thread;
        public bool Running { get; private set; }

        public Listener(int port) { _port = port; }

        public void Start()
        {
            _tcp = new TcpListener(IPAddress.Loopback, _port);
            _tcp.Start();
            Security.NewToken(_port);
            Running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Civil3DMcp.Listener" };
            _thread.Start();
        }

        public void Stop()
        {
            // A second Civil 3D can't bind the port and never writes a token, so it must not delete the first one's.
            if (Running) Security.DropToken(_port);
            Running = false;
            try { _tcp?.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (Running)
            {
                try
                {
                    var client = _tcp.AcceptTcpClient();
                    new Thread(() => Serve(client)) { IsBackground = true }.Start();
                }
                catch { if (!Running) return; }
            }
        }

        private void Serve(TcpClient client)
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true })
            {
                string line;
                while (Running && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    JsonObject req = null;
                    try { req = JsonNode.Parse(line) as JsonObject; } catch { }
                    if (req == null)
                    {
                        // Not our protocol (for example a browser's HTTP request): answer once and hang up, so
                        // nothing later on this connection is ever read as a request.
                        writer.WriteLine(Refusal(null, "Not a Civil 3D MCP request."));
                        return;
                    }
                    JsonNode id = req["id"]?.DeepClone();
                    if (!Security.TokenMatches(req["token"]?.ToString()))
                    {
                        writer.WriteLine(Refusal(id,
                            "Missing or wrong token. The MCP server reads it from " + Security.TokenFile(_port)
                            + ", which Civil 3D writes when the plugin starts. Make sure the MCP server and the plugin come "
                            + "from the same version (run the installer again), then restart Claude."));
                        return;
                    }
                    JsonObject reply;
                    try
                    {
                        string tool = req["tool"]?.ToString();
                        var args = req["args"] as JsonObject ?? new JsonObject();
                        // Everything that touches the drawing runs on the main thread.
                        reply = MainThread.Run(() => Router.Execute(tool, args));
                    }
                    catch (Exception ex)
                    {
                        reply = new JsonObject { ["ok"] = false, ["error"] = ex.Message };
                    }
                    reply["id"] = id;
                    writer.WriteLine(reply.ToJsonString());
                }
            }
        }

        static string Refusal(JsonNode id, string error) =>
            new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = error }.ToJsonString();
    }
}

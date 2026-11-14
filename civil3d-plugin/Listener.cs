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
    /// Request:  {"id":1,"tool":"drafting.line","args":{...}}
    /// Reply:    {"id":1,"ok":true,"result":{...}}  or  {"id":1,"ok":false,"error":"..."}
    /// Only listens on 127.0.0.1, so nothing outside your PC can reach it.
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
            Running = true;
            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Civil3DMcp.Listener" };
            _thread.Start();
        }

        public void Stop()
        {
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
                    JsonNode id = null;
                    JsonObject reply;
                    try
                    {
                        var req = JsonNode.Parse(line).AsObject();
                        id = req["id"]?.DeepClone();
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
    }
}

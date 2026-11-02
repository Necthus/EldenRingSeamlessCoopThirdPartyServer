using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

internal static class HttpGuardTests
{
    private static int count;
    private static void Check(bool test, string message) { count++; if (!test) throw new Exception(message); }
    private static Dictionary<string, string> Empty()
    { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "http_proxy", "" }, { "https_proxy", "" }, { "all_proxy", "" }, { "no_proxy", "" } }; }
    private static string Header(Stream stream)
    {
        var bytes = new List<byte>();
        while (bytes.Count < 16384)
        {
            int b = stream.ReadByte(); if (b < 0) throw new EndOfStreamException(); bytes.Add((byte)b);
            int n = bytes.Count; if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) return Encoding.ASCII.GetString(bytes.ToArray());
        }
        throw new Exception("Test header too large");
    }
    private static void Send(Stream stream, string value)
    { byte[] bytes = Encoding.ASCII.GetBytes(value); stream.Write(bytes, 0, bytes.Length); stream.Flush(); }
    private static TcpClient Client(int port)
    { var client = new TcpClient { ReceiveTimeout = 4000, SendTimeout = 4000 }; client.Connect(IPAddress.Loopback, port); return client; }
    private static string Request(ModHttpProxy proxy, string request, bool authenticate)
    {
        using (var client = Client(proxy.Port))
        {
            Stream stream = client.GetStream(); Send(stream, request + "\r\n" + (authenticate ? "Proxy-Authorization: " + proxy.Authorization + "\r\n" : "") + "\r\n");
            return new StreamReader(stream, Encoding.ASCII).ReadToEnd();
        }
    }
    private static void Echo(ModHttpProxy proxy, int port, string host)
    {
        using (var client = Client(proxy.Port))
        {
            Stream stream = client.GetStream(); Send(stream, "CONNECT " + host + ":" + port + " HTTP/1.1\r\nProxy-Authorization: " + proxy.Authorization + "\r\n\r\n");
            Check(Header(stream).StartsWith("HTTP/1.1 200"), "Allowed CONNECT failed");
            Send(stream, "game-data-123"); byte[] bytes = new byte[13]; int at = 0;
            while (at < bytes.Length) { int read = stream.Read(bytes, at, bytes.Length - at); if (read == 0) throw new EndOfStreamException(); at += read; }
            Check(Encoding.ASCII.GetString(bytes) == "game-data-123", "Allowed tunnel altered data");
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
        internal readonly Thread Thread;
        internal Exception Error;
        internal int Port { get { return ((IPEndPoint)Listener.LocalEndpoint).Port; } }
        internal Fixture(Action<TcpClient> action)
        {
            Listener.Start(); Thread = new Thread(delegate() {
                try { using (var client = Listener.AcceptTcpClient()) { client.ReceiveTimeout = 4000; client.SendTimeout = 4000; action(client); } }
                catch (Exception error) { Error = error; }
            }) { IsBackground = true }; Thread.Start();
        }
        public void Dispose() { Listener.Stop(); Thread.Join(5000); }
        internal void Finished() { Check(Thread.Join(5000), "Fixture did not finish"); if (Error != null) throw Error; }
    }
    [STAThread]
    private static int Main(string[] args)
    {
        string folder = Path.Combine(Path.GetTempPath(), "ersc-http-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string log = Path.Combine(folder, "guard.log");
        try
        {
            Check(ModHttpProxy.IsBlocked("raw.githubusercontent.com"), "Update target not denied");
            Check(ModHttpProxy.IsBlocked("RAW.GITHUBUSERCONTENT.COM."), "Case/trailing-dot bypass");
            Check(!ModHttpProxy.IsBlocked("raw.githubusercontent.com.evil.invalid"), "Unrelated host denied");
            Check(!ModHttpProxy.IsBlocked("api.steampowered.com") && !ModHttpProxy.IsBlocked("39.96.9.72") && !ModHttpProxy.IsBlocked("10.77.42.2"), "Game/network hosts denied");
            using (var proxy = new ModHttpProxy(log, Empty()))
            {
                var timer = Stopwatch.StartNew();
                Check(Request(proxy, "CONNECT raw.githubusercontent.com:443 HTTP/1.1", true).Contains("503 Mod Startup Check Disabled"), "HTTPS update was not blocked");
                Check(timer.ElapsedMilliseconds < 2000, "Blocked request waited on network");
                Check(Request(proxy, "GET http://RAW.GITHUBUSERCONTENT.COM./LukeYui/release/VERSION HTTP/1.1", true).Contains("503"), "HTTP update was not blocked");
                Check(proxy.Blocked == 2 && proxy.Forwarded == 0, "Blocked request opened upstream route");
                Check(Request(proxy, "CONNECT raw.githubusercontent.com:443 HTTP/1.1", false).Contains("403"), "Unauthenticated access allowed");
                Check(proxy.Blocked == 2, "Unauthenticated caller reached policy processing");
                using (var echo = new Fixture(delegate(TcpClient client) {
                    Stream stream = client.GetStream(); byte[] data = new byte[13]; int pos = 0;
                    while (pos < data.Length) { int n = stream.Read(data, pos, data.Length - pos); if (n == 0) throw new EndOfStreamException(); pos += n; }
                    stream.Write(data, 0, data.Length); stream.Flush();
                })) { Echo(proxy, echo.Port, "127.0.0.1"); echo.Finished(); }
                using (var http = new Fixture(delegate(TcpClient client) {
                    Stream stream = client.GetStream(); string header = Header(stream);
                    Check(header.StartsWith("GET /allowed?q=example HTTP/1.1"), "Allowed HTTP request path changed");
                    Check(header.IndexOf("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) < 0, "Guard credential leaked upstream");
                    Send(stream, "HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nOK");
                }))
                { Check(Request(proxy, "GET http://127.0.0.1:" + http.Port + "/allowed?q=example HTTP/1.1\r\nHost: 127.0.0.1", true).EndsWith("OK"), "Unrelated HTTP did not pass"); http.Finished(); }
                Check(proxy.Forwarded == 2, "Forward counters wrong");
                string parentHttp = Environment.GetEnvironmentVariable("http_proxy"), parentBypass = Environment.GetEnvironmentVariable("no_proxy");
                var start = new ProcessStartInfo("ersc_launcher.original.exe"); ModUpdateGuard.ConfigureChild(start, proxy.Endpoint);
                Check(!start.UseShellExecute && start.CreateNoWindow, "Original launcher environment not scoped");
                foreach (string name in new string[] { "http_proxy", "https_proxy", "all_proxy" }) Check(start.EnvironmentVariables[name] == proxy.Endpoint, "Proxy environment missing " + name);
                Check(start.EnvironmentVariables["no_proxy"] == "", "Child can bypass guard using no_proxy");
                Check(Environment.GetEnvironmentVariable("http_proxy") == parentHttp && Environment.GetEnvironmentVariable("no_proxy") == parentBypass, "Parent environment mutated");
                Check(!File.ReadAllText(log).Contains(proxy.Authorization) && !File.ReadAllText(log).Contains(proxy.Endpoint) && !File.ReadAllText(log).Contains("q=example"), "Log leaked credentials or query");
            }
            // Original user HTTP proxies are retained for allowed hosts.
            using (var upstream = new Fixture(delegate(TcpClient client) {
                Stream stream = client.GetStream(); string header = Header(stream);
                Check(header.StartsWith("CONNECT allowed.invalid:443 HTTP/1.1"), "Original proxy did not receive target");
                Check(header.Contains("Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("original:password"))), "Original proxy credential missing");
                Send(stream, "HTTP/1.1 200 Connection Established\r\n\r\n"); byte[] data = new byte[13]; int pos = 0;
                while (pos < data.Length) { int n = stream.Read(data, pos, data.Length - pos); if (n == 0) throw new EndOfStreamException(); pos += n; }
                stream.Write(data, 0, data.Length); stream.Flush();
            }))
            {
                var env = Empty(); env["https_proxy"] = "http://original:password@127.0.0.1:" + upstream.Port;
                using (var proxy = new ModHttpProxy(log, env)) { Echo(proxy, 443, "allowed.invalid"); upstream.Finished(); }
            }
            using (var echo = new Fixture(delegate(TcpClient client) {
                Stream stream = client.GetStream(); byte[] data = new byte[13]; int pos = 0;
                while (pos < data.Length) { int n = stream.Read(data, pos, data.Length - pos); if (n == 0) throw new EndOfStreamException(); pos += n; }
                stream.Write(data, 0, data.Length); stream.Flush();
            }))
            {
                var env = Empty(); env["https_proxy"] = "http://127.0.0.1:1"; env["no_proxy"] = "127.0.0.1";
                using (var proxy = new ModHttpProxy(log, env)) { Echo(proxy, echo.Port, "127.0.0.1"); echo.Finished(); }
            }
            bool refused = false;
            try { var env = Empty(); env["all_proxy"] = "socks5://localhost:1234"; using (var proxy = new ModHttpProxy(log, env)) { } }
            catch (InvalidOperationException) { refused = true; }
            Check(refused, "Unsupported original proxy silently discarded");
            Check(File.ReadAllText(log).Contains("guard_stopped"), "Guard did not log shutdown");
            if (args.Length >= 2)
            {
                string fixture = Path.GetFullPath(args[1]), ready = Path.Combine(folder, "worker-ready.json");
                var start = new ProcessStartInfo(Path.GetFullPath(args[0])) {
                    Arguments = "--mod-http-guard \"" + fixture + "\" \"" + ready + "\"", WorkingDirectory = fixture,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using (Process worker = Process.Start(start))
                {
                    Check(worker.WaitForExit(15000), "Worker failed to follow fixture game lifecycle");
                    Check(worker.ExitCode == 0, "Worker exited with failure");
                    var state = new JavaScriptSerializer().Deserialize<HttpGuardReady>(File.ReadAllText(ready));
                    Check(state.Success && state.WorkerPid == worker.Id, "Worker launch handshake invalid");
                    Check(File.ReadAllText(Path.Combine(fixture, "fixture-result.txt")).Contains("503 Mod Startup Check Disabled"), "Original launcher child did not inherit guard");
                    string activity = File.ReadAllText(state.Log);
                    Check(activity.Contains("blocked host=raw.githubusercontent.com") && activity.Contains("game_attached") && activity.Contains("game_exited") && activity.Contains("guard_stopped"), "Worker did not block and stop correctly");
                }
            }
            if (args.Length == 5)
            {
                using (var proxy = new ModHttpProxy(log, Empty()))
                {
                    var start = new ProcessStartInfo(Path.GetFullPath(args[2])) {
                        Arguments = "\"" + Path.GetFullPath(args[3]) + "\" \"" + Path.GetFullPath(args[4]) + "\"",
                        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = folder
                    };
                    ModUpdateGuard.ConfigureChild(start, proxy.Endpoint);
                    using (Process probe = Process.Start(start))
                    {
                        string stdout = probe.StandardOutput.ReadToEnd(), stderr = probe.StandardError.ReadToEnd();
                        Check(probe.WaitForExit(10000) && probe.ExitCode == 0, "GSE transport probe failed under guard: " + stderr);
                        Check(stdout.Contains("lobby_discovery") && stdout.Contains("legacy_reliable") && stdout.Contains("reliable_65536"), "GSE transport coverage incomplete");
                        Check(proxy.Blocked == 0 && proxy.Forwarded == 0, "Game transport unexpectedly traversed HTTP guard");
                        Console.WriteLine(stdout);
                    }
                }
            }
            Console.WriteLine("PASS: " + count + " HTTP guard checks. Only loopback fixtures were contacted."); return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Directory.Delete(folder, true); }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Process-scoped HTTP proxy. No DLL patches, system proxy, firewall or hosts
// changes. Only the exact update host observed in this Mod build is denied.
internal sealed class ModHttpProxy : IDisposable
{
    private readonly TcpListener listener;
    private readonly Thread acceptThread;
    private readonly object gate = new object();
    private readonly List<TcpClient> clients = new List<TcpClient>();
    private readonly Dictionary<string, string> inherited;
    private readonly string token, log;
    private volatile bool stopping;
    private int active, blocked, forwarded;
    internal int Port { get { return ((IPEndPoint)listener.LocalEndpoint).Port; } }
    internal int Blocked { get { return blocked; } }
    internal int Forwarded { get { return forwarded; } }
    internal string Endpoint { get { return "http://guard:" + token + "@127.0.0.1:" + Port; } }
    internal string Authorization { get { return "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes("guard:" + token)); } }
    internal static bool IsBlocked(string host)
    { return String.Equals(host.TrimEnd('.'), "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase); }
    internal static Dictionary<string, string> EnvironmentProxy()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in new string[] { "http_proxy", "https_proxy", "all_proxy", "no_proxy" }) result[name] = Environment.GetEnvironmentVariable(name) ?? "";
        return result;
    }
    internal ModHttpProxy(string logPath, Dictionary<string, string> environment)
    {
        log = logPath; inherited = environment;
        byte[] random = new byte[24]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(random);
        token = BitConverter.ToString(random).Replace("-", "").ToLowerInvariant();
        foreach (string name in new string[] { "http_proxy", "https_proxy", "all_proxy" })
        {
            string configured; if (inherited.TryGetValue(name, out configured) && configured.Length > 0)
            {
                Uri parsed;
                if (!Uri.TryCreate(configured.IndexOf("://", StringComparison.Ordinal) < 0 ? "http://" + configured : configured, UriKind.Absolute, out parsed) ||
                    (parsed.Scheme != "http" && parsed.Scheme != "https"))
                    throw new InvalidOperationException("禁止更新检查暂不支持当前的 " + name + " 代理协议；请在启动器关闭此选项后启动。");
            }
        }
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(16);
        acceptThread = new Thread(Accept) { IsBackground = true, Name = "Mod HTTP guard" }; acceptThread.Start();
        WriteLog("guard_started loopback_only=true blocked_host=raw.githubusercontent.com");
    }
    internal void WriteLog(string text)
    {
        lock (gate)
        {
            if (String.IsNullOrEmpty(log)) return;
            try { File.AppendAllText(log, DateTime.Now.ToString("o") + " " + text + Environment.NewLine, new UTF8Encoding(false)); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    private void Accept()
    {
        while (!stopping)
        {
            TcpClient client;
            try { client = listener.AcceptTcpClient(); } catch (SocketException) { break; } catch (ObjectDisposedException) { break; }
            lock (gate)
            {
                if (stopping || active >= 24) { client.Close(); continue; }
                clients.Add(client); active++;
            }
            ThreadPool.QueueUserWorkItem(delegate {
                try { Serve(client); }
                catch (Exception error) { if (!(error is IOException) && !(error is SocketException) && !(error is ObjectDisposedException)) WriteLog("proxy_error type=" + error.GetType().Name); }
                finally { client.Close(); lock (gate) { clients.Remove(client); active--; } }
            });
        }
    }
    private static string Header(Stream stream)
    {
        using (var bytes = new MemoryStream())
        {
            int state = 0;
            for (int i = 0; i < 16384; i++)
            {
                int value = stream.ReadByte(); if (value < 0) throw new EndOfStreamException();
                bytes.WriteByte((byte)value);
                state = state == 0 ? (value == 13 ? 1 : 0) : state == 1 ? (value == 10 ? 2 : 0) : state == 2 ? (value == 13 ? 3 : 0) : value == 10 ? 4 : 0;
                if (state == 4) return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }
        throw new InvalidDataException("Proxy header too long");
    }
    private static void Reply(Stream stream, string status)
    {
        byte[] bytes = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"); stream.Write(bytes, 0, bytes.Length); stream.Flush();
    }
    private bool BypassUpstream(string host, int port)
    {
        string bypass; if (!inherited.TryGetValue("no_proxy", out bypass)) return false;
        foreach (string item in bypass.Split(','))
        {
            string part = item.Trim(); if (part == "*") return true; if (part.Length == 0) continue;
            int colon = part.LastIndexOf(':'); int specified;
            if (colon > 0 && Int32.TryParse(part.Substring(colon + 1), out specified))
            { if (specified != port) continue; part = part.Substring(0, colon); }
            part = part.TrimStart('.');
            if (String.Equals(host, part, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + part, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    private Uri Upstream(string scheme, string host, int port)
    {
        if (BypassUpstream(host, port)) return null;
        string value;
        if (!inherited.TryGetValue(scheme + "_proxy", out value) || value.Length == 0) inherited.TryGetValue("all_proxy", out value);
        return String.IsNullOrWhiteSpace(value) ? null : new Uri(value.IndexOf("://", StringComparison.Ordinal) < 0 ? "http://" + value : value);
    }
    private Stream Connect(TcpClient upstreamClient, string host, int port, string scheme)
    {
        Uri proxy = Upstream(scheme, host, port); string address = proxy == null ? host : proxy.DnsSafeHost; int service = proxy == null ? port : proxy.Port;
        IAsyncResult pending = upstreamClient.BeginConnect(address, service, null, null);
        try { if (!pending.AsyncWaitHandle.WaitOne(7000)) throw new IOException("HTTP proxy upstream connection timeout"); upstreamClient.EndConnect(pending); }
        finally { pending.AsyncWaitHandle.Close(); }
        upstreamClient.ReceiveTimeout = 10000; upstreamClient.SendTimeout = 10000;
        Stream stream = upstreamClient.GetStream();
        if (proxy != null)
        {
            if (proxy.Scheme == "https") { var tls = new SslStream(stream, false); tls.AuthenticateAsClient(proxy.DnsSafeHost); stream = tls; }
            string auth = proxy.UserInfo.Length == 0 ? "" : "Proxy-Authorization: Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(proxy.UserInfo))) + "\r\n";
            string authority = host.IndexOf(':') >= 0 ? "[" + host + "]:" + port : host + ":" + port;
            byte[] request = Encoding.ASCII.GetBytes("CONNECT " + authority + " HTTP/1.1\r\nHost: " + authority + "\r\n" + auth + "\r\n"); stream.Write(request, 0, request.Length); stream.Flush();
            string answer = Header(stream); string[] line = answer.Split(new char[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (line.Length < 2 || line[1] != "200") throw new IOException("Original upstream proxy rejected connection");
        }
        return stream;
    }
    private void Serve(TcpClient client)
    {
        client.ReceiveTimeout = 5000; client.SendTimeout = 5000;
        Stream input = client.GetStream(); string request = Header(input); string[] lines = request.Split(new string[] { "\r\n" }, StringSplitOptions.None);
        string[] first = lines[0].Split(' '); if (first.Length != 3 || !first[2].StartsWith("HTTP/1.", StringComparison.Ordinal)) { Reply(input, "400 Bad Request"); return; }
        bool authorized = false;
        for (int i = 1; i < lines.Length; i++)
            if (lines[i].StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) && lines[i].Substring(lines[i].IndexOf(':') + 1).Trim() == Authorization) authorized = true;
        if (!authorized) { Reply(input, "403 Forbidden"); return; }
        bool tunnel = first[0] == "CONNECT"; Uri uri;
        if (!Uri.TryCreate(tunnel ? "https://" + first[1] : first[1], UriKind.Absolute, out uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https") || uri.UserInfo.Length > 0 || uri.Port < 1 || uri.Host.Length == 0)
        { Reply(input, "400 Bad Request"); return; }
        string host = uri.DnsSafeHost.Trim('[', ']'); int port = uri.Port;
        // Deny before any DNS lookup, socket connect, TLS or forwarding.
        if (IsBlocked(host))
        { Interlocked.Increment(ref blocked); WriteLog("blocked host=" + host + " port=" + port + " method=" + first[0]); Reply(input, "503 Mod Startup Check Disabled"); return; }
        using (var outgoing = new TcpClient())
        {
            lock (gate) { if (stopping) return; clients.Add(outgoing); }
            try
            {
                Stream output;
                try { output = Connect(outgoing, host, port, uri.Scheme); }
                catch (Exception) { Reply(input, "502 Bad Gateway"); return; }
                using (output)
                {
                    Interlocked.Increment(ref forwarded); WriteLog("forwarded host=" + host + " port=" + port + " method=" + first[0]);
                    if (tunnel) { byte[] ok = Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"); input.Write(ok, 0, ok.Length); input.Flush(); }
                    else
                    {
                        if (uri.Scheme == "https") { Reply(input, "400 Use CONNECT For HTTPS"); return; }
                        var header = new StringBuilder(first[0] + " " + uri.PathAndQuery + " HTTP/1.1\r\n");
                        for (int i = 1; i < lines.Length; i++)
                        {
                            if (lines[i].Length == 0 || lines[i].StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase) ||
                                lines[i].StartsWith("Proxy-Connection:", StringComparison.OrdinalIgnoreCase) || lines[i].StartsWith("Connection:", StringComparison.OrdinalIgnoreCase)) continue;
                            header.Append(lines[i]).Append("\r\n");
                        }
                        header.Append("Connection: close\r\n\r\n"); byte[] bytes = Encoding.ASCII.GetBytes(header.ToString()); output.Write(bytes, 0, bytes.Length); output.Flush();
                    }
                    client.ReceiveTimeout = 0; outgoing.ReceiveTimeout = 0;
                    var upload = new Thread(delegate() {
                        try { input.CopyTo(output); } catch (Exception) { }
                        finally { try { outgoing.Client.Shutdown(SocketShutdown.Send); } catch (Exception) { } }
                    }) { IsBackground = true, Name = "Mod HTTP tunnel upload" };
                    upload.Start();
                    try { output.CopyTo(input); } catch (Exception) { }
                    finally { client.Close(); outgoing.Close(); upload.Join(1000); }
                }
            }
            finally { lock (gate) clients.Remove(outgoing); }
        }
    }
    public void Dispose()
    {
        lock (gate) { stopping = true; listener.Stop(); foreach (var client in clients) client.Close(); }
        acceptThread.Join(1000); WriteLog("guard_stopped blocked=" + blocked + " forwarded=" + forwarded);
    }
}

internal sealed class HttpGuardReady
{
    public bool Success;
    public string Error, Log;
    public int WorkerPid, OriginalPid;
}
internal static class ModUpdateGuard
{
    internal static string LogDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SeamlessCoopLauncher", "NetworkLogs"); } }
    private static string Quote(string value)
    {
        // Arguments are paths: no control characters and no embedded quotes.
        if (value.IndexOfAny(new char[] { '"', '\r', '\n', '\0' }) >= 0) throw new ArgumentException("启动路径包含不受支持的字符。");
        return "\"" + value.TrimEnd('\\') + "\"";
    }
    internal static void ConfigureChild(ProcessStartInfo start, string endpoint)
    {
        start.UseShellExecute = false; start.CreateNoWindow = true;
        foreach (string name in new string[] { "http_proxy", "https_proxy", "all_proxy" }) start.EnvironmentVariables[name] = endpoint;
        start.EnvironmentVariables["no_proxy"] = "";
    }
    internal static void Launch(string root)
    {
        string ready = Path.Combine(Path.GetTempPath(), "ersc-http-guard-" + Guid.NewGuid().ToString("N") + ".json");
        var start = new ProcessStartInfo(System.Windows.Forms.Application.ExecutablePath) {
            Arguments = "--mod-http-guard " + Quote(Path.GetFullPath(root)) + " " + Quote(ready),
            WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true
        };
        using (Process worker = Process.Start(start))
        {
            try
            {
                var timer = Stopwatch.StartNew();
                while (!File.Exists(ready) && !worker.HasExited && timer.ElapsedMilliseconds < 12000) Thread.Sleep(50);
                if (!File.Exists(ready)) throw new IOException("联网检查守卫未能启动。请查看日志或关闭“禁止 Mod 启动联网检查”后重试。");
                var result = new JavaScriptSerializer().Deserialize<HttpGuardReady>(File.ReadAllText(ready));
                if (result == null || !result.Success) throw new IOException(result == null ? "守卫启动状态无效。" : result.Error);
            }
            finally { if (File.Exists(ready)) File.Delete(ready); }
        }
    }
    private static void Publish(string path, HttpGuardReady ready)
    {
        string staging = path + ".tmp"; File.WriteAllText(staging, new JavaScriptSerializer().Serialize(ready), new UTF8Encoding(false)); File.Move(staging, path);
    }
    internal static int Run(string root, string readyPath)
    {
        string log = null; bool published = false;
        try
        {
            root = Path.GetFullPath(root);
            string original = Path.Combine(root, "ersc_launcher.original.exe"); if (!File.Exists(original)) throw new FileNotFoundException("缺少原 Mod 启动器。");
            Directory.CreateDirectory(LogDirectory); log = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Process.GetCurrentProcess().Id + ".log");
            using (var proxy = new ModHttpProxy(log, ModHttpProxy.EnvironmentProxy()))
            {
                DateTime started = DateTime.UtcNow;
                var start = new ProcessStartInfo(original) { WorkingDirectory = root }; ConfigureChild(start, proxy.Endpoint);
                using (Process launcher = Process.Start(start))
                {
                    Publish(readyPath, new HttpGuardReady { Success = true, WorkerPid = Process.GetCurrentProcess().Id, OriginalPid = launcher.Id, Log = log }); published = true;
                    Process game = null; var timer = Stopwatch.StartNew();
                    while (game == null && timer.ElapsedMilliseconds < 90000)
                    {
                        Process[] candidates = Process.GetProcessesByName("eldenring");
                        foreach (var candidate in candidates)
                        {
                            bool matches = false;
                            try { matches = candidate.StartTime.ToUniversalTime() >= started.AddSeconds(-1) && String.Equals(Path.GetFullPath(candidate.MainModule.FileName), Path.Combine(root, "eldenring.exe"), StringComparison.OrdinalIgnoreCase); }
                            catch (Exception) { }
                            if (matches && game == null) game = candidate; else candidate.Dispose();
                        }
                        if (game == null)
                        {
                            if (launcher.HasExited && launcher.ExitCode != 0) { proxy.WriteLog("original_launcher_failed code=" + launcher.ExitCode); return 1; }
                            Thread.Sleep(250);
                        }
                    }
                    if (game == null) { proxy.WriteLog("game_not_found_within_90s"); return 1; }
                    using (game) { proxy.WriteLog("game_attached pid=" + game.Id); game.WaitForExit(); proxy.WriteLog("game_exited"); }
                }
            }
            return 0;
        }
        catch (Exception error)
        {
            if (!published) try { Publish(readyPath, new HttpGuardReady { Success = false, Error = error.Message, Log = log }); } catch (Exception) { }
            return 1;
        }
    }
}

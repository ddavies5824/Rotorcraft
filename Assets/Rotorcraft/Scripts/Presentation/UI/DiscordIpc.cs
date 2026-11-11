// Discord's local connection: the named pipe "discord-ipc-N" the Discord desktop app opens for games to set the player's
// activity (Rich Presence). No Discord SDK or extra DLL: the protocol is a handful of JSON messages, each framed by two
// little-endian 32-bit numbers (opcode, payload length). It never leaves the computer; the Discord app decides who sees it.
// Everything runs on one background thread, so a slow, missing or closing Discord never holds up a frame. Windows only:
// a browser cannot reach the pipe.
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Debug = UnityEngine.Debug;

namespace Rotorcraft.Presentation.UI
{
    public sealed class DiscordIpc : IDisposable
    {
        public enum Link { Waiting, Connected, Refused }

        const int OpHandshake = 0, OpFrame = 1, OpClose = 2, OpPing = 3, OpPong = 4;
        /// <summary>Discord allows five activity updates in 20 seconds; changes in between are merged into the next one.</summary>
        public const int MinGapMs = 4500;
        const int RetryMs = 10000, RefusedRetryMs = 300000, ReadyTimeoutMs = 10000;

        readonly string appId;
        readonly int pid = Process.GetCurrentProcess().Id;
        readonly Thread thread;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly object gate = new object();
        volatile bool stop;
        string wanted;          // the activity to show (JSON), or null for none
        bool haveWanted;        // Set has been called at least once
        SafeFileHandle pipe;
        int nonce;

        public volatile Link Status = Link.Waiting;
        /// <summary>Why Discord refused the game (for example an unknown application id), or the last reply's error.</summary>
        public volatile string Problem;
        /// <summary>Discord's answer to the last activity update (JSON), and how many answers have come back.</summary>
        public volatile string LastReply;
        public volatile int Replies;

        public DiscordIpc(string applicationId)
        {
            appId = applicationId;
            thread = new Thread(Run) { IsBackground = true, Name = "Discord presence" };
            thread.Start();
        }

        /// <summary>Show this activity (Discord's activity object), or none when null. Sent as soon as the rate limit allows.</summary>
        public void Set(JObject activity)
        {
            var json = activity?.ToString(Formatting.None);
            lock (gate) { if (haveWanted && json == wanted) return; wanted = json; haveWanted = true; }
            wake.Set();
        }

        public void Dispose()
        {
            stop = true;
            wake.Set();
            if (!thread.Join(1500)) Close();   // closing the pipe is what clears the activity in Discord
        }

        // ------------------------------------------------------------------------------------- thread
        void Run()
        {
            while (!stop)
            {
                var wait = RetryMs;
                if (Open())
                {
                    try { wait = Session(); }
                    catch (Exception e) { if (!stop) Debug.Log($"Discord: connection lost ({e.Message})"); }
                    Close();
                }
                if (Status != Link.Refused) Status = Link.Waiting;
                if (!stop) wake.WaitOne(wait);
            }
            Close();
        }

        /// <summary>Discord opens discord-ipc-0, or the next free number when another copy already has it.</summary>
        bool Open()
        {
            for (var i = 0; i < 10; i++)
            {
                var h = CreateFileW($@"\\.\pipe\discord-ipc-{i}", GenericRead | GenericWrite, 0, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (!h.IsInvalid) { pipe = h; return true; }
                h.Dispose();
            }
            return false;
        }

        void Close()
        {
            var p = pipe; pipe = null;
            try { p?.Dispose(); } catch { }
        }

        /// <summary>Handshake, then keep Discord's activity matching the wanted one until the pipe closes. Returns how long to
        /// wait before trying again.</summary>
        int Session()
        {
            Write(OpHandshake, new JObject { ["v"] = 1, ["client_id"] = appId });
            var t0 = Environment.TickCount;
            while (true)
            {
                if (stop) return 0;
                if (Environment.TickCount - t0 > ReadyTimeoutMs) throw new Exception("no answer to the handshake");
                if (Available() < 8) { wake.WaitOne(50); continue; }
                var (op, msg) = Read();
                if (op == OpClose) return Refuse(msg);
                if (op == OpPing) { Write(OpPong, msg); continue; }
                if (op == OpFrame && (string)msg["evt"] == "READY") break;
            }
            Status = Link.Connected; Problem = null;
            Debug.Log("Discord: connected");
            string sent = null; var sentAny = false; var lastSend = Environment.TickCount - MinGapMs;
            while (!stop)
            {
                string want; bool have;
                lock (gate) { want = wanted; have = haveWanted; }
                if (have && (!sentAny || want != sent) && Environment.TickCount - lastSend >= MinGapMs)
                {
                    var args = new JObject { ["pid"] = pid };
                    if (want != null) args["activity"] = JObject.Parse(want);
                    Write(OpFrame, new JObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = (++nonce).ToString() });
                    sent = want; sentAny = true; lastSend = Environment.TickCount;
                }
                while (Available() >= 8)
                {
                    var (op, msg) = Read();
                    if (op == OpClose) return Refuse(msg);
                    if (op == OpPing) Write(OpPong, msg);
                    else if (op == OpFrame && (string)msg["cmd"] == "SET_ACTIVITY")
                    {
                        if ((string)msg["evt"] == "ERROR") { Problem = (string)msg["data"]?["message"]; Debug.LogWarning($"Discord: activity refused: {Problem}"); }
                        LastReply = msg.ToString(Formatting.None);
                        Replies++;
                    }
                }
                // wake early for a new activity, but no sooner than the rate limit lets it go
                var gap = MinGapMs - (Environment.TickCount - lastSend);
                wake.WaitOne(Math.Max(50, Math.Min(250, gap)));
            }
            return 0;
        }

        /// <summary>Discord closed the connection: code 4000 is an application id it does not know.</summary>
        int Refuse(JObject msg)
        {
            var code = (int?)msg?["code"] ?? 0;
            Problem = (string)msg?["message"] ?? $"closed ({code})";
            if (code >= 4000 && code < 5000)
            {
                Status = Link.Refused;
                Debug.LogWarning($"Discord: refused the game ({code} {Problem})");
                return RefusedRetryMs;
            }
            return RetryMs;
        }

        // ------------------------------------------------------------------------------------- frames
        void Write(int op, JObject payload)
        {
            var body = Encoding.UTF8.GetBytes(payload.ToString(Formatting.None));
            var buf = new byte[8 + body.Length];
            BitConverter.GetBytes(op).CopyTo(buf, 0);
            BitConverter.GetBytes(body.Length).CopyTo(buf, 4);
            body.CopyTo(buf, 8);
            if (!WriteFile(pipe, buf, buf.Length, out var n, IntPtr.Zero) || n != buf.Length) throw new Exception($"write failed ({Marshal.GetLastWin32Error()})");
        }

        (int op, JObject msg) Read()
        {
            var head = ReadExactly(8);
            var op = BitConverter.ToInt32(head, 0); var len = BitConverter.ToInt32(head, 4);
            if (len < 0 || len > 1 << 20) throw new Exception($"bad frame length {len}");
            var body = len == 0 ? "{}" : Encoding.UTF8.GetString(ReadExactly(len));
            JObject msg;
            try { msg = JObject.Parse(body); } catch { msg = new JObject(); }
            return (op, msg);
        }

        byte[] ReadExactly(int n)
        {
            var buf = new byte[n]; var chunk = new byte[n]; var got = 0;
            while (got < n)
            {
                if (!ReadFile(pipe, chunk, n - got, out var r, IntPtr.Zero) || r <= 0) throw new Exception($"read failed ({Marshal.GetLastWin32Error()})");
                Buffer.BlockCopy(chunk, 0, buf, got, r);
                got += r;
            }
            return buf;
        }

        /// <summary>Bytes waiting in the pipe (throws once Discord has gone).</summary>
        int Available()
        {
            if (!PeekNamedPipe(pipe, IntPtr.Zero, 0, IntPtr.Zero, out var avail, IntPtr.Zero)) throw new Exception($"pipe closed ({Marshal.GetLastWin32Error()})");
            return avail;
        }

        const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int count, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, byte[] buffer, int count, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool PeekNamedPipe(SafeFileHandle h, IntPtr buffer, int size, IntPtr read, out int available, IntPtr left);
    }
}
#endif

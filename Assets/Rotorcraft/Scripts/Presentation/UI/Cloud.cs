// Google sign-in, cloud saves and the pilot area (port of src/ui/cloudSync.ts, src/game/cloud.ts and the
// online half of src/game/pilots.ts), for a desktop game.
//   Sign-in: Google's OAuth flow for installed apps. The system browser opens Google's own sign-in page; the game
//   listens on a local port (127.0.0.1) for the answer and exchanges it (with PKCE) for tokens. The game never
//   sees the Google password. The refresh token is kept in the save folder so the player stays signed in.
//   Saves: one file in the player's own Google Drive, in the hidden app-data folder only this game can see.
//   Pilot area: the Google token is exchanged for a Firebase session; each pilot publishes a public profile
//   (game name, rank and statistics only) to a Firestore collection read for the leaderboards.
// Configuration (OAuth client and Firebase project) comes from Resources/OnlineConfig.json, or is pasted into
// Settings › Login, and is stored in the save folder.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rotorcraft.Game;
using UnityEngine;
using UnityEngine.Networking;

namespace Rotorcraft.Presentation.UI
{
    public enum CloudStatus { Unconfigured, SignedOut, Working, SignedIn, Error }

    public sealed class OnlineConfig
    {
        /// <summary>Google OAuth client of type "Desktop app".</summary>
        public string googleClientId, googleClientSecret;
        /// <summary>Firebase web API key and project id (pilot area). Optional.</summary>
        public string firebaseApiKey, firebaseProjectId;
        /// <summary>Google OAuth client of type "Web application" (the web build), with the site as an authorised origin.</summary>
        public string googleWebClientId;
#if UNITY_WEBGL && !UNITY_EDITOR
        public bool HasGoogle => !string.IsNullOrWhiteSpace(googleWebClientId);
#else
        public bool HasGoogle => !string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret);
#endif
        public bool HasFirebase => !string.IsNullOrWhiteSpace(firebaseApiKey) && !string.IsNullOrWhiteSpace(firebaseProjectId);
    }

    public sealed class CloudBundle { public string app = "rotorcraft"; public int v = 1; public long savedAt; public Career career; public FreeplayRecord freeplay; public Company company; }

    public sealed class PilotProfile
    {
        public string name, rank; public int rankLevel; public double rankScore, hours; public int flights, landings, smoothPct, accidents, rescues, passengers, operational;
        public List<string> quals = new List<string>(); public int qualCount, bestStanding; public double careerEarned;
        public int fpShifts, fpJobs; public double fpEarned, fpBest;
        public Dictionary<string, double> acHours = new Dictionary<string, double>();
        public long startedAt, updatedAt;
    }
    public sealed class PilotEntry { public string uid; public PilotProfile profile; }

    public sealed class Cloud : MonoBehaviour
    {
        public static Cloud Instance { get; private set; }

        const string CLOUD_FILE = "rotorcraft-save.json";
        const string SCOPES = "openid email https://www.googleapis.com/auth/drive.appdata";
        public static readonly string[] RANKS = { "Student Pilot", "Solo Student", "Private Pilot", "Commercial Pilot", "Junior Line Pilot", "Experienced Line Pilot", "Senior Pilot", "Training Captain" };

        public CloudStatus Status { get; private set; } = CloudStatus.SignedOut;
        public string Message { get; private set; }
        public string Email => prefs.email;
        public long? LastSync => prefs.lastSync;
        public bool PilotVisible => prefs.visible;
        public string PilotUid => prefs.uid;
        public string PilotMessage { get; private set; }
        /// <summary>Two different careers found: waiting for the player to pick one.</summary>
        public (Career local, Career cloud, FreeplayRecord cloudFp, Company cloudCo)? Conflict { get; private set; }
        public event Action Changed_;

        sealed class Prefs { public string email, refreshToken, uid, fbRefreshToken; public long? lastSync, lastPublish; public bool visible = true; }
        Prefs prefs = new Prefs();
        OnlineConfig config;
        string accessToken; DateTime accessExp;
        string fbIdToken; DateTime fbExp;
        string fileId;
        bool busy, dirty;
        float flushAt = -1;

        string Dir => GameApp.Instance != null ? GameApp.Instance.Saves.Folder : Path.Combine(Application.persistentDataPath, "Saves");

        void Awake()
        {
            Instance = this;
            LoadConfig();
            try { var p = Path.Combine(Dir, "cloud.json"); if (File.Exists(p)) prefs = JsonConvert.DeserializeObject<Prefs>(File.ReadAllText(p)) ?? new Prefs(); } catch { prefs = new Prefs(); }
            Status = config.HasGoogle ? CloudStatus.SignedOut : CloudStatus.Unconfigured;
        }

        async void Start()
        {
            // signed in last time: renew quietly and sync
            if (config.HasGoogle && !string.IsNullOrEmpty(prefs.refreshToken)) { await RenewAccess(); if (accessToken != null) { await PilotSignIn(); await SyncNow(); } }
        }

        void Update()
        {
            if (flushAt > 0 && Time.unscaledTime >= flushAt) { flushAt = -1; _ = Flush(); }
        }

        void SavePrefs() { try { File.WriteAllText(Path.Combine(Dir, "cloud.json"), JsonConvert.SerializeObject(prefs)); WebStorage.Flush(); } catch { } }
        void Set(CloudStatus s, string msg = null) { Status = s; Message = msg; Changed_?.Invoke(); RefreshUi(); }
        static void RefreshUi() { var app = GameApp.Instance; if (app != null && (app.flight == null || !app.flight.Active)) app.Later(app.Refresh); }

        // ----------------------------------------------------------------------------------- config
        public OnlineConfig Config => config;

        void LoadConfig()
        {
            config = new OnlineConfig();
            try { var ta = Resources.Load<TextAsset>("OnlineConfig"); if (ta != null) config = JsonConvert.DeserializeObject<OnlineConfig>(ta.text) ?? config; } catch { }
            try
            {
                var p = Path.Combine(Dir, "online.json");
                if (File.Exists(p))
                {
                    var o = JsonConvert.DeserializeObject<OnlineConfig>(File.ReadAllText(p));
                    if (o != null)
                    {
                        if (!string.IsNullOrWhiteSpace(o.googleClientId)) { config.googleClientId = o.googleClientId; config.googleClientSecret = o.googleClientSecret; }
                        if (!string.IsNullOrWhiteSpace(o.googleWebClientId)) config.googleWebClientId = o.googleWebClientId;
                        if (!string.IsNullOrWhiteSpace(o.firebaseApiKey)) { config.firebaseApiKey = o.firebaseApiKey; config.firebaseProjectId = o.firebaseProjectId; }
                    }
                }
            }
            catch { }
        }

        /// <summary>Accept the OAuth client JSON Google offers to download (installed client), or "id secret".</summary>
        public bool SetGoogleClient(string text)
        {
            text = (text ?? "").Trim();
            string id = Regex.Match(text, @"[0-9]+-[0-9a-z]+\.apps\.googleusercontent\.com").Value;
            string secret = Regex.Match(text, "client_secret\"?\\s*[:=]\\s*\"([^\"]+)\"").Groups[1].Value;
            if (secret.Length == 0) secret = Regex.Match(text, @"\b(GOCSPX-[A-Za-z0-9_\-]{10,60})\b").Groups[1].Value;
#if UNITY_WEBGL && !UNITY_EDITOR
            // the web build uses a "Web application" client: the id alone
            if (id.Length == 0) return false;
            config.googleWebClientId = id;
#else
            if (id.Length == 0 || secret.Length == 0) return false;
            config.googleClientId = id; config.googleClientSecret = secret;
#endif
            SaveConfig();
            Status = CloudStatus.SignedOut; Changed_?.Invoke();
            return true;
        }

        /// <summary>Accept the Firebase web config as Firebase shows it (a JavaScript object), as JSON, or as two lines.</summary>
        public bool SetFirebase(string text)
        {
            var apiKey = Regex.Match(text ?? "", "apiKey[\"']?\\s*[:=]\\s*[\"']([A-Za-z0-9_\\-]{20,60})[\"']").Groups[1].Value;
            if (apiKey.Length == 0) apiKey = Regex.Match(text ?? "", @"\b(AIza[0-9A-Za-z_\-]{30,40})\b").Groups[1].Value;
            var projectId = Regex.Match(text ?? "", "projectId[\"']?\\s*[:=]\\s*[\"']([a-z0-9\\-]{4,40})[\"']").Groups[1].Value;
            if (apiKey.Length == 0 || projectId.Length == 0) return false;
            config.firebaseApiKey = apiKey; config.firebaseProjectId = projectId;
            SaveConfig();
            fbIdToken = null;
            Changed_?.Invoke();
            return true;
        }

        void SaveConfig() { try { File.WriteAllText(Path.Combine(Dir, "online.json"), JsonConvert.SerializeObject(config, Formatting.Indented)); } catch { } }

        // ----------------------------------------------------------------------------------- HTTP
        sealed class Resp { public long code; public string text; public bool Ok => code >= 200 && code < 300; }

        static async Task<Resp> Http(string method, string url, string body = null, string contentType = "application/json", Dictionary<string, string> headers = null)
        {
            using (var r = new UnityWebRequest(url, method))
            {
                if (body != null) { r.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)); r.SetRequestHeader("Content-Type", contentType); }
                r.downloadHandler = new DownloadHandlerBuffer();
                if (headers != null) foreach (var kv in headers) r.SetRequestHeader(kv.Key, kv.Value);
                r.timeout = 30;
                await r.SendWebRequest();
                if (r.result == UnityWebRequest.Result.ConnectionError) throw new Exception($"Could not reach {new Uri(url).Host}: {r.error}. Check your internet connection or firewall.");
                return new Resp { code = r.responseCode, text = r.downloadHandler?.text ?? "" };
            }
        }

        static string Form(params (string k, string v)[] kv) => string.Join("&", kv.Select(p => $"{Uri.EscapeDataString(p.k)}={Uri.EscapeDataString(p.v)}"));
        Dictionary<string, string> Bearer(string t) => new Dictionary<string, string> { ["Authorization"] = "Bearer " + t };

        // ----------------------------------------------------------------------------------- sign-in
        public bool IsSignedIn => accessToken != null && DateTime.UtcNow < accessExp.AddSeconds(-30);
        public bool PilotSignedIn => fbIdToken != null || !string.IsNullOrEmpty(prefs.fbRefreshToken);

        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Open Google's sign-in page in the browser and wait for the answer on a local port.</summary>
        public async void SignIn()
        {
            if (!config.HasGoogle) { Set(CloudStatus.Unconfigured); return; }
            if (busy) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            Set(CloudStatus.Working, "Loading Google sign-in…");
            WebStorage.GoogleToken(config.googleWebClientId, SCOPES, gameObject.name);
            return;
#endif
            busy = true;
            Set(CloudStatus.Working, "Opening Google sign-in in your browser…");
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var redirect = $"http://127.0.0.1:{port}/";
                var verifier = B64Url(RandomBytes(32));
                string challenge; using (var sha = SHA256.Create()) challenge = B64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
                var state = B64Url(RandomBytes(16));
                var url = "https://accounts.google.com/o/oauth2/v2/auth?" + Form(("client_id", config.googleClientId), ("redirect_uri", redirect), ("response_type", "code"), ("scope", SCOPES),
                    ("code_challenge", challenge), ("code_challenge_method", "S256"), ("state", state), ("access_type", "offline"), ("prompt", "consent"));
                Application.OpenURL(url);
                Set(CloudStatus.Working, "Finish signing in in your browser, then come back to the game.");
                var acceptTask = listener.AcceptTcpClientAsync();
                if (await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromMinutes(5))) != acceptTask) { Set(CloudStatus.SignedOut, "Sign-in timed out."); return; }
                string query;
                using (var client = acceptTask.Result)
                using (var stream = client.GetStream())
                {
                    var buf = new byte[8192];
                    var n = await stream.ReadAsync(buf, 0, buf.Length);
                    var req = Encoding.ASCII.GetString(buf, 0, n);
                    var line = req.Split('\n')[0];
                    var m = Regex.Match(line, @"GET\s+(\S+)");
                    query = m.Success && m.Groups[1].Value.Contains("?") ? m.Groups[1].Value.Substring(m.Groups[1].Value.IndexOf('?') + 1) : "";
                    var okPage = "<html><head><meta charset='utf-8'><title>Rotorcraft</title></head><body style='background:#11181d;color:#e6ecef;font-family:sans-serif;display:flex;align-items:center;justify-content:center;height:100vh'><div style='text-align:center'><h2>Signed in to Rotorcraft</h2><p>You can close this tab and go back to the game.</p></div></body></html>";
                    var resp = Encoding.UTF8.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(okPage)}\r\nConnection: close\r\n\r\n{okPage}");
                    await stream.WriteAsync(resp, 0, resp.Length);
                }
                var q = query.Split('&').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
                if (q.TryGetValue("error", out var err)) { Set(CloudStatus.SignedOut, err == "access_denied" ? "Sign-in cancelled." : $"Sign-in failed: {err}"); return; }
                if (!q.TryGetValue("code", out var code) || !q.TryGetValue("state", out var st) || st != state) { Set(CloudStatus.SignedOut, "Sign-in failed: no answer from Google."); return; }
                var tok = await Http("POST", "https://oauth2.googleapis.com/token", Form(("code", code), ("client_id", config.googleClientId), ("client_secret", config.googleClientSecret), ("redirect_uri", redirect), ("grant_type", "authorization_code"), ("code_verifier", verifier)), "application/x-www-form-urlencoded");
                if (!tok.Ok) { Set(CloudStatus.Error, $"Sign-in failed ({tok.code}): {ErrorText(tok.text)}"); return; }
                var j = JObject.Parse(tok.text);
                accessToken = (string)j["access_token"]; accessExp = DateTime.UtcNow.AddSeconds((double?)j["expires_in"] ?? 3600);
                if (j["refresh_token"] != null) prefs.refreshToken = (string)j["refresh_token"];
                var idt = (string)j["id_token"];
                if (idt != null) prefs.email = EmailFromIdToken(idt) ?? prefs.email;
                SavePrefs();
                busy = false;
                await PilotSignIn();
                await SyncNow();
                _ = PublishProfile();
            }
            catch (Exception e) { Set(CloudStatus.Error, e.Message); }
            finally { busy = false; try { listener?.Stop(); } catch { } }
        }

        /// <summary>The web build's answer from Google Identity Services (called from the page by SendMessage).</summary>
        public async void OnWebToken(string json)
        {
            try
            {
                var j = JObject.Parse(json);
                if (j["pending"] != null) { Set(CloudStatus.Working, "Click anywhere on the page to open Google's sign-in window."); return; }
                var err = (string)j["error"];
                if (err != null || j["access_token"] == null) { Set(CloudStatus.SignedOut, err == "access_denied" || err == "popup_closed" ? "Sign-in cancelled." : $"Sign-in failed: {err ?? "no answer from Google"}"); return; }
                accessToken = (string)j["access_token"]; accessExp = DateTime.UtcNow.AddSeconds((double?)j["expires_in"] ?? 3600);
                var me = await Http("GET", "https://www.googleapis.com/oauth2/v3/userinfo", headers: Bearer(accessToken));
                if (me.Ok) prefs.email = (string)JObject.Parse(me.text)["email"] ?? prefs.email;
                prefs.refreshToken = "web";   // marks "signed in before": the browser asks Google again next time
                SavePrefs();
                await PilotSignIn();
                await SyncNow();
                _ = PublishProfile();
            }
            catch (Exception e) { Set(CloudStatus.Error, e.Message); }
        }

        static byte[] RandomBytes(int n) { var b = new byte[n]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }

        static string EmailFromIdToken(string idt)
        {
            try
            {
                var part = idt.Split('.')[1].Replace('-', '+').Replace('_', '/');
                part = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
                return (string)JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(part)))["email"];
            }
            catch { return null; }
        }

        static string ErrorText(string body)
        {
            try { var j = JObject.Parse(body); return (string)j["error_description"] ?? (string)j["error"]?["message"] ?? (string)j["error"] ?? body; }
            catch { return body.Length > 160 ? body.Substring(0, 160) : body; }
        }

        async Task RenewAccess()
        {
            if (string.IsNullOrEmpty(prefs.refreshToken) || !config.HasGoogle) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            // no refresh tokens in the browser: Google's session lasts an hour, then the player signs in again
            if (accessToken == null) Set(CloudStatus.SignedOut, "Sign in with Google to sync this session.");
            return;
#endif
            try
            {
                var r = await Http("POST", "https://oauth2.googleapis.com/token", Form(("client_id", config.googleClientId), ("client_secret", config.googleClientSecret), ("refresh_token", prefs.refreshToken), ("grant_type", "refresh_token")), "application/x-www-form-urlencoded");
                if (!r.Ok)
                {
                    if (r.code == 400 || r.code == 401) { prefs.refreshToken = null; SavePrefs(); Set(CloudStatus.SignedOut, "Google sign-in expired: sign in again."); }
                    return;
                }
                var j = JObject.Parse(r.text);
                accessToken = (string)j["access_token"]; accessExp = DateTime.UtcNow.AddSeconds((double?)j["expires_in"] ?? 3600);
            }
            catch (Exception e) { Set(CloudStatus.Error, e.Message); }
        }

        async Task<string> Token()
        {
            if (!IsSignedIn) await RenewAccess();
            return IsSignedIn ? accessToken : null;
        }

        public async void SignOut()
        {
            var t = accessToken ?? prefs.refreshToken;
            accessToken = null; fbIdToken = null; Conflict = null;
            prefs = new Prefs { visible = prefs.visible };
            SavePrefs();
            Set(CloudStatus.SignedOut, "Signed out. Your saves stay on this computer and in your Google Drive.");
            if (t != null) try { await Http("POST", "https://oauth2.googleapis.com/revoke", Form(("token", t)), "application/x-www-form-urlencoded"); } catch { }
        }

        // ----------------------------------------------------------------------------------- Drive
        async Task<string> FindFile(string token)
        {
            var q = Uri.EscapeDataString($"name='{CLOUD_FILE}'");
            var r = await Http("GET", $"https://www.googleapis.com/drive/v3/files?spaces=appDataFolder&q={q}&fields=files(id,modifiedTime)&pageSize=10", headers: Bearer(token));
            if (!r.Ok) throw new Exception(r.code == 401 ? "Google sign-in has expired" : $"Looking for the save failed ({r.code}) {ErrorText(r.text)}");
            fileId = (string)JObject.Parse(r.text)["files"]?.FirstOrDefault()?["id"];
            return fileId;
        }

        async Task<CloudBundle> ReadBundle(string token)
        {
            var id = fileId ?? await FindFile(token);
            if (id == null) return null;
            var r = await Http("GET", $"https://www.googleapis.com/drive/v3/files/{Uri.EscapeDataString(id)}?alt=media", headers: Bearer(token));
            if (!r.Ok) throw new Exception($"Downloading the save failed ({r.code})");
            try
            {
                var b = SaveData.Read<CloudBundle>(r.text);
                if (b == null || b.app != "rotorcraft" || b.v != 1) return null;
                // the same checks as a local save or an imported file
                if (b.career != null) { b.career = SaveData.ValidateCareer(b.career); if (b.career == null) return null; }
                b.freeplay = b.freeplay != null ? SaveData.ValidateFreeplay(b.freeplay) : null;
                b.company = b.company != null ? SaveData.ValidateCompany(b.company) : null;
                return b;
            }
            catch { return null; }
        }

        async Task WriteBundle(string token, CloudBundle b)
        {
            var json = SaveData.Write(b);
            var id = fileId ?? await FindFile(token);
            if (id != null)
            {
                var r = await Http("PATCH", $"https://www.googleapis.com/upload/drive/v3/files/{Uri.EscapeDataString(id)}?uploadType=media", json, "application/json", Bearer(token));
                if (r.code == 404) { fileId = null; await WriteBundle(token, b); return; }
                if (!r.Ok) throw new Exception($"Uploading the save failed ({r.code}) {ErrorText(r.text)}");
                return;
            }
            var boundary = "rotorcraft" + B64Url(RandomBytes(6));
            var body = $"--{boundary}\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n{{\"name\":\"{CLOUD_FILE}\",\"parents\":[\"appDataFolder\"]}}\r\n--{boundary}\r\nContent-Type: application/json\r\n\r\n{json}\r\n--{boundary}--";
            var c = await Http("POST", "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id", body, $"multipart/related; boundary={boundary}", Bearer(token));
            if (!c.Ok) throw new Exception($"Creating the save failed ({c.code}) {ErrorText(c.text)}");
            fileId = (string)JObject.Parse(c.text)["id"];
        }

        static FreeplayRecord MergeFreeplay(FreeplayRecord a, FreeplayRecord b)
        {
            if (a == null) return b; if (b == null) return a;
            if (a.shifts != b.shifts) return a.shifts > b.shifts ? a : b;
            return a.seconds >= b.seconds ? a : b;
        }

        static Company NewerCompany(Company a, Company b)
        {
            if (a == null) return b; if (b == null) return a;
            if (a.founded != b.founded) return a;   // different companies: keep this computer's
            return a.shift >= b.shift ? a : b;
        }

        /// <summary>Compare with the cloud copy and download, upload or ask.</summary>
        public async Task SyncNow()
        {
            var app = GameApp.Instance;
            if (app == null || busy) return;
            var token = await Token();
            if (token == null) { Set(CloudStatus.SignedOut, string.IsNullOrEmpty(prefs.refreshToken) ? null : "Reconnect to Google to sync."); return; }
            busy = true;
            Set(CloudStatus.Working, "Syncing with Google Drive…");
            try
            {
                var cloud = await ReadBundle(token);
                var local = app.Career;
                var fp = MergeFreeplay(app.Freeplay, cloud?.freeplay);
                var co = NewerCompany(app.Company, cloud?.company);
                var cc = cloud?.career;
                string plan;
                if (local == null && cc == null) plan = "none";
                else if (cc == null) plan = "upload";
                else if (local == null) plan = "download";
                else if (local.id != cc.id) plan = "ask";
                else if (local.updatedAt == cc.updatedAt) plan = "none";
                else plan = local.updatedAt > cc.updatedAt ? "upload" : "download";
                if (plan == "ask") { Conflict = (local, cc, cloud?.freeplay, cloud?.company); busy = false; Set(CloudStatus.SignedIn, "Two different careers found: choose which to keep."); return; }
                if (plan == "download") app.ApplyCloud(cc, fp, co);
                else if (!ReferenceEquals(fp, app.Freeplay) || !ReferenceEquals(co, app.Company)) app.ApplyCloud(null, fp, co);
                if (plan == "upload" || cloud == null || !ReferenceEquals(fp, cloud.freeplay) || !ReferenceEquals(co, cloud.company))
                    await WriteBundle(token, new CloudBundle { savedAt = CareerManager.Now(), career = app.Career, freeplay = app.Freeplay, company = app.Company });
                Done(plan == "download" ? "Loaded your save from Google Drive." : "Saved to Google Drive.");
            }
            catch (Exception e) { Fail(e); }
            busy = false;
        }

        /// <summary>Pick a side after a conflict.</summary>
        public async void Resolve(bool keepCloud)
        {
            var c = Conflict; var app = GameApp.Instance;
            if (c == null || app == null) return;
            Conflict = null;
            if (keepCloud) { app.ApplyCloud(c.Value.cloud, MergeFreeplay(app.Freeplay, c.Value.cloudFp), NewerCompany(app.Company, c.Value.cloudCo)); Done("Loaded your save from Google Drive."); return; }
            var token = await Token();
            if (token == null) { Set(CloudStatus.SignedOut, "Reconnect to Google to sync."); return; }
            busy = true; Set(CloudStatus.Working, "Saving this computer's career to Google Drive…");
            try { await WriteBundle(token, new CloudBundle { savedAt = CareerManager.Now(), career = app.Career, freeplay = MergeFreeplay(app.Freeplay, c.Value.cloudFp), company = app.Company }); Done("Saved to Google Drive."); }
            catch (Exception e) { Fail(e); }
            busy = false;
        }

        /// <summary>Called after every local save: uploads a few seconds later (batched).</summary>
        public void Changed()
        {
            if (Conflict != null) return;
            if (string.IsNullOrEmpty(prefs.refreshToken) && !IsSignedIn) return;
            dirty = true;
            flushAt = Time.unscaledTime + 3;
        }

        async Task Flush()
        {
            _ = PublishProfile();
            var app = GameApp.Instance;
            if (!dirty || app == null || busy) return;
            var token = await Token();
            if (token == null) { Set(CloudStatus.SignedOut, "Not synced: reconnect to Google."); return; }
            busy = true; dirty = false;
            try { await WriteBundle(token, new CloudBundle { savedAt = CareerManager.Now(), career = app.Career, freeplay = app.Freeplay, company = app.Company }); Done("Saved to Google Drive."); }
            catch (Exception e) { dirty = true; Fail(e); }
            busy = false;
        }

        void Done(string msg) { prefs.lastSync = CareerManager.Now(); SavePrefs(); Set(CloudStatus.SignedIn, msg); }
        void Fail(Exception e)
        {
            if (e.Message.Contains("expired")) { accessToken = null; Set(CloudStatus.SignedOut, "Google sign-in expired: reconnect to keep syncing."); return; }
            Set(CloudStatus.Error, e.Message);
        }

        // ----------------------------------------------------------------------------------- pilot area
        string FsBase => $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(config.firebaseProjectId)}/databases/(default)/documents";
        string FsKey => "key=" + Uri.EscapeDataString(config.firebaseApiKey);

        async Task PilotSignIn()
        {
            if (!config.HasFirebase || accessToken == null) return;
            try
            {
                var body = JsonConvert.SerializeObject(new { postBody = $"access_token={Uri.EscapeDataString(accessToken)}&providerId=google.com", requestUri = "http://localhost", returnSecureToken = true, returnIdpCredential = false });
                var r = await Http("POST", $"https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key={Uri.EscapeDataString(config.firebaseApiKey)}", body);
                if (!r.Ok) { PilotMessage = $"Pilot area: sign-in failed ({r.code}) {ErrorText(r.text)}"; RefreshUi(); return; }
                var j = JObject.Parse(r.text);
                fbIdToken = (string)j["idToken"]; fbExp = DateTime.UtcNow.AddSeconds(double.Parse((string)j["expiresIn"] ?? "3600"));
                prefs.uid = (string)j["localId"]; prefs.fbRefreshToken = (string)j["refreshToken"];
                SavePrefs();
                PilotMessage = null;
            }
            catch (Exception e) { PilotMessage = "Pilot area: " + e.Message; }
            RefreshUi();
        }

        async Task<string> PilotToken()
        {
            if (!config.HasFirebase) return null;
            if (fbIdToken != null && DateTime.UtcNow < fbExp.AddSeconds(-60)) return fbIdToken;
            if (string.IsNullOrEmpty(prefs.fbRefreshToken)) return null;
            try
            {
                var r = await Http("POST", $"https://securetoken.googleapis.com/v1/token?key={Uri.EscapeDataString(config.firebaseApiKey)}", Form(("grant_type", "refresh_token"), ("refresh_token", prefs.fbRefreshToken)), "application/x-www-form-urlencoded");
                if (!r.Ok) { if (r.code == 400) { prefs.fbRefreshToken = null; SavePrefs(); PilotMessage = "Pilot sign-in expired: sign in with Google again."; } return null; }
                var j = JObject.Parse(r.text);
                fbIdToken = (string)j["id_token"]; fbExp = DateTime.UtcNow.AddSeconds(double.Parse((string)j["expires_in"] ?? "3600"));
                prefs.uid = (string)j["user_id"] ?? prefs.uid; prefs.fbRefreshToken = (string)j["refresh_token"] ?? prefs.fbRefreshToken;
                SavePrefs();
                return fbIdToken;
            }
            catch { return null; }
        }

        public static PilotProfile ProfileFromCareer(Career c, FreeplayRecord fp)
        {
            if (c == null) return null;
            var rank = CareerManager.RankOf(c);
            var lvl = Math.Max(0, Array.IndexOf(RANKS, rank));
            var h = Math.Min(100000, CareerManager.Hours(c));
            var st = c.stats;
            double Hrs(string id) => Math.Round((st.perAircraftS.TryGetValue(id, out var s) ? s : 0) / 3600 * 10) / 10;
            var name = Regex.Replace(c.pilotName ?? "", @"[^\p{L}\p{N} .'\-_]", "").Trim();
            if (name.Length > 24) name = name.Substring(0, 24);
            return new PilotProfile
            {
                name = name.Length >= 2 ? name : "Pilot", rank = rank, rankLevel = lvl, rankScore = lvl * 100000 + Math.Round(Math.Min(h, 9999) * 10), hours = Math.Round(h * 10) / 10,
                flights = st.flights, landings = st.landings, smoothPct = st.landings > 0 ? (int)Math.Round(100.0 * st.smoothLandings / st.landings) : 0, accidents = st.accidents,
                rescues = st.rescues, passengers = st.passengers, operational = st.operational,
                quals = Qualifications.ALL.Where(q => CareerManager.HasQual(c, q.id)).Select(q => q.id).ToList(), qualCount = Qualifications.ALL.Count(q => CareerManager.HasQual(c, q.id)),
                bestStanding = Employers.ALL.Where(e => c.reputation.ContainsKey(e.id)).Select(e => CareerManager.StandingWith(c, e.id)).DefaultIfEmpty(0).Max(),
                careerEarned = Math.Round(c.money.earned), fpShifts = fp?.shifts ?? 0, fpJobs = fp?.jobs ?? 0, fpEarned = Math.Round(fp?.earned ?? 0), fpBest = Math.Round(fp?.bestShift ?? 0),
                acHours = new Dictionary<string, double> { ["kestrel"] = Hrs("kestrel"), ["northstar"] = Hrs("northstar"), ["guardian"] = Hrs("guardian"), ["nightjar"] = Hrs("nightjar"), ["peregrine"] = Hrs("peregrine") },
                startedAt = c.createdAt, updatedAt = CareerManager.Now(),
            };
        }

        static JToken ToFs(JToken v)
        {
            switch (v.Type)
            {
                case JTokenType.Null: return new JObject { ["nullValue"] = null };
                case JTokenType.Boolean: return new JObject { ["booleanValue"] = v };
                case JTokenType.Integer: return new JObject { ["integerValue"] = v.ToString() };
                case JTokenType.Float: var d = (double)v; return Math.Abs(d % 1) < 1e-9 && Math.Abs(d) < 1e15 ? new JObject { ["integerValue"] = ((long)d).ToString() } : new JObject { ["doubleValue"] = d };
                case JTokenType.String: return new JObject { ["stringValue"] = v };
                case JTokenType.Array: return new JObject { ["arrayValue"] = new JObject { ["values"] = new JArray(v.Select(ToFs)) } };
                case JTokenType.Object: return new JObject { ["mapValue"] = new JObject { ["fields"] = FsFields((JObject)v) } };
                default: return new JObject { ["stringValue"] = v.ToString() };
            }
        }
        static JObject FsFields(JObject o) { var f = new JObject(); foreach (var p in o.Properties()) f[p.Name] = ToFs(p.Value); return f; }
        static JToken FromFs(JToken v)
        {
            if (!(v is JObject o)) return JValue.CreateNull();
            if (o["stringValue"] != null) return o["stringValue"];
            if (o["integerValue"] != null) return new JValue(double.Parse((string)o["integerValue"]));
            if (o["doubleValue"] != null) return o["doubleValue"];
            if (o["booleanValue"] != null) return o["booleanValue"];
            if (o["arrayValue"] != null) return new JArray((o["arrayValue"]["values"] as JArray ?? new JArray()).Select(FromFs));
            if (o["mapValue"] != null) return FromFsFields(o["mapValue"]["fields"] as JObject);
            return JValue.CreateNull();
        }
        static JObject FromFsFields(JObject f) { var o = new JObject(); if (f != null) foreach (var p in f.Properties()) o[p.Name] = FromFs(p.Value); return o; }

        /// <summary>Publish this pilot's profile (or remove it when hidden). Never throws.</summary>
        public async Task PublishProfile(bool force = false)
        {
            var app = GameApp.Instance;
            if (app == null || !config.HasFirebase) return;
            var t = await PilotToken();
            if (t == null || prefs.uid == null) return;
            var prof = ProfileFromCareer(app.Career, app.Freeplay);
            try
            {
                var url = $"{FsBase}/pilots/{Uri.EscapeDataString(prefs.uid)}?{FsKey}";
                if (!prefs.visible || prof == null) { if (force) await Http("DELETE", url, headers: Bearer(t)); }
                else
                {
                    var body = new JObject { ["fields"] = FsFields(JObject.FromObject(prof)) }.ToString(Formatting.None);
                    var r = await Http("PATCH", url, body, "application/json", Bearer(t));
                    if (!r.Ok) throw new Exception($"Publishing your pilot profile failed ({r.code}) {ErrorText(r.text)}");
                    prefs.lastPublish = CareerManager.Now(); SavePrefs();
                }
                PilotMessage = null;
            }
            catch (Exception e) { PilotMessage = e.Message; }
            RefreshUi();
        }

        public async void SetVisible(bool v) { prefs.visible = v; SavePrefs(); RefreshUi(); await PublishProfile(true); }

        /// <summary>Top pilots on one board, or the most recently active (field "updatedAt").</summary>
        public async Task<List<PilotEntry>> Query(string field, int limit)
        {
            if (!config.HasFirebase) throw new Exception("The pilot area is not set up yet.");
            var t = await PilotToken();
            var q = new JObject { ["structuredQuery"] = new JObject { ["from"] = new JArray(new JObject { ["collectionId"] = "pilots" }), ["orderBy"] = new JArray(new JObject { ["field"] = new JObject { ["fieldPath"] = field }, ["direction"] = "DESCENDING" }), ["limit"] = limit } };
            var r = await Http("POST", $"{FsBase}:runQuery?{FsKey}", q.ToString(Formatting.None), "application/json", t != null ? Bearer(t) : null);
            if (!r.Ok) throw new Exception($"Loading the pilots failed ({r.code}) {ErrorText(r.text)}");
            var list = new List<PilotEntry>();
            foreach (var row in JArray.Parse(r.text))
            {
                var d = row["document"];
                var name = (string)d?["name"];
                if (name == null) continue;
                try
                {
                    var p = FromFsFields(d["fields"] as JObject).ToObject<PilotProfile>();
                    if (p?.name == null) continue;
                    if (!RANKS.Contains(p.rank)) p.rank = "Student Pilot";
                    p.rankLevel = Math.Max(0, Array.IndexOf(RANKS, p.rank));
                    p.name = Regex.Replace(p.name, @"[^\p{L}\p{N} .'\-_]", "");
                    list.Add(new PilotEntry { uid = name.Substring(name.LastIndexOf('/') + 1), profile = p });
                }
                catch { }
            }
            return list;
        }
    }
}

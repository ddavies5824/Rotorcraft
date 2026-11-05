// Films the game's trailer from the real game: a build made with RC_TRAILER (GameSceneBuilder.BuildWindowsTrailer), started
// with "-trailer <folder>", flies each shot and saves every frame as a picture, with the titles drawn over it. Time is fixed
// per frame, so a slow frame cannot show, and a bar of music (120 beats a minute, four to the bar) is exactly 60 frames, so
// the cuts land on the beat. "-parts a,b" films only those parts, to re-shoot one; "-trailerfast" films quickly and saves
// contact sheets instead. Lives in the VR assembly because it also shows the VR hands and switches. Not in the real game.
#if RC_TRAILER
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rotorcraft.Game;
using Rotorcraft.Inputs;
using Rotorcraft.Presentation;
using Rotorcraft.Presentation.UI;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.UIElements;
using SimAircraft = Rotorcraft.Sim.Aircraft;

namespace Rotorcraft.VR
{
    [DefaultExecutionOrder(11000)]   // the camera is placed last of all
    public sealed class TrailerRun : MonoBehaviour
    {
        struct View { public Vector3 pos; public Quaternion rot; public float fov, near; }

        /// <summary>A smooth line through points, in the simulation's axes.</summary>
        sealed class FlightLine
        {
            readonly Vec3[] p;
            public readonly double Length;
            public FlightLine(Vec3[] points)
            {
                p = points;
                var last = At(0);
                for (int i = 1; i <= 100; i++)
                {
                    var here = At(i / 100f);
                    Length += Math.Sqrt((here.x - last.x) * (here.x - last.x) + (here.y - last.y) * (here.y - last.y) + (here.z - last.z) * (here.z - last.z));
                    last = here;
                }
            }
            public Vec3 At(float u)
            {
                var n = p.Length - 1;
                var f = Mathf.Clamp01(u) * n;
                var i = Mathf.Min(Mathf.FloorToInt(f), n - 1);
                var t = (double)(f - i);
                Vec3 a = p[Mathf.Max(i - 1, 0)], b = p[i], c = p[i + 1], d = p[Mathf.Min(i + 2, n)];
                double C(double p0, double p1, double p2, double p3) => 0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t * t * t);
                return new Vec3(C(a.x, b.x, c.x, d.x), C(a.y, b.y, c.y, d.y), C(a.z, b.z, c.z, d.z));
            }
        }

        public string folder;
        int fps = 30;
        int FramesPerBar => fps * 2;
        bool fast;
        HashSet<string> parts;

        GameApp app;
        FlightSession f;
        HelicopterController heli;
        HeliState s;
        Camera cam;
        Func<float, View> view;
        float shotU, secs;
        double phiS, thetaS, psiS, psiHold;
        bool settling;
        const double MaxBank = 30 * Math.PI / 180, MaxYawRate = 50 * Math.PI / 180;
        string loaded;

        string partDir;
        int frameNo;
        StringBuilder timeline, attitude;
        const int SoundRate = 48000;
        BinaryWriter soundFile;
        RotorSynth synth;
        AircraftConfig soundFor;
        float[] mono;
        byte[] pcm;
        float lastGain = 1, lastPan;
        bool soundSettled, hornOn;
        Texture2D sheet;
        int sheetCell, sheetNo, midNo;
        string shotName;
        const int ThumbW = 320, ThumbH = 180, SheetCols = 6, SheetRows = 7;

        VisualElement fadeBox, barTop, barBottom, capBox, bigBox, logo;
        VisualElement bigLetters;
        Label capTitle, capSub, bigSub, bigFoot, debugLabel;

        public static string ArgFolder() => Arg("-trailer");

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            app = GameApp.Instance;
            f = app.flight;
            cam = f.cam.GetComponent<Camera>();
            fast = Array.IndexOf(Environment.GetCommandLineArgs(), "-trailerfast") >= 0;
            if (fast) fps = 10;
            var only = Arg("-parts");
            if (only != null) parts = new HashSet<string>(only.Split(','));
            Directory.CreateDirectory(folder);
            var st = app.Settings.Clone(); st.fullscreen = false; st.sound = false; st.graphics = GraphicsQuality.High; st.assist = AssistMode.Beginner;
            app.CommitSettings(st);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return new WaitForSecondsRealtime(2);
            Debug.Log($"TRAILER: screen {Screen.width}x{Screen.height}; {Missions.All.Count} missions, {Theory.MODULES.Count} ground school modules, {Theory.EXAMS.Count} exams, {Qualifications.ALL.Count} qualifications, {Employers.ALL.Count} employers, {SimAircraft.All.Count} aircraft");
            var pilot = CareerManager.CreateCareer("Alex Ross", null, CareerStart.Commercial);
            var st2 = pilot.stats;
            st2.flights = 186; st2.landings = 412; st2.smoothLandings = 365; st2.passengers = 1240; st2.rescues = 23; st2.operational = 97; st2.distanceKm = 48200;
            foreach (var (id, hours) in new[] { ("kestrel", 96.0), ("northstar", 142.0), ("guardian", 88.0), ("nightjar", 51.0), ("peregrine", 37.0) }) st2.perAircraftS[id] = hours * 3600;
            pilot.flightSeconds = 414 * 3600;
            app.CommitCareer(pilot);
            app.CommitCompany(Companies.CreateCompany("Kinloch Helicopters", StartPackage.Owner, 1800000000000, 7));
            MakeOverlay();

            yield return Part("open", Opening);
            yield return Part("world", Region);
            yield return Part("weather", WeatherAndLight);
            yield return Part("aircraft", Fleet);
            yield return Part("cockpit", Cockpits);
            yield return Part("missions", Jobs);
            yield return Part("career", CareerScreens);
            yield return Part("vr", VirtualReality);
            yield return Part("end", Ending);
            Debug.Log("TRAILER: done");
            Time.captureFramerate = 0;
            Application.Quit();
        }

        IEnumerator Part(string name, Func<IEnumerator> body)
        {
            if (parts != null && !parts.Contains(name)) yield break;
            partDir = Path.Combine(folder, "frames", name);
            if (Directory.Exists(partDir)) Directory.Delete(partDir, true);
            Directory.CreateDirectory(partDir);
            frameNo = 0;
            timeline = new StringBuilder();
            attitude = new StringBuilder();
            soundFile = fast ? null : new BinaryWriter(File.Create(Path.Combine(partDir, "game.pcm")));
            yield return body();
            soundFile?.Close();
            soundFile = null;
            File.WriteAllText(Path.Combine(partDir, "timeline.txt"), timeline.ToString());
            File.WriteAllText(Path.Combine(partDir, "attitude.txt"), attitude.ToString());
            if (sheet != null) SaveSheet();
            Debug.Log($"TRAILER: part {name}: {frameNo} frames");
        }

        void LateUpdate()
        {
            AudioListener.volume = 0;
            if (view == null || cam == null) return;
            var v = view(shotU);
            cam.transform.SetPositionAndRotation(v.pos, v.rot);
            cam.fieldOfView = v.fov;
            cam.nearClipPlane = v.near;
            cam.cullingMask |= 1 << 5;
        }

        // ------------------------------------------------------------------------------------------ shots
        /// <summary>Film a shot: the pre-roll lets the world, the rain and the rotor wash settle before anything is kept.</summary>
        IEnumerator Shot(string name, float bars, Action<float> act, Func<float, View> look, string title = null, string sub = null, bool hud = false, bool letterbox = true,
            int preRoll = 15, float fadeIn = 0, float fadeOut = 0, bool big = false, string bigSubtitle = null, string foot = null, bool black = false, float captionBottom = -1)
        {
            var n = Mathf.RoundToInt(bars * FramesPerBar);
            secs = n / (float)fps;
            shotName = name;
            view = look;
            shotU = 0;
            ShowHud(hud);
            barTop.style.height = barBottom.style.height = letterbox ? 104 : 0;
            capBox.style.bottom = captionBottom >= 0 ? captionBottom : letterbox ? 140 : 56;
            capTitle.text = title ?? ""; capSub.text = sub ?? "";
            capSub.style.display = string.IsNullOrEmpty(sub) ? DisplayStyle.None : DisplayStyle.Flex;
            capBox.style.opacity = 0;
            capBox.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, hud ? 0.88f : 0.5f);
            bigBox.style.opacity = 0;
            bigLetters.style.display = big ? DisplayStyle.Flex : DisplayStyle.None; bigSub.text = bigSubtitle ?? ""; bigFoot.text = foot ?? "";
            logo.style.display = foot != null ? DisplayStyle.Flex : DisplayStyle.None;
            fadeBox.style.opacity = fadeIn > 0 ? 1 : 0;
            debugLabel.text = fast ? name : "";
            settling = true;
            soundSettled = false;
            for (int i = 0; i < preRoll * fps / 30; i++) { act?.Invoke(0); yield return null; }
            settling = false;
            double firstPhi = 0, worstPhi = 0, worstRoll = 0, worstYaw = 0, worstPitch = 0, lastPhi = 0, lastPsi = 0, lastTheta = 0;
            var attitudeKnown = false;

            timeline.AppendLine($"{frameNo}\t{n}\t{name}\t{title}");
            var edge = Mathf.Max(1, fps / 3);
            for (int i = 0; i < n; i++)
            {
                shotU = n > 1 ? i / (float)(n - 1) : 0;
                act?.Invoke(shotU);
                if (act != null && look != null && s != null)
                {
                    if (attitudeKnown)
                    {
                        var yawStep = s.psi - lastPsi;
                        worstYaw = Math.Max(worstYaw, Math.Abs(Math.Atan2(Math.Sin(yawStep), Math.Cos(yawStep))));
                        worstRoll = Math.Max(worstRoll, Math.Abs(s.phi - lastPhi));
                        worstPitch = Math.Max(worstPitch, Math.Abs(s.theta - lastTheta));
                    }
                    else firstPhi = s.phi;
                    attitudeKnown = true;
                    worstPhi = Math.Max(worstPhi, Math.Abs(s.phi));
                    lastPhi = s.phi; lastPsi = s.psi; lastTheta = s.theta;
                }
                var t = i / (float)fps;
                var left = (n - 1 - i) / (float)fps;
                if (!string.IsNullOrEmpty(title))
                {
                    var a = Mathf.Clamp01(Mathf.Min((i - edge * 0.3f) / edge, (n - 1 - i) / (float)edge));
                    capBox.style.opacity = a;
                    capBox.style.translate = new Translate(Mathf.Lerp(-24, 0, Ease(Mathf.Clamp01(i / (float)(edge * 1.5f)))), 0);
                }
                if (big)
                {
                    var a = Mathf.Clamp01(Mathf.Min((t - bigStart) / 0.25f, left / Mathf.Max(0.3f, fadeOut)));
                    bigBox.style.opacity = a;
                }
                var fade = 0f;
                if (fadeIn > 0) fade = Mathf.Max(fade, 1 - t / fadeIn);
                if (fadeOut > 0) fade = Mathf.Max(fade, 1 - left / fadeOut);
                if (black) fade = 1;
                fadeBox.style.opacity = Mathf.Clamp01(fade);
                yield return new WaitForEndOfFrame();
                Capture(i, n);
                RecordSound(look != null);
                yield return null;
            }
            const double Deg = 180 / Math.PI;
            if (attitudeKnown) attitude.AppendLine($"{name}\t{firstPhi * Deg:F1}\t{worstPhi * Deg:F1}\t{worstRoll * Deg * fps:F1}\t{worstYaw * Deg * fps:F1}\t{worstPitch * Deg * fps:F1}");
        }

        float bigStart;

        void Capture(int i, int n)
        {
            if (fast)
            {
                if (i == 0 || i == n / 2 || i == n - 1) Thumbnail(i == n / 2);
                frameNo++;
                return;
            }
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(partDir, $"f{frameNo:00000}.jpg"), tex.EncodeToJPG(97));
            Destroy(tex);
            frameNo++;
        }

        void Thumbnail(bool keep)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (keep)
            {
                Directory.CreateDirectory(Path.Combine(folder, "mids"));
                File.WriteAllBytes(Path.Combine(folder, "mids", $"{midNo++:000}-{shotName.Replace(' ', '-')}.jpg"), tex.EncodeToJPG(88));
            }
            if (sheet == null) { sheet = new Texture2D(ThumbW * SheetCols, ThumbH * SheetRows, TextureFormat.RGB24, false); sheetCell = 0; }
            var rt = RenderTexture.GetTemporary(ThumbW, ThumbH, 0);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            int col = sheetCell % SheetCols, row = SheetRows - 1 - sheetCell / SheetCols;
            sheet.ReadPixels(new Rect(0, 0, ThumbW, ThumbH), col * ThumbW, row * ThumbH);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
            if (++sheetCell >= SheetCols * SheetRows) SaveSheet();
        }

        void SaveSheet()
        {
            sheet.Apply();
            File.WriteAllBytes(Path.Combine(folder, $"sheet-{sheetNo++:00}.jpg"), sheet.EncodeToJPG(88));
            Destroy(sheet);
            sheet = null;
        }

        void ShowHud(bool on) => app.document.rootVisualElement.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        static float Ease(float u) => u * u * (3 - 2 * u);
        static Vec3 A(double x, double agl, double z) => new Vec3(x, Math.Max(WorldMap.TerrainHeight(x, z), 0) + agl, z);
        static Vec3 Abs(double x, double y, double z) => new Vec3(x, y, z);
        static FlightLine Line(params Vec3[] p) => new FlightLine(p);
        static Vector3 U(Vec3 v) => SimToUnity.Position(v);

        // -------------------------------------------------------------------------------------- the sound
        /// <summary>
        /// The game's own sound generator run in step with the picture, one frame's worth at a time, from the same readings
        /// RotorAudio gives it in play, so it cannot drift. In the game it is the same wherever the camera is; a film needs the
        /// helicopter to be quieter far off and to pass from one side to the other, so this adds that on top. Silent when the
        /// camera is not in the world (the menus and the end card).
        /// </summary>
        void RecordSound(bool inWorld)
        {
            if (soundFile == null) return;
            var count = SoundRate / fps;
            if (mono == null || mono.Length != count) { mono = new float[count]; pcm = new byte[count * 4]; }
            Array.Clear(mono, 0, count);
            if (synth == null) synth = new RotorSynth(SoundRate) { volume = 0.6f };
            var hearing = inWorld && heli != null && heli.State != null && heli.Aircraft != null;
            synth.paused = !hearing;
            float gain = lastGain, pan = lastPan;
            if (hearing)
            {
                var state = heli.State; var read = state.@out; var ac = heli.Aircraft;
                if (soundFor != ac)
                {
                    soundFor = ac;
                    synth.turbine = ac.engines > 1 || (ac.realWorldClass != null && ac.realWorldClass.ToLowerInvariant().Contains("turbine"));
                    synth.bladePassHz = (ac.engines > 1 ? (ac.rotorDiameter > 15 ? 5 : 4) : 2) * heli.rotorRpmAt100 / 60f;
                    synth.tailBuzzHz = 2 * heli.rotorRpmAt100 * heli.tailRotorRatio / 60f;
                }
                synth.rpm = (float)read.rpm;
                synth.torque = (float)read.torque;
                synth.engine = state.engine == EngineState.Running ? 1 : (float)state.engineN;
                synth.airspeedKt = (float)read.iasKt;
                synth.horn = hornOn;
                synth.inside = heli.CockpitView;
                if (heli.CockpitView) { gain = 1; pan = 0; }
                else
                {
                    var away = heli.transform.position - cam.transform.position;
                    gain = Mathf.Clamp(22f / (away.magnitude + 8f), 0.08f, 1f);
                    pan = away.sqrMagnitude > 0.01f ? Vector3.Dot(cam.transform.right, away.normalized) * 0.8f : 0;
                }
                if (!soundSettled) { lastGain = gain; lastPan = pan; soundSettled = true; }
            }
            synth.Render(mono, 1);
            for (int i = 0; i < count; i++)
            {
                var k = (i + 1) / (float)count;
                var g = Mathf.Lerp(lastGain, gain, k); var a = (Mathf.Lerp(lastPan, pan, k) + 1) * Mathf.PI / 4;
                var v = mono[i] * g * Mathf.Sqrt(2);
                short left = (short)Mathf.Clamp(v * Mathf.Cos(a) * 32767f, -32767f, 32767f), right = (short)Mathf.Clamp(v * Mathf.Sin(a) * 32767f, -32767f, 32767f);
                pcm[i * 4] = (byte)left; pcm[i * 4 + 1] = (byte)(left >> 8); pcm[i * 4 + 2] = (byte)right; pcm[i * 4 + 3] = (byte)(right >> 8);
            }
            lastGain = gain; lastPan = pan;
            soundFile.Write(pcm);
        }

        // ------------------------------------------------------------------------------------- setting up
        IEnumerator Setup(string id, string weather, TimeOfDay tod, string skin = null, List<KitId> kit = null)
        {
            var key = $"{id}/{weather}/{tod}/{skin}/{kit?.Count}";
            if (key != loaded || !f.Active)
            {
                yield return Leave();
                var ac = SimAircraft.Get(id);
                app.FlyFreeplay(new FreeplayConfig { aircraftId = id, kit = kit ?? Loadout.DefaultKit(ac), fuelKg = ac.fuelCapacity * 0.6, pace = FreeplayPace.Off, weatherId = weather, tod = tod, skin = skin });
                yield return Ready();
                loaded = key;
            }
            Prepare(tod);
        }

        IEnumerator Mission(string id, double fuel, TimeOfDay tod, string weather)
        {
            yield return Leave();
            app.FlyMission(id, fuel, tod, weather);
            yield return Ready();
            loaded = null;
            Prepare(tod);
        }

        IEnumerator Leave()
        {
            view = null;
            Time.captureFramerate = 0;
            if (f.Active)
            {
                heli.ControlFilter = null;
                f.Finish(FlightEnd.Aborted);
                yield return new WaitForSecondsRealtime(1);
            }
        }

        IEnumerator Ready()
        {
            while (!f.Active || f.Runtime == null) yield return null;
            yield return new WaitForSecondsRealtime(3);
            f.SkipForTour();
            f.ShowForTour();
            heli = f.heli;
            s = heli.State;
            heli.SetCamera(CameraMode.Orbit);
            Time.captureFramerate = fps;
        }

        void Prepare(TimeOfDay tod)
        {
            s = heli.State;
            heli.ControlFilter = (c, dt) => c.WithCollective(0.6);
            s.sys.beacon = s.sys.nav = true;
            s.sys.strobe = tod != TimeOfDay.Day;
            s.sys.landingLight = false;
            s.sys.searchlight = false;
            s.sys.governor = true;
            s.sys.throttle = s.sys.throttleTarget = 1;
            if (heli.Aircraft.retractableGear) { s.sys.gearDown = false; s.gearPos = 0; }
            heli.SetCamera(CameraMode.Orbit);
        }

        void Running(double? groundY = null)
        {
            s.engine = EngineState.Running; s.engineN = 1; s.rotorE = 1; s.govTarget = 1; s.crashed = false;
            s.onGround = groundY.HasValue && s.pos.y <= groundY.Value + 0.05;
            s.p = s.q = s.r = 0;
        }

        void Place() => heli.transform.SetPositionAndRotation(SimToUnity.Position(s.pos), SimToUnity.Attitude(s));

        /// <summary>Fly the aircraft along a line, nose on the way it is going, banking into turns and leaning into speed.</summary>
        void Fly(FlightLine l, float u, float maxSpeed = 62, float anchor = 0.5f, bool steady = false)
        {
            var k0 = Mathf.Min(1, (float)(maxSpeed * secs / Math.Max(l.Length, 1)));
            float On(float v) => anchor + (v - anchor) * k0;
            const float du = 0.015f;
            float u0 = Mathf.Max(u - du, 0), u1 = Mathf.Min(u + du, 1);
            var dt = Mathf.Max(0.001f, (u1 - u0) * secs);
            Vec3 a = l.At(On(u0)), b = l.At(On(u1));
            var vel = new Vec3((b.x - a.x) / dt, (b.y - a.y) / dt, (b.z - a.z) / dt);
            var speed = Math.Sqrt(vel.x * vel.x + vel.z * vel.z);
            // which way the line runs at a moment in the shot; two different points, so never a direction from nothing
            double Heading(float v)
            {
                Vec3 from = l.At(On(Mathf.Max(v - du, 0))), to = l.At(On(Mathf.Min(v + du, 1)));
                return Math.Atan2(to.x - from.x, -(to.z - from.z));
            }
            var moving = speed > 1;
            var psi = moving ? Math.Atan2(vel.x, -vel.z) : psiHold;
            if (steady && !settling) psi = psiHold;
            psiHold = psi;
            double turn = 0;
            if (moving && !steady)
            {
                float t0 = Mathf.Max(u - 0.045f, 0), t1 = Mathf.Min(u + 0.045f, 1);
                var dh = Heading(t1) - Heading(t0);
                turn = Math.Atan2(Math.Sin(dh), Math.Cos(dh)) / Math.Max(0.001, (t1 - t0) * secs);
            }
            var phi = Clamp(Math.Atan(speed * turn / 9.81), -MaxBank, MaxBank);
            var theta = moving ? -Clamp(speed / 70, 0, 1) * 7 * Math.PI / 180 : 0;
            if (settling)
            {
                // a shot opens already flying as it will fly, not still leaning from the shot before
                phiS = phi; thetaS = theta; psiS = psi;
            }
            else
            {
                var k = 1 - Math.Exp(-2.5 / fps);
                phiS += (phi - phiS) * k; thetaS += (theta - thetaS) * k;
                var yaw = psi - psiS;
                yaw = Math.Atan2(Math.Sin(yaw), Math.Cos(yaw));
                var most = MaxYawRate / fps;
                psiS += Clamp(yaw, -most, most);
            }
            s.pos = l.At(On(u)); s.vel = vel; s.psi = psiS; s.phi = phiS; s.theta = thetaS;
            Running();
            Place();
        }

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;

        /// <summary>Hold a hover, drifting a very little as a real one does.</summary>
        void Hover(Vec3 at, double psiDeg)
        {
            var t = Time.time;
            s.pos = new Vec3(at.x + Math.Sin(t * 0.7) * 0.25, at.y + Math.Sin(t * 0.9) * 0.12, at.z + Math.Sin(t * 0.53) * 0.2);
            s.vel = new Vec3(0, 0, 0);
            s.psi = psiDeg * Math.PI / 180;
            s.phi = Math.Sin(t * 0.7 + 1) * 0.012; s.theta = Math.Sin(t * 0.53 + 2) * 0.01;
            phiS = 0; thetaS = 0; psiHold = s.psi; psiS = s.psi;
            Running();
            Place();
        }

        // ---------------------------------------------------------------------------------------- cameras
        Vector3 Subject => heli.transform.position + Vector3.up * 1.4f * ModelScale;
        float ModelScale => heli.Aircraft != null ? (float)(heli.Aircraft.rotorDiameter / 7.7) : 1;

        static float GroundUnder(Vector3 p)
        {
            var sim = SimToUnity.ToSim(p);
            return (float)Math.Max(Ground.Current.GroundAt(sim.x, sim.z).h, 0);
        }

        /// <summary>Round the aircraft: yaw is from its nose (180 behind), dist is in lengths of a small helicopter.</summary>
        Func<float, View> Orbit(float yaw0, float yaw1, float pitch0, float pitch1, float dist0, float dist1, float fov = 40) => u =>
        {
            var e = Ease(u);
            var c = Subject;
            var rot = Quaternion.Euler(Mathf.Lerp(pitch0, pitch1, e), heli.transform.eulerAngles.y + Mathf.Lerp(yaw0, yaw1, e), 0);
            var pos = c - rot * Vector3.forward * Mathf.Lerp(dist0, dist1, e) * ModelScale;
            var g = GroundUnder(pos);
            if (pos.y < g + 0.8f) pos.y = g + 0.8f;
            return new View { pos = pos, rot = Quaternion.LookRotation(c - pos, Vector3.up), fov = fov, near = 0.1f };
        };

        /// <summary>A camera moving between two places in the world, watching the aircraft.</summary>
        Func<float, View> Dolly(Vec3 a, Vec3 b, float fov0 = 40, float fov1 = 40) => u =>
        {
            var e = Ease(u);
            var pos = Vector3.Lerp(U(a), U(b), e);
            return new View { pos = pos, rot = Quaternion.LookRotation(Subject - pos, Vector3.up), fov = Mathf.Lerp(fov0, fov1, e), near = 0.1f };
        };

        /// <summary>A camera moving between two places, watching a place rather than the aircraft.</summary>
        Func<float, View> Pan(Vec3 a, Vec3 b, Vec3 lookA, Vec3 lookB, float fov = 40) => u =>
        {
            var e = Ease(u);
            var pos = Vector3.Lerp(U(a), U(b), e);
            return new View { pos = pos, rot = Quaternion.LookRotation(Vector3.Lerp(U(lookA), U(lookB), e) - pos, Vector3.up), fov = fov, near = 0.1f };
        };

        /// <summary>The pilot's eye: yaw and pitch from straight ahead, in degrees.</summary>
        Func<float, View> Eye(float yaw0, float yaw1, float pitch0, float pitch1, float fov = 72) => u =>
        {
            var e = Ease(u);
            var rot = heli.transform.rotation * Quaternion.Euler(-Mathf.Lerp(pitch0, pitch1, e), Mathf.Lerp(yaw0, yaw1, e), 0);
            return new View { pos = heli.Eye.position, rot = rot, fov = fov, near = 0.03f };
        };

        const float ConsoleBelowEye = 0.62f;

        void ConsoleDisplays(bool on)
        {
            var eyeHeight = heli.transform.InverseTransformPoint(heli.Eye.position).y;
            foreach (var r in heli.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.name.StartsWith("Live display")) continue;
                var below = eyeHeight - heli.transform.InverseTransformPoint(r.bounds.center).y;
                var console = below > ConsoleBelowEye;
                Debug.Log($"TRAILER: {r.name} is {below:F2} m below the eye: {(console ? "console" : "main panel")}");
                if (console) r.enabled = on;
            }
        }

        // ---------------------------------------------------------------------------------------- the film
        IEnumerator Opening()
        {
            yield return Setup("guardian", "fair_nw", TimeOfDay.Dusk);
            var sea = Line(A(2012, 30, -3230), A(2006, 32, -3400), A(2004, 34, -3530), A(2008, 36, -3700));
            yield return Shot("coast at dusk", 4, u => Fly(sea, u), Dolly(A(2230, 36, -3470), A(2225, 37, -3490), 12, 10), fadeIn: 1.6f);
            var lit = Line(A(1500, 60, -3300), A(900, 70, -3800), A(300, 100, -4300));
            heli.SetCamera(CameraMode.Cockpit);
            yield return Shot("cockpit at dusk", 1, u => Fly(lit, u), Eye(-8, 4, -6, -4));
            heli.SetCamera(CameraMode.Orbit);
            var cx = Rotorcraft.World.Terrain.CoastX(2600);
            var overhead = Line(A(cx + 420, 14, 2590), A(cx + 120, 14, 2600), A(cx - 160, 16, 2610), A(cx - 460, 24, 2625));
            yield return Shot("overhead on the beach", 1.5f, u => Fly(overhead, u, 78), Dolly(A(cx - 40, 1.3, 2612), A(cx - 40, 1.3, 2612), 66, 66));

            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            var p0 = s.pos.Clone();
            double noseX = Math.Sin(s.psi), noseZ = -Math.Cos(s.psi);
            Vec3 Ahead(double metres, double up) => new Vec3(p0.x + noseX * metres, p0.y + up, p0.z + noseZ * metres);
            var lift = Line(p0, Ahead(1, 2.5), Ahead(9, 9), Ahead(30, 17), Ahead(70, 27));
            psiHold = s.psi; psiS = s.psi;
            bigStart = 0.2f;
            yield return Shot("title over the lift-off", 2.5f, u => Fly(lift, Mathf.Pow(u, 1.5f), steady: true), Dolly(new Vec3(p0.x + 22, p0.y + 1.2, p0.z + 26), new Vec3(p0.x + 20, p0.y + 3, p0.z + 22), 44, 50), big: true, bigSubtitle: "HELICOPTER FLIGHT SIMULATOR");
        }

        IEnumerator Region()
        {
            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            var town = Line(A(150, 130, -450), A(-550, 135, -850), A(-1250, 145, -1200));
            yield return Shot("over Kinloch", 1.5f, u => Fly(town, u), Orbit(150, 125, 22, 16, 42, 36, 48), "A WHOLE HIGHLAND REGION", "Towns, glens, mountains and coast");

            var castle = A(-2120, 12, -630);
            var byCastle = Line(A(-1860, 40, -770), A(-2090, 42, -800), A(-2330, 46, -830));
            yield return Shot("castle", 1, u => Fly(byCastle, u), Pan(A(-1960, 42, -860), A(-2240, 48, -880), castle, castle, 40), "DUNARDOCH CASTLE");
            var light = A(880, 18, -3600);
            var byLight = Line(A(1128, 26, -3480), A(1128, 28, -3600), A(1128, 30, -3720));
            yield return Shot("lighthouse", 1, u => Fly(byLight, u), Pan(A(1260, 12, -3540), A(1250, 15, -3620), light, light, 34), "TARBAT LIGHT");
            var quay = WorldMap.SiteById("portcraig_quay");
            var harbour = A(quay.x - 40, 5, quay.z);
            var overHarbour = Line(A(quay.x + 198, 20, quay.z + 150), A(quay.x + 198, 20, quay.z), A(quay.x + 198, 20, quay.z - 150));
            yield return Shot("harbour", 1, u => Fly(overHarbour, u), Pan(A(quay.x + 285, 9, quay.z + 40), A(quay.x + 280, 11, quay.z - 10), harbour, harbour, 46), "PORTCRAIG HARBOUR");
            var dram = A(-2440, 8, 380);
            var byDram = Line(A(-2267, 15, 500), A(-2267, 16, 385), A(-2267, 17, 270));
            yield return Shot("distillery", 1, u => Fly(byDram, u), Pan(A(-2210, 12, 410), A(-2215, 14, 360), dram, dram, 44), "GLEN ARDOCH DISTILLERY");

            var woods = Line(A(-3700, 46, 1440), A(-4000, 46, 1560), A(-4320, 48, 1640));
            yield return Shot("over the forest", 1.5f, u => Fly(woods, u), Orbit(150, 195, 16, 11, 19, 16, 54), "88,000 TREES", "Forestry and woodland, from the glens to the coast");

            var life = FindAnyObjectByType<AmbientLife>();
            var road = Places.ROADS.First(r => r.name == "Kinloch road");
            var rp = road.pts[road.pts.Length / 2];
            var carFound = new Vector2((float)rp.x, (float)rp.z);
            var alongRoad = Line(A(rp.x + 240, 60, rp.z + 160), A(rp.x, 62, rp.z), A(rp.x - 240, 70, rp.z - 160));
            yield return Shot("traffic", 1, u => { Fly(alongRoad, u); carFound = life.Nearest("car", carFound) ?? carFound; },
                u => { var at = U(A(carFound.x, 1.2, carFound.y)); var pos = at + new Vector3(13 - 7 * u, 5.5f, 11 + 3 * u); return new View { pos = pos, rot = Quaternion.LookRotation(at - pos, Vector3.up), fov = 46, near = 0.1f }; },
                "A WORLD GOING ABOUT ITS DAY", "Traffic on the roads", preRoll: 40);
            var settlement = Places.SETTLEMENTS[0];
            var walkerAt = new Vector2((float)settlement.x, (float)settlement.z);
            var overTown = A(settlement.x, 70, settlement.z);
            yield return Shot("people", 1, u => { Hover(overTown, 40); walkerAt = life.Nearest("walker", walkerAt) ?? walkerAt; },
                u => { var at = U(A(walkerAt.x, 1, walkerAt.y)); var pos = at + new Vector3(5.5f, 1.2f, 6 - u * 1.2f); return new View { pos = pos, rot = Quaternion.LookRotation(at - pos, Vector3.up), fov = 44, near = 0.1f }; },
                "A WORLD GOING ABOUT ITS DAY", "People in the towns", preRoll: 40);
            var boatNear = new Vector2((float)Rotorcraft.World.Terrain.CoastX(-6000) + 2500, -6000);
            var boatAt = boatNear;
            yield return Shot("fishing boat", 1, u => { Hover(Abs(boatNear.x, 220, boatNear.y), 300); boatAt = life.Nearest("boat", boatAt) ?? boatAt; },
                u => { var at = U(Abs(boatAt.x, 2, boatAt.y)); var pos = at + new Vector3(-26 + u * 7, 6, -22); return new View { pos = pos, rot = Quaternion.LookRotation(at - pos, Vector3.up), fov = 42, near = 0.1f }; },
                "A WORLD GOING ABOUT ITS DAY", "Fishing boats working offshore", preRoll: 40);

            yield return Setup("northstar", "fair_nw", TimeOfDay.Day);
            var rig = WorldMap.SiteById("cormorant_a");
            var toRig = Line(Abs(rig.x - 250, 50, rig.z + 95), Abs(rig.x - 150, 54, rig.z + 70), Abs(rig.x - 50, 60, rig.z + 45));
            yield return Shot("platform", 1.5f, u => Fly(toRig, u), Pan(Abs(rig.x - 175, 30, rig.z + 150), Abs(rig.x - 150, 34, rig.z + 160), Abs(rig.x - 100, 62, rig.z + 40), Abs(rig.x - 20, 52, rig.z + 10), 46), "OUT TO THE NORTH SEA", "Working platforms, helidecks and gas flares");
        }

        IEnumerator WeatherAndLight()
        {
            yield return Setup("northstar", "showers", TimeOfDay.Day);
            var wet = Line(A(-1200, 130, -1600), A(-1750, 135, -1900), A(-2300, 145, -2250));
            yield return Shot("rain", 1.5f, u => Fly(wet, u), Orbit(178, 165, 10, 7, 20, 17, 48), "WEATHER THAT CHANGES THE JOB", "Rain, gusts, sea fog and lowering cloud", preRoll: 45);
            heli.SetCamera(CameraMode.Cockpit);
            yield return Shot("rain from the seat", 1, u => Fly(wet, u), Eye(-6, 5, -5, -5), "WEATHER THAT CHANGES THE JOB", "Rain, gusts, sea fog and lowering cloud", preRoll: 30);
            heli.SetCamera(CameraMode.Orbit);
            var baseM = 1400 / 3.28084;
            var topM = baseM + Rotorcraft.Sim.Weather.CLOUD_DEPTH_M + 130;
            var overTop = Line(Abs(-1400, topM, -1600), Abs(-1900, topM + 6, -1900));
            yield return Shot("above the cloud", 1, u => Fly(overTop, u), Orbit(120, 90, 14, 10, 30, 26, 46), "BREAK OUT ON TOP", "Real cloud layers to climb through", preRoll: 45);

            yield return Setup("nightjar", "haar", TimeOfDay.Day, "ambulance");
            var fog = Line(A(1700, 60, -2100), A(1250, 55, -2400), A(850, 50, -2700));
            yield return Shot("sea fog", 1, u => Fly(fog, u), Orbit(150, 170, 8, 6, 19, 16, 50), "COASTAL HAAR", "When the sea fog rolls in", preRoll: 45);

            yield return Setup("northstar", "breezy_w", TimeOfDay.Day);
            var spray = Abs(2500, 3.5, 0);
            yield return Shot("rotor spray", 1, u => Hover(spray, 310), Orbit(40, 70, 9, 12, 16, 14, 46), "YOUR ROTOR WASH", "Spray off the sea", preRoll: 90);
            const double dustX = 58, dustZ = 30;
            var dust = new Vec3(dustX, Ground.Current.GroundAt(dustX, dustZ).h + 3.5, dustZ);
            yield return Shot("rotor dust", 1, u => Hover(dust, 200), Orbit(-30, -55, 8, 10, 16, 14, 46), "YOUR ROTOR WASH", "Dust off the ground", preRoll: 90);

            yield return Setup("guardian", "calm", TimeOfDay.Night);
            s.sys.searchlight = true;
            s.sys.landingLight = true;
            var nightTown = Line(A(-250, 150, -650), A(-720, 145, -950), A(-1200, 150, -1250));
            yield return Shot("night over the town", 2, u => Fly(nightTown, u), Orbit(120, 150, 16, 22, 34, 44, 50), "DAY, DUSK AND NIGHT", "Lit towns, landing lights and searchlights", preRoll: 40);
            var rig = WorldMap.SiteById("cormorant_a");
            var toFlare = Line(Abs(rig.x - 560, 90, rig.z - 40), Abs(rig.x - 300, 75, rig.z - 26));
            yield return Shot("flare at night", 1, u => Fly(toFlare, u), Orbit(140, 155, 6, 4, 22, 19, 54), "DAY, DUSK AND NIGHT", "Gas flares out on the North Sea", preRoll: 40);
        }

        IEnumerator Fleet()
        {
            yield return Setup("kestrel", "light_sw", TimeOfDay.Day);
            var farm = Line(A(-2600, 30, -2100), A(-2900, 28, -2290), A(-3200, 34, -2460));
            yield return Shot("Kestrel", 1.5f, u => Fly(farm, u), Orbit(60, 100, 6, 4, 8, 7, 42), "KESTREL K2", "Two-seat piston trainer");
            yield return Setup("northstar", "light_sw", TimeOfDay.Day);
            var seaRun = Line(A(2300, 45, -700), A(1500, 50, -980), A(700, 70, -1250));
            yield return Shot("Northstar", 1.5f, u => Fly(seaRun, u), Orbit(-60, -105, 5, 3, 13, 12, 40), "NORTHSTAR 175", "Twin-turbine offshore crew change");
            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            var glen = Line(A(-1500, 42, -1500), A(-1900, 44, -1800), A(-2300, 52, -2050));
            yield return Shot("Guardian", 1.5f, u => Fly(glen, u), Orbit(50, 88, 5, 3, 13, 12, 42), "GUARDIAN S9", "Search and rescue, with winch and searchlight");
            yield return Setup("nightjar", "light_sw", TimeOfDay.Day, "ambulance");
            var overHospital = Line(A(-1450, 75, -4300), A(-1300, 72, -4620), A(-1120, 80, -4950));
            yield return Shot("Nightjar", 1.5f, u => Fly(overHospital, u), Orbit(-40, -80, 7, 4, 10, 9, 42), "NIGHTJAR N4", "Air ambulance and police");
            yield return Setup("peregrine", "light_sw", TimeOfDay.Day, "police");
            s.sys.gearDown = true; s.gearPos = 1;
            var climb = Line(A(-400, 14, 280), A(-800, 32, 300), A(-1300, 62, 320));
            yield return Shot("Peregrine", 1.5f, u => { Fly(climb, u); if (u > 0.05f && s.sys.gearDown) heli.Input.Fire(GameAction.Gear); }, Orbit(70, 110, 3, 2, 12, 11, 40), "PEREGRINE P3", "Police and military, with wheels that retract");

            foreach (var (id, skin, label) in new[] { ("peregrine", "army", "Army"), ("peregrine", "navy", "Navy"), ("peregrine", "rescue", "Rescue"), ("peregrine", "executive", "Executive") })
            {
                yield return Setup(id, "light_sw", TimeOfDay.Day, skin);
                var hop = Line(A(-500, 22, 400), A(-760, 24, 420));
                yield return Shot("paint " + skin, 0.5f, u => Fly(hop, u), Orbit(55, 70, 6, 5, 10, 9.5f, 42), "PAINT SCHEMES AND ROLE KIT", label);
            }
        }

        IEnumerator Cockpits()
        {
            foreach (var (id, yaw) in new[] { ("kestrel", -6f), ("northstar", 8f), ("nightjar", -8f), ("peregrine", 6f) })
            {
                yield return Setup(id, "light_sw", TimeOfDay.Day, id == "nightjar" ? "ambulance" : id == "peregrine" ? "police" : null);
                heli.SetCamera(CameraMode.Cockpit);
                var run = Line(A(-1200, 90, -600), A(-1600, 92, -820));
                yield return Shot("cockpit " + id, 0.5f, u => Fly(run, u), Eye(yaw, yaw * 0.4f, -17, -15, 66), "FIVE COCKPITS, ALL LIVE", "Working instruments and screens in every one");
            }
            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            heli.SetCamera(CameraMode.Cockpit);
            ConsoleDisplays(false);
            var h = WorldMap.SiteElevation(WorldMap.SiteById("kin_h1"));
            var down = Line(Abs(70, h + 30, 70), Abs(30, h + 18, 30), Abs(6, h + 8, 6), Abs(0, h + 4, 0));
            yield return Shot("chin window", 1.5f, u => Fly(down, u, 14, 1, true), Eye(-8, -2, -48, -60, 80), "SEE PAST YOUR FEET", "Glazed chin windows for the last few metres");

            // the real walk-round from a real lesson, its checklist on the screen
            yield return Leave();
            app.FlyMission("ex01_controls", 60, TimeOfDay.Day, "calm");
            while (!f.Active || f.Runtime == null) yield return null;
            yield return new WaitForSecondsRealtime(4);
            heli = f.heli; s = heli.State;
            Time.captureFramerate = fps;
            heli.SetCamera(CameraMode.Orbit);
            yield return Shot("walk-round", 1.5f, null, Orbit(40, 95, 10, 7, 8, 7.5f, 46), "WALK ROUND, STRAP IN, START UP", "Full pre-flight checks before every flight", hud: true, letterbox: false, captionBottom: 170);
            f.SkipForTour(); f.ShowForTour();
            s.engine = EngineState.Off;
            FlightModel.CommandEngine(s, EngineCommand.Start);
            yield return Shot("engine start", 1, null, Orbit(115, 130, 6, 6, 7, 6.5f, 42), "WALK ROUND, STRAP IN, START UP", "Real start sequences, smoke and all", preRoll: 45);

            yield return Setup("northstar", "calm", TimeOfDay.Day);
            var start = s.pos.Clone();
            heli.ControlFilter = null;
            for (int i = 0; i < 36 * fps / 30; i++) { s.pos = new Vec3(start.x, start.y + 0.6, start.z); s.vel = new Vec3(0, 0, 0); yield return null; }
            for (int i = 0; i < 9 * fps / 30; i++) { s.pos = new Vec3(start.x, start.y + 0.03, start.z); s.vel = new Vec3(0, 0, 0); yield return null; }
            yield return Shot("crew walk out", 1, null, Orbit(150, 165, 9, 8, 8.5f, 8, 44), "WALK ROUND, STRAP IN, START UP", "And the crew walk away at the end of it", preRoll: 330);

            yield return Setup("kestrel", "breezy_w", TimeOfDay.Day);
            var auto = Line(A(-2500, 260, -2000), A(-2700, 150, -2170), A(-2850, 45, -2280), A(-2900, 8, -2300));
            yield return Shot("autorotation", 1.5f, u => { Fly(auto, u, 26, 1, true); hornOn = u > 0.12f && u < 0.62f; }, Orbit(135, 80, -6, 5, 11, 9, 44), "A FLIGHT MODEL THAT MEANS IT", "Ground effect, vortex ring, blade stall, autorotation");
            hornOn = false;
            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            heli.SetCamera(CameraMode.Cockpit);
            var cruise = Line(A(-200, 240, -1500), A(-900, 245, -1900), A(-1600, 250, -2300));
            f.Runtime.ap.alt = true; f.Runtime.ap.hdg = true;
            f.ShowForTour(map: true);
            yield return Shot("autopilot", 1.5f, u => Fly(cruise, u), Eye(-6, 6, -9, -9, 72), "BEGINNER, STANDARD OR REALISTIC", "Trim, autopilot and a handling level to suit you", hud: true, letterbox: false, captionBottom: 190);
            f.ShowForTour();
        }

        IEnumerator Jobs()
        {
            // the winch: a real rescue mission's liferaft on the water, the winchman going down to it
            yield return Mission("sar05_night_liferaft", 800, TimeOfDay.Day, "breezy_w");
            var target = f.Runtime.targets.Count > 0 ? f.Runtime.targets[0] : null;
            double rx = target != null ? target.x : 3000, rz = target != null ? target.z : -2000;
            var over = Abs(rx - 1.5, 26, rz - 1);
            var hoist = f.Runtime.hoist;
            yield return Shot("winch rescue", 1.5f, u =>
            {
                Hover(over, 240);
                var arm = Hoist.ArmPoint(s, heli.Aircraft.rotorDiameter / 15);
                var len = Mathf.Lerp(7, 20, u);
                hoist.phase = HoistPhase.Lowering; hoist.cable = len; hoist.attached = false;
                hoist.hook = new Vec3(arm.x, arm.y - len, arm.z);
                f.actors?.UpdateHoist(hoist, s, heli.Aircraft);
            }, Pan(Abs(rx + 27, 15, rz + 22), Abs(rx + 22, 13, rz + 28), Abs(rx, 15, rz), Abs(rx, 15, rz), 68), "WINCH RESCUES", "Hold the hover while the winchman goes down", preRoll: 45);
            hoist.phase = HoistPhase.Stowed;
            f.actors?.UpdateHoist(hoist, s, heli.Aircraft);

            yield return Mission("sar_t1_search", 400, TimeOfDay.Dusk, "calm");
            var area = f.Runtime.NavTargetNow();
            double ax = area != null ? area.x : -3000, az = area != null ? area.z : 1000;
            s.sys.searchlight = true;
            var sweep = Line(A(ax + 420, 95, az - 180), A(ax + 120, 90, az - 40), A(ax - 200, 90, az + 80));
            yield return Shot("search", 1.5f, u => Fly(sweep, u), Orbit(150, 190, 10, 14, 24, 22, 46), "SEARCHES", "Find the missing, by day and by night", preRoll: 40);

            yield return Mission("off02_crew_change", 900, TimeOfDay.Day, "fair_nw");
            var deck = WorldMap.SiteById("cormorant_a");
            var deckY = deck.deckHeight.Value;
            var settle = Line(Abs(deck.x - 110, deckY + 34, deck.z - 22), Abs(deck.x - 45, deckY + 14, deck.z - 8), Abs(deck.x - 8, deckY + 5, deck.z - 1), Abs(deck.x, deckY + 3, deck.z));
            yield return Shot("offshore", 1.5f, u => Fly(settle, u, 18, 1, true), Dolly(Abs(deck.x + 44, deckY + 16, deck.z + 42), Abs(deck.x + 40, deckY + 12, deck.z + 34), 44, 42), "OFFSHORE CREW CHANGES", "Land on a helideck with the wind across it", preRoll: 40);

            yield return Setup("nightjar", "calm", TimeOfDay.Day, "ambulance");
            var roof = WorldMap.SiteById("kinloch_royal");
            var ry = roof.deckHeight.Value;
            var toRoof = Line(Abs(roof.x + 62, ry + 26, roof.z + 44), Abs(roof.x + 32, ry + 14, roof.z + 22), Abs(roof.x + 9, ry + 6, roof.z + 6), Abs(roof.x, ry + 3, roof.z));
            yield return Shot("hospital roof", 1, u => Fly(toRoof, u, 15, 1, true), Dolly(Abs(roof.x - 42, ry + 13, roof.z + 44), Abs(roof.x - 36, ry + 10, roof.z + 46), 42, 40), "AIR AMBULANCE", "Rooftop helipads at city hospitals", preRoll: 40);

            yield return Mission("sar04_road_accident", 700, TimeOfDay.Day, "calm");
            var scene = A(-4052, 7, -132);
            yield return Shot("road accident", 1, u => Hover(scene, 150), Pan(A(-4019, 1.5, -86), A(-4014, 1.7, -90), A(-4034, 3, -112), A(-4034, 3.4, -112), 46), "AIR AMBULANCE", "And roadside accidents, wherever they happen", preRoll: 45);

            yield return Setup("peregrine", "fair_nw", TimeOfDay.Dusk, "police");
            s.sys.searchlight = true;
            var patrol = Line(A(-1050, 42, -4230), A(-1250, 40, -4360), A(-1450, 44, -4500));
            yield return Shot("police at night", 1.5f, u => Fly(patrol, u), Orbit(55, 95, 9, 6, 11, 10, 44), "POLICE AIR SUPPORT", "Search for missing people and suspects from the air", preRoll: 40);

            yield return Setup("peregrine", "fair_nw", TimeOfDay.Day, "army");
            var ranges = Line(A(-2500, 16, -2050), A(-2850, 14, -2280), A(-3250, 18, -2500));
            yield return Shot("military low level", 1.5f, u => Fly(ranges, u, 72), Orbit(25, -25, 3, 2, 13, 11, 44), "MILITARY CONTRACTS", "Troop insertions, casualty evacuation, low-level navigation");

            yield return Mission("cpl2_confined_area", 120, TimeOfDay.Day, "calm");
            var clearing = WorldMap.SiteById("glen_clearing");
            var ge = WorldMap.SiteElevation(clearing);
            var into = Line(Abs(clearing.x + 26, ge + 34, clearing.z - 20), Abs(clearing.x + 12, ge + 22, clearing.z - 9), Abs(clearing.x + 2, ge + 9, clearing.z - 1), Abs(clearing.x, ge + 3, clearing.z));
            yield return Shot("confined landing", 1.5f, u => Fly(into, u, 12, 1, true), Dolly(Abs(clearing.x - 34, ge + 18, clearing.z + 30), Abs(clearing.x - 30, ge + 14, clearing.z + 34), 30, 30), "CONFINED-AREA LANDINGS", "Bring it down between the trees", preRoll: 40);
        }

        /// <summary>A screen of the game, sliding gently down it if it scrolls.</summary>
        IEnumerator Page(string route, string title, string sub, float bars = 1)
        {
            app.Go(route);
            for (int i = 0; i < 6; i++) yield return null;
            ScrollView best = null; float extra = 0;
            foreach (var sv in app.document.rootVisualElement.Query<ScrollView>().ToList())
            {
                var e = sv.contentContainer.layout.height - sv.contentViewport.layout.height;
                if (e > extra) { extra = e; best = sv; }
            }
            var travel = Mathf.Min(extra, 170);
            yield return Shot("screen " + route, bars, u => { if (best != null) best.scrollOffset = new Vector2(0, travel * Ease(u)); }, null, title, sub, hud: true, letterbox: false, preRoll: 6);
        }

        IEnumerator CareerScreens()
        {
            yield return Leave();
            f.SetMenuBackdrop(true);
            yield return new WaitForSecondsRealtime(1);
            Time.captureFramerate = fps;
            var zoomBefore = app.document.panelSettings.scale;
            app.document.panelSettings.scale = 1.6f;
            yield return Page("hq", "A FULL CAREER", $"{Missions.All.Count} missions, from your first lesson to a commercial licence");
            yield return Page("school", "GROUND SCHOOL", $"{Theory.MODULES.Count} modules of real theory and {Theory.EXAMS.Count} exams");
            yield return Page("quals", "LICENCES AND RATINGS", "Instrument, night, type ratings and more");
            yield return Page("employers", $"{Employers.ALL.Count} EMPLOYERS", "Offshore, rescue, police, military, medical and utility");
            yield return Page("hangar", "YOUR HANGAR", "Fit the kit each job needs: winch, floats, stretcher, searchlight");
            yield return Page("freeplay", "FREEPLAY", "Pick a helicopter and take random callouts");
            yield return Page("company", "RUN YOUR OWN COMPANY", "Buy aircraft, win contracts, fly the jobs");
            yield return Page("pilots", "THE PILOT AREA", "Leaderboards, plus cloud saves between web and Windows");
            yield return Page("settings", "FLY IT YOUR WAY", "Keyboard, gamepad, joystick, HOTAS and pedals");
            app.document.panelSettings.scale = zoomBefore;
        }

        IEnumerator VirtualReality()
        {
            yield return Setup("guardian", "light_sw", TimeOfDay.Day);
            heli.SetCamera(CameraMode.Cockpit);
            ConsoleDisplays(false);
            var run = Line(A(-1800, 70, -1200), A(-2300, 62, -1500), A(-2800, 58, -1850));
            yield return Shot("VR head", 1.5f, u => Fly(run, u), Eye(-46, 42, -14, -3, 96), "FLY IT IN VR", "Quest over Link or Virtual Desktop, or any PC headset");

            // both hands on the controls, moving them
            var grips = CockpitGrip.Find(heli.model, heli.transform, heli.Eye.position);
            var cyclic = grips.Find(g => g.cyclic); var lever = grips.Find(g => !g.cyclic);
            var right = new VrHand(heli.transform, false, 5);
            var left = new VrHand(heli.transform, true, 5);
            var fill = new GameObject("Trailer fill light").AddComponent<Light>();
            fill.type = LightType.Point; fill.range = 1.3f; fill.intensity = 0.16f; fill.color = new Color(1, 0.96f, 0.9f); fill.shadows = LightShadows.None;
            fill.transform.SetParent(heli.transform, false);
            var moving = 0f;
            heli.ControlFilter = (c, dt) => { var t = Time.time; return new Controls(0.55 + Math.Sin(t * 0.9) * 0.09 * moving, Math.Sin(t * 1.3) * 0.4 * moving, Math.Sin(t * 1.7 + 1) * 0.45 * moving, 0); };
            void Reach(VrHand hand, CockpitGrip grip, bool isLeft, float u)
            {
                var rot = grip.HandRotation(heli.transform, isLeft);
                var away = heli.transform.up * 0.2f - heli.transform.forward * 0.1f + heli.transform.right * (isLeft ? -0.12f : 0.12f);
                var near = Ease(Mathf.Clamp01(u / 0.3f));
                hand.root.SetPositionAndRotation(grip.Anchor + away * (1 - near), rot);
                hand.SetReady(u > 0.14f && u < 0.32f);
                hand.SetCurl(Mathf.Clamp01((u - 0.24f) / 0.08f));
            }
            var hold = Line(A(-2800, 58, -1850), A(-3300, 60, -2200));
            Func<float, View> atControls = u =>
            {
                var mid = (cyclic.Anchor + lever.Anchor) / 2;
                fill.transform.position = mid + heli.transform.up * 0.55f - heli.transform.forward * 0.15f;
                return new View { pos = heli.Eye.position, rot = Quaternion.LookRotation(mid + heli.transform.forward * 0.05f - heli.Eye.position, heli.transform.up), fov = 62, near = 0.03f };
            };
            yield return Shot("VR hands", 2, u =>
            {
                Fly(hold, u);
                moving = Mathf.Clamp01((u - 0.5f) / 0.2f);
                Reach(right, cyclic, false, u);
                Reach(left, lever, true, u - 0.14f);
            }, atControls, "YOUR HANDS ON THE CONTROLS", "Reach out, squeeze and take hold");
            Destroy(fill.gameObject);
            right.Dispose(); left.Dispose();

            // a fingertip on the switches
            heli.ControlFilter = (c, dt) => c.WithCollective(0.6);
            var panel = new VrSwitchPanel(app.document.panelSettings.themeStyleSheet, 5);
            panel.Mount(heli.transform, heli.Eye.position);
            var hand = new VrHand(heli.transform, false, 5);
            s.sys.beacon = false; s.sys.nav = false;
            f.Runtime.ap.alt = f.Runtime.ap.hdg = f.Runtime.ap.hover = false;
            const float CellW = VrSwitchPanel.Width / CockpitSwitches.Columns, CellH = VrSwitchPanel.Height / CockpitSwitches.Rows;
            Vector3 Cell(int c, int r, float depth) => panel.root.TransformPoint(-VrSwitchPanel.Width / 2 + CellW * (c + 0.5f), VrSwitchPanel.Height / 2 - CellH * (r + 0.5f), -depth);
            var hold2 = Line(A(-3300, 60, -2200), A(-3800, 62, -2550));
            yield return Shot("VR switches", 2, u =>
            {
                Fly(hold2, u);
                // to BCN and press, then across to NAV and press
                var press1 = Mathf.Clamp01(u / 0.34f); var away1 = Mathf.Clamp01((u - 0.34f) / 0.14f); var press2 = Mathf.Clamp01((u - 0.48f) / 0.2f); var away2 = Mathf.Clamp01((u - 0.68f) / 0.2f);
                Vector3 tip;
                if (u < 0.34f) tip = Vector3.Lerp(Cell(0, 1, 0.13f), Cell(0, 1, 0), Ease(press1));
                else if (u < 0.48f) tip = Vector3.Lerp(Cell(0, 1, 0), Cell(1, 1, 0.04f), Ease(away1));
                else if (u < 0.68f) tip = Vector3.Lerp(Cell(1, 1, 0.04f), Cell(1, 1, 0), Ease(press2));
                else tip = Vector3.Lerp(Cell(1, 1, 0), Cell(1, 1, 0.12f), Ease(away2));
                if (u >= 0.34f) s.sys.beacon = true;
                if (u >= 0.68f) s.sys.nav = true;
                var towards = (panel.root.forward * 0.72f - panel.root.right * 0.5f + panel.root.up * 0.48f).normalized;
                var palm = Vector3.ProjectOnPlane(-panel.root.up, towards).normalized;
                hand.root.rotation = Quaternion.LookRotation(Vector3.Cross(palm, towards), towards);
                hand.SetCurl(0, 1);
                hand.root.position += tip - hand.IndexTip;
                var hover = u < 0.34f ? 5 : u >= 0.48f && u < 0.68f ? 6 : -1;
                panel.Draw(CockpitSwitches.Build(s, heli.Aircraft, f.Runtime.ap, false), hover, -1);
            }, u => new View { pos = heli.Eye.position, rot = Quaternion.LookRotation(panel.root.position + heli.transform.right * 0.05f - heli.Eye.position, heli.transform.up), fov = 62, near = 0.03f }, "REACH OUT AND PRESS THE SWITCHES", "Engine, lights, trim, autopilot and gear, at your fingertip");
            hand.Dispose();
            panel.Dispose();
        }

        IEnumerator Ending()
        {
            yield return Setup("guardian", "fair_nw", TimeOfDay.Dusk);
            var cx = Rotorcraft.World.Terrain.CoastX(-2800);
            var hero = Line(A(cx + 900, 30, -2300), A(cx + 280, 34, -2800), A(cx - 200, 60, -3400), A(cx - 500, 120, -4100));
            yield return Shot("hero", 4, u => Fly(hero, u), Orbit(40, 145, 3, 8, 14, 22, 40), fadeOut: 1.2f);
            view = null;
            bigStart = 0.1f;
            yield return Shot("end card", 3, null, null, big: true, bigSubtitle: "WINDOWS  ·  VR  ·  WEB", foot: "rotorcraft.netlify.app", letterbox: false, preRoll: 3, fadeOut: 0.8f, black: true);
        }

        // -------------------------------------------------------------------------------------- the titles
        void MakeOverlay()
        {
            var ps = ScriptableObject.CreateInstance<PanelSettings>();
            ps.name = "Trailer titles";
            ps.themeStyleSheet = app.document.panelSettings.themeStyleSheet;
            ps.scaleMode = PanelScaleMode.ConstantPixelSize;
            ps.scale = Screen.height / 1080f;
            ps.sortingOrder = 1000;
            ps.clearColor = false;
            var go = new GameObject("Trailer titles");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = ps;
            go.SetActive(true);
            var root = doc.rootVisualElement;
            if (app.styleSheet != null) root.styleSheets.Add(app.styleSheet);
            root.AddToClassList("app-root");
            root.style.flexGrow = 1;
            root.style.backgroundColor = Color.clear;
            root.pickingMode = PickingMode.Ignore;

            var shadow = new StyleTextShadow(new TextShadow { offset = new Vector2(0, 3), blurRadius = 10, color = new Color(0, 0, 0, 0.9f) });
            VisualElement Fill() { var e = new VisualElement { pickingMode = PickingMode.Ignore }; e.style.position = Position.Absolute; e.style.left = e.style.right = e.style.top = e.style.bottom = 0; return e; }
            Label Text(string cls, float size, float spacing)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList(cls);
                l.style.fontSize = size; l.style.letterSpacing = spacing; l.style.color = Color.white; l.style.textShadow = shadow;
                l.style.marginBottom = 0; l.style.whiteSpace = WhiteSpace.NoWrap;
                return l;
            }

            barTop = new VisualElement { pickingMode = PickingMode.Ignore };
            barTop.style.position = Position.Absolute; barTop.style.left = barTop.style.right = barTop.style.top = 0; barTop.style.backgroundColor = Color.black;
            barBottom = new VisualElement { pickingMode = PickingMode.Ignore };
            barBottom.style.position = Position.Absolute; barBottom.style.left = barBottom.style.right = barBottom.style.bottom = 0; barBottom.style.backgroundColor = Color.black;

            capBox = new VisualElement { pickingMode = PickingMode.Ignore };
            capBox.style.position = Position.Absolute; capBox.style.left = 84;
            capBox.style.borderLeftWidth = 7; capBox.style.borderLeftColor = new Color(0.98f, 0.75f, 0.14f);
            capBox.style.paddingLeft = 22; capBox.style.paddingRight = 26; capBox.style.paddingTop = 8; capBox.style.paddingBottom = 10;
            capBox.style.backgroundColor = new Color(0.02f, 0.03f, 0.05f, 0.5f);
            capTitle = Text("menu-title", 60, 3);
            capSub = Text("h1", 29, 1);
            capSub.style.marginTop = 2;
            capBox.Add(capTitle); capBox.Add(capSub);

            fadeBox = Fill();
            fadeBox.style.backgroundColor = Color.black;
            fadeBox.style.opacity = 0;

            bigBox = Fill();
            bigBox.style.alignItems = Align.Center; bigBox.style.justifyContent = Justify.Center;
            logo = new VisualElement { pickingMode = PickingMode.Ignore };
            logo.AddToClassList("menu-logo");
            bigLetters = new VisualElement { pickingMode = PickingMode.Ignore };
            bigLetters.style.flexDirection = FlexDirection.Row;
            foreach (var ch in "ROTORCRAFT")
            {
                var letter = Text("menu-title", 172, 0);
                letter.text = ch.ToString();
                letter.style.marginLeft = letter.style.marginRight = 5;
                letter.style.paddingLeft = letter.style.paddingRight = 0;
                bigLetters.Add(letter);
            }
            bigSub = Text("h1", 38, 6);
            bigFoot = Text("h1", 28, 2);
            bigFoot.style.marginTop = 22;
            bigFoot.style.color = new Color(0.98f, 0.75f, 0.14f);
            bigBox.Add(logo); bigBox.Add(bigLetters); bigBox.Add(bigSub); bigBox.Add(bigFoot);

            debugLabel = Text("h1", 30, 0);
            debugLabel.style.position = Position.Absolute; debugLabel.style.left = 12; debugLabel.style.top = 8;
            debugLabel.style.color = new Color(1, 1, 0);

            root.Add(barTop); root.Add(barBottom); root.Add(capBox); root.Add(fadeBox); root.Add(bigBox); root.Add(debugLabel);
        }
    }
}
#endif

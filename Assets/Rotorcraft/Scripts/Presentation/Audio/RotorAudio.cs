// Plays the aircraft's synthesised sound (RotorSynth) through an AudioSource on the helicopter, fed every frame
// from the flight model as the browser game does (FlightView: rpm, torque, engine, airspeed, low RPM horn) and
// thumping on each touchdown.
using System.Collections.Generic;
using System;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Rotorcraft.Presentation
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class RotorAudio : MonoBehaviour
    {
        /// <summary>The player's volume setting.</summary>
        public static float Master = 0.6f;
        public HelicopterController heli;
        [Tooltip("Main rotor blades (blade passages a second = blades × RPM / 60).")]
        public int blades = 2;

        /// <summary>How much of the world's sound is left with the aircraft running at flying rotor speed.</summary>
        public const float AmbientUnderRotor = 0.1f;

        RotorSynth synth;
        AmbientSynth ambient;
        float listenIn;
        AudioSource source;

        void Awake()
        {
            if (heli == null) heli = GetComponent<HelicopterController>();
            source = GetComponent<AudioSource>();
#if UNITY_WEBGL && !UNITY_EDITOR
            var rate = RcAudioStart();
            synth = new RotorSynth(rate > 0 ? rate : 48000) { volume = Master };
            ambient = new AmbientSynth(rate > 0 ? rate : 48000);
            webAudio = rate > 0;
            return;
#else
            AmbientRecordings.EnsureLoading();
            synth = new RotorSynth(AudioSettings.outputSampleRate) { volume = Master };
            ambient = new AmbientSynth(AudioSettings.outputSampleRate);
            // a silent looping clip keeps the source playing; the synth writes over it in OnAudioFilterRead
            source.clip = AudioClip.Create("silence", AudioSettings.outputSampleRate, 1, AudioSettings.outputSampleRate, false);
            source.loop = true; source.spatialBlend = 0; source.playOnAwake = false; source.dopplerLevel = 0;
            source.Play();
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Web builds never call OnAudioFilterRead, so the sound is rendered here and handed to the browser: enough to carry over an
        // uneven frame, little enough that it still answers the controls at once.
        [DllImport("__Internal")] static extern int RcAudioStart();
        [DllImport("__Internal")] static extern float RcAudioQueued();
        [DllImport("__Internal")] static extern void RcAudioPush(float[] samples, int count);
        const float KeepQueued = 0.13f;
        readonly float[] block = new float[2048];
        bool webAudio;

        void PumpWebAudio()
        {
            if (!webAudio) return;
            for (int i = 0; i < 8 && RcAudioQueued() < KeepQueued; i++)
            {
                System.Array.Clear(block, 0, block.Length);
                synth.Render(block, 1);
                ambient.Render(block, 1);
                RcAudioPush(block, block.Length);
            }
        }
#endif

        void OnEnable() { if (heli != null) heli.Stepped += OnStepped; }
        void OnDisable() { if (heli != null) heli.Stepped -= OnStepped; }

        void Start() => Configure();

        /// <summary>Match the sound to the aircraft (piston or turbine, blade count, rotor speed).</summary>
        public void Configure()
        {
            if (heli == null || heli.Aircraft == null || synth == null) return;
            var ac = heli.Aircraft;
            synth.turbine = ac.engines > 1 || (ac.realWorldClass != null && ac.realWorldClass.ToLowerInvariant().Contains("turbine"));   // every twin here is turbine-powered
            blades = ac.engines > 1 ? (ac.rotorDiameter > 15 ? 5 : 4) : 2;
            synth.bladePassHz = blades * heli.rotorRpmAt100 / 60f;
            synth.tailBuzzHz = 2 * heli.rotorRpmAt100 * heli.tailRotorRatio / 60f;
        }

        void OnStepped(HeliState s, IList<SimEvent> events, double dt, Controls c)
        {
            foreach (var e in events) if (e.type == SimEventType.Touchdown) synth.Thump((float)e.vs);
        }

        void Update()
        {
            if (heli == null || heli.State == null) return;
            var s = heli.State; var o = s.@out; var ac = heli.Aircraft;
            synth.volume = Master;
#if UNITY_WEBGL && !UNITY_EDITOR
            synth.volume *= AudioListener.volume;   // the "sound" setting: nothing downstream applies it here
#endif
            synth.rpm = (float)o.rpm;
            synth.torque = (float)o.torque;
            synth.engine = s.engine == EngineState.Running ? 1 : (float)s.engineN;
            synth.airspeedKt = (float)o.iasKt;
            synth.horn = !s.crashed && s.engine != EngineState.Off && o.rpm < ac.rpmMin && o.rpm > 20 && (s.engine == EngineState.Running || !s.onGround);
            synth.paused = heli.Paused || Time.timeScale == 0;
            synth.inside = heli.CockpitView;
            // with the engines and rotor going the world is all but drowned out (and the crew wear headsets): about a tenth of its
            // level at flying rotor speed, the full of it only with everything stopped
            var running = Mathf.Max(Mathf.Clamp01((float)o.rpm / 100f), synth.engine * 0.6f);
            ambient.volume = synth.volume * Mathf.Lerp(1f, AmbientUnderRotor, running);
            ambient.paused = synth.paused; ambient.inside = synth.inside;
            if ((listenIn -= Time.unscaledDeltaTime) <= 0) { listenIn = 0.25f; Listen(); }
#if UNITY_WEBGL && !UNITY_EDITOR
            PumpWebAudio();
#endif
        }

        static float Smooth(float a, float b, float x) { var t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        /// <summary>What the world sounds like where the camera is: the ground's sounds fade with height, the sea's more slowly.</summary>
        void Listen()
        {
            var cam = Camera.main;
            if (cam == null || ambient == null) return;
            var p = cam.transform.position;
            double x = p.x, z = -p.z;
            var g = WorldMap.GroundAt(x, z);
            var agl = Mathf.Max(0, p.y - (float)g.h);
            float low = 1 - Smooth(30, 380, agl), mid = 1 - Smooth(60, 900, agl);
            var env = SkyEnvironment.Current;
            float night = env != null ? env.Night : 0;
            var w = env != null ? env.Weather : null;
            float wind = w != null ? (float)(w.windKt + w.gustKt * 0.5) : 4;
            float wet = w == null ? 0 : w.precip == Precip.Rain ? 1 : w.precip == Precip.Drizzle ? 0.5f : 0;
            var trees = TreeField.Current != null ? Mathf.Clamp01(TreeField.Current.TreesNear(p, 140) / 45f) : 0;
            var overSea = g.surface == Surface.Water;
            var shore = 1 - Smooth(40, 700, (float)Math.Abs(x - Rotorcraft.World.Terrain.CoastX(z)));
            float town = 0, deck = 0;
            foreach (var s in Places.SETTLEMENTS) town = Mathf.Max(town, 1 - Smooth((float)s.r * 0.5f, (float)s.r * 1.8f, (float)Js.Hypot(s.x - x, s.z - z)));
            foreach (var s in WorldMap.SITES) if (s.kind == SiteKind.Helideck) deck = Mathf.Max(deck, 1 - Smooth(35, 260, (float)Js.Hypot(s.x - x, s.z - z)));
            ambient.birds = low * (overSea ? 0.05f : 0.3f + 0.7f * trees) * (1 - night) * (1 - 0.8f * wet) * (1 - 0.5f * town);
            ambient.gulls = Mathf.Max(low * shore, deck * 0.6f) * (1 - 0.85f * night) * (1 - 0.6f * wet);
            ambient.owls = low * trees * Mathf.Clamp01((night - 0.5f) * 2);
            ambient.leaves = low * trees * Mathf.Clamp01(0.2f + wind / 16);
            var waves = Mathf.Clamp01(0.45f + wind / 22);
            ambient.sea = mid * Mathf.Max(shore, overSea ? 0.45f : 0) * waves;
            ambient.shore = mid * shore * waves;
            ambient.rain = wet * (heli.CockpitView ? 1.2f : 0.8f);   // drumming on the canopy from inside
            ambient.hum = low * town * (night > 0.5f ? 0.5f : 1);
            ambient.plant = deck;
            ambient.breeze = Mathf.Clamp01(wind / 22) * (0.35f + 0.65f * low);
            // Windows: recordings in place of the generated sounds, as each is read (none on the web)
            ambient.birdsRec = AmbientRecordings.Get("birds"); ambient.leavesRec = AmbientRecordings.Get("leaves");
            ambient.rainRec = AmbientRecordings.Get("rain"); ambient.humRec = AmbientRecordings.Get("hum");
            ambient.shoreRec = AmbientRecordings.Get("shore"); ambient.gullsRec = AmbientRecordings.Get("gulls");
            ambient.owlRec = AmbientRecordings.Get("owls");
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        void OnAudioFilterRead(float[] data, int channels) { synth?.Render(data, channels); ambient?.Render(data, channels); }
#endif
    }
}

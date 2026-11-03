// Plays the aircraft's synthesised sound (RotorSynth) through an AudioSource on the helicopter, fed every frame
// from the flight model as the browser game does (FlightView: rpm, torque, engine, airspeed, low RPM horn) and
// thumping on each touchdown.
using System.Collections.Generic;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class RotorAudio : MonoBehaviour
    {
        [Range(0, 1)] public float volume = 0.6f;
        public HelicopterController heli;
        [Tooltip("Main rotor blades (blade passages a second = blades × RPM / 60).")]
        public int blades = 2;

        RotorSynth synth;
        AudioSource source;

        void Awake()
        {
            if (heli == null) heli = GetComponent<HelicopterController>();
            synth = new RotorSynth(AudioSettings.outputSampleRate) { volume = volume };
            source = GetComponent<AudioSource>();
            // a silent looping clip keeps the source playing; the synth writes over it in OnAudioFilterRead
            source.clip = AudioClip.Create("silence", AudioSettings.outputSampleRate, 1, AudioSettings.outputSampleRate, false);
            source.loop = true; source.spatialBlend = 0; source.playOnAwake = false; source.dopplerLevel = 0;
            source.Play();
        }

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
            synth.volume = volume;
            synth.rpm = (float)o.rpm;
            synth.torque = (float)o.torque;
            synth.engine = s.engine == EngineState.Running ? 1 : (float)s.engineN;
            synth.airspeedKt = (float)o.iasKt;
            synth.horn = !s.crashed && s.engine != EngineState.Off && o.rpm < ac.rpmMin && o.rpm > 20 && (s.engine == EngineState.Running || !s.onGround);
            synth.paused = heli.Paused || Time.timeScale == 0;
            synth.inside = heli.CockpitView;
        }

        void OnAudioFilterRead(float[] data, int channels) => synth?.Render(data, channels);
    }
}

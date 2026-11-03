// The aircraft's own effects (port of updateParticles in the browser game's render/scene.ts): rotor downwash
// raising spray over water and dust and grass over land, exhaust smoke (a burst at light-off that thins as the
// engine spools up, a faint haze while it runs, a wisp after shutdown), and a fire where it crashed.
using System.Collections.Generic;
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class AircraftEffects : MonoBehaviour
    {
        /// <summary>Exhausts in the model's own axes (x right, y up, z aft): position and the way the gas leaves.</summary>
        static readonly Dictionary<string, (Vector3 pos, Vector3 dir)[]> EXHAUSTS = new Dictionary<string, (Vector3, Vector3)[]>
        {
            ["kestrel"] = new[] { (new Vector3(0.33f, 0.92f, 1.66f), new Vector3(0.05f, -0.25f, 1)) },
            ["northstar"] = new[] { (new Vector3(-0.86f, 3.02f, 2.85f), new Vector3(-0.5f, 0.05f, 0.85f)), (new Vector3(0.86f, 3.02f, 2.85f), new Vector3(0.5f, 0.05f, 0.85f)) },
            ["nightjar"] = new[] { (new Vector3(-0.56f, 2.5f, 2.05f), new Vector3(-0.3f, 0.1f, 1)), (new Vector3(0.56f, 2.5f, 2.05f), new Vector3(0.3f, 0.1f, 1)) },
            ["peregrine"] = new[] { (new Vector3(-0.6f, 2.58f, 2.1f), new Vector3(-0.4f, 0.15f, 1)), (new Vector3(0.6f, 2.58f, 2.1f), new Vector3(0.4f, 0.15f, 1)) },
        };

        public HelicopterController heli;
        ParticleSystem dust, spray, smoke, flames, fireSmoke;
        (Vector3 pos, Vector3 dir)[] exhausts;
        EngineState lastEngine = EngineState.Off;
        float sinceStop = 99, emitDust, emitSmoke, emitFlame, emitFireSmoke;
        bool fireLaidOut;
        Transform root;

        void OnDestroy() { if (root != null) Destroy(root.gameObject); }

        void Start() => Configure();

        public void Configure()
        {
            if (heli == null) heli = GetComponent<HelicopterController>();
            if (heli == null) return;
            if (dust == null)
            {
                // the systems live in the world, not on the aircraft, so what it leaves behind stays put
                root = new GameObject("Aircraft effects").transform;
                dust = ParticleFx.Make("Downwash dust", root, false, 500, 0.06f, 1.3f, 2.6f, 0.1f, 0.35f);
                spray = ParticleFx.Make("Downwash spray", root, false, 700, 0.12f, 1.0f, 3.2f, 0.08f, 0.3f);
                smoke = ParticleFx.Make("Exhaust smoke", root, false, 300, -0.06f, 0.6f, 8f, 0.06f, 0.3f);
                flames = ParticleFx.Make("Fire", root, true, 220, 0, 0, 1.8f, 0.05f, 0.4f);
                fireSmoke = ParticleFx.Make("Fire smoke", root, false, 220, 0, 0, 8f, 0.05f, 0.5f);
            }
            var id = heli.aircraftId;
            if (!EXHAUSTS.TryGetValue(id, out exhausts))
            {
                exhausts = EXHAUSTS["northstar"];
                if (id == "guardian") exhausts = System.Array.ConvertAll(exhausts, e => (e.pos * 1.12f, e.dir));
            }
            foreach (var ps in new[] { dust, spray, smoke, flames, fireSmoke }) ps.Clear();
            fireLaidOut = false; sinceStop = 99; lastEngine = EngineState.Off;
        }

        static Vector3 Local(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        /// <summary>How many to emit this frame at `rate` a second, carrying the fraction over.</summary>
        static int Count(ref float acc, float rate, float dt) { acc += rate * dt; var n = (int)acc; acc -= n; return n; }

        void LateUpdate()
        {
            if (heli == null || heli.State == null || dust == null || fireSmoke == null) return;
            var s = heli.State; var o = s.@out; var ac = heli.Aircraft;
            var dt = heli.Paused ? 0 : Time.deltaTime;
            var wx = heli.WeatherAt != null ? heli.WeatherAt(s.t) : heli.Weather;
            var mw = wx != null ? Rotorcraft.Sim.Weather.MeanWind(wx) : new Vec3(0, 0, 0);
            var wind = SimToUnity.Direction(mw);
            ParticleFx.Wind(smoke, wind * 0.8f);
            ParticleFx.Wind(fireSmoke, wind * 0.8f);
            foreach (var ps in new[] { dust, spray, smoke, flames, fireSmoke }) { var m = ps.main; m.simulationSpeed = heli.Paused ? 0 : 1; }
            if (dt <= 0) return;

            // ---- rotor downwash: spray over water, dust and grass over land (not on decks and roofs)
            var R = (float)ac.rotorDiameter / 2;
            float rpm = (float)o.rpm;
            float downwash = Mathf.Clamp01(1 - (float)o.aglM / ((float)ac.rotorDiameter * 1.4f)) * Mathf.Clamp01((rpm - 60) / 30) * Mathf.Clamp((float)(o.thrustN / (o.mass * 9.81 + 1)), 0, 1.2f);
            var surf = o.surfaceBelow;
            if (!s.crashed && downwash > 0.05f && surf != Surface.Deck && surf != Surface.Roof)
            {
                bool water = surf == Surface.Water;
                var gy = (float)(s.pos.y - o.aglM);
                var ps = water ? spray : dust;
                var n = Count(ref emitDust, downwash * (water ? 140 : surf == Surface.Pad ? 35 : 60), dt);
                var c = transform.position;
                Color col = water ? new Color(0.85f, 0.9f, 0.95f, 0.3f) : surf == Surface.Pad ? new Color(0.66f, 0.64f, 0.58f, 0.16f) : new Color(0.58f, 0.56f, 0.42f, 0.26f);
                for (int i = 0; i < n; i++)
                {
                    var a = Random.value * Mathf.PI * 2; var rad = R * (0.5f + Random.value * 0.6f);
                    var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var sp = 5 + Random.value * 8 * downwash;
                    ParticleFx.Emit(ps, new Vector3(c.x, gy + 0.3f, c.z) + d * rad, d * sp + Vector3.up * (0.6f + Random.value * 1.8f),
                        1.2f + Random.value * 1.4f, (water ? 1.4f : 1.0f) * (0.6f + Random.value) * 1.6f, col);
                }
            }

            // ---- exhaust smoke
            if (s.engine == EngineState.Off && lastEngine != EngineState.Off) sinceStop = 0;
            sinceStop += dt; lastEngine = s.engine;
            if (!s.crashed)
            {
                float N = (float)s.engineN; bool piston = ac.engines == 1;
                float rate = 0, dark = 0.3f, alpha = 0.3f;
                if (s.engine == EngineState.Starting)
                {
                    var lightOff = Smooth01((N - 0.06f) / 0.12f) * (1 - Smooth01((N - 0.35f) / 0.6f));
                    rate = 5 + 30 * lightOff; alpha = 0.3f + 0.45f * lightOff; dark = piston ? 0.5f : 0.62f - 0.25f * lightOff;
                }
                else if (s.engine == EngineState.Running) { rate = 5; alpha = 0.05f; dark = 0.6f; }
                else if (sinceStop < 4) { rate = 6 * (1 - sinceStop / 4); alpha = 0.12f; dark = 0.4f; }
                var n = Count(ref emitSmoke, rate * exhausts.Length, dt);
                for (int i = 0; i < n; i++)
                {
                    var (pos, dir) = exhausts[i % exhausts.Length];
                    var p = transform.TransformPoint(Local(pos)); var d = transform.TransformDirection(Local(dir).normalized);
                    var sp = 3 + Random.value * 3;
                    var tone = dark * (0.85f + Random.value * 0.3f);
                    ParticleFx.Emit(smoke, p, d * sp + Vector3.up * 0.4f, 3 + Random.value * 2.5f, (piston ? 0.3f : 0.55f) * (0.8f + Random.value * 0.4f) * 1.6f,
                        new Color(tone, tone * 0.98f, tone * (piston ? 1.05f : 0.96f), alpha));
                }
            }

            // ---- a crashed aircraft burns: flame low down and a plume of dark smoke downwind
            if (s.crashed)
            {
                var g = WorldMap.GroundAt(s.pos.x, s.pos.z).h;
                var tip = SimToUnity.Position(new Vec3(s.pos.x, System.Math.Max(s.pos.y, g + 0.3), s.pos.z));
                Fire(tip, wind, dt, ref fireLaidOut, flames, fireSmoke, ref emitFlame, ref emitFireSmoke, 0.6f);
            }
            else fireLaidOut = false;
        }

        /// <summary>
        /// A fire: tongues of flame leaning downwind, yellow-white at the root cooling to orange, and a plume of
        /// smoke. When a fire first comes into range its plume is laid out already, as if it had been burning.
        /// </summary>
        public static void Fire(Vector3 tip, Vector3 wind, float dt, ref bool laidOut, ParticleSystem flames, ParticleSystem smoke, ref float accFlame, ref float accSmoke, float scale = 1, float lod = 1)
        {
            var n = Count(ref accFlame, 120 * lod * scale, dt);
            for (int i = 0; i < n; i++)
            {
                var a = Random.value * Mathf.PI * 2; var r = Random.value * 0.7f * scale;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var hot = Random.value;
                ParticleFx.Emit(flames, tip + d * r + Vector3.up * 0.3f, wind * 0.8f + d * 0.7f + Vector3.up * (8 + Random.value * 9) * scale,
                    0.45f + Random.value * 0.45f, (2.0f + Random.value * 2.0f) * scale, new Color(1.0f, 0.5f + hot * 0.2f, 0.1f + hot * 0.08f, 0.55f));
            }
            void Puff(float age)
            {
                var tone = 0.26f + Random.value * 0.08f; var life = 9 + Random.value * 5;
                // the system adds the wind itself (ParticleFx.Wind); a puff laid out late starts where the wind took it
                var v = new Vector3(Random.value - 0.5f, 0, Random.value - 0.5f) * 1.2f; var rise = 2.2f + Random.value * 1.2f;
                var tA = Mathf.Min(age, life - 0.1f);
                var pos = tip + (v + wind * 0.8f) * tA + new Vector3((Random.value - 0.5f) * 2, 7 * scale + Random.value * 2 + rise * tA, (Random.value - 0.5f) * 2);
                ParticleFx.Emit(smoke, pos, v + Vector3.up * rise, life, (2.5f + Random.value * 1.5f) * scale, new Color(tone, tone * 0.97f, tone * 0.94f, 0.3f), tA);
            }
            if (!laidOut) { laidOut = true; for (int i = 0; i < 40; i++) Puff(Random.value * 15); }
            var m = Count(ref accSmoke, 4 * lod * Mathf.Max(scale, 0.7f), dt);
            for (int i = 0; i < m; i++) Puff(0);
        }
    }
}

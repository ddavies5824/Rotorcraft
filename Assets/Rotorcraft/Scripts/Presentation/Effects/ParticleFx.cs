// Particle systems for the effects, made in code with the "Rotorcraft/Particles" shader: puffs that are emitted
// one by one by the effect that owns them (EmitParams), then drift, slow, grow and fade by themselves.
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public static class ParticleFx
    {
        static Material blended, flame;
        static readonly int RcParticleLight = Shader.PropertyToID("_RcParticleLight");
        static readonly Vector3[] Up = { Vector3.up };
        static readonly Color[] Probe = new Color[1];

        static Material Mat(bool additive)
        {
            var shader = Shader.Find("Rotorcraft/Particles");
            if (shader == null) { Debug.LogWarning("Rotorcraft/Particles shader missing"); return null; }
            var m = new Material(shader) { name = additive ? "Fx flame" : "Fx smoke" };
            m.SetTexture("_Noise", FxNoise.NoiseTexture);
            m.SetFloat("_SrcBlend", additive ? (float)BlendMode.One : (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Emissive", additive ? 1 : 0);
            m.SetFloat("_Breakup", additive ? 0.35f : 0.6f);
            if (Shader.GetGlobalVector(RcParticleLight) == Vector4.zero) Shader.SetGlobalVector(RcParticleLight, Vector4.one);
            return m;
        }

        public static Material Blended => blended != null ? blended : blended = Mat(false);
        public static Material Flame => flame != null ? flame : flame = Mat(true);

        /// <summary>
        /// A world-space system that only moves what it is given: gravity (negative to rise), drag, growth to
        /// `grow` times the start size, and a fade in and out over each puff's life.
        /// </summary>
        public static ParticleSystem Make(string name, Transform parent, bool additive, int max, float gravity, float drag, float grow, float fadeIn = 0.08f, float fadeOutFrom = 0.45f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.duration = 10; main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max; main.gravityModifier = gravity;
            main.startSpeed = 0; main.startLifetime = 2; main.startSize = 1;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            if (drag > 0)
            {
                var lv = ps.limitVelocityOverLifetime;
                lv.enabled = true; lv.limit = 1000; lv.drag = drag;
                lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false;
            }
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, fadeIn), new GradientAlphaKey(1, fadeOutFrom), new GradientAlphaKey(0, 1) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            // grows fast at first and then slowly, like a puff spreading out
            sz.size = new ParticleSystem.MinMaxCurve(grow, new AnimationCurve(new Keyframe(0, 1 / grow, 0, 3), new Keyframe(0.35f, 0.6f + 0.4f / grow), new Keyframe(1, 1)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = additive ? Flame : Blended;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = additive ? ParticleSystemSortMode.None : ParticleSystemSortMode.Distance;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.minParticleSize = 0; r.maxParticleSize = 1.5f;
            ps.Play();
            return ps;
        }

        /// <summary>Make smoke drift with the wind (m/s, Unity axes).</summary>
        public static void Wind(ParticleSystem ps, Vector3 wind)
        {
            var v = ps.velocityOverLifetime;
            v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
            v.x = wind.x; v.y = 0; v.z = wind.z;
        }

        public static void Emit(ParticleSystem ps, Vector3 pos, Vector3 vel, float life, float size, Color c, float age = 0)
        {
            var e = new ParticleSystem.EmitParams
            {
                position = pos, velocity = vel, startLifetime = life, startSize = size, startColor = c,
                rotation = Random.value * 360, applyShapeToPosition = false,
            };
            ps.Emit(e, 1);
            if (age > 0)
            {
                // a puff that has been going a while already (the plume of a fire coming into range)
                int n = ps.particleCount;
                if (n == 0) return;
                var parts = Parts(n);
                ps.GetParticles(parts, n);
                var p = parts[n - 1];
                p.remainingLifetime = Mathf.Max(0.1f, life - age);
                parts[n - 1] = p;
                ps.SetParticles(parts, n);
            }
        }

        static ParticleSystem.Particle[] buf;
        static ParticleSystem.Particle[] Parts(int n) => buf != null && buf.Length >= n ? buf : buf = new ParticleSystem.Particle[Mathf.NextPowerOfTwo(n)];

        /// <summary>The light on the particles: the sky's ambient from above plus the sun or moon.</summary>
        public static void UpdateLight(Light sun)
        {
            RenderSettings.ambientProbe.Evaluate(Up, Probe);
            var c = Probe[0] * 0.9f;
            if (sun != null && sun.isActiveAndEnabled) c += sun.color * sun.intensity * 0.5f * Mathf.Clamp01(0.3f - sun.transform.forward.y);
            c.r = Mathf.Min(c.r, 1.15f); c.g = Mathf.Min(c.g, 1.15f); c.b = Mathf.Min(c.b, 1.2f);
            Shader.SetGlobalVector(RcParticleLight, new Vector4(c.r, c.g, c.b, 1));
        }
    }
}

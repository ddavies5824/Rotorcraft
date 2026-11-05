// The turning rotors of the wind farm (port of buildTurbineRotor in the browser game's render/npcModels.ts and the part of its
// scene that draws them). One mesh shared by all of them: a hub and three blades, wide at the root and narrow at the tip, each
// turbine at its own place in the turn.
using Rotorcraft.World;
using UnityEngine;
using UnityEngine.Rendering;

namespace Rotorcraft.Presentation
{
    public sealed class TurbineRotors : MonoBehaviour
    {
        /// <summary>The middle of the rotor above the top of the tower, and ahead of the tower's centre (m).</summary>
        public const float HubAboveTower = 1.6f, HubAhead = 3.6f;
        const float Speed = 1.45f, Spread = 1.3f;   // radians a second, and between one turbine and the next

        Transform[] rotors;
        Quaternion[] facing;

        /// <summary>The way a rotor faces in Unity's axes, from its yaw in the sim's (x east, z south).</summary>
        public static Vector3 Facing(double yaw) => new Vector3(Mathf.Sin((float)yaw), 0, -Mathf.Cos((float)yaw));

        public static void Add(Transform parent, Material white)
        {
            if (Places.TURBINES.Count == 0) return;
            var built = BuildMesh((float)Places.TURBINES[0].r, white);
            if (built == null) return;
            var host = new GameObject("Turbine rotors").AddComponent<TurbineRotors>();
            host.transform.SetParent(parent, false);
            var n = Places.TURBINES.Count;
            host.rotors = new Transform[n];
            host.facing = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                var t = Places.TURBINES[i];
                var face = Facing(t.yaw);
                var rotor = new GameObject("Rotor " + i);
                rotor.transform.SetParent(host.transform, false);
                rotor.transform.position = SimToUnity.Position(new Rotorcraft.Sim.Vec3(t.x, WorldMap.TerrainHeight(t.x, t.z) + t.hub + HubAboveTower, t.z)) + face * HubAhead;
                rotor.AddComponent<MeshFilter>().sharedMesh = built.Value.mesh;
                var mr = rotor.AddComponent<MeshRenderer>();
                mr.sharedMaterials = built.Value.materials;
                mr.shadowCastingMode = ShadowCastingMode.On;
                host.rotors[i] = rotor.transform;
                host.facing[i] = Quaternion.LookRotation(face);
            }
            host.Update();
        }

        void Update()
        {
            // clockwise from in front, as real ones turn
            for (int i = 0; i < rotors.Length; i++) rotors[i].rotation = facing[i] * Quaternion.Euler(0, 0, (Time.time * Speed + i * Spread) * Mathf.Rad2Deg);
        }

        /// <summary>The rotor about its own origin, facing +z.</summary>
        static (Mesh mesh, Material[] materials)? BuildMesh(float r, Material m)
        {
            var b = new MeshBuilder();
            // the hub: a nose cone, longer than it is wide
            const int Around = 10, Along = 6;
            Vector3 Hub(int i, int j)
            {
                float a = i * Mathf.PI * 2 / Around, e = j * Mathf.PI / Along;
                return new Vector3(Mathf.Cos(a) * Mathf.Sin(e) * 1.3f, Mathf.Sin(a) * Mathf.Sin(e) * 1.3f, 0.6f + Mathf.Cos(e) * 2f);
            }
            for (int i = 0; i < Around; i++)
                for (int j = 0; j < Along; j++)
                {
                    Vector3 p0 = Hub(i, j), p1 = Hub(i + 1, j), p2 = Hub(i + 1, j + 1), p3 = Hub(i, j + 1);
                    var outward = ((p0 + p1 + p2 + p3) / 4 - new Vector3(0, 0, 0.6f)).normalized;
                    if (j == 0) b.Tri(m, p0, p2, p3, outward);
                    else if (j == Along - 1) b.Tri(m, p0, p1, p2, outward);
                    else b.Quad(m, p0, p1, p2, p3, default, outward);
                }
            // three blades, seen from both sides, with a little twist across the chord
            var chord = new[] { (1.2f, 2.2f), (r * 0.22f, 3.4f), (r * 0.6f, 2.0f), (r, 0.6f) };
            for (int k = 0; k < 3; k++)
            {
                float a = k * Mathf.PI * 2 / 3, ux = Mathf.Sin(a), uy = Mathf.Cos(a), px = Mathf.Cos(a), py = -Mathf.Sin(a);
                Vector3 P(float t, float half, float side) => new Vector3(ux * t + px * half * side, uy * t + py * half * side, 0.3f - side * half * 0.15f);
                for (int s = 0; s + 1 < chord.Length; s++)
                {
                    var (t0, w0) = chord[s]; var (t1, w1) = chord[s + 1];
                    Vector3 c0 = P(t0, w0 / 2, -1), c1 = P(t1, w1 / 2, -1), c2 = P(t1, w1 / 2, 1), c3 = P(t0, w0 / 2, 1);
                    b.Quad(m, c0, c1, c2, c3, default, Vector3.forward);
                    b.Quad(m, c0, c1, c2, c3, default, Vector3.back);
                }
            }
            return b.Build("Turbine rotor");
        }
    }
}

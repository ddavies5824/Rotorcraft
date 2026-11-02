// Builds the Kinloch region when the scene starts and makes it the flight model's ground. Nothing generated is
// saved in the scene: the terrain, roads and buildings come from the world data every time.
using Rotorcraft.Sim;
using Rotorcraft.World;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public sealed class KinlochWorld : MonoBehaviour
    {
        [Tooltip("The imported landmarks.glb: one child per Blender-modelled structure, named by its key.")]
        public GameObject landmarks;
        [Tooltip("Material for the terrain (URP Terrain Lit).")]
        public Material terrainMaterial;
        [Tooltip("One terrain layer per SurfaceLayer: grass, moor, rock, sand, pasture, stubble, ploughed, hedge.")]
        public TerrainLayer[] surfaceLayers;
        [Tooltip("The imported trees.glb: one child per tree variant.")]
        public GameObject trees;
        [Tooltip("Grow grass blades near the camera.")]
        public bool grass = true;
        public int treeBudget = 100000;
        [Tooltip("Re-rendered once the world is built, so paint and glass reflect the real surroundings.")]
        public ReflectionProbe reflectionProbe;

        [Tooltip("Build as soon as the scene starts (test scenes). The game builds behind its loading screen instead.")]
        public bool buildOnAwake = true;

        public bool Built { get; private set; }

        void Awake()
        {
            Ground.Current = new WorldGround();
            if (GetComponent<SkyEnvironment>() == null) gameObject.AddComponent<SkyEnvironment>();
            if (!Built && buildOnAwake) Build();
        }

        public void Build()
        {
            var e = BuildRoutine(null);
            while (e.MoveNext()) { }
        }

        /// <summary>Build in stages, a frame apart, reporting progress for the loading screen.</summary>
        public System.Collections.IEnumerator BuildRoutine(System.Action<float, string> progress)
        {
            Clear();
            Ground.Current = new WorldGround();
            var t0 = Time.realtimeSinceStartup;
#if UNITY_WEBGL && !UNITY_EDITOR
            // the browser has one thread and less memory: fewer trees, no grass blades
            treeBudget = Mathf.Min(treeBudget, 35000);
            grass = false;
#endif
            progress?.Invoke(0.05f, "Shaping the hills and the firth");
            yield return null;
            var steps = KinlochTerrain.BuildSteps(transform, terrainMaterial, surfaceLayers, grass ? GrassBlades.Prototypes(GrassBlades.Texture()) : null, new System.Collections.Generic.List<UnityEngine.Terrain>(),
                f => progress?.Invoke(0.05f + 0.4f * f, "Shaping the hills and the firth"));
            while (steps.MoveNext()) yield return steps.Current;
            progress?.Invoke(0.45f, "Planting the forests");
            yield return null;
            Debug.Log($"World build: terrain done at {Time.realtimeSinceStartup - t0:F1} s");
            KinlochTrees.Plant(transform, trees, treeBudget);
            progress?.Invoke(0.65f, "Building Kinloch, the roads and the heliport");
            yield return null;
            Debug.Log($"World build: trees done at {Time.realtimeSinceStartup - t0:F1} s");
            KinlochDressing.Build(transform, WorldMaterials.Make(), landmarks);
            progress?.Invoke(0.85f, "Switching on the lights");
            yield return null;
            Debug.Log($"World build: buildings done at {Time.realtimeSinceStartup - t0:F1} s");
            WorldLightsRenderer.Build(transform);
            DynamicGI.UpdateEnvironment();
            if (reflectionProbe != null) reflectionProbe.RenderProbe();
            Built = true;
            progress?.Invoke(0.92f, "World ready");
            Debug.Log($"Kinloch built in {Time.realtimeSinceStartup - t0:F1} s");
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var c = transform.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }
            Built = false;
        }

        /// <summary>Unity-space position of a point on the ground (or a deck) at sim (x, z).</summary>
        public static Vector3 GroundPoint(double x, double z) => SimToUnity.Position(new Vec3(x, WorldMap.GroundAt(x, z).h, z));
    }
}

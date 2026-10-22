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

        public bool Built { get; private set; }

        void Awake()
        {
            Ground.Current = new WorldGround();
            if (GetComponent<SkyEnvironment>() == null) gameObject.AddComponent<SkyEnvironment>();
            if (!Built) Build();
        }

        public void Build()
        {
            Clear();
            Ground.Current = new WorldGround();
            var t0 = Time.realtimeSinceStartup;
            var tiles = KinlochTerrain.Build(transform, terrainMaterial, surfaceLayers, grass ? GrassBlades.Prototypes(GrassBlades.Texture()) : null);
            KinlochTrees.Plant(transform, trees, treeBudget);
            KinlochDressing.Build(transform, WorldMaterials.Make(), landmarks);
            WorldLightsRenderer.Build(transform);
            DynamicGI.UpdateEnvironment();
            if (reflectionProbe != null) reflectionProbe.RenderProbe();
            Built = true;
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

// The people pack (Models/People: eleven characters, one shared animation file and two tables, made in Blender from MPFB
// characters and Mixamo motion; see NPC_IMPLEMENTATION_NOTES.md in the pack's export folder). Unity keeps a .glb's
// embedded images as uncompressed textures (about 200 MB of them for the eleven characters), so Generate pulls each image
// out beside the pack to be imported compressed, and smaller for the web; copies each material onto those textures; and
// saves one prefab per character whose rig root is renamed npc_animation_rig, so the shared clips (their curve paths
// start with the animation file's rig name) bind to every character. The .glb files stay the source: run Generate again
// after replacing them.
//   bench.ps1 exec Rotorcraft.EditorTools.NpcImport.Generate        (then copy Models/People back from the bench)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.Collections;
using Rotorcraft.Presentation;
using UnityEditor;
using UnityEngine;

namespace Rotorcraft.EditorTools
{
    public static class NpcImport
    {
        public const string Dir = "Assets/Rotorcraft/Models/People";
        public const string TexDir = Dir + "/Textures", MatDir = Dir + "/Materials", PrefabDir = Dir + "/Prefabs", MeshDir = Dir + "/Merged";
        public const string AnimFile = Dir + "/npc_animations.glb";

        public static IEnumerable<string> Ids() => ((JObject)JObject.Parse(File.ReadAllText(Dir + "/npc_characters.json"))).Properties().Select(p => p.Name);

        /// <summary>Each character with its parts separate (null where one is missing: run Generate).</summary>
        public static GameObject[] PartPrefabs() => Ids().Select(id => AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab")).ToArray();

        /// <summary>The prefabs the game ships: the merged looks only (the separate-part characters are for measuring in the editor, and
        /// would carry a second copy of every mesh into the build).</summary>
        public static GameObject[] Prefabs() => AssetDatabase.FindAssets("t:Prefab", new[] { PrefabDir }).Select(g => AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g))).Where(g => g != null && g.name.Contains("@")).ToArray();

        /// <summary>The shared clips, in the clip table's order.</summary>
        public static AnimationClip[] Clips()
        {
            var all = AssetDatabase.LoadAllAssetsAtPath(AnimFile).OfType<AnimationClip>().ToDictionary(c => c.name);
            var order = ((JObject)JObject.Parse(File.ReadAllText(Dir + "/npc_clips.json"))["clips"]).Properties().Select(p => p.Name);
            return order.Select(n => all.TryGetValue(n, out var c) ? c : null).ToArray();
        }

        public static bool Generated() => PartPrefabs().All(p => p != null) && NpcRoles.Looks().All(l => File.Exists($"{PrefabDir}/{NpcLibrary.Look(l.id, l.without)}.prefab"));

        [MenuItem("Rotorcraft/People/Generate prefabs from the pack")]
        public static void Generate()
        {
            foreach (var d in new[] { TexDir, MatDir, PrefabDir, MeshDir }) Directory.CreateDirectory(d);
            // 1. the images, written only when they differ, so an unchanged pack leaves git quiet
            var written = 0;
            foreach (var id in Ids())
            {
                var (json, bin) = ReadGlb($"{Dir}/{id}.glb");
                foreach (var img in (JArray)json["images"])
                {
                    var bv = json["bufferViews"][(int)img["bufferView"]];
                    var bytes = new byte[(int)bv["byteLength"]];
                    Buffer.BlockCopy(bin, (int?)bv["byteOffset"] ?? 0, bytes, 0, bytes.Length);
                    var path = $"{TexDir}/{(string)img["name"]}{((string)img["mimeType"] == "image/png" ? ".png" : ".jpg")}";
                    if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) continue;
                    File.WriteAllBytes(path, bytes);
                    written++;
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"People: {written} images written");
            // 2. materials on the compressed images, 3. one prefab per character
            foreach (var id in Ids()) BuildPrefab(id);
            AssetDatabase.SaveAssets();
            // 3b. each look the game asks for, its figure merged into one renderer; looks it no longer asks for go
            var wanted = new HashSet<string>();
            foreach (var (id, without) in NpcRoles.Looks()) { BuildLook(id, without); wanted.Add(NpcLibrary.Look(id, without)); }
            foreach (var path in Directory.GetFiles(PrefabDir, "*@*.prefab").Select(p => p.Replace(Path.DirectorySeparatorChar, '/')))
            {
                var look = Path.GetFileNameWithoutExtension(path);
                if (wanted.Contains(look)) continue;
                AssetDatabase.DeleteAsset(path);
                AssetDatabase.DeleteAsset($"{MeshDir}/{look}.asset");
                Debug.Log($"People: removed the look {look}");
            }
            AssetDatabase.SaveAssets();
            // 4. how far each character's soles end up from the ground in each clip
            MeasureGround();
            Debug.Log($"People: {Prefabs().Count(p => p != null)} prefabs, {Clips().Count(c => c != null)} clips");
        }

        static void BuildPrefab(string id)
        {
            var glb = $"{Dir}/{id}.glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (src == null) { Debug.LogError("People: cannot load " + glb); return; }
            var mats = new Dictionary<Material, Material>();
            Material Converted(Material m)
            {
                if (m == null) return null;
                if (mats.TryGetValue(m, out var done)) return done;
                var copy = new Material(m) { name = m.name };
                foreach (var prop in copy.GetTexturePropertyNames())
                {
                    var t = copy.GetTexture(prop);
                    if (t == null) continue;
                    var file = new[] { ".jpg", ".png" }.Select(e => $"{TexDir}/{t.name}{e}").FirstOrDefault(File.Exists);
                    var tex = file != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(file) : null;
                    if (tex != null) copy.SetTexture(prop, tex);
                    else Debug.LogWarning($"People: no extracted image for {m.name}.{prop} ({t.name})");
                }
                var path = $"{MatDir}/{m.name}.mat";
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing != null) { EditorUtility.CopySerialized(copy, existing); UnityEngine.Object.DestroyImmediate(copy); copy = existing; }
                else AssetDatabase.CreateAsset(copy, path);
                mats[m] = copy;
                return copy;
            }

            var inst = UnityEngine.Object.Instantiate(src);
            var hips = inst.GetComponentsInChildren<Transform>(true).First(t => t.name == Npc.HipsBone);
            var rig = hips.parent;
            var root = new GameObject(id);
            rig.SetParent(root.transform, false);
            rig.name = Npc.RigName;
            if (inst != rig.gameObject) UnityEngine.Object.DestroyImmediate(inst);
            foreach (var r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.sharedMaterials = r.sharedMaterials.Select(Converted).ToArray();
                r.updateWhenOffscreen = false;
                // bounds that follow the hips and hold every clip: lying, swimming, hanging by the hands
                r.rootBone = hips;
                r.localBounds = new Bounds(Vector3.zero, new Vector3(2.4f, 2.7f, 2.4f));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                r.skinnedMotionVectors = false;
            }
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;   // Npc evaluates it, and decides when
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}/{id}.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        /// <summary>Clips that rest on the body rather than the feet (lifted so the lowest point of anything touches the ground), and clips
        /// with no ground under them (water, the winch, hanging, ladders: not lifted).</summary>
        static readonly string[] Resting = { "sit_ground", "lie_back", "kneel_idle" };
        static readonly string[] NoGround = { "tread_water", "swim", "hang_idle", "climb_ladder", "winch_hang_idle", "winch_descend", "winch_ascend", "winch_double_rescuer", "winch_double_casualty" };

        /// <summary>For each character and clip, the lift that puts the soles on the ground: minus the median of the lowest sole through
        /// the clip (minus the lowest point of all for a clip that rests on the body). Written to npc_ground.json.</summary>
        public static void MeasureGround()
        {
            var host = new GameObject("people (measuring)");
            var lib = host.AddComponent<NpcLibrary>();
            lib.EnsureLoaded();
            Npc.Lifted = false;
            var table = new JObject();
            var mesh = new Mesh();
            try
            {
                foreach (var id in Ids())
                {
                    var per = new JObject();
                    foreach (var clip in Clips())
                    {
                        if (clip == null || NoGround.Contains(clip.name)) continue;
                        var resting = Resting.Contains(clip.name);
                        var n = lib.Spawn(id, null, null, true);
                        var ys = new List<float>();
                        for (int k = 0; k < 24; k++)
                        {
                            n.Play(clip.name, 0, 0, k / 24f, true);
                            n.Evaluate(0);
                            var low = float.MaxValue;
                            foreach (var r in n.GetComponentsInChildren<SkinnedMeshRenderer>())
                            {
                                if (!resting && !r.name.EndsWith("_shoes")) continue;
                                r.BakeMesh(mesh, true);
                                var m = r.transform.localToWorldMatrix;
                                foreach (var v in mesh.vertices) low = Mathf.Min(low, m.MultiplyPoint3x4(v).y);
                            }
                            ys.Add(low);
                        }
                        ys.Sort();
                        var lift = -(resting ? ys[0] : ys[ys.Count / 2]);
                        per[clip.name] = Math.Round(lift, 4);
                        UnityEngine.Object.DestroyImmediate(n.gameObject);
                    }
                    table[id] = per;
                }
            }
            finally
            {
                Npc.Lifted = true;
                UnityEngine.Object.DestroyImmediate(mesh);
                UnityEngine.Object.DestroyImmediate(host);
            }
            var path = Dir + "/npc_ground.json";
            File.WriteAllText(path, table.ToString());
            AssetDatabase.ImportAsset(path);
            Debug.Log($"People: soles measured, idle lifts {string.Join(", ", Ids().Select(i => $"{i} {(double)table[i]["idle"] * 100:F1} cm"))}");
        }

        /// <summary>
        /// One look (a character with some parts hidden) with its figure as one skinned renderer instead of five to seven: body, suit,
        /// shoes, hair and the big kit, casting shadows. The small parts (eyes, brows, ear defenders, visor, straps) stay as they are:
        /// they never cast a shadow and are dropped beyond 25 m, and Unity will not renumber the bones of meshes whose vertices follow
        /// one bone each (CombineMeshes leaves them pointing past the skeleton). The parts share one skin (one bone list, one set of
        /// bind poses), so their meshes combine as they are, one submesh each.
        /// Merged here rather than in the game, where the imported meshes may not be readable. PeoplePerf: each skinned renderer
        /// costs its own culling, skinning and draws, which is most of what a crowd costs on a handheld.
        /// </summary>
        static void BuildLook(string id, string[] without)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}/{id}.prefab");
            if (src == null) return;
            var look = NpcLibrary.Look(id, without);
            var inst = UnityEngine.Object.Instantiate(src);
            inst.name = id;
            var parts = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            bool Hidden(SkinnedMeshRenderer r) => without != null && without.Any(w => r.name.StartsWith(id + "_" + w));
            bool Small(SkinnedMeshRenderer r) => Npc.SmallParts.Any(p => r.name.StartsWith(id + "_" + p));
            var keep = parts.Where(r => !Hidden(r)).ToArray();
            var template = keep.First(r => r.name == id + "_suit");
            var meshPath = $"{MeshDir}/{look}.asset";
            var figure = Combine(keep.Where(r => !Small(r)).ToArray(), id + "_figure");
            // keep the asset (and its GUID) when it is already there: replace the meshes inside it
            var old = AssetDatabase.LoadAllAssetsAtPath(meshPath).OfType<Mesh>().ToDictionary(m => m.name);
            Mesh Store(Mesh m, bool first)
            {
                if (old.TryGetValue(m.name, out var o)) { EditorUtility.CopySerialized(m, o); UnityEngine.Object.DestroyImmediate(m); return o; }
                if (first && !File.Exists(meshPath)) AssetDatabase.CreateAsset(m, meshPath); else AssetDatabase.AddObjectToAsset(m, meshPath);
                return m;
            }
            var figureMesh = Store(figure.mesh, true);
            foreach (var stale in old.Values.Where(m => m.name != figureMesh.name).ToList()) { AssetDatabase.RemoveObjectFromAsset(stale); UnityEngine.Object.DestroyImmediate(stale, true); }
            var rig = template.transform.parent;
            void Renderer(string name, Mesh mesh, Material[] mats, bool shadows)
            {
                if (mesh == null) return;
                var go = new GameObject(name);
                go.transform.SetParent(rig, false);
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = mesh; r.bones = template.bones; r.rootBone = template.rootBone; r.sharedMaterials = mats;
                r.updateWhenOffscreen = false; r.localBounds = template.localBounds; r.skinnedMotionVectors = false;
                r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = true; r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
            }
            Renderer(id + "_figure", figureMesh, figure.mats, true);
            foreach (var p in parts)
                if (Hidden(p) || !Small(p)) UnityEngine.Object.DestroyImmediate(p.gameObject);
                else p.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            PrefabUtility.SaveAsPrefabAsset(inst, $"{PrefabDir}/{look}.prefab");
            UnityEngine.Object.DestroyImmediate(inst);
        }

        static (Mesh mesh, Material[] mats) Combine(SkinnedMeshRenderer[] rs, string name)
        {
            if (rs.Length == 0) return (null, new Material[0]);
            var bind = rs[0].sharedMesh.bindposes;
            foreach (var r in rs)
                if (r.sharedMesh.bindposes.Length != bind.Length || !r.bones.SequenceEqual(rs[0].bones))
                    throw new Exception($"People: {r.name} has its own skin, so it cannot be merged");
            var cis = new List<CombineInstance>();
            var mats = new List<Material>();
            foreach (var r in rs)
            {
                var src = Readable(r.sharedMesh);
                for (int s = 0; s < src.subMeshCount; s++)
                {
                    cis.Add(new CombineInstance { mesh = src, subMeshIndex = s, transform = Matrix4x4.identity });
                    mats.Add(r.sharedMaterials[Math.Min(s, r.sharedMaterials.Length - 1)]);
                }
            }
            var verts = cis.Sum(c => c.mesh.vertexCount);   // each submesh brings its mesh's vertices
            var mesh = new Mesh { name = name, indexFormat = verts < 65000 ? UnityEngine.Rendering.IndexFormat.UInt16 : UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(cis.ToArray(), false, false);
            // CombineMeshes takes each piece to have bones of its own and numbers the second piece's from 52, the third's from 104...;
            // here every piece has the same 52, so the numbers go back onto them
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            var fixedWeights = new NativeArray<BoneWeight1>(weights.Length, Allocator.Temp);
            for (int i = 0; i < weights.Length; i++) { var w = weights[i]; w.boneIndex %= bind.Length; fixedWeights[i] = w; }
            var perVertexCopy = new NativeArray<byte>(perVertex, Allocator.Temp);
            mesh.SetBoneWeights(perVertexCopy, fixedWeights);
            fixedWeights.Dispose(); perVertexCopy.Dispose();
            mesh.bindposes = bind;
            mesh.RecalculateBounds();
            if (mesh.boneWeights.Length != mesh.vertexCount) throw new Exception($"People: merging {name} lost the skin weights");
            return (mesh, mats.ToArray());
        }

        /// <summary>The importer leaves its meshes unreadable, which CombineMeshes refuses: a readable copy, read back from the graphics
        /// card as glTFast's own exporter does (NonReadableMeshData), every vertex stream and the indices as they are.</summary>
        static Mesh Readable(Mesh m)
        {
            if (m.isReadable) return m;
            var copy = new Mesh { name = m.name, indexFormat = m.indexFormat };
            copy.SetVertexBufferParams(m.vertexCount, m.GetVertexAttributes());
            const UnityEngine.Rendering.MeshUpdateFlags quiet = UnityEngine.Rendering.MeshUpdateFlags.DontRecalculateBounds | UnityEngine.Rendering.MeshUpdateFlags.DontValidateIndices;
            for (int s = 0; s < m.vertexBufferCount; s++)
            {
                using var vb = m.GetVertexBuffer(s);
                var data = new NativeArray<byte>(vb.count * vb.stride, Allocator.Persistent);
                var req = UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref data, vb);
                req.WaitForCompletion();
                if (req.hasError) { data.Dispose(); throw new Exception($"People: could not read {m.name} back from the graphics card"); }
                copy.SetVertexBufferData(data, 0, 0, data.Length, s, quiet);
                data.Dispose();
            }
            using (var ib = m.GetIndexBuffer())
            {
                copy.SetIndexBufferParams(ib.count, m.indexFormat);
                if (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16)
                {
                    var idx = new NativeArray<ushort>(ib.count, Allocator.Persistent);
                    var req = UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref idx, ib); req.WaitForCompletion();
                    copy.SetIndexBufferData(idx, 0, 0, idx.Length, quiet); idx.Dispose();
                }
                else
                {
                    var idx = new NativeArray<uint>(ib.count, Allocator.Persistent);
                    var req = UnityEngine.Rendering.AsyncGPUReadback.RequestIntoNativeArray(ref idx, ib); req.WaitForCompletion();
                    copy.SetIndexBufferData(idx, 0, 0, idx.Length, quiet); idx.Dispose();
                }
            }
            copy.subMeshCount = m.subMeshCount;
            for (int i = 0; i < m.subMeshCount; i++) copy.SetSubMesh(i, m.GetSubMesh(i), quiet);
            copy.bindposes = m.bindposes;
            copy.bounds = m.bounds;
            return copy;
        }

        static (JObject json, byte[] bin) ReadGlb(string path)
        {
            var b = File.ReadAllBytes(path);
            var jsonLen = BitConverter.ToInt32(b, 12);
            var json = JObject.Parse(Encoding.UTF8.GetString(b, 20, jsonLen));
            var at = 20 + jsonLen;
            var binLen = BitConverter.ToInt32(b, at);
            var bin = new byte[binLen];
            Buffer.BlockCopy(b, at + 8, bin, 0, binLen);
            return (json, bin);
        }

        /// <summary>What the importer made of the pack, in the log (clip paths, sizes, names), to check it against the notes.</summary>
        public static void Probe()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(AnimFile).OfType<AnimationClip>().ToArray();
            long bytes = 0;
            foreach (var c in clips)
            {
                var b = AnimationUtility.GetCurveBindings(c);
                bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(c);
                if (c.name == "idle" || c.name == "walk")
                    Debug.Log($"People probe: clip {c.name} {c.length:F2} s legacy {c.legacy} loop {c.isLooping} {b.Length} curves; first {string.Join(" | ", b.Take(3).Select(x => x.path + "." + x.propertyName))}; hips {string.Join(", ", b.Where(x => x.path.EndsWith(Npc.HipsBone)).Select(x => x.propertyName))}");
            }
            Debug.Log($"People probe: {clips.Length} clips, {bytes / 1024} KB in the editor");
            var g = AssetDatabase.LoadAssetAtPath<GameObject>($"{Dir}/pilot.glb");
            void Walk(Transform t, int depth) { if (depth > 2) return; Debug.Log($"People probe: {new string(' ', depth * 2)}{t.name} [{string.Join(",", t.GetComponents<Component>().Select(c => c.GetType().Name))}]"); foreach (Transform c in t) Walk(c, depth + 1); }
            Walk(g.transform, 0);
            foreach (var r in g.GetComponentsInChildren<SkinnedMeshRenderer>())
                Debug.Log($"People probe: {r.name} root bone {(r.rootBone ? r.rootBone.name : "none")} bones {r.bones.Length} mats {string.Join(",", r.sharedMaterials.Select(m => m.name + ":" + m.shader.name + ":" + string.Join("/", m.GetTexturePropertyNames().Where(p => m.GetTexture(p) != null).Select(p => p + "=" + m.GetTexture(p).name))))}");
            foreach (var t in AssetDatabase.LoadAllAssetsAtPath($"{Dir}/pilot.glb").OfType<Texture2D>())
                Debug.Log($"People probe: texture {t.name} {t.width}x{t.height} {t.format} {UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(t) / 1024} KB");
        }
    }

    /// <summary>The images pulled out of the people pack: compressed, colour in sRGB, the clothing normal maps linear RGB (glTFast's
    /// shader reads all three channels), hair keeping its coverage in the distance; the web at half size.</summary>
    public sealed class NpcTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(NpcImport.TexDir + "/")) return;
            var ti = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var normal = name.EndsWith("_n");
            var hair = name.EndsWith("_hair") || name.EndsWith("_brows");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = !normal;
            ti.mipmapEnabled = true;
            ti.alphaIsTransparency = hair;
            ti.mipMapsPreserveCoverage = name.EndsWith("_hair");
            ti.alphaTestReferenceValue = 0.5f;
            ti.anisoLevel = 2;
            ti.maxTextureSize = 1024;
            ti.textureCompression = normal ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Compressed;
            ti.crunchedCompression = !normal;
            ti.compressionQuality = 70;
            var web = ti.GetPlatformTextureSettings("WebGL");
            web.overridden = true;
            web.maxTextureSize = normal ? 256 : 512;
            web.format = TextureImporterFormat.Automatic;
            web.textureCompression = TextureImporterCompression.Compressed;
            web.crunchedCompression = true;
            web.compressionQuality = 60;
            ti.SetPlatformTextureSettings(web);
        }
    }
}

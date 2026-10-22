// Pictures of each aircraft for the hangar, rendered from the real models once and kept: three-quarter view
// from the front left, soft studio light, transparent background.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Rotorcraft.Presentation.UI
{
    public static class AircraftPortraits
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        const int W = 640, H = 360, LAYER = 31;

        public static Texture2D Get(string aircraftId)
        {
            if (cache.TryGetValue(aircraftId, out var t) && t != null) return t;
            var lib = Object.FindAnyObjectByType<AircraftLibrary>();
            var prefab = lib != null ? lib.ModelFor(aircraftId) : null;
            if (prefab == null) return null;
            t = Render(prefab);
            cache[aircraftId] = t;
            return t;
        }

        static Texture2D Render(GameObject prefab)
        {
            var stage = new GameObject("Portrait stage");
            stage.transform.position = new Vector3(0, -6000, 0);
            var model = Object.Instantiate(prefab, stage.transform, false);
            model.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var tr in model.GetComponentsInChildren<Transform>(true))
            {
                tr.gameObject.layer = LAYER;
                if (tr.name == "cockpit_only") tr.gameObject.SetActive(false);
            }
            var bounds = new Bounds(stage.transform.position, Vector3.zero);
            foreach (var r in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);

            var camGo = new GameObject("Portrait camera");
            var cam = camGo.AddComponent<Camera>();
            cam.cullingMask = 1 << LAYER;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.fieldOfView = 26; cam.nearClipPlane = 0.5f; cam.farClipPlane = 400;
            var data = cam.GetUniversalAdditionalCameraData(); data.renderPostProcessing = false; data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var size = bounds.extents.magnitude;
            var dir = Quaternion.Euler(14, 140, 0) * Vector3.forward;   // from the front left, a little above
            cam.transform.position = bounds.center - dir * size / Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.62f;
            cam.transform.rotation = Quaternion.LookRotation(bounds.center - cam.transform.position);

            var key = new GameObject("Key").AddComponent<Light>(); key.type = LightType.Directional; key.intensity = 1.6f; key.cullingMask = 1 << LAYER; key.transform.rotation = Quaternion.Euler(40, 110, 0); key.shadows = LightShadows.Soft;
            var fill = new GameObject("Fill").AddComponent<Light>(); fill.type = LightType.Directional; fill.intensity = 0.55f; fill.cullingMask = 1 << LAYER; fill.transform.rotation = Quaternion.Euler(20, -80, 0);

            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { name = prefab.name + " portrait" };
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            rt.Release();
            // at once, not at the end of the frame: the next portrait is rendered straight after this one
            Object.DestroyImmediate(stage); Object.DestroyImmediate(camGo); Object.DestroyImmediate(key.gameObject); Object.DestroyImmediate(fill.gameObject);
            return tex;
        }
    }
}

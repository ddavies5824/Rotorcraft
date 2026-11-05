// A gloved hand in VR, built from simple shapes so it costs no download. Its origin is the centre of the fist, Z runs through
// the fist from little finger to thumb, Y points from wrist to knuckles, and the palm is on the -X side (the left hand is
// the mirror image). The OpenXR grip pose has the same origin and Z, but in it Y points back towards the wrist and the
// palm is on the +X side (-X in the left hand before mirroring): FromGripPose turns one into the other.
using System.Collections.Generic;
using UnityEngine;

namespace Rotorcraft.VR
{
    public sealed class VrHand
    {
        const float PalmX = -0.036f, KnuckleY = 0.046f;
        public static readonly Quaternion FromGripPose = Quaternion.Euler(0, 0, 180);
        static readonly Color Glove = new Color(0.72f, 0.63f, 0.47f), Cuff = new Color(0.24f, 0.26f, 0.28f), Ready = new Color(0.55f, 0.85f, 1f);

        public readonly Transform root;
        readonly Material glove, cuff;
        readonly List<(Transform joint, float base_, float bend)> joints = new List<(Transform, float, float)>();
        readonly int layer;
        float curl = -1;
        bool ready;

        public VrHand(Transform parent, bool left, int layer)
        {
            this.layer = layer;
            root = new GameObject(left ? "Left hand" : "Right hand").transform;
            root.SetParent(parent, false);
            var model = new GameObject("Model").transform;
            model.SetParent(root, false);
            if (left) model.localScale = new Vector3(-1, 1, 1);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            glove = new Material(lit) { name = "VR glove" };
            glove.SetColor("_BaseColor", Glove); glove.SetFloat("_Smoothness", 0.2f);
            cuff = new Material(lit) { name = "VR cuff" };
            cuff.SetColor("_BaseColor", Cuff); cuff.SetFloat("_Smoothness", 0.3f);

            Part(PrimitiveType.Cube, model, new Vector3(PalmX, 0.002f, 0), new Vector3(0.028f, 0.09f, 0.086f), glove);
            Part(PrimitiveType.Cylinder, model, new Vector3(PalmX, -0.072f, 0), new Vector3(0.05f, 0.03f, 0.066f), cuff);
            // index to little finger, from the thumb's end of the palm to the other
            foreach (var (z, length) in new[] { (0.031f, 1f), (0.0105f, 1.08f), (-0.0105f, 1f), (-0.031f, 0.82f) })
                Finger(model, new Vector3(PalmX, KnuckleY, z), length);
            Thumb(model);
            SetCurl(0);
        }

        void Finger(Transform model, Vector3 at, float length)
        {
            var knuckle = Joint(model, at, 0, -74);
            Part(PrimitiveType.Capsule, knuckle, new Vector3(0, 0.022f * length, 0), new Vector3(0.019f, 0.03f * length, 0.019f), glove);
            var middle = Joint(knuckle, new Vector3(0, 0.043f * length, 0), 0, -82);
            Part(PrimitiveType.Capsule, middle, new Vector3(0, 0.015f * length, 0), new Vector3(0.018f, 0.022f * length, 0.018f), glove);
            var tip = Joint(middle, new Vector3(0, 0.03f * length, 0), 0, -55);
            Part(PrimitiveType.Capsule, tip, new Vector3(0, 0.011f * length, 0), new Vector3(0.017f, 0.017f * length, 0.017f), glove);
        }

        void Thumb(Transform model)
        {
            var root = new GameObject("Thumb").transform;
            root.SetParent(model, false);
            root.localPosition = new Vector3(PalmX + 0.006f, 0.008f, 0.04f);
            root.localRotation = Quaternion.Euler(48, 0, 0);
            var first = Joint(root, Vector3.zero, 0, -38);
            Part(PrimitiveType.Capsule, first, new Vector3(0, 0.02f, 0), new Vector3(0.023f, 0.028f, 0.023f), glove);
            var second = Joint(first, new Vector3(0, 0.04f, 0), 0, -34);
            Part(PrimitiveType.Capsule, second, new Vector3(0, 0.014f, 0), new Vector3(0.02f, 0.021f, 0.02f), glove);
        }

        Transform Joint(Transform parent, Vector3 at, float base_, float bend)
        {
            var t = new GameObject("Joint").transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            joints.Add((t, base_, bend));
            return t;
        }

        void Part(PrimitiveType type, Transform parent, Vector3 at, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = layer;
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = at;
            t.localScale = size;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>0 an open hand, 1 a closed fist.</summary>
        public void SetCurl(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(value, curl)) return;
            curl = value;
            foreach (var (joint, base_, bend) in joints) joint.localRotation = Quaternion.Euler(0, 0, base_ + bend * curl);
        }

        /// <summary>Show that the hand is close enough to take hold of something.</summary>
        public void SetReady(bool on)
        {
            if (on == ready) return;
            ready = on;
            glove.SetColor("_BaseColor", on ? Ready : Glove);
        }

        public void SetVisible(bool on)
        {
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
        }

        public void Dispose()
        {
            if (glove != null) Object.Destroy(glove);
            if (cuff != null) Object.Destroy(cuff);
            if (root != null) Object.Destroy(root.gameObject);
        }
    }
}

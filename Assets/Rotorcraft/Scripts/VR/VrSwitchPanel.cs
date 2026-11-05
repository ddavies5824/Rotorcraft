// The switches in VR: the cockpit models' own toggles are scenery and many are out of reach, so the aircraft's real switches
// are also on this labelled panel, fixed in the cockpit within easy reach of the seat, towards the middle of the aircraft
// like a centre console, and pressed with a fingertip. It only repaints when something on it changes.
using Rotorcraft.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.VR
{
    public sealed class VrSwitchPanel
    {
        public const float Width = 0.215f, Height = 0.129f;
        // From the eye at the seat, in the aircraft's axes: towards the middle of the aircraft, down, forward (m). Near enough to reach
        // without leaning, and low enough to stay out of the way of the view ahead and the instruments.
        public static readonly Vector3 FromEye = new Vector3(0.2f, -0.37f, 0.33f);
        const int TextureW = 1024, TextureH = 614;
        static readonly Color Face = new Color(0.075f, 0.08f, 0.09f), Key = new Color(0.2f, 0.22f, 0.24f), KeyDead = new Color(0.11f, 0.12f, 0.13f);
        static readonly Color Text = new Color(0.9f, 0.92f, 0.93f), LampOff = new Color(0.3f, 0.31f, 0.32f), LampOn = new Color(0.35f, 1f, 0.45f), Over = new Color(0.55f, 0.85f, 1f);

        sealed class Cell
        {
            public VisualElement key, lamp;
            public Label label;
            public string text;
            public bool on, available, over, drawn;
        }

        public readonly Transform root;
        readonly RenderTexture texture;
        readonly PanelSettings settings;
        readonly Material face, backing;
        readonly GameObject host;
        readonly Cell[] cells = new Cell[CockpitSwitches.Columns * CockpitSwitches.Rows];
        readonly VisualElement top;
        bool built;

        public VrSwitchPanel(ThemeStyleSheet theme, int layer)
        {
            root = new GameObject("VR switch panel").transform;
            texture = new RenderTexture(TextureW, TextureH, 24, RenderTextureFormat.ARGB32) { name = "VR switches", useMipMap = true, autoGenerateMips = true, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            texture.Create();
            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "VR switches";
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1;
            settings.clearColor = true;
            settings.colorClearValue = Face;
            settings.targetTexture = texture;
            // kept apart from the panel in the cockpit: a UI document that is switched off loses what is in it
            host = new GameObject("VR switch panel UI");
            host.SetActive(false);
            var doc = host.AddComponent<UIDocument>();
            doc.panelSettings = settings;
            host.SetActive(true);
            top = doc.rootVisualElement;
            top.style.flexGrow = 1;
            top.pickingMode = PickingMode.Ignore;   // it is touched, not pointed at, and must not get in the way of the mouse

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            face = new Material(unlit) { name = "VR switch panel" };
            face.SetTexture("_BaseMap", texture);
            face.mainTexture = texture;
            var quad = new GameObject("Face") { layer = layer };
            quad.transform.SetParent(root, false);
            quad.transform.localScale = new Vector3(Width, Height, 1);
            var mesh = new Mesh { name = "VR switch panel" };
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(0.5f, 0.5f), new Vector3(-0.5f, 0.5f) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            NoShadows(quad.AddComponent<MeshRenderer>()).sharedMaterial = face;

            // a box behind the face, so it reads as a thing in the cockpit rather than a picture in the air
            backing = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "VR switch panel box" };
            backing.SetColor("_BaseColor", new Color(0.05f, 0.055f, 0.06f));
            backing.SetFloat("_Smoothness", 0.25f);
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = "Box";
            Object.Destroy(box.GetComponent<Collider>());
            box.layer = layer;
            box.transform.SetParent(root, false);
            box.transform.localPosition = new Vector3(0, 0, 0.011f);
            box.transform.localScale = new Vector3(Width + 0.012f, Height + 0.012f, 0.02f);
            NoShadows(box.GetComponent<MeshRenderer>()).sharedMaterial = backing;
        }

        static MeshRenderer NoShadows(MeshRenderer r)
        {
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        /// <summary>Fix it in this aircraft, placed from where the pilot's eye is and tilted to face it.</summary>
        public void Mount(Transform aircraft, Vector3 eye)
        {
            root.SetParent(aircraft, false);
            var seat = aircraft.InverseTransformPoint(eye);
            // towards the aircraft's centre line from a side seat; from the middle, to the left
            var at = seat + new Vector3(seat.x < -0.02f ? FromEye.x : -FromEye.x, FromEye.y, FromEye.z);
            root.localPosition = at;
            root.localRotation = Quaternion.LookRotation(at - seat, Vector3.up);
        }

        public void SetVisible(bool on)
        {
            if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
        }

        public bool Visible => root.gameObject.activeSelf;

        void Build()
        {
            built = true;
            for (int i = 0; i < cells.Length; i++)
            {
                var key = new VisualElement();
                key.style.position = Position.Absolute;
                key.style.left = Length.Percent(100f * (i % CockpitSwitches.Columns) / CockpitSwitches.Columns + 0.8f);
                key.style.top = Length.Percent(100f * (i / CockpitSwitches.Columns) / CockpitSwitches.Rows + 1.4f);
                key.style.width = Length.Percent(100f / CockpitSwitches.Columns - 1.6f);
                key.style.height = Length.Percent(100f / CockpitSwitches.Rows - 2.8f);
                key.style.alignItems = Align.Center;
                key.style.justifyContent = Justify.Center;
                key.pickingMode = PickingMode.Ignore;
                key.style.borderTopWidth = key.style.borderBottomWidth = key.style.borderLeftWidth = key.style.borderRightWidth = 7;
                key.style.borderTopLeftRadius = key.style.borderTopRightRadius = key.style.borderBottomLeftRadius = key.style.borderBottomRightRadius = 10;
                var lamp = new VisualElement { pickingMode = PickingMode.Ignore };
                lamp.style.width = Length.Percent(56);
                lamp.style.height = 18;
                lamp.style.marginBottom = 14;
                lamp.style.borderTopLeftRadius = lamp.style.borderTopRightRadius = lamp.style.borderBottomLeftRadius = lamp.style.borderBottomRightRadius = 6;
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.style.fontSize = 35;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.color = Text;
                label.style.whiteSpace = WhiteSpace.NoWrap;
                key.Add(lamp);
                key.Add(label);
                top.Add(key);
                cells[i] = new Cell { key = key, lamp = lamp, label = label };
            }
        }

        /// <summary>Show the switches as they stand, and which ones a fingertip is over (-1 for none).</summary>
        public void Draw(CockpitSwitch[] switches, int overA, int overB)
        {
            if (!built) Build();
            for (int i = 0; i < cells.Length && i < switches.Length; i++)
            {
                var c = cells[i];
                var sw = switches[i];
                var over = sw.available && (i == overA || i == overB);
                if (c.drawn && c.text == sw.label && c.on == sw.on && c.available == sw.available && c.over == over) continue;
                c.drawn = true; c.text = sw.label; c.on = sw.on; c.available = sw.available; c.over = over;
                c.label.text = sw.label;
                c.label.style.color = sw.available ? Text : new Color(Text.r, Text.g, Text.b, 0.3f);
                c.key.style.backgroundColor = sw.available ? Key : KeyDead;
                var edge = over ? Over : sw.available ? Key : KeyDead;
                c.key.style.borderTopColor = c.key.style.borderBottomColor = c.key.style.borderLeftColor = c.key.style.borderRightColor = edge;
                c.lamp.style.backgroundColor = sw.on ? LampOn : LampOff;
                c.lamp.style.display = sw.label.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            }
        }

        public void Dispose()
        {
            if (texture != null) { texture.Release(); Object.Destroy(texture); }
            if (settings != null) Object.Destroy(settings);
            if (face != null) Object.Destroy(face);
            if (backing != null) Object.Destroy(backing);
            if (host != null) Object.Destroy(host);
            if (root != null) Object.Destroy(root.gameObject);
        }
    }
}

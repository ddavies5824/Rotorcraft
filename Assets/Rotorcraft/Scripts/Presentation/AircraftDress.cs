// Paint and kit on an aircraft model. Models built with "liveryTint" bake their painted surfaces as shading
// alone, in materials named "<part> livery1/2/3" (and small painted fittings keep the plain "RC paint" family),
// for the game to colour with the paint scheme; the scheme's lettering is a part of its own (mark_<skin>). The
// role kit (eq_*) and cabin fits (fit_*) are shown or hidden to match the loadout.
using System.Collections.Generic;
using System.Linq;
using Rotorcraft.Sim;
using UnityEngine;

namespace Rotorcraft.Presentation
{
    public static class AircraftDress
    {
        /// <summary>Plain materials with a livery slot, and the shade of it they take (the Blender script's MATS).</summary>
        static readonly Dictionary<string, (int slot, float k)> PAINT = new Dictionary<string, (int, float)>
        {
            ["RC paint"] = (1, 1f), ["RC belly"] = (1, 0.82f), ["RC seam"] = (1, 0.42f), ["RC stripe"] = (2, 1f), ["RC bladetip"] = (2, 1f), ["RC accent"] = (3, 1f), ["RC accentdk"] = (3, 0.62f),
            ["RC chk_a"] = (2, 1f), ["RC chk_b"] = (3, 1f),   // the two colours of the Nightjar's check band
        };

        public static void Apply(Transform model, AircraftConfig ac)
        {
            if (model == null || ac == null) return;
            var rends = model.GetComponentsInChildren<Renderer>(true);
            Unflip(rends);
            if (rends.Any(r => r.sharedMaterials.Any(m => m != null && m.name.Contains(" livery"))))
                foreach (var r in rends)
                {
                    var slots = r.sharedMaterials.Select(Slot).ToArray();
                    if (slots.All(x => x.slot == 0)) continue;
                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                        if (slots[i].slot > 0) Tint(mats[i], Colour(ac.livery, slots[i].slot, slots[i].k));
                    r.materials = mats;
                }

            var all = model.GetComponentsInChildren<Transform>(true);
            void Show(string part, bool on) { foreach (var t in all) if (t.name == part) t.gameObject.SetActive(on); }
            var e = ac.equipment;
            Show("eq_flir", e.flir); Show("eq_searchlight", e.searchlight); Show("eq_hoist", e.hoist); Show("eq_floats", e.floats);
            Show("fit_hems", e.medical); Show("fit_police", e.flir);
            Show("fit_seats", !e.medical && (ac.fitted == null || ac.fitted.Contains("seats")));
            var skin = ac.skinId ?? ac.skins?.FirstOrDefault()?.id;
            foreach (var t in all) if (t.name.StartsWith("mark_")) t.gameObject.SetActive(t.name == "mark_" + skin);
        }

        static readonly string[] TextureSlots = { "baseColorTexture", "metallicRoughnessTexture", "normalTexture", "occlusionTexture", "emissiveTexture" };
        static readonly Vector4 Flipped = new Vector4(1, -1, 0, 1);

        /// <summary>
        /// glTFast flips a texture's rows a second time when another texture entry already uses the same image: the livery
        /// materials share their part's atlas, so they sampled it upside down and showed patches of other parts (the Nightjar's
        /// streaky tail boom, the Peregrine's striped gearbox cover). None of the models has a texture transform of its own, so
        /// a bare vertical flip is that mistake: take it off.
        /// </summary>
        static void Unflip(Renderer[] rends)
        {
            foreach (var r in rends)
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.IsKeywordEnabled("_TEXTURE_TRANSFORM") || !m.HasProperty("baseColorTexture_ST")) continue;
                    if (m.GetVector("baseColorTexture_ST") != Flipped) continue;
                    if (m.HasProperty("baseColorTexture_Rotation") && m.GetVector("baseColorTexture_Rotation") != Vector4.zero) continue;
                    foreach (var t in TextureSlots) if (m.HasProperty(t + "_ST")) m.SetVector(t + "_ST", new Vector4(1, 1, 0, 0));
                    m.DisableKeyword("_TEXTURE_TRANSFORM");
                }
        }

        static (int slot, float k) Slot(Material m)
        {
            if (m == null) return (0, 0);
            var n = m.name.Replace(" (Instance)", "");
            var i = n.IndexOf(" livery");
            if (i >= 0 && i + 7 < n.Length && int.TryParse(n.Substring(i + 7, 1), out var s)) return (s, 1);
            return PAINT.TryGetValue(n, out var p) ? p : (0, 0);
        }

        static Color Colour(Livery l, int slot, float k)
        {
            var c = slot == 1 ? l.body : slot == 2 ? l.stripe : l.tail;
            return new Color((float)c[0] * k, (float)c[1] * k, (float)c[2] * k, 1);
        }

        static void Tint(Material m, Color c)
        {
            if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }
    }
}

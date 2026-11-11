// Scenery coloured per vertex, as the browser game draws its generated meshes (render/shaders.ts, "generic lit"):
// hedges, walls, fences, ground clutter and the modelled houses. The vertex colour is the surface colour (sRGB, so it
// is linearised here) and its alpha says what the surface is, in the browser game's code: below 0.03 a window pane
// (dark reflective glass by day, some rooms lit after dark), otherwise gloss = (a - 0.08) / 0.92. uv0.x picks a
// surface pattern drawn from the world position, in place of the browser game's material textures: 1 drystone, 2
// harling, 3 slate, 4 profiled sheeting, 5 foliage. Both sides of every triangle are drawn (single blades of grass, fence wires). Lit by URP like everything else (sun, shadows, lamps, ambient occlusion, fog).
// Global set by the environment: _RcNight (0 day .. 1 night).
Shader "Rotorcraft/World Colour"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct AttributesD { float3 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct VaryingsD { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
        VaryingsD vertD(AttributesD i)
        {
            VaryingsD o = (VaryingsD)0;
            UNITY_SETUP_INSTANCE_ID(i);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            o.positionCS = TransformObjectToHClip(i.positionOS);
            o.normalWS = TransformObjectToWorldNormal(i.normalOS);
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "WorldColour"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float _RcNight;

            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 w : TEXCOORD0;
                float3 n : TEXCOORD1;
                half4 color : COLOR;
                float pattern : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.w = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.w);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                o.color = i.color;
                o.pattern = i.uv.x;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash21(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
            float Hash31(float3 p) { p = frac(p * 0.1031); p += dot(p, p.zyx + 31.32); return frac((p.x + p.y) * p.z); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p), u = f * f * (3 - 2 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + 1), u.x), u.y);
            }

            // The surface's own pattern, as a brightness factor about 1. u runs along a face (horizontal), v up it.
            float Pattern(int kind, float3 w, float3 n, float near)
            {
                if (kind == 0 || near <= 0) return 1;
                float2 t = normalize(float2(-n.z, n.x) + float2(1e-5, 0));
                bool flat = abs(n.y) > 0.75;
                float u = flat ? w.x : dot(w.xz, t), v = flat ? w.z : w.y;
                float k = 1;
                if (kind == 1)   // drystone: irregular stones laid roughly in courses (cells of a jittered grid, longer than high), dark gaps between
                {
                    float2 p = float2(u / 0.42, v / 0.2), ip = floor(p), fp = frac(p);
                    float d1 = 8, d2 = 8, id = 0;
                    for (int y = -1; y <= 1; y++)
                        for (int x = -1; x <= 1; x++)
                        {
                            float2 c = ip + float2(x, y);
                            float2 r = float2(x, y) + float2(Hash21(c), Hash21(c + 17.3)) * 0.8 + 0.1 - fp;
                            float d = dot(r, r);
                            if (d < d1) { d2 = d1; d1 = d; id = Hash21(c + 5.1); } else if (d < d2) d2 = d;
                        }
                    float gap = smoothstep(0.03, 0.16, sqrt(d2) - sqrt(d1));
                    k = (0.7 + 0.48 * id) * lerp(0.42, 1, gap);
                }
                else if (kind == 2)   // harling: a fine roughcast grain with broad weathering
                    k = 0.9 + 0.12 * Noise(float2(u, v) * 9) + 0.08 * Noise(float2(u, v) * 0.6);
                else if (kind == 3)   // slate: rows parallel to the eaves, slates a little different from each other
                {
                    float row = floor(v / 0.16), s = floor(u / 0.3 + Hash21(float2(row, 1.3)));
                    float fv = frac(v / 0.16);
                    k = (0.86 + 0.24 * Hash21(float2(s, row))) * lerp(0.7, 1, smoothstep(0, 0.12, fv));
                }
                else if (kind == 4)   // profiled sheeting: ribs across the face every 0.2 m
                    k = 0.88 + 0.16 * abs(frac(u / 0.2) * 2 - 1);
                else if (kind == 5)   // foliage: clumps of leaves, lighter and darker
                    k = 0.62 + 0.62 * Noise(float2(u, v) * 2.1) + 0.3 * Noise(float2(u, v) * 7.3) - 0.15;
                return lerp(1, k, near);
            }

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 w = i.w;
                float3 n = normalize(i.n);
                // drawn from both sides, as the browser game does. Seen from behind, the normal is mirrored through the face
                // (not reversed, which the browser game does), so a blade of grass leaning up is lit from above on both sides.
                float3 g = normalize(cross(ddy(i.w), ddx(i.w)));
                g *= sign(dot(g, n) + 1e-5);
                if (!IS_FRONT_VFACE(face, true, false)) n = reflect(n, g);
                float3 toEye = _WorldSpaceCameraPos - w;
                float dist = length(toEye);
                float3 V = toEye / max(dist, 1e-4);
                float m = i.color.a;
                float3 base = SRGBToLinear(i.color.rgb);
                float near = 1 - smoothstep(40, 220, dist);
                int kind = (int)(i.pattern + 0.5);
                base *= Pattern(kind, w, n, near);
                if (kind == 5 && near > 0)
                {
                    // leafy: the clumps lean their own ways, so the light breaks up across the hedge
                    float2 q = (abs(n.y) > 0.75 ? w.xz : float2(w.x + w.z, w.y)) * 1.7;
                    n = normalize(n + float3(Noise(q + 3.1) - 0.5, 0, Noise(q + 7.7) - 0.5) * 0.9 * near);
                }
                // weathering: broad staining and a fine grain, as on the browser game's buildings
                float2 uvw = abs(n.y) > 0.5 ? w.xz : (abs(n.x) > abs(n.z) ? w.zy : w.xy);
                base *= (0.86 + 0.24 * Noise(uvw / 23)) * lerp(1, 0.9 + 0.2 * Noise(uvw / 2.3), 1 - smoothstep(30, 300, dist));

                bool pane = m < 0.03;
                InputData d = (InputData)0;
                d.positionWS = w;
                d.positionCS = i.positionCS;
                d.normalWS = n;
                d.viewDirectionWS = V;
                d.shadowCoord = TransformWorldToShadowCoord(w);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(n);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                SurfaceData s = (SurfaceData)0;
                s.albedo = pane ? float3(0.02, 0.025, 0.03) : base;
                s.smoothness = pane ? 0.9 : saturate((m - 0.08) / 0.92) * 0.85;
                s.metallic = 0;
                s.specular = 0;
                s.occlusion = 1;
                s.alpha = 1;
                half4 col = UniversalFragmentPBR(d, s);
                if (pane)
                {
                    // some rooms lit after dark, each pane its own
                    float3 cell = floor(w * 0.5);
                    float lit = step(0.45, Hash31(cell));
                    col.rgb += float3(1.0, 0.62, 0.3) * (0.6 + 0.8 * Hash31(cell + 7)) * 0.9 * lit * _RcNight;
                }
                col.rgb = MixFog(col.rgb, i.fog);
                return half4(col.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off
            HLSLPROGRAM
            #pragma vertex vertS
            #pragma fragment fragS
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            VaryingsD vertS(AttributesD i)
            {
                VaryingsD o = (VaryingsD)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = TransformObjectToWorld(i.positionOS);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 l = normalize(_LightPosition - p);
            #else
                float3 l = _LightDirection;
            #endif
                float4 c = TransformWorldToHClip(ApplyShadowBias(p, nw, l));
            #if UNITY_REVERSED_Z
                c.z = min(c.z, UNITY_NEAR_CLIP_VALUE);
            #else
                c.z = max(c.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = c;
                return o;
            }
            half4 fragS(VaryingsD i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            Cull Off
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            half4 fragD(VaryingsD i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Off
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragN
            half4 fragN(VaryingsD i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target { return half4(normalize(i.normalWS) * IS_FRONT_VFACE(face, 1, -1), 0); }
            ENDHLSL
        }
    }
}

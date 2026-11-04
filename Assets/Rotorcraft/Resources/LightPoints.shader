// Glowing light points (port of POINTS_VS / POINTS_FS in the browser game's render/shaders.ts). Each light is a
// quad of four vertices at the light's centre; the vertex shader turns it to face the camera at a size in pixels
// (never smaller than a few pixels, however far away), flashes it by its mode and dims it by day and in haze.
//   COLOR      rgb colour (HDR), a = how much of it shows by day
//   TEXCOORD0  corner (-1..1)
//   TEXCOORD1  x size (m), y mode, z period (s), w phase (0..1)
// Globals set by the environment: _RcNight (0 day .. 1 night), _RcFogDensity (1/m), _RcLightExposure.
Shader "Rotorcraft/Light Points"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "LightPoints"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _RcNight;
            float _RcFogDensity;
            float _RcLightExposure;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 corner : TEXCOORD0;
                float4 anim : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 color : COLOR;
                float2 q : TEXCOORD0;
                float streak : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 p = TransformObjectToWorld(i.positionOS);
                float3 toCam = _WorldSpaceCameraPos - p;
                float d = length(toCam);
                p += toCam / max(d, 0.001) * min(0.6, d * 0.1);   // nudge towards the camera so mounted lights are not buried
                float4 c = TransformWorldToHClip(p);

                float mode = i.anim.y, period = max(i.anim.z, 0.01), phase = i.anim.w, T = _Time.y;
                float k = 1;
                if (mode > 0.5 && mode < 1.5) { float t = frac(T / period + phase); k = step(t, 0.04) + step(0.11, t) * step(t, 0.15); }              // double strobe
                else if (mode > 1.5 && mode < 2.5) { float t = frac(T / period + phase); k = pow(max(cos(t * 6.2832), 0.0), 10.0) * 1.6 + 0.04; }  // rotating beacon
                else if (mode > 2.5 && mode < 3.5) { float t = frac(T / period + phase); k = smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(0.05, 0.22, t)) * 1.6 + 0.08; }   // lighthouse
                else if (mode > 3.5 && mode < 4.5) { k = 0.75 + 0.25 * sin(T * 13.0 + phase * 40.0) * sin(T * 7.3 + phase); }                       // flame flicker
                else if (mode > 4.5 && mode < 5.5) { k = step(0.5, frac(T / period + phase)); }                                                        // slow blink

                float vis = lerp(i.color.a, 1.0, _RcNight) * k * exp(-d * _RcFogDensity * 0.45);
                float pxPerRad = _ScreenParams.y * 0.5 * abs(UNITY_MATRIX_P._m11);
                float px = i.anim.x * pxPerRad / max(d, 0.5);
                float size = clamp(px * 3.0, 2.5, 160.0);
                // distant lights shrink below their minimum size: keep their energy by brightening the core
                float boost = clamp(px * 3.0 / 2.5, 0.25, 1.0);
                c.xy += i.corner * size / _ScreenParams.xy * c.w;
                o.positionCS = c;
                o.q = i.corner;
                o.color = i.color.rgb * vis * lerp(1.0, 1.0 / boost, 0.5);
                o.streak = (mode > 0.5 && mode < 1.5) ? k : (mode > 5.5 ? saturate((max(i.color.r, max(i.color.g, i.color.b)) - 1.0) * 0.5) : 0.0);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                if (r2 > 1.0) discard;
                // a hot, nearly white core, the coloured glow round it, a wide faint halo, and spikes on the brightest
                float core = exp(-r2 * 40.0);
                float glow = exp(-r2 * 9.0) * 0.45;
                float halo = exp(-r2 * 3.0) * 0.12;
                float spikes = i.streak * (exp(-abs(i.q.x) * 60.0) + exp(-abs(i.q.y) * 60.0)) * exp(-r2 * 2.5) * 0.6;
                float3 hue = i.color;
                float3 white = max(hue.r, max(hue.g, hue.b)).xxx;
                float3 c = (lerp(hue, white, 0.65) * core * 3.0 + hue * (glow + halo) + lerp(hue, white, 0.4) * spikes) * _RcLightExposure;
                return half4(min(c, 64.0), 1.0);
            }
            ENDHLSL
        }
    }
}

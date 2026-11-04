// The night sky's stars and the moon (port of the stars in SKY_FS in the browser game's render/shaders.ts).
// The mesh holds one quad per star at its direction; the vertex shader puts it just inside the far plane round
// the camera at a fixed size in pixels, so stars stay sharp points; the moon is one larger, softly shaded disc.
// _RcStars fades them all (night, clear sky, out of cloud).
Shader "Rotorcraft/Stars"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Stars"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _RcStars;
            float3 _RcMoonDir;

            struct Attributes { float3 dir : POSITION; float2 corner : TEXCOORD0; float4 info : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };  // info: size (px), brightness, twinkle phase, moon
            struct Varyings { float4 positionCS : SV_POSITION; float2 q : TEXCOORD0; float3 col : TEXCOORD1; float moon : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float moon = i.info.w;
                float3 dir = moon > 0.5 ? normalize(_RcMoonDir) : i.dir;
                float far = _ProjectionParams.z * 0.92;
                float4 c = TransformWorldToHClip(_WorldSpaceCameraPos + dir * far);
                float size = moon > 0.5 ? 0.0095 * _ScreenParams.y * 0.5 * abs(UNITY_MATRIX_P._m11) : i.info.x;
                c.xy += i.corner * size / _ScreenParams.xy * c.w;
                o.positionCS = c;
                o.q = i.corner;
                // stars low in the sky are dimmed by the haze; they twinkle a little
                float alt = saturate(dir.y * 4);
                float tw = 0.8 + 0.2 * sin(_Time.y * (2.3 + i.info.z * 3) + i.info.z * 40);
                float b = i.info.y * alt * (moon > 0.5 ? 1 : tw) * _RcStars;
                float3 tint = lerp(float3(0.75, 0.85, 1.1), float3(1.1, 0.95, 0.8), frac(i.info.z * 7.3));
                o.col = moon > 0.5 ? float3(1.6, 1.6, 1.5) * _RcStars : tint * b;
                o.moon = moon;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.q, i.q);
                if (r2 > 1) discard;
                if (i.moon > 0.5)
                {
                    // a lit disc with soft dark seas and a faint glow round it
                    float disc = smoothstep(0.42, 0.38, sqrt(r2));
                    float2 m = i.q / 0.4;
                    float seas = 0.82 + 0.18 * sin(m.x * 5.1 + 1.3) * sin(m.y * 4.3 + 0.4);
                    float glow = exp(-r2 * 6) * 0.06;
                    return half4(i.col * (disc * seas + glow), 1);
                }
                float k = exp(-r2 * 6);
                return half4(i.col * k, 1);
            }
            ENDHLSL
        }
    }
}

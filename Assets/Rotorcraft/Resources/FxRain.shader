// Rain streaks (port of RAIN_VS / RAIN_FS in the browser game's render/shaders.ts). The mesh holds one thin quad
// per drop at a random point in a unit box; the vertex shader moves each drop with the rain, wraps it into a
// box round the camera, and stretches it along the drop's motion relative to the aircraft, so the streaks
// slant and lengthen with airspeed. Drops too close to the eye are dropped (inside the cockpit).
Shader "Rotorcraft/Rain"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Rain"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float3 _RainVel;     // the rain's own velocity (m/s, world)
            float3 _RainRel;     // its velocity relative to the aircraft (m/s, world)
            float3 _RainBox;     // box size round the camera (m)
            float4 _RainCol;     // rgb, a = opacity
            float _RainAmount;   // fraction of the drops shown
            float _RainNear;     // no drops nearer the eye than this (m)
            float _RainTime;

            struct Attributes { float3 seed : POSITION; float2 end : TEXCOORD0; };   // end: x 0 head / 1 tail, y side -1 / 1
            struct Varyings { float4 positionCS : SV_POSITION; float a : TEXCOORD0; float side : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 cam = _WorldSpaceCameraPos;
                float3 p = i.seed * _RainBox + _RainVel * _RainTime;
                p = cam + (frac((p - cam) / _RainBox) - 0.5) * _RainBox;
                float keep = step(frac(i.seed.x * 57.31 + i.seed.z * 13.7), _RainAmount);
                float d = distance(p, cam);
                keep *= smoothstep(_RainNear, _RainNear + 1.5, d);
                // a streak as long as the drop moves (relative to us) in a fraction of a frame's exposure
                float3 rel = _RainRel;
                float len = clamp(length(rel) * 0.045, 0.25, 3.5);
                float3 dir = rel / max(length(rel), 0.01);
                float3 tail = p - dir * len;
                float3 at = lerp(p, tail, i.end.x);
                float3 side = normalize(cross(dir, at - cam)) * 0.009 * (1 + d * 0.02);
                at += side * i.end.y;
                o.positionCS = TransformWorldToHClip(at);
                o.a = keep * _RainCol.a * (1 - i.end.x * 0.7) * saturate(1 - d / (_RainBox.x * 0.5));
                o.side = i.end.y;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a = i.a * (1 - abs(i.side));
                return half4(_RainCol.rgb, a);
            }
            ENDHLSL
        }
    }
}

// The mission's target marked in the world: a glowing ring laid on the ground and translucent beacons. Unlit and
// never written to the depth buffer. Each point is drawn a little towards the camera so the terrain's simplified
// distant mesh cannot swallow a ring laid on it, the sides of a beacon fade out at their silhouette so it reads as
// a soft column of light, and haze dims it all with distance.
//   COLOR   rgb tint, a opacity
//   NORMAL  up for a flat ring, outwards for the sides of a beacon
// Global set by the environment: _RcFogDensity (1/m).
Shader "Rotorcraft/Mission Marker"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "MissionMarker"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _RcFogDensity;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float3 normalWS : TEXCOORD0;
                float3 toCam : TEXCOORD1;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 p = TransformObjectToWorld(i.positionOS);
                float3 toCam = _WorldSpaceCameraPos - p;
                float d = length(toCam);
                p += toCam / max(d, 0.001) * min(14.0, d * 0.012);
                o.positionCS = TransformWorldToHClip(p);
                o.normalWS = TransformObjectToWorldDir(i.normalOS);
                o.toCam = toCam;
                o.color = i.color;
                o.color.a *= exp(-d * _RcFogDensity * 0.45);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float a = i.color.a;
                float3 n = normalize(i.normalWS);
                if (abs(n.y) < 0.5) a *= pow(saturate(abs(dot(n, normalize(i.toCam)))), 1.4);   // a beacon's side
                return half4(i.color.rgb * 1.4, a);
            }
            ENDHLSL
        }
    }
}

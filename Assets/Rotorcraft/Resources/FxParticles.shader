// Soft round particles for the effects (port of the particle half of POINTS_FS in the browser game's
// render/shaders.ts): dust, spray and smoke blended normally and lit by the sky (_RcParticleLight), flames
// added on top and glowing by themselves. Each puff is broken up by the noise texture so the sprites do not
// read as discs; they fade out close to the camera and into the fog.
Shader "Rotorcraft/Particles"
{
    Properties
    {
        _Noise ("Noise", 2D) = "grey" {}
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst", Float) = 10
        _Emissive ("Emissive (flame)", Float) = 0
        _Breakup ("Noise breakup", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Particles"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            float _Emissive, _Breakup;
            float4 _RcParticleLight;     // sky and sun light on the particles (rgb)
            float _RcLightExposure;

            struct Attributes { float3 positionOS : POSITION; float4 color : COLOR; float4 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 nuv : TEXCOORD1;      // noise coordinates (offset by where the puff is) and fade
                float fog : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float3 w = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(w);
                o.color = i.color;
                o.uv = i.uv.xy;
                float d = distance(w, _WorldSpaceCameraPos);
                float3 c = frac(floor(w * 0.37) * 0.0731);
                o.nuv = float3(i.uv.xy * 0.35 + c.xz + c.y, saturate((d - 0.6) / 2.5));
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 q = i.uv * 2 - 1;
                float r2 = dot(q, q);
                if (r2 > 1) discard;
                float n = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, i.nuv.xy).g;
                float a = saturate(1 - r2) ;
                a *= a;
                a *= lerp(1, saturate(n * 1.8 - 0.2), _Breakup);
                a *= i.color.a * i.nuv.z;
                float3 rgb;
                if (_Emissive > 0.5)
                {
                    // a flame: hottest at the centre of each tongue
                    rgb = i.color.rgb * (1 + (1 - r2) * 1.5) * _RcLightExposure * 1.6;
                    rgb = MixFogColor(rgb, 0, i.fog);
                    return half4(rgb * a, 1);
                }
                rgb = i.color.rgb * _RcParticleLight.rgb;
                rgb = MixFog(rgb, i.fog);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}

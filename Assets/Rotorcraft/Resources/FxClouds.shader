// The cloud layer (port of the cloud material in the browser game's render/shaders.ts): two sheets, at the base
// and the top of the layer, drawn from the same noise field as Weather.CloudDensityAt, so the gaps you see are
// the gaps you can fly through. Lit by the sun and sky from above, darker underneath, drifting with the wind
// and fading into the haze with distance.
Shader "Rotorcraft/Clouds"
{
    Properties
    {
        _Noise ("Noise", 2D) = "grey" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Clouds"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Noise); SAMPLER(sampler_Noise);
            float _CloudCover;       // 0..1
            float2 _CloudDrift;      // noise offset from the wind (u, v)
            float4 _CloudLit;        // colour of the sunlit tops
            float4 _CloudShade;      // colour of the base seen from below
            float _CloudBase, _CloudTop;

            struct Attributes { float3 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.w = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.w);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                // the sim's axes: x east, z south
                float u = i.w.x / 3100 + _CloudDrift.x, v = -i.w.z / 3100 + _CloudDrift.y;
                float c = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u, v)).r * 0.55
                        + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 2.7 + 0.31, v * 2.7 + 0.31)).g * 0.3
                        + SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 8.3 + 0.77, v * 8.3 + 0.77)).b * 0.15;
                float lo = 1 - _CloudCover - 0.08, hi = 1 - _CloudCover + 0.22;
                float s = smoothstep(lo, hi, c);
                float detail = SAMPLE_TEXTURE2D(_Noise, sampler_Noise, float2(u * 31, v * 31)).a;
                bool top = i.w.y > (_CloudBase + _CloudTop) * 0.5;
                bool above = _WorldSpaceCameraPos.y > i.w.y;
                // thick cloud is darker underneath; thin edges let the light through
                float3 col = top || above ? _CloudLit.rgb * (0.86 + 0.14 * detail) : lerp(_CloudLit.rgb, _CloudShade.rgb, saturate(s * 1.3)) * (0.9 + 0.1 * detail);
                float a = s * (0.9 + 0.1 * detail);
                // seen from inside the layer the sheets would cut across the view: the whiteout does that job
                float inside = step(_CloudBase, _WorldSpaceCameraPos.y) * step(_WorldSpaceCameraPos.y, _CloudTop);
                a *= 1 - inside * saturate(1 - distance(i.w, _WorldSpaceCameraPos) / 900);
                col = MixFog(col, i.fog);
                #if defined(FOG_LINEAR) || defined(FOG_EXP) || defined(FOG_EXP2)
                a *= 1 - (1 - ComputeFogIntensity(i.fog)) * 0.85;
                #endif
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}

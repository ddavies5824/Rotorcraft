// The sea (after WATER_FS in the browser game's render/shaders.ts): two wave normal maps, long swell and short
// chop, scrolled downwind at different speeds and angles; the waves calm with distance (so they never shimmer
// into rings), with a broad, slow variation that keeps the far sea from looking flat. Fresnel reflection of the
// sky, the sun's glitter, light scattered in the water, whitecaps on the crests once the wind is up, the main
// light's shadows, the aircraft's lamps, and fog.
Shader "Rotorcraft/Sea"
{
    Properties
    {
        _Swell ("Swell (normal xy, height)", 2D) = "bump" {}
        _Chop ("Chop (normal xy, height)", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        struct AttributesD { float3 positionOS : POSITION; };
        float4 vertD(AttributesD i) : SV_POSITION { return TransformObjectToHClip(i.positionOS); }
        ENDHLSL

        Pass
        {
            Name "Sea"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Swell); SAMPLER(sampler_Swell);
            TEXTURE2D(_Chop); SAMPLER(sampler_Chop);
            float4 _SwellST, _ChopST;   // xy repeats per metre, zw offset
            float _Waves;               // wave strength 0..1 (wind)
            float _Foam;                // whitecaps 0..1 (wind)
            float4 _SeaColor, _DeepColor;

            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 w : TEXCOORD0; float fog : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.w = TransformObjectToWorld(i.positionOS);
                o.positionCS = TransformWorldToHClip(o.w);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 w = i.w;
                float3 toEye = _WorldSpaceCameraPos - w;
                float d = length(toEye);
                float3 V = toEye / d;
                // the sim's axes: x east, z south
                float2 p = float2(w.x, -w.z);
                float4 s = SAMPLE_TEXTURE2D(_Swell, sampler_Swell, p * _SwellST.xy + _SwellST.zw);
                float4 c = SAMPLE_TEXTURE2D(_Chop, sampler_Chop, p * _ChopST.xy + _ChopST.zw);
                float4 big = SAMPLE_TEXTURE2D(_Swell, sampler_Swell, p * _SwellST.xy * 0.13 + _SwellST.zw * 0.3 + 0.37);
                // calm the detail with distance, keep a broad variation
                float near = saturate(1 - d / 1800);
                float mid = saturate(1 - d / 6000);
                float2 g = (s.xy * 2 - 1) * (0.25 + 0.75 * _Waves) * (0.15 + 0.85 * mid)
                         + (c.xy * 2 - 1) * (0.2 + 0.8 * _Waves) * near * 0.8
                         + (big.xy * 2 - 1) * 0.35 * (1 - near);
                float3 N = normalize(float3(g.x, 1.6, -g.y));
                float NdotV = saturate(dot(N, V));

                // the sky in the water
                float F = 0.02 + 0.98 * pow(1 - NdotV, 5);
                float3 R = reflect(-V, N);
                R.y = abs(R.y);
                float3 refl = GlossyEnvironmentReflection(R, 0.06, 1);

                float4 shadowCoord = TransformWorldToShadowCoord(w);
                Light sun = GetMainLight(shadowCoord);
                float atten = sun.shadowAttenuation * sun.distanceAttenuation;
                // light scattered in the water: brighter on the faces of the waves towards the light
                float3 amb = SampleSH(N);
                float3 body = lerp(_DeepColor.rgb, _SeaColor.rgb, saturate(0.4 + s.z * 0.6)) * (amb * 0.9 + sun.color * atten * saturate(dot(N, sun.direction)) * 0.25);
                // the sun's glitter: a sharp core and a broad sheen
                float3 H = normalize(sun.direction + V);
                float nh = saturate(dot(N, H));
                float3 spec = sun.color * atten * (pow(nh, 900) * 60 + pow(nh, 120) * 1.2) * (0.3 + 0.7 * near);

                // whitecaps on the crests
                float crest = s.z * 0.65 + c.z * 0.35;
                float foam = smoothstep(0.86 - 0.12 * _Foam, 0.95, crest) * saturate(_Foam * 1.4) * (0.4 + 0.6 * mid);
                float3 foamCol = (amb + sun.color * atten * saturate(sun.direction.y)) * 0.8;

                float3 col = lerp(body, refl, F) + spec * (1 - foam);
                col = lerp(col, foamCol, foam);

                // the aircraft's lamps and the lights ashore on the water
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                InputData inputData = (InputData)0;
                inputData.positionWS = w;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                LIGHT_LOOP_BEGIN(count)
                    Light l = GetAdditionalLight(lightIndex, w, half4(1, 1, 1, 1));
                    float3 lc = l.color * l.distanceAttenuation * l.shadowAttenuation;
                    float3 hl = normalize(l.direction + V);
                    col += lc * (saturate(dot(N, l.direction)) * _SeaColor.rgb * 0.6 + pow(saturate(dot(N, hl)), 200) * 3);
                LIGHT_LOOP_END
                #endif

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            half4 fragD() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            half4 fragD() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vertD
            #pragma fragment fragD
            half4 fragD() : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
}

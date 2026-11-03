// Inside cloud: a sphere round the camera in the cloud's colour, closing off the sky (which the fog does not
// reach) while the fog greys out everything nearer.
Shader "Rotorcraft/Veil"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Veil"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _VeilColor;

            float4 vert(float3 positionOS : POSITION) : SV_POSITION { return TransformObjectToHClip(positionOS); }
            half4 frag() : SV_Target { return _VeilColor; }
            ENDHLSL
        }
    }
}

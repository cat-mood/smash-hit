Shader "SmashHit/FadingGround"
{
    Properties
    {
        _NearColor ("Near Color", Color) = (0.028, 0.038, 0.055, 1)
        _FarColor ("Far Color", Color) = (0.070, 0.185, 0.260, 1)
        _FadeStart ("Fade Start", Float) = 8
        _FadeEnd ("Fade End", Float) = 94
        _SideVignette ("Side Vignette", Range(0, 1)) = 0.16
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _NearColor;
                half4 _FarColor;
                float _FadeStart;
                float _FadeEnd;
                float _SideVignette;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionHCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionHCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = saturate((input.positionWS.z - _FadeStart) / max(0.001, _FadeEnd - _FadeStart));
                t = t * t * (3.0 - 2.0 * t);
                half4 color = lerp(_NearColor, _FarColor, t);
                float side = saturate(abs(input.positionWS.x) / 118.0);
                color.rgb *= lerp(1.0, 1.0 - _SideVignette, side * side);
                return color;
            }
            ENDHLSL
        }
    }
}
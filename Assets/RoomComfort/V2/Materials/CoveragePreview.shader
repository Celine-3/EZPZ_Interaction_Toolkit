Shader "RoomComfort/CoveragePreview"
{
 Properties { _Tint("Tint", Color) = (0.1,0.65,0.95,0.045) }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
   struct Varyings { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; };
   CBUFFER_START(UnityPerMaterial)
    float4 _Tint; float4 _RoomMin; float4 _RoomMax;
   CBUFFER_END
   Varyings vert(Attributes i) { Varyings o; o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);return o; }
   half4 frag(Varyings i):SV_Target
   {
    clip(i.world-_RoomMin.xyz);clip(_RoomMax.xyz-i.world);
    float edge=pow(1-abs(dot(normalize(i.normal),normalize(GetCameraPositionWS()-i.world))),3);
    return half4(_Tint.rgb,_Tint.a+edge*.16);
   }
   ENDHLSL
  }
 }
}

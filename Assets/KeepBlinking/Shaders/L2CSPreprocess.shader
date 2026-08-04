Shader "Hidden/KeepBlinking/L2CSPreprocess"
{
  Properties
  {
    _MainTex ("Source", 2D) = "white" {}
  }

  SubShader
  {
    Cull Off
    ZWrite Off
    ZTest Always

    Pass
    {
      HLSLPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #include "UnityCG.cginc"

      sampler2D _MainTex;
      float4 _CropRect;
      float _FlipHorizontal;
      float _FlipVertical;
      float _RotationQuarterTurns;

      struct Attributes
      {
        float4 vertex : POSITION;
        float2 uv : TEXCOORD0;
      };

      struct Varyings
      {
        float4 vertex : SV_POSITION;
        float2 uv : TEXCOORD0;
      };

      Varyings vert(Attributes input)
      {
        Varyings output;
        output.vertex = UnityObjectToClipPos(input.vertex);
        output.uv = input.uv;
        return output;
      }

      float2 UndoRotation(float2 coordinate, float quarterTurns)
      {
        if (quarterTurns < 0.5)
          return coordinate;
        if (quarterTurns < 1.5)
          return float2(coordinate.y, 1.0 - coordinate.x);
        if (quarterTurns < 2.5)
          return 1.0 - coordinate;
        return float2(1.0 - coordinate.y, coordinate.x);
      }

      float4 frag(Varyings input) : SV_Target
      {
        // FaceLandmarker coordinates use a top-left origin after flip and rotation.
        float2 oriented = float2(
          lerp(_CropRect.x, _CropRect.z, input.uv.x),
          lerp(_CropRect.y, _CropRect.w, 1.0 - input.uv.y));
        float2 preRotation = UndoRotation(oriented, _RotationQuarterTurns);
        if (_FlipHorizontal > 0.5)
          preRotation.x = 1.0 - preRotation.x;
        if (_FlipVertical > 0.5)
          preRotation.y = 1.0 - preRotation.y;

        float2 sourceUv = float2(preRotation.x, 1.0 - preRotation.y);
        float3 rgb = tex2D(_MainTex, saturate(sourceUv)).rgb;
        float3 mean = float3(0.485, 0.456, 0.406);
        float3 standardDeviation = float3(0.229, 0.224, 0.225);
        return float4((rgb - mean) / standardDeviation, 1.0);
      }
      ENDHLSL
    }
  }
}

Shader "KeepBlinking/DistanceBackgroundFeedback"
{
  Properties
  {
    [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
    _Color ("Tint", Color) = (1,1,1,1)
    _NearAmount ("Near Amount", Range(0,1)) = 0
    _BarrelStrength ("Barrel Strength", Range(0,0.3)) = 0
    _GridBend ("Grid Bend", Range(0,0.24)) = 0
    _VignetteStrength ("Vignette", Range(0,0.4)) = 0
    _EdgeBlurPixels ("Edge Blur Pixels", Range(0,4)) = 0
    _EdgeDesaturation ("Edge Desaturation", Range(0,0.2)) = 0
    [MaterialToggle] PixelSnap ("Pixel snap", Float) = 0
  }

  SubShader
  {
    Tags
    {
      "Queue"="Background"
      "IgnoreProjector"="True"
      "RenderType"="Transparent"
      "PreviewType"="Plane"
      "CanUseSpriteAtlas"="True"
    }

    Cull Off
    Lighting Off
    ZWrite Off
    Blend SrcAlpha OneMinusSrcAlpha

    Pass
    {
      CGPROGRAM
      #pragma vertex vert
      #pragma fragment frag
      #pragma target 2.0
      #pragma multi_compile _ PIXELSNAP_ON
      #include "UnityCG.cginc"

      struct appdata_t
      {
        float4 vertex : POSITION;
        float4 color : COLOR;
        float2 texcoord : TEXCOORD0;
      };

      struct v2f
      {
        float4 vertex : SV_POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
      };

      sampler2D _MainTex;
      fixed4 _Color;
      float _NearAmount;
      float _BarrelStrength;
      float _GridBend;
      float _VignetteStrength;
      float _EdgeBlurPixels;
      float _EdgeDesaturation;

      v2f vert(appdata_t input)
      {
        v2f output;
        output.vertex = UnityObjectToClipPos(input.vertex);
        output.texcoord = input.texcoord;
        output.color = input.color * _Color;
        #ifdef PIXELSNAP_ON
        output.vertex = UnityPixelSnap(output.vertex);
        #endif
        return output;
      }

      fixed4 frag(v2f input) : SV_Target
      {
        float2 centered = input.texcoord - 0.5;
        float radiusSquared = dot(centered, centered) * 4.0;
        float radialScale = 1.0 + min(_BarrelStrength, 0.3) * radiusSquared;
        float2 warped = 0.5 + centered * radialScale;

        float bend = min(_GridBend, 0.24);
        warped.x += centered.x * abs(centered.y) * centered.y * bend;
        warped.y += centered.y * abs(centered.x) * centered.x * bend;
        warped = clamp(warped, 0.003, 0.997);

        float edge = smoothstep(0.28, 1.65, radiusSquared);
        float2 blurStep = min(_EdgeBlurPixels, 4.0) * edge / max(_ScreenParams.xy, float2(1.0, 1.0));
        fixed4 color = tex2D(_MainTex, warped) * 0.44;
        color += tex2D(_MainTex, clamp(warped + float2(blurStep.x, 0.0), 0.003, 0.997)) * 0.14;
        color += tex2D(_MainTex, clamp(warped - float2(blurStep.x, 0.0), 0.003, 0.997)) * 0.14;
        color += tex2D(_MainTex, clamp(warped + float2(0.0, blurStep.y), 0.003, 0.997)) * 0.14;
        color += tex2D(_MainTex, clamp(warped - float2(0.0, blurStep.y), 0.003, 0.997)) * 0.14;

        float edgeDesaturation = min(_EdgeDesaturation, 0.2) * edge;
        float luminance = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
        color.rgb = lerp(color.rgb, luminance.xxx, edgeDesaturation);
        float vignette = 1.0 - min(_VignetteStrength, 0.4) * smoothstep(0.3, 1.55, radiusSquared);
        color.rgb *= vignette;
        color *= input.color;
        return color;
      }
      ENDCG
    }
  }

  Fallback "Sprites/Default"
}

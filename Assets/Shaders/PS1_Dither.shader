Shader "Custom/PS1_Dither"
{
  Properties {
    _MainTex ("Base Texture", 2D) = "white" {}
    _ColorTint ("Tint", Color) = (1,1,1,1)
    _ColorSteps ("Color Steps", Range(2,16)) = 6
    _DitherIntensity ("Dither Intensity", Range(0,1)) = 1.0
    _PixelSize ("Pixelate Size (px, 0 = off)", Float) = 0
  }
  SubShader {
    Tags { "RenderType"="Opaque" "Queue"="Geometry" }
    LOD 200

    CGPROGRAM
    // Surface shader with full shadow support
    #pragma surface surf Standard fullforwardshadows

    #include "UnityCG.cginc"

    sampler2D _MainTex;
    //float4 _MainTex_ST;
    float4 _ColorTint;
    float _ColorSteps;
    float _DitherIntensity;
    float _PixelSize;

    struct Input {
      float2 uv_MainTex;
      float4 screenPos; // needed for per-pixel dithering
    };

    // --- 4x4 Bayer Matrix as global constants (Correction!) ---
    // We use int4 to represent each row.
    // Values are *not* normalized (0..15).
    const static int4 M_Row0 = int4(0,  8,  2, 10);
    const static int4 M_Row1 = int4(12, 4, 14, 6);
    const static int4 M_Row2 = int4(3, 11, 1, 9);
    const static int4 M_Row3 = int4(15, 7, 13, 5);

    // Bayer 4x4 matrix (values 0..15 normalized to 0..1)
    float Bayer4x4(int x, int y) {
      int val;
      // We select the row and then the element
      if (y == 0) val = M_Row0[x];
      else if (y == 1) val = M_Row1[x];
      else if (y == 2) val = M_Row2[x];
      else val = M_Row3[x]; // y == 3

      return (float)val / 16.0;
    }
    // -------------------------------------------------------------------


    // get Bayer threshold for a pixel coordinate
    float BayerThreshold(float2 pixelCoord) {
      // In Cg, fmod with float to float may not be optimal.
      // Better to use the integer version of the pixel coordinate
      // and only use the % operator if we are sure it is compatible.
      // Since you are using 'int ix = (int)fmod(...)' and 'pixelCoord' is a floor
      // of a positive screen coordinate, `fmod` is fine, or
      // simply use `%` on the integer version of the coordinate:

      int ix_i = (int)pixelCoord.x;
      int iy_i = (int)pixelCoord.y;

      // ensure 0..3 (fmod already gives 0..3 if the input is positive,
      // but using % 4 is more robust if pixelCoord were negative,
      // although floor already makes it an integer here)
      int ix = ix_i % 4;
      int iy = iy_i % 4;

      // if x or y were negative, which doesn't happen in screen coordinates,
      // we would make the correction: ix = (ix_i % 4 + 4) % 4;

      return Bayer4x4(ix, iy);
    }

    // optional pixelate: compute uv block
    float2 PixelateUV(float2 uv, float2 screenPosN) {
      if (_PixelSize <= 0.5) return uv;
      // screenPosN in 0..1
      float2 pixelCoord = screenPosN * _ScreenParams.xy; // requires Unity's _ScreenParams
      float2 block = floor(pixelCoord / _PixelSize) * _PixelSize;
      float2 uvPix = block / _ScreenParams.xy;
      return uvPix;
    }

    void surf (Input IN, inout SurfaceOutputStandard o) {
      // normalized screen UV 0..1
      float2 screenUV = (IN.screenPos.xy / IN.screenPos.w) * 0.5 + 0.5;

      // optional pixelate on texture UV
      float2 uv = IN.uv_MainTex;
      uv = PixelateUV(uv, screenUV);

      // base sampling
      fixed4 tex = tex2D(_MainTex, uv) * _ColorTint;
      float3 col = tex.rgb;

      // --- DITHER + QUANTIZE ---
      // calculate integer pixel coordinate
      float2 pixelCoord = screenUV * _ScreenParams.xy;
      float2 pixelFloor = floor(pixelCoord);

      // Bayer 4x4 threshold
      float bThreshold = BayerThreshold(pixelFloor);

      // scale threshold by intensity (0..1)
      float dither = lerp(0.0, bThreshold, _DitherIntensity);

      // quantize per channel
      float steps = max(1.0, _ColorSteps);
      // add dither before floor to shift quantization
      float3 q = floor(col * steps + dither) / steps;
      q = saturate(q);

      // assign quantized color as Albedo (Unity will handle lighting)
      o.Albedo = q;
      o.Metallic = 0.0;
      o.Smoothness = 0.2;
      o.Alpha = tex.a;
    }
    ENDCG
  }
  FallBack "Diffuse"
}

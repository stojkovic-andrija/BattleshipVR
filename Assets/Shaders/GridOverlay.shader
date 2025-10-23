Shader "Hidden/BSVR/GridOverlay"
{
    Properties
    {
        _GridScale("Grid Scale (Cells X,Y)", Vector) = (10,10,0,0)
        _GridColor("Grid Line Color", Color) = (0.85,0.9,1,0.6)
        _LineWidth("Line Width (cell fraction)", Range(0.001,0.1)) = 0.02

        _GoodColor("Good Fill Color", Color) = (0.25,1,0.5,1)
        _BadColor("Bad Fill Color",  Color)  = (1,0.25,0.25,1)
        _GoodAlpha("Good Min Alpha", Range(0,1)) = 0.35
        _BadAlpha("Bad Min Alpha",  Range(0,1)) = 0.50
        _GoodFlashHz("Good Flash Hz", Float) = 1.0
        _BadFlashHz("Bad Flash Hz",  Float) = 3.3333

        [NoScaleOffset]_MarkTex("Mark Texture", 2D) = "black" {} // NEW
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "GridOverlay"
            Tags { "LightMode"="UniversalForward" }
            ZWrite Off
            ZTest [_ZTest]
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _GridScale;
            float4 _GridColor;
            float  _LineWidth;

            float4 _GoodColor, _BadColor;
            float  _GoodAlpha, _BadAlpha, _GoodFlashHz, _BadFlashHz;

            float4 _BoardMin;
            float4 _BoardSize;

            int    _GreenCount, _RedCount;
            float  _GreenIdx[16];
            float  _RedIdx[16];

            TEXTURE2D(_MarkTex);                     // NEW
            SAMPLER(sampler_MarkTex);                // NEW

            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings   { float4 positionCS:SV_POSITION; float3 posOS:TEXCOORD0; float2 uv:TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posOS = v.positionOS.xyz;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(ws);
                o.uv = v.uv;
                return o;
            }

            float LineAA(float d, float width)
            {
                float fw = fwidth(d) * 1.5;
                return 1.0 - smoothstep(width - fw, width + fw, d);
            }

            bool Listed(int idx, float arr[16], int count)
            {
                [unroll(16)]
                for (int k = 0; k < 16; k++)
                {
                    if (k >= count) break;
                    if ((int)round(arr[k]) == idx) return true;
                }
                return false;
            }

            half4 frag(Varyings i) : SV_Target
            {
                int Nx = max(1, (int)_GridScale.x);
                int Ny = max(1, (int)_GridScale.y);

                float2 uv;
                bool useBounds = (_BoardSize.x > 1e-6) && (_BoardSize.z > 1e-6);
                if (useBounds)
                {
                    uv.x = saturate((i.posOS.x - _BoardMin.x) / _BoardSize.x);
                    uv.y = saturate((i.posOS.z - _BoardMin.z) / _BoardSize.z);
                }
                else
                {
                    uv = i.uv;
                }

                float2 uvCells = uv * float2(Nx, Ny);
                float2 fracUV  = frac(uvCells);
                int cx = (int)floor(uvCells.x);
                int cy = (int)floor(uvCells.y);
                if (cx < 0 || cy < 0 || cx >= Nx || cy >= Ny) return half4(0,0,0,0);

                int idx = cy * Nx + cx;

                float dToEdge = min(min(fracUV.x, 1.0 - fracUV.x), min(fracUV.y, 1.0 - fracUV.y));
                float lineA   = LineAA(dToEdge, _LineWidth);

                // --- Placement preview (greens/reds) ---
                bool isRed   = Listed(idx, _RedIdx, _RedCount);
                bool isGreen = (!isRed) && Listed(idx, _GreenIdx, _GreenCount);

                float t = _Time.y;
                float goodK = 0.5 + 0.5 * sin(6.2831853 * _GoodFlashHz * t);
                float badK  = 0.5 + 0.5 * sin(6.2831853 * _BadFlashHz  * t);

                float4 fillCol = 0;
                float  fillA   = 0;
                if (isRed)   { fillCol = _BadColor;  fillA = lerp(_BadAlpha,  saturate(_BadColor.a),  badK); }
                if (isGreen) { fillCol = _GoodColor; fillA = lerp(_GoodAlpha, saturate(_GoodColor.a), goodK); }

                // --- Shot marks overlay from _MarkTex (white miss, red hit) ---
                // Sample at continuous UV so texel per cell maps naturally.
                float4 mark = SAMPLE_TEXTURE2D(_MarkTex, sampler_MarkTex, uv);
                // If alpha > 0, override the preview fill with the mark color.
                if (mark.a > 0.001)
                {
                    fillCol = mark;
                    fillA   = mark.a;
                }

                float4 lineCol = _GridColor;
                float  outA    = saturate(fillA + lineA * lineCol.a * (1.0 - fillA));
                float3 outRGB  = (fillCol.rgb * fillA) + (lineCol.rgb * (lineA * lineCol.a) * (1.0 - fillA));
                return half4(outRGB, outA);
            }
            ENDHLSL
        }
    }
    FallBack Off
}

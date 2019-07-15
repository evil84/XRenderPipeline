Shader "MyPipeline/Lit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color("Color", Color) = (1, 1, 1, 1)
    }
    
    SubShader
    {
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "ForwardLit"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment LitPassFrag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include "Lit.hlsl" 
            ENDHLSL
        }
    }
}

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
            Name "DepthOnly"
            Tags {"LightMode" = "DepthOnly"}
            ZWrite On
            ColorMask 0
            
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFrag
            #include "Lit.hlsl" 
            ENDHLSL
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "ForwardLit"}
            ZWrite On
            //ZTest Equal
            ColorMask RBG
            
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

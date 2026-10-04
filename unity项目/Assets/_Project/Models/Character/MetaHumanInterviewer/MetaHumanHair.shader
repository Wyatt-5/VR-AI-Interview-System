Shader "MetaHuman/DoubleSidedHair" {
 Properties {
  _Color("Hair Color",Color)=(0.016,0.008,0.004,1)
  _MainTex("Albedo / Coverage",2D)="white" {}
  _Cutoff("Coverage Cutoff",Range(0,1))=0.33
  _Glossiness("Smoothness",Range(0,1))=0.35
 }
 SubShader {
  Tags {"Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True"}
  Cull Off
  CGPROGRAM
  #pragma surface surf Standard alphatest:_Cutoff fullforwardshadows addshadow
  #pragma target 3.0
  sampler2D _MainTex; fixed4 _Color; half _Glossiness;
  struct Input { float2 uv_MainTex; float facing:VFACE; };
  void surf(Input IN,inout SurfaceOutputStandard o) {
   fixed4 c=tex2D(_MainTex,IN.uv_MainTex)*_Color;
   o.Albedo=c.rgb;o.Alpha=c.a;o.Metallic=0;o.Smoothness=_Glossiness;
   o.Normal=float3(0,0,IN.facing>=0?1:-1);
  }
  ENDCG
 }
 Fallback "Transparent/Cutout/Diffuse"
}

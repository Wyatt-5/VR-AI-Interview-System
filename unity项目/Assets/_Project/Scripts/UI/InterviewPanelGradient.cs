using UnityEngine;
using UnityEngine.UI;
[RequireComponent(typeof(CanvasRenderer))]
public class InterviewPanelGradient : MaskableGraphic {
 protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();var r=rectTransform.rect;const int nx=32,ny=18;for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++){float u=(float)x/nx,v=(float)y/ny;var c=Color.Lerp(new Color(.145f,.205f,.23f),new Color(.105f,.15f,.25f),u*(1-v));float edge=Mathf.Min(Mathf.Min(u,1-u)*r.width,Mathf.Min(v,1-v)*r.height);c+=new Color(.12f,.14f,.15f,0)*Mathf.Exp(-edge/14);c.a=1;vh.AddVert(new Vector2(r.x+u*r.width,r.y+v*r.height),c,Vector2.zero);}for(int y=0;y<ny;y++)for(int x=0;x<nx;x++){int n=y*(nx+1)+x;vh.AddTriangle(n,n+nx+1,n+1);vh.AddTriangle(n+1,n+nx+1,n+nx+2);}}
}


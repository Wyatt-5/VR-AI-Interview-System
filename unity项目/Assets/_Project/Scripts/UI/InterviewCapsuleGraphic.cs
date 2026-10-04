using UnityEngine;
using UnityEngine.UI;
[RequireComponent(typeof(CanvasRenderer))]
public class InterviewCapsuleGraphic : MaskableGraphic {
 protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();var r=rectTransform.rect;float rad=r.height*.5f;vh.AddVert(r.center,color,Vector2.zero);const int count=80;for(int i=0;i<=count;i++){float a=2*Mathf.PI*i/count;float cx=Mathf.Cos(a)>=0?r.xMax-rad:r.xMin+rad;var p=new Vector2(cx+Mathf.Cos(a)*rad,r.center.y+Mathf.Sin(a)*rad);var c=color;float k=(p.y-r.yMin)/r.height;c.r*=Mathf.Lerp(1.12f,.86f,k);c.g*=Mathf.Lerp(1.12f,.86f,k);c.b*=Mathf.Lerp(1.12f,.86f,k);vh.AddVert(p,c,Vector2.zero);if(i>0)vh.AddTriangle(0,i,i+1);}}
}

using UnityEngine;
using UnityEngine.UI;
// 四个扇区显示四项分数的相对占比；数值变化时才更新顶点，不使用实时模糊。
[RequireComponent(typeof(CanvasRenderer))]
public class InterviewScoreRing : MaskableGraphic {
 public float[] scores={86,82,91,89};
 public void SetScores(float a,float b,float c,float d){if(scores[0]==a&&scores[1]==b&&scores[2]==c&&scores[3]==d)return;scores=new[]{a,b,c,d};SetVerticesDirty();}
 protected override void OnPopulateMesh(VertexHelper vh){
  vh.Clear();var r=rectTransform.rect;float outer=Mathf.Min(r.width,r.height)*.5f,inner=outer*.45f,total=0;foreach(float s in scores)total+=s;
  Color32[] colors={new Color32(75,136,152,255),new Color32(110,163,178,255),new Color32(122,177,191,255),new Color32(113,150,161,255)};
  float angle=26;for(int k=0;k<4;k++){float sweep=360*(total>0?scores[k]/total:.25f);int steps=Mathf.Max(1,Mathf.CeilToInt(sweep/3));for(int j=0;j<steps;j++){
   float a=(angle+sweep*j/steps)*Mathf.Deg2Rad,b=(angle+sweep*(j+1)/steps)*Mathf.Deg2Rad;int n=vh.currentVertCount;
   vh.AddVert(r.center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*inner,colors[k],Vector2.zero);vh.AddVert(r.center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*outer,colors[k],Vector2.zero);vh.AddVert(r.center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*outer,colors[k],Vector2.zero);vh.AddVert(r.center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*inner,colors[k],Vector2.zero);vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
  }angle+=sweep;}
 }
}


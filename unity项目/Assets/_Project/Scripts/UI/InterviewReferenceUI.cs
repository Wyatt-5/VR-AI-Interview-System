using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 只负责界面展示，问题生成、语音录制和评分仍由原来的 Manager 处理。
public class InterviewReferenceUI : MonoBehaviour {
 public InterviewManager manager;
 public GameObject welcomeDecor;
 public CanvasGroup questionGroup;
 public TMP_Text positionLabel, notice, historyText;
 public GameObject historyPanel;
 public InterviewScoreRing ring;
 public ScrollRect highlightsScroll, suggestionsScroll;
 bool resultWasVisible;
 string lastHighlight, lastSuggestion;
 [Serializable] public class Entry { public string date,position,overall,language,reaction,professional,demeanor,highlights,suggestions; }
 [Serializable] public class Archive { public List<Entry> items=new List<Entry>(); }
 Archive archive=new Archive();
 string ArchivePath=>Path.Combine(Application.persistentDataPath,"interview-ui-history.json");
 void Awake(){
  if(!manager)manager=FindObjectOfType<InterviewManager>(true);
  try{if(File.Exists(ArchivePath))archive=JsonUtility.FromJson<Archive>(File.ReadAllText(ArchivePath))??new Archive();}catch(Exception){archive=new Archive();}
 }
 void LateUpdate(){Refresh();}
 public void Refresh(){
  if(!manager)manager=FindObjectOfType<InterviewManager>(true);
  if(!manager||!manager.targetPositionInput||!manager.resultUI)return;
  bool result=manager.resultUI.activeSelf;
  bool welcome=manager.targetPositionInput.gameObject.activeSelf&&!result;
  if(welcomeDecor)welcomeDecor.SetActive(welcome);
  if(questionGroup){questionGroup.alpha=welcome||result?0:1;questionGroup.blocksRaycasts=false;}
  // 保留状态提示（包括录音与错误提示），只隐藏普通欢迎提示。
  if(manager.statusText){var c=manager.statusText.GetComponent<CanvasGroup>();if(c)c.alpha=welcome&&manager.statusText.text=="请输入目标岗位，然后点击“开始面试”。"?0:1;}
  if(result){
   if(positionLabel)positionLabel.text=string.IsNullOrWhiteSpace(manager.targetPositionInput.text)?"未指定岗位":manager.targetPositionInput.text;
   if(ring)ring.SetScores(Parse(manager.languageScoreText),Parse(manager.reactionScoreText),Parse(manager.professionalScoreText),Parse(manager.demeanorScoreText));
   ResizeText(manager.highlightsText,highlightsScroll,ref lastHighlight);ResizeText(manager.suggestionsText,suggestionsScroll,ref lastSuggestion);
   if(!resultWasVisible&&Application.isPlaying)SaveCurrent();
  }
  resultWasVisible=result;
 }
 static float Parse(TMP_Text t){float n;return float.TryParse(t.text,out n)?Mathf.Clamp(n,0,100):0;}
 static void ResizeText(TMP_Text text,ScrollRect scroll,ref string previous){
  if(!text||!scroll||previous==text.text)return;previous=text.text;
  float height=Mathf.Max(scroll.viewport.rect.height,text.GetPreferredValues(text.text,scroll.viewport.rect.width,0).y+10);
  text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,height);scroll.verticalNormalizedPosition=1;
 }
 Entry Current()=>new Entry{date=DateTime.Now.ToString("yyyy-MM-dd HH:mm"),position=positionLabel?positionLabel.text:"未指定岗位",overall=manager.overallScoreText.text,language=manager.languageScoreText.text,reaction=manager.reactionScoreText.text,professional=manager.professionalScoreText.text,demeanor=manager.demeanorScoreText.text,highlights=manager.highlightsText.text,suggestions=manager.suggestionsText.text};
 void SaveCurrent(){try{archive.items.Insert(0,Current());if(archive.items.Count>30)archive.items.RemoveRange(30,archive.items.Count-30);File.WriteAllText(ArchivePath,JsonUtility.ToJson(archive,true));}catch(Exception){ShowNotice("历史记录未能保存，请检查本地存储空间。");}}
 public void ShowHistory(){
  if(!historyPanel||!historyText){ShowNotice("历史记录界面尚未配置。");return;}
  historyPanel.SetActive(true);var b=new StringBuilder();
  foreach(var e in archive.items)b.AppendLine(e.date+"    "+e.position+"    综合得分 "+e.overall+" /100\n");
  historyText.text=b.Length==0?"暂无历史记录。完成一次面试后，结果会保存在本机。":b.ToString();
  historyText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,Mathf.Max(540,historyText.GetPreferredValues(historyText.text,1240,0).y+20));
 }
 public void CloseHistory(){if(historyPanel)historyPanel.SetActive(false);}
 public void PrintReport(){
  // 点击后生成本地 HTML 报告，由浏览器的打印功能选择打印机或另存 PDF；不自动发送打印任务。
  try{var e=Current();string path=Path.Combine(Application.persistentDataPath,"interview-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".html");
   var b=new StringBuilder("<!doctype html><html lang='zh-CN'><meta charset='utf-8'><title>面试结果报告</title><style>body{font:18px sans-serif;max-width:850px;margin:48px auto;color:#23333c}h1{font-size:36px}pre{white-space:pre-wrap;font:inherit;line-height:1.8}button{padding:12px}@media print{button{display:none}}</style><h1>面试结果报告</h1>");
   b.Append("<p>"+Escape(e.date)+" · "+Escape(e.position)+"</p><h2>综合得分 "+Escape(e.overall)+" /100</h2><p>语言表达 "+Escape(e.language)+"　临场反应 "+Escape(e.reaction)+"　专业匹配度 "+Escape(e.professional)+"　仪态表现 "+Escape(e.demeanor)+"</p><h2>表现亮点</h2><pre>"+Escape(e.highlights)+"</pre><h2>改进建议</h2><pre>"+Escape(e.suggestions)+"</pre><button onclick='window.print()'>打印 / 保存 PDF</button></html>");
   File.WriteAllText(path,b.ToString(),Encoding.UTF8);Application.OpenURL(new Uri(path).AbsoluteUri);
  }catch(Exception){ShowNotice("报告生成失败，请检查本地存储权限。");}
 }
 static string Escape(string s)=>(s??"").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");
 void ShowNotice(string text){if(notice){notice.text=text;notice.gameObject.SetActive(true);}else Debug.LogWarning(text,this);}
}

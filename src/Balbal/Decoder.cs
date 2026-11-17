using System.Runtime.InteropServices;
using System.Globalization;
namespace Balbal;
/// <summary>Same native CTC search, token bonuses, KenLM and SentencePiece as upstream. Use one instance per thread.</summary>
public sealed class Decoder : IDisposable
{
 readonly DecoderHandle handle; readonly DecoderSettings settings; readonly string[] pieces; readonly string language;
 public int VocabularySize=>pieces.Length;
 public Decoder(string lmDirectory,DecoderSettings settings,string language="tr")
 {
  settings.Validate(); this.settings=settings;this.language=language;
  var tokenizer=Path.Combine(lmDirectory,"tokenizer.model"); var domain=settings.DomainLm;
  if(domain is not null && settings.DomainWeight>0 && !File.ReadAllBytes(tokenizer).SequenceEqual(File.ReadAllBytes(Path.Combine(domain,"tokenizer.model")))) throw new ArgumentException("Domain LM uses another tokenizer");
  handle=Native.bb_create(tokenizer,Path.Combine(lmDirectory,"model.binary"),domain is null?"":Path.Combine(domain,"model.binary"),settings.DomainWeight,settings.Alpha,settings.Beta);
  if(handle.IsInvalid){handle.Dispose();throw Native.Error();}
  pieces=Enumerable.Range(0,Native.bb_vocab(handle)).Select(i=>Marshal.PtrToStringUTF8(Native.bb_piece(handle,i))!).ToArray();
 }
 string DecodeText(IEnumerable<int> tokens){var ids=tokens.ToArray();var p=Native.bb_text(handle,ids,ids.Length);if(p==IntPtr.Zero)throw Native.Error();return Marshal.PtrToStringUTF8(p)!;}
 (int[] Tokens,float[] Bonuses,int[] Lengths) Hotwords(IEnumerable<string>? words)
 {
  var dict=pieces.Select((p,i)=>(p,i)).ToDictionary(x=>x.p,x=>x.i); var sequences=new Dictionary<string,int[]>();
  var culture=CultureInfo.GetCultureInfo(language is "tr" or "az"?"tr-TR":"en-US");
  foreach(var word in (words??[]).SelectMany(x=>x.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries))) {
   string lower=word.ToLower(culture),capital=lower.Length==0?lower:lower[..1].ToUpper(culture)+lower[1..];
   foreach(var variant in new[]{word,lower,capital}.Distinct()) {
    string text="▁"+variant; var ends=new bool[text.Length+1]; ends[^1]=true;
    for(int i=text.Length-1;i>=0;i--) for(int j=i+1;j<=text.Length;j++) if(ends[j]&&dict.ContainsKey(text[i..j])){ends[i]=true;break;}
    int found=0; List<int> path=[];
    void Walk(int i){if(found>=64)return;if(i==text.Length){var seq=path.ToArray();sequences[string.Join(',',seq)]=seq;found++;return;}
     for(int j=text.Length;j>i;j--) if(ends[j]&&dict.TryGetValue(text[i..j],out int id)){path.Add(id);Walk(j);path.RemoveAt(path.Count-1);}}
    Walk(0);
   }
  }
  var seqs=sequences.Values.OrderBy(s=>s,Comparer<int[]>.Create((a,b)=>{for(int i=0;i<Math.Min(a.Length,b.Length);i++)if(a[i]!=b[i])return a[i].CompareTo(b[i]);return a.Length.CompareTo(b.Length);})).ToArray();
  var flat=seqs.SelectMany(s=>s).ToArray();double scale=settings.Alpha==0?1:settings.Alpha;
  return(flat,flat.Select(t=>(float)(settings.HotwordBonus*pieces[t].EnumerateRunes().Count()/scale)).ToArray(),seqs.Select(s=>s.Length).ToArray());
 }
 public (string Text,IReadOnlyList<Word> Words) Decode(Emissions input,IEnumerable<string>? hotwords=null,double offset=0,double frameSeconds=.04)
 {
  ObjectDisposedException.ThrowIf(handle.IsClosed,this); if(input.Classes!=pieces.Length+1)throw new ArgumentException("Model/tokenizer vocabulary mismatch");
  var(a,kept)=Emissions.Prepare(input,settings);var hot=Hotwords(hotwords);var path=new int[a.Frames+2];
  int count=Native.bb_decode(handle,a.Data,a.Frames,a.Classes,settings.Beam,settings.TopTokens,settings.Threshold,settings.Alpha,settings.Beta,hot.Tokens,hot.Bonuses,hot.Lengths,hot.Lengths.Length,path,path.Length);
  if(count<0)throw Native.Error();if(count==0)throw new InvalidOperationException("Beam search returned no hypothesis");
  List<(int Id,int Start,int End,double Confidence)> spans=[];int previous=-1;
  for(int i=0;i<count;i++){int t=path[i],f=i-1;if(t>=0&&t<pieces.Length){if(f<0||f>=a.Frames)throw new InvalidOperationException("Invalid native path");
   double confidence=Math.Exp(a.Data[f*a.Classes+t]);if(t!=previous)spans.Add((t,f,f,confidence));else {var s=spans[^1];spans[^1]=(t,s.Start,f,Math.Max(s.Confidence,confidence));}}previous=t;}
  spans=spans.Select(s=>(s.Id,kept[s.Start],kept[s.End],s.Confidence)).ToList();
  return(DecodeText(spans.Select(s=>s.Id)),Words(spans,offset,frameSeconds));
 }
 sealed class Raw { public string Text="";public int Start,End;public double Confidence; }
 IReadOnlyList<Word> Words(List<(int Id,int Start,int End,double Confidence)> spans,double offset,double frame)
 {
  const string punct=".,?!:;\"'“”‘’()-…«»„‚‹›¿¡،؟؛。、！？：；";
  int Scale(int n)=>Math.Max(1,(int)Math.Round(n*.04/frame)); int lead=Scale(1),tail=Scale(5),join=Scale(12),gap=Scale(8);
  var groups=new List<List<(int Id,int Start,int End,double Confidence)>>();
  foreach(var s in spans){if(groups.Count==0||pieces[s.Id].StartsWith('▁'))groups.Add([]);groups[^1].Add(s);}
  List<Raw> raw=[];
  foreach(var g in groups){string text=DecodeText(g.Select(s=>s.Id)).Trim();var content=g.Where(s=>pieces[s.Id].Replace("▁","").Any(c=>!punct.Contains(c))).ToArray();
   if(content.Length==0){if(raw.Count>0)raw[^1].Text+=text;continue;}int i=0;while(i+1<content.Length&&content[i+1].Start-content[i].End>gap)i++;
   raw.Add(new(){Text=text,Start=content[i].Start,End=content[^1].End+1,Confidence=content.Min(s=>s.Confidence)});
  }
  if(raw.Count>1&&raw[0].Start<=1&&raw[1].Start-raw[0].End>join){raw[0].Start=Math.Max(0,raw[1].Start-Math.Max(6,(int)Math.Round(raw[0].Text.EnumerateRunes().Count()*1.75)));raw[0].End=raw[1].Start;}
  foreach(var w in raw)w.Start=Math.Max(0,w.Start-lead);
  for(int k=0;k<raw.Count;k++){var w=raw[k];if(k+1<raw.Count){int next=raw[k+1].Start;w.End=Math.Min(next-w.End<join?next:w.End+tail,next);}else w.End+=tail;}
  return raw.Select(w=>new Word(w.Text,Math.Round(offset+w.Start*frame,2),Math.Round(offset+w.End*frame,2),Math.Round(w.Confidence,3))).ToArray();
 }
 public void Dispose()=>handle.Dispose();
}

using System.Text.RegularExpressions;
namespace Balbal;
public static class TurkishItn
{
 public static string Lower(string text)=>text.Replace('I','ı').Replace('İ','i').ToLowerInvariant();
 static readonly Dictionary<string,long> Units=new(){["bir"]=1,["iki"]=2,["üç"]=3,["dört"]=4,["beş"]=5,["altı"]=6,["yedi"]=7,["sekiz"]=8,["dokuz"]=9};
 static readonly Dictionary<string,long> Tens=new(){["on"]=10,["yirmi"]=20,["otuz"]=30,["kırk"]=40,["elli"]=50,["altmış"]=60,["yetmiş"]=70,["seksen"]=80,["doksan"]=90};
 static readonly HashSet<string> Numbers=[..Units.Keys,..Tens.Keys,"sıfır","yüz","bin","milyon"];
 static readonly HashSet<string> Ambiguous=["bir","on","yüz","altı","bin"];
 public static string[] Parse(IEnumerable<string> words)
 {
  List<string> result=[]; long hi=0,lo=0; long? place=null;
  void Close(){ if(place.HasValue) result.Add((hi+lo).ToString(System.Globalization.CultureInfo.InvariantCulture)); hi=lo=0; place=null; }
  foreach(var w in words) {
   if(w=="sıfır") { Close(); result.Add("0"); }
   else if(Units.TryGetValue(w,out var u)) { if(place>1) {lo+=u;place=1;} else {Close();lo=u;place=1;} }
   else if(Tens.TryGetValue(w,out var t)) { if(place>10) {lo+=t;place=10;} else {Close();lo=t;place=10;} }
   else if(w=="yüz") { if(place==1 && lo<10) {lo*=100;place=100;} else if(place>100) {lo+=100;place=100;} else {Close();lo=100;place=100;} }
   else if(w=="bin") { if(place.HasValue && place<1000) {hi+=(lo==0?1:lo)*1000;lo=0;place=1000;} else {Close();hi=1000;place=1000;} }
   else if(w=="milyon") { if(place.HasValue && place<1000000) {hi=(hi+lo==0?1:hi+lo)*1000000;lo=0;place=1000000;} else {Close();hi=1000000;place=1000000;} }
   else throw new ArgumentException("Unknown number word: "+w);
  } Close(); return result.ToArray();
 }
 static (string Pre,string Core,string Post) Split(string t) { var m=Regex.Match(t,@"^(\W*)(.*?)(\W*)$",RegexOptions.Singleline); return(m.Groups[1].Value,m.Groups[2].Value,m.Groups[3].Value); }
 static IEnumerable<(string Text,int First,int End)> Runs(string[] tokens)
 {
  int i=0; while(i<tokens.Length) {
   var(pre,core,post)=Split(tokens[i]); if(!Numbers.Contains(Lower(core))) { yield return(tokens[i],i,i+1); i++; continue; }
   int j=i; List<string> words=[]; string last="";
   while(j<tokens.Length) {
    var(p,c,q)=Split(tokens[j]); string low=Lower(c);
    if(!Numbers.Contains(low) && j>i && p.Length==0) {
     var w=Numbers.Where(n=>low.StartsWith(n,StringComparison.Ordinal)&&Regex.IsMatch(low[n.Length..],@"^[a-zçğıöşü]{1,4}$")).OrderByDescending(n=>n.Length).FirstOrDefault();
     if(w is not null) { words.Add(w);j++;last="'"+c[w.Length..]+q; } break;
    }
    if(!Numbers.Contains(low)||(p.Length>0&&j>i)) break;
    words.Add(low);j++;last=q;if(q.Length>0) break;
   }
   if(words.Count==1&&Ambiguous.Contains(words[0])) {yield return(tokens[i],i,i+1);i++;continue;}
   yield return(pre+string.Join(' ',Parse(words))+last,i,j);i=j;
  }
 }
 public static string Text(string text)=>string.Join(' ',Runs(text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Select(r=>r.Text));
 public static IReadOnlyList<Word> Words(IReadOnlyList<Word> words)=>Runs(words.Select(w=>w.Text).ToArray()).Select(r=>
  words[r.First] with {Text=r.Text,End=words[r.End-1].End,Confidence=words.Skip(r.First).Take(r.End-r.First).Min(w=>w.Confidence)}).ToArray();
}

namespace Balbal;
public sealed record Emissions(float[] Data,int Frames,int Classes)
{
 public static (Emissions Emissions,int[] Kept) Prepare(Emissions input,DecoderSettings settings)
 {
  settings.Validate(); if(input.Frames<1||input.Classes<2||input.Data.Length!=checked(input.Frames*input.Classes)) throw new ArgumentException("Expected nonempty [frames,vocabulary+1] matrix");
  var a=(float[])input.Data.Clone(); int n=input.Classes;
  if(settings.Temperature!=1||settings.BlankBias!=0) for(int t=0;t<input.Frames;t++) {
   int start=t*n; for(int j=0;j<n;j++) a[start+j]/=(float)settings.Temperature; a[start+n-1]+=(float)settings.BlankBias;
   float max=a.AsSpan(start,n).ToArray().Max(),sum=0; for(int j=0;j<n;j++) sum+=MathF.Exp(a[start+j]-max);
   float norm=max+MathF.Log(sum); for(int j=0;j<n;j++) a[start+j]-=norm;
  }
  if(a.Any(x=>!float.IsFinite(x))) throw new ArithmeticException("Nonfinite decoder input");
  List<int> kept=[]; double threshold=settings.BlankCollapse is {} c?Math.Log(c):double.PositiveInfinity;
  for(int t=0;t<input.Frames;t++) if(t==input.Frames-1||!(a[t*n+n-1]>threshold&&a[(t+1)*n+n-1]>threshold)) kept.Add(t);
  var data=new float[kept.Count*n]; for(int t=0;t<kept.Count;t++) Array.Copy(a,kept[t]*n,data,t*n,n);
  return(new(data,kept.Count,n),kept.ToArray());
 }
 public static int SilenceCut(Emissions e,int first=0)
 {
  int bestStart=0,bestLength=0,run=0,argmax=0; float max=float.NegativeInfinity;
  for(int t=first;t<e.Frames;t++) { float b=e.Data[t*e.Classes+e.Classes-1]; if(b>max){max=b;argmax=t-first;}
   if(b>Math.Log(.99)) {run++; if(run>bestLength){bestLength=run;bestStart=t-first-run+1;}} else run=0; }
  return Math.Max(1,bestLength>0?(2*bestStart+bestLength)/2:argmax);
 }
}

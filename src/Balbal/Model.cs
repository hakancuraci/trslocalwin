using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Diagnostics;
using System.Text.Json;
namespace Balbal;
/// <summary>Offline ASR. Model graph includes the trained Conformer, loop refiner, acoustic memory and audio frontend.</summary>
public sealed class Model : IDisposable
{
 readonly IntPtr opsHandle;readonly InferenceSession session;readonly Decoder decoder;readonly SemaphoreSlim gate=new(1,1);bool disposed;
 public ModelInfo Info {get;}
 public Model(string directory,int threads=0,DecoderSettings? settings=null,string? customOpsLibrary=null)
 {
  directory=Path.GetFullPath(directory);Info=ModelInfo.Load(directory);
  var config=Path.Combine(directory,"decoder.json");if(!File.Exists(config))config=Path.Combine(AppContext.BaseDirectory,"configs",Info.Family,"decoder.json");
  settings??=File.Exists(config)?JsonSerializer.Deserialize<DecoderSettings>(File.ReadAllText(config))!:new();
  if(settings.DomainLm is {} d&&!Path.IsPathRooted(d))settings=settings with{DomainLm=Path.GetFullPath(Path.Combine(directory,d))};
  settings.Validate();using var options=new SessionOptions {IntraOpNumThreads=threads>0?threads:Math.Min(8,Environment.ProcessorCount),InterOpNumThreads=1,EnableMemoryPattern=false,GraphOptimizationLevel=GraphOptimizationLevel.ORT_ENABLE_ALL,LogSeverityLevel=OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR};
  if(customOpsLibrary is not null)options.RegisterCustomOpLibraryV2(Path.GetFullPath(customOpsLibrary),out opsHandle);
  var graph=Path.Combine(directory,"onnx",customOpsLibrary is not null||Info.Family is "balbal-v2e-zellige" or "balbal-v2e-calliope"?"model.onnx":"model-plain.onnx");
  session=new InferenceSession(graph,options);
  try {decoder=new Decoder(Path.Combine(directory,"lm"),settings,Info.Languages[0]);}catch{session.Dispose();if(opsHandle!=IntPtr.Zero)System.Runtime.InteropServices.NativeLibrary.Free(opsHandle);throw;}
 }
 Emissions ForwardCore(float[] wave)
 {
  if(wave.Length<4000)Array.Resize(ref wave,4000);
  var input=NamedOnnxValue.CreateFromTensor("audio",new DenseTensor<float>(wave,[1,wave.Length]));
  using var output=session.Run([input]);var tensor=output.First().AsTensor<float>();
  var shape=tensor.Dimensions.ToArray();int classes=shape[^1],frames=checked((int)(tensor.Length/classes));
  return new(tensor.ToArray(),frames,classes);
 }
 public Emissions Forward(float[] wave)
 {
  gate.Wait();try{ObjectDisposedException.ThrowIf(disposed,this);return ForwardCore(Audio.Canonical(wave));}finally{gate.Release();}
 }
 public async Task<Transcript> TranscribeAsync(string path,IEnumerable<string>? hotwords=null,bool itn=true,CancellationToken cancellationToken=default,AudioReadOptions? audioOptions=null)
  =>Transcribe(await Audio.ReadAsync(path,audioOptions??new AudioReadOptions(),cancellationToken),16000,hotwords,itn,cancellationToken:cancellationToken);
 public Transcript Transcribe(float[] audio,int sampleRate=16000,IEnumerable<string>? hotwords=null,bool itn=true,double? chunkSeconds=null,double? overlapSeconds=null,double segmentSeconds=300,CancellationToken cancellationToken=default)
 {
  var wave=Audio.Canonical(audio,sampleRate);var hot=hotwords?.ToArray();double window=chunkSeconds??Info.Window,overlap=overlapSeconds??Info.Overlap;
  if(!double.IsFinite(window)||window<=0||!double.IsFinite(overlap)||overlap<0||!double.IsFinite(segmentSeconds)||segmentSeconds<=0)throw new ArgumentException("Invalid window settings");
  if(wave.Length<=30*16000)window=30;
  gate.Wait(cancellationToken);try {
   ObjectDisposedException.ThrowIf(disposed,this);var timer=Stopwatch.StartNew();List<string> texts=[];List<Word> words=[];
   void Decode(Emissions e,int first) { cancellationToken.ThrowIfCancellationRequested();if(e.Frames==0)return;var result=decoder.Decode(e,hot,first*Info.FrameSamples/16000d,Info.FrameSamples/16000d);if(result.Text.Length>0)texts.Add(result.Text);words.AddRange(result.Words); }
   if(overlap==0){int step=checked((int)(window*16000));if(step<1)throw new ArgumentException("Window too short");for(int s=0;s<wave.Length;s+=step){cancellationToken.ThrowIfCancellationRequested();Decode(ForwardCore(wave[s..Math.Min(s+step,wave.Length)]),s/Info.FrameSamples);}}
   else {
    int hop=Info.FrameSamples,size=Math.Max(hop,checked((int)(window*16000))/hop*hop),ov=Math.Min(checked((int)(overlap*16000))/hop*hop,size/2);
    List<int> starts=[0];while(starts[^1]+size<wave.Length)starts.Add(starts[^1]+size-ov);
    List<float> pending=[];int start=0,classes=0,segment=Math.Max(1,checked((int)(segmentSeconds*16000/hop)));
    for(int k=0;k<starts.Count;k++) {
     cancellationToken.ThrowIfCancellationRequested();int s=starts[k];var e=ForwardCore(wave[s..Math.Min(wave.Length,s+size)]);classes=e.Classes;
     int first=k==0?0:(s+ov/2)/hop-s/hop,end=k+1==starts.Count?e.Frames:(starts[k+1]+ov/2)/hop-s/hop;
     if(first<0||end>e.Frames||first>end)throw new InvalidOperationException("Model frame grid does not match metadata");
     pending.AddRange(e.Data.AsSpan(first*classes,(end-first)*classes).ToArray());
     if(pending.Count/classes<segment)continue;
     var buffer=new Emissions(pending.ToArray(),pending.Count/classes,classes);int latest=end-first;
     int cut=buffer.Frames-latest+Emissions.SilenceCut(buffer,buffer.Frames-latest);
     Decode(new(buffer.Data[..(cut*classes)],cut,classes),start);start+=cut;pending.RemoveRange(0,cut*classes);
    }
    if(pending.Count>0)Decode(new(pending.ToArray(),pending.Count/classes,classes),start);
   }
   string text=string.Join(' ',texts);IReadOnlyList<Word> resultWords=words;
   if(itn&&Info.Itn?.Split('-','_')[0]=="tr"){text=TurkishItn.Text(text);resultWords=TurkishItn.Words(words);}
   timer.Stop();return new(text,resultWords,!resultWords.Any(w=>w.Confidence>=.15),Info.Name,wave.Length/16000d,timer.Elapsed.TotalSeconds);
  }finally{gate.Release();}
 }
 public void Dispose(){gate.Wait();try{if(disposed)return;disposed=true;decoder.Dispose();session.Dispose();if(opsHandle!=IntPtr.Zero)System.Runtime.InteropServices.NativeLibrary.Free(opsHandle);}finally{gate.Release();}}
}

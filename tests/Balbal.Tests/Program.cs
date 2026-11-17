using Balbal;
using System.Text.Json;
if(args.FirstOrDefault()=="--ffmpeg-tests") { await FfmpegIntegration.RunAsync(); return; }
string fixtures=Path.Combine(AppContext.BaseDirectory,"fixtures");int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception("FAIL: "+name);}
JsonDocument Fixture(string file)=>JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures,file)));
using(var doc=Fixture("itn.json"))foreach(var row in doc.RootElement.EnumerateArray())Check(TurkishItn.Text(row.GetProperty("input").GetString()!)==row.GetProperty("expected").GetString(),"ITN "+row.GetProperty("input"));
using(var doc=Fixture("resample.json"))foreach(var row in doc.RootElement.EnumerateArray()){
 var x=row.GetProperty("audio").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var expected=row.GetProperty("expected").EnumerateArray().Select(x=>x.GetSingle()).ToArray();var actual=Audio.Canonical(x,row.GetProperty("rate").GetInt32());
 Check(actual.Length==expected.Length,"resample length");double max=actual.Zip(expected).Max(p=>Math.Abs(p.First-p.Second));Check(max<2e-6,"scipy resample max error "+max);
}
var words=TurkishItn.Words([new("yirmi",0,.4,.9),new("beş",.4,.7,.6),new("lira",.8,1.1,.95)]);Check(words[0]==new Word("25",0,.7,.6),"ITN timings/confidence");
var settings=new DecoderSettings{Alpha=0,Beam=16,TopTokens=8};
using(var decoder=new Decoder(fixtures,settings))using(var doc=Fixture("decoder.json"))foreach(var row in doc.RootElement.EnumerateArray()){
 var e=new Emissions(row.GetProperty("data").EnumerateArray().Select(x=>x.GetSingle()).ToArray(),row.GetProperty("frames").GetInt32(),row.GetProperty("classes").GetInt32());
 var actual=decoder.Decode(e,row.GetProperty("hotwords").EnumerateArray().Select(x=>x.GetString()!));var expected=row.GetProperty("expected");
 Check(actual.Text==expected.GetProperty("text").GetString(),"decoder text: "+actual.Text+" expected "+expected.GetProperty("text"));
 var ew=expected.GetProperty("words").EnumerateArray().ToArray();Check(actual.Words.Count==ew.Length,"word count");
 for(int j=0;j<ew.Length;j++){var a=actual.Words[j];var b=ew[j];Check(a.Text==b.GetProperty("word").GetString()&&Math.Abs(a.Start-b.GetProperty("start").GetDouble())<.011&&Math.Abs(a.End-b.GetProperty("end").GetDouble())<.011&&Math.Abs(a.Confidence-b.GetProperty("confidence").GetDouble())<=.002,"word timing/confidence");}
}
float[] Floats(string path) {var b=File.ReadAllBytes(path);var a=new float[b.Length/4];Buffer.BlockCopy(b,0,a,0,b.Length);return a;}
if(args.Length>0){
 var directory=args[0];
 using(var realDecoder=new Decoder(Path.Combine(directory,"lm"),new()))using(var doc=Fixture("real-decoder.json"))foreach(var row in doc.RootElement.EnumerateArray()){
  var e=new Emissions(Floats(Path.Combine(fixtures,row.GetProperty("file").GetString()!)),row.GetProperty("frames").GetInt32(),row.GetProperty("classes").GetInt32());
  var actual=realDecoder.Decode(e,row.GetProperty("hotwords").EnumerateArray().Select(x=>x.GetString()!));var expected=row.GetProperty("expected");
  Check(actual.Text==expected.GetProperty("text").GetString(),"real KenLM text");
  var ew=expected.GetProperty("words").EnumerateArray().ToArray();Check(actual.Words.Count==ew.Length,"real KenLM word count");
  for(int j=0;j<ew.Length;j++){var a=actual.Words[j];var b=ew[j];Check(a.Text==b.GetProperty("word").GetString()&&Math.Abs(a.Start-b.GetProperty("start").GetDouble())<.011&&Math.Abs(a.End-b.GetProperty("end").GetDouble())<.011&&Math.Abs(a.Confidence-b.GetProperty("confidence").GetDouble())<=.002,"real KenLM word timing/confidence");}
 }
 using var model=new Model(directory,threads:4);var wave=Floats(Path.Combine(fixtures,"model-input.bin"));var e0=model.Forward(wave);var expectedEmissions=Floats(Path.Combine(fixtures,"model-expected.bin"));
 Check(e0.Classes==1025&&e0.Frames==25,"real ONNX shape");
 double max=e0.Data.Zip(expectedEmissions).Max(p=>Math.Abs(p.First-p.Second));Check(max<.001,"real ONNX log-prob max error "+max);Console.WriteLine("ONNX max absolute error: "+max);
 var r=model.Transcribe(wave);using(var doc=Fixture("model-expected.json")){var expected=doc.RootElement;Check(r.Text==expected.GetProperty("text").GetString(),"real ONNX transcript");Check(r.NoSpeech,"tone is non-speech");}
 if(File.Exists(Path.Combine(fixtures,"long-input.bin"))){var longWave=Floats(Path.Combine(fixtures,"long-input.bin"));var actual=model.Transcribe(longWave);using var doc=Fixture("long-expected.json");Check(actual.Text==doc.RootElement.GetProperty("text").GetString(),"long overlapping windows");var ew=doc.RootElement.GetProperty("words").EnumerateArray().ToArray();Check(actual.Words.Count==ew.Length,"long word count");for(int j=0;j<ew.Length;j++){var a=actual.Words[j];var b=ew[j];Check(a.Text==b.GetProperty("word").GetString()&&Math.Abs(a.Start-b.GetProperty("start").GetDouble())<.011&&Math.Abs(a.End-b.GetProperty("end").GetDouble())<.011,"long word timing");}}
}
Console.WriteLine($"PASS: {checks} assertions, Python reference fixtures");

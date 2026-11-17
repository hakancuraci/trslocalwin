using TrsLocal;
int checks=0;
void Check(bool yes,string message){checks++;if(!yes)throw new Exception("FAIL: "+message);}
var root=Path.Combine(Path.GetTempPath(),"trslocal-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
 string FileAt(string name)=>Path.Combine(root,name);
 string Output(string name)=>Path.Combine(root,"transcript",name+".txt");
 File.WriteAllText(FileAt("b.MP3"),"audio");File.WriteAllText(FileAt("a.wav"),"audio");File.WriteAllText(FileAt("skip.txt"),"x");
 List<string> order=[];int concurrent=0,max=0;
 var p=new FolderProcessor(root,async(path,ct)=>{concurrent++;max=Math.Max(concurrent,max);order.Add(Path.GetFileName(path));await Task.Delay(10,ct);concurrent--;return "Türkçe çıktı";});
 await p.RunAsync(once:true);
 Check(order.SequenceEqual(new[]{"a.wav","b.MP3"}),"stable ordering/extensions");Check(max==1,"sequential conversion");Check(File.ReadAllText(Output("a.wav"))=="Türkçe çıktı","UTF8 output naming");
 Check(!File.ReadAllBytes(Output("a.wav")).Take(3).SequenceEqual(new byte[]{239,187,191}),"no BOM");
 await p.RunAsync(once:true);Check(order.Count==2,"existing TXT skipped");Check(!FolderProcessor.Publish(Output("a.wav"),"overwrite"),"atomic no overwrite");Check(File.ReadAllText(Output("a.wav"))=="Türkçe çıktı","content preserved");
 File.WriteAllText(FileAt("change.mp3"),"a");var changing=new FolderProcessor(root,(path,ct)=>{File.AppendAllText(path,"changed");return Task.FromResult("wrong");});await changing.ScanAsync(true);Check(!File.Exists(Output("change.mp3")),"modified during inference not published");File.Delete(FileAt("change.mp3"));
 File.WriteAllText(FileAt("c.mp3"),"audio");File.WriteAllText(FileAt("d.mp3"),"audio");int failures=0;
 var errors=new FolderProcessor(root,(path,ct)=>{if(Path.GetFileName(path)=="c.mp3"){failures++;throw new IOException("bad audio");}return Task.FromResult("ok");});await errors.ScanAsync(true);Check(File.Exists(Output("d.mp3")),"continue after corrupt input");await errors.ScanAsync(true);Check(failures==1,"retry cooldown");
 File.Delete(FileAt("c.mp3"));File.WriteAllText(FileAt("stable.mp3"),"a");int conversions=0;var stable=new FolderProcessor(root,(path,ct)=>{conversions++;return Task.FromResult("ok");},new WatchOptions{StableTime=TimeSpan.FromMilliseconds(100)});await stable.ScanAsync();Check(conversions==0,"initial stability wait");await Task.Delay(130);File.AppendAllText(FileAt("stable.mp3"),"b");await stable.ScanAsync();Check(conversions==0,"changed input resets stability");await Task.Delay(130);await stable.ScanAsync();Check(conversions==1,"stable input converted");
 File.WriteAllText(FileAt("locked.mp3"),"a");var entered=new TaskCompletionSource();var release=new TaskCompletionSource();var locked=new FolderProcessor(root,async(path,ct)=>{entered.SetResult();await release.Task;return "ok";});var running=locked.RunAsync(true);await entered.Task;
 try{await new FolderProcessor(root,(_,_)=>Task.FromResult("x")).RunAsync(true);Check(false,"second watcher excluded");}catch(IOException){checks++;}release.SetResult();await running;
 File.WriteAllText(FileAt("stop.mp3"),"a");File.WriteAllText(FileAt("transcript/STOP"),"");await p.ScanAsync(true);Check(!File.Exists(Output("stop.mp3")),"STOP honored");File.Delete(FileAt("transcript/STOP"));
 using var cancel=new CancellationTokenSource();var cancellable=new FolderProcessor(root,(_,_)=>{cancel.Cancel();return Task.FromResult("partial");});try{await cancellable.ScanAsync(true,cancel.Token);}catch(OperationCanceledException){checks++;}Check(!File.Exists(Output("stop.mp3")),"cancelled output not published");Check(!Directory.EnumerateFiles(FileAt("transcript"),"*.part").Any(),"temporary outputs cleaned");
 using(var runtime=new PortableRuntime(root)){try{runtime.CheckModel();Check(false,"missing local model");}catch(FileNotFoundException e){Check(e.Message.Contains("Otomatik indirme kapalı"),"offline missing-model error");}try{runtime.BundledFfmpeg();Check(false,"no PATH fallback");}catch(FileNotFoundException){checks++;}}
 if(args.Length>=2){using var runtime=new PortableRuntime(args[0]);string result=await runtime.TranscribeAsync(args[1]);Console.WriteLine(result);if(args.Length>=3){File.WriteAllText(args[2],result);}}
 Console.WriteLine($"PASS: {checks} portable/watcher assertions");
}
finally{Directory.Delete(root,true);}

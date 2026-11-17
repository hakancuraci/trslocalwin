using TrsLocal;
namespace TrsLocal.Desktop;

internal sealed class WatcherForm : Form
{
    private readonly string folder;
    private readonly Label status = new() { Text = "İzleyici başlatılıyor…", AutoSize = true, MaximumSize = new Size(1000,0) };
    private readonly Label summary = new() { AutoSize = true, Text = "Dosyalar taranıyor…" };
    private readonly Label current = new() { AutoSize = true, Text = "İşlenen dosya: —" };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = new Font("Consolas",10) };
    private readonly ListView table = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Top, Style = ProgressBarStyle.Marquee, Height = 8 };
    private readonly WorkerController worker = new();
    private readonly Dictionary<string,WatchEvent> events = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,DateTimeOffset> started = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    public WatcherForm(string folder)
    {
        this.folder=folder;Text="Balbal İzleyici • İşlem Durumu";ClientSize=new Size(1080,740);MinimumSize=new Size(850,600);Font=new Font("Segoe UI",10);StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=10};
        for(int i=0;i<10;i++)layout.RowStyles.Add(i==6?new RowStyle(SizeType.Percent,100):i==8?new RowStyle(SizeType.Absolute,145):new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text="Klasör izleyicisi",AutoSize=true,Font=new Font("Segoe UI",20,FontStyle.Bold)},0,0);
        layout.Controls.Add(new Label{Text="Pencere açıkken izler. Kapatınca izleyici ve devam eden dönüşüm durur.",AutoSize=true,Margin=new Padding(3,5,3,10)},0,1);
        layout.Controls.Add(new Label{Text="İzlenen klasör: "+folder,AutoSize=true,MaximumSize=new Size(1000,0)},0,2);
        layout.Controls.Add(summary,0,3);layout.Controls.Add(current,0,4);
        var info=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,FlowDirection=FlowDirection.TopDown,WrapContents=false};info.Controls.Add(status);info.Controls.Add(progress);layout.Controls.Add(info,0,5);
        foreach(var c in new[]{("Dosya",350),("Durum",160),("Başlangıç",130),("Bitiş",130),("Süre",90)})table.Columns.Add(c.Item1,c.Item2);
        layout.Controls.Add(table,0,6);layout.Controls.Add(new Label{Text="Saatli işlem günlüğü",AutoSize=true,Margin=new Padding(3,10,3,3)},0,7);layout.Controls.Add(log,0,8);
        var footer=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Margin=new Padding(0,12,0,0)};footer.Controls.Add(new Label{Text="Çıktılar: transcript klasörü    ·    Powered by balbal",AutoSize=true,Padding=new Padding(0,8,0,0)});var close=new Button{Text="Kapat ve durdur",AutoSize=true};close.Click+=(_,_)=>Close();footer.Controls.Add(close);layout.Controls.Add(footer,0,9);Controls.Add(layout);
        worker.Event+=Accept;
        refresh.Tick+=(_,_)=>RefreshFiles();
        Shown+=async(_,_)=>{refresh.Start();RefreshFiles();try{await worker.StartAsync("--job","watch","--folder",folder);if(!IsDisposed){status.Text="İzleyici durdu. Yeniden başlatmak için pencereyi kapatıp açabilirsin.";progress.Visible=false;}}catch(Exception e){if(!IsDisposed){status.Text="Başlatılamadı: "+e.Message;Append(status.Text);}}};
        FormClosing+=(_,_)=>{refresh.Stop();worker.Dispose();};
    }
    private void Append(string line)
    {
        log.AppendText(line+Environment.NewLine);
        if(log.Lines.Length>500)log.Lines=log.Lines[^400..];log.SelectionStart=log.TextLength;log.ScrollToCaret();
    }
    private void Accept(WatchEvent e)
    {
        if(IsDisposed)return;Append($"{e.Time.LocalDateTime:HH:mm:ss}  {e.Message}"+(e.File.Length>0?" — "+e.File:""));
        if(e.File.Length>0)events[e.File]=e;
        if(e.Kind=="processing"){started[e.File]=e.Time;current.Text="İşlenen dosya: "+e.File;status.Text="Dönüştürme sürüyor…";progress.Visible=true;}
        else if(e.Kind is "done" or "existing" or "waiting" or "error"){current.Text="İşlenen dosya: —";status.Text="Klasör izleniyor. Bekleyen ve yeni dosyalar sırayla işlenecek.";progress.Visible=false;}
        else {status.Text=e.Message;progress.Visible=e.Kind=="loading";}
        RefreshFiles();
    }
    private void RefreshFiles()
    {
        try
        {
            var files=Directory.EnumerateFiles(folder).Where(f=>FolderProcessor.Extensions.Contains(Path.GetExtension(f))).OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).ToArray();
            int done=0,existing=0,waiting=0,error=0;table.BeginUpdate();table.Items.Clear();
            foreach(var path in files)
            {
                string name=Path.GetFileName(path);events.TryGetValue(name,out var e);bool output=File.Exists(Path.Combine(folder,"transcript",name+".txt"));string kind=e?.Kind??(output?"existing":"waiting");
                if(output&&kind is not "done" and not "processing")kind="existing";
                string state=kind switch{"done"=>"Tamamlandı","existing"=>"Çıktı zaten var","error"=>"Hata — yeniden denenecek","processing"=>"İşleniyor",_=>"Bekliyor"};
                if(kind=="done")done++;else if(kind=="existing")existing++;else if(kind=="error")error++;else waiting++;
                started.TryGetValue(name,out var start);var finish=kind is "done" or "error"?e?.Time:null;
                var row=new ListViewItem([name,state,start==default?"":start.LocalDateTime.ToString("HH:mm:ss"),finish?.LocalDateTime.ToString("HH:mm:ss")??"",start==default?"":$"{((finish??DateTimeOffset.Now)-start).TotalSeconds:F0} sn"]);
                row.ForeColor=kind switch{"done"=>Color.FromArgb(24,114,59),"error"=>Color.FromArgb(162,43,43),"processing"=>Color.FromArgb(23,95,166),_=>SystemColors.WindowText};table.Items.Add(row);
            }
            table.EndUpdate();summary.Text=$"Toplam: {files.Length}  •  Tamamlanan: {done}  •  Önceden mevcut: {existing}  •  Bekleyen: {waiting}  •  Hata: {error}";
        }
        catch(Exception e){table.EndUpdate();status.Text="Klasör okunamadı: "+e.Message;}
    }
    protected override void Dispose(bool disposing){if(disposing)refresh.Dispose();base.Dispose(disposing);}
}

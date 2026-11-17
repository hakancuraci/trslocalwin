using TrsLocal;
namespace TrsLocal.Desktop;

internal sealed class MainForm : Form
{
    private readonly TextBox source = new() { Dock = DockStyle.Fill, PlaceholderText = "Ses veya video dosyası" };
    private readonly TextBox hotwords = new() { Dock = DockStyle.Fill };
    private readonly TextBox transcript = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12) };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(820, 0), Text = "Yerel model kullanılır. Otomatik indirme ve ağ erişimi gerektiren işlem yapılmaz." };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Visible = false, Height = 8 };
    private readonly Button check = new() { Text = "1. Yerel modeli kontrol et", AutoSize = true };
    private readonly Button convert = new() { Text = "2. Metne dönüştür", AutoSize = true };
    private readonly Button choose = new() { Text = "Dosya seç", AutoSize = true };
    private WorkerController? worker;
    private bool busy;
    public MainForm()
    {
        Text = "Balbal Türkçe • Ses → Metin"; ClientSize = new Size(920, 720); MinimumSize = new Size(700, 540);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(244, 246, 250); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 11 };
        for(int i=0;i<11;i++) layout.RowStyles.Add(i==8?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "Sesini metne dönüştür", AutoSize = true, Font = new Font("Segoe UI", 23, FontStyle.Bold), ForeColor = Color.FromArgb(20,33,61) },0,0);
        layout.Controls.Add(new Label { Text = "Balbal-v2-manas · Yerel model · Telemetri kapalı", AutoSize = true, Margin = new Padding(3,5,3,18) },0,1);
        var fileRow = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileRow.Controls.Add(source,0,0);fileRow.Controls.Add(choose,1,0);layout.Controls.Add(fileRow,0,2);
        layout.Controls.Add(new Label { Text = "Dosyaları uygulama klasörüne koy. MP3/M4A/video için FFmpeg dahildir.", AutoSize = true, Margin = new Padding(3,7,3,14) },0,3);
        layout.Controls.Add(new Label { Text = "Özel isimler ve terimler (isteğe bağlı, virgülle ayır)", AutoSize = true },0,4);
        layout.Controls.Add(hotwords,0,5);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0,14,0,5) };buttons.Controls.Add(check);buttons.Controls.Add(convert);
        layout.Controls.Add(buttons,0,6);
        var information = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        information.Controls.Add(new Label { Text = "Model: EXE yanındaki models/balbal-v2-manas klasörü", AutoSize = true });information.Controls.Add(status);information.Controls.Add(progress);
        layout.Controls.Add(information,0,7);layout.Controls.Add(transcript,0,8);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0,12,0,0) };var save=new Button { Text="TXT olarak kaydet",AutoSize=true };footer.Controls.Add(save);footer.Controls.Add(new Label { Text="Powered by balbal · Çevrimdışı sürüm",AutoSize=true,Padding=new Padding(12,8,0,0),ForeColor=Color.FromArgb(53,96,160) });layout.Controls.Add(footer,0,9);Controls.Add(layout);
        choose.Click += (_,_) => { using var dialog=new OpenFileDialog { InitialDirectory=AppContext.BaseDirectory,Filter="Ses ve video|*.wav;*.flac;*.ogg;*.mp3;*.m4a;*.mp4;*.mkv;*.webm;*.opus;*.aac;*.wma|Tüm dosyalar|*.*" };if(dialog.ShowDialog(this)==DialogResult.OK)source.Text=dialog.FileName; };
        check.Click += async (_,_) => await StartAsync(true);convert.Click += async (_,_) => await StartAsync(false);
        save.Click += (_,_) => {if(string.IsNullOrWhiteSpace(transcript.Text)){MessageBox.Show(this,"Önce bir kayıt dönüştürmelisin.","Metin yok");return;}Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory,"transcript"));using var dialog=new SaveFileDialog {InitialDirectory=Path.Combine(AppContext.BaseDirectory,"transcript"),DefaultExt="txt",Filter="Türkçe metin — UTF-8|*.txt",FileName="transkript.txt"};if(dialog.ShowDialog(this)==DialogResult.OK){try{File.WriteAllText(dialog.FileName,transcript.Text,new System.Text.UTF8Encoding(false));status.Text="Metin kaydedildi: "+dialog.FileName;}catch(Exception e){MessageBox.Show(this,e.Message,"Kaydetme hatası");}}};
        FormClosing += (_,e) => { if(busy&&MessageBox.Show(this,"Kapatırsan devam eden işlem duracak. Kapatılsın mı?","İşlem sürüyor",MessageBoxButtons.YesNo)!=DialogResult.Yes){e.Cancel=true;return;}worker?.Dispose(); };
    }
    private async Task StartAsync(bool checkOnly)
    {
        if(busy)return;var path=source.Text.Trim().Trim('"');if(!checkOnly&&!File.Exists(path)){MessageBox.Show(this,"Geçerli bir ses dosyası seçmelisin.","Dosya seç");return;}
        busy=true;check.Enabled=convert.Enabled=choose.Enabled=false;progress.Visible=true;status.Text="İşlem başlatılıyor…";
        worker?.Dispose();worker=new WorkerController();worker.Event += e => {if(IsDisposed)return;status.Text=e.Message;if(e.Kind=="text")transcript.Text=e.Message;if(e.Kind=="fatal")status.Text="İşlem tamamlanamadı: "+e.Message;};
        try{await worker.StartAsync("--job",checkOnly?"model":"transcribe","--file",path,"--hotwords",hotwords.Text);}catch(Exception error){status.Text=error.Message;}finally{if(!IsDisposed){busy=false;check.Enabled=convert.Enabled=choose.Enabled=true;progress.Visible=false;}}
    }
}

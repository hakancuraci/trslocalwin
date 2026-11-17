namespace TrsLocal.Desktop;
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if(args.Contains("--worker") || args.Contains("--stop")) return Worker.Run(args);
        ApplicationConfiguration.Initialize();
        try
        {
            var watcher = Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Contains("Izleyici",StringComparison.OrdinalIgnoreCase) || args.Contains("--watch");
            int index=Array.IndexOf(args,"--folder");string folder=index>=0&&index+1<args.Length?Path.GetFullPath(args[index+1]):AppContext.BaseDirectory;
            Application.Run(watcher ? new WatcherForm(folder) : new MainForm()); return 0;
        }
        catch(Exception error) {MessageBox.Show(error.Message,"Balbal",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
    }
}

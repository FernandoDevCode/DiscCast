namespace DiscCast;
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DiscCast-error.log");
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now}: {eventArgs.Exception}\n");
            System.Windows.MessageBox.Show(eventArgs.Exception.Message + "\nDetalhes: " + path, "Erro do DiscCast");
            eventArgs.Handled = true;
            Shutdown(1);
        };
        base.OnStartup(e);
    }
}

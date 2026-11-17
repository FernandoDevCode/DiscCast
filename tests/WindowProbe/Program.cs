using DiscCast;
using LibVLCSharp.WPF;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        int result = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/DiscCast;component/Theme.xaml")
        });
        var window = new MainWindow();
        window.Loaded += async (_, _) =>
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            try
            {
                T Control<T>(string name) where T : class => (T)window.FindName(name);
                var source = Control<ComboBox>("SourceBox");
                var start = Control<Button>("StartButton");
                var watch = Control<RadioButton>("WatchTab");
                var transmit = Control<RadioButton>("TransmitTab");
                var address = Control<TextBox>("WatchAddress");
                var host = Control<Grid>("VideoHost");
                var placeholder = Control<TextBlock>("PlayerPlaceholder");
                var status = Control<TextBlock>("StatusText");

                Check(window.IsVisible && ((SolidColorBrush)window.Background).Color == Color.FromRgb(13, 16, 23), "Tema escuro não aplicado.");
                Check(source.Items.Count > 0 && start.IsEnabled, "Nenhum monitor selecionado.");
                var audio = Control<ComboBox>("AudioModeBox");
                var process = Control<ComboBox>("ProcessBox");
                audio.SelectedIndex = 1;
                Check(process.Visibility == Visibility.Visible, "O modo aplicativo não mostrou a seleção.");
                process.SelectedIndex = -1;
                Check(!start.IsEnabled, "A interface permitiu iniciar sem selecionar um app de áudio.");
                audio.SelectedIndex = 2;
                Check(process.Visibility == Visibility.Collapsed && start.IsEnabled, "O modo PC não atualizou os controles.");
                audio.SelectedIndex = 0;

                window.Width = window.MinWidth;
                window.Height = window.MinHeight;
                Render(window, ".tools/ui-transmit-v3.png");
                var startPosition = start.TranslatePoint(new Point(0, start.ActualHeight), window);
                Check(start.IsVisible && startPosition.Y < window.ActualHeight, "O botão Iniciar ficou fora da janela.");
                string transmitStatus = status.Text;

                for (int i = 0; i < 2; i++)
                {
                    watch.IsChecked = true;
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    Check(host.Children.Count == 0 && placeholder.IsVisible, "O estado ocioso criou um player nativo.");
                    if (i == 0) Render(window, ".tools/ui-watch-v3.png");

                    if (args.Length > 0)
                    {
                        address.Text = args[0];
                        Control<Button>("ConnectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var view = host.Children.OfType<VideoView>().Single();
                        var player = view.MediaPlayer!;
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
                        while (host.Children.Contains(view) && player.VoutCount == 0)
                            await Task.Delay(100, timeout.Token);
                        Check(host.Children.Contains(view), "O player encerrou a sessão: " + status.Text);
                        var hwnd = player.Hwnd;
                        Check(hwnd != IntPtr.Zero && IsWindow(hwnd), "A reprodução não abriu uma janela nativa.");
                        transmit.IsChecked = true;
                        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                        Check(!IsWindow(hwnd), "A janela nativa sobreviveu à troca de aba.");
                        Check(address.Text == args[0], "O convite foi apagado ao sair da aba.");
                    }
                    else
                    {
                        address.Text = "convite inválido";
                        Control<Button>("ConnectButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(status.Text.Contains("convite RTSP completo"), "A interface não explicou o convite inválido.");
                        transmit.IsChecked = true;
                    }

                    Check(host.Children.Count == 0 && !placeholder.IsVisible, "O player continuou na aba Transmitir.");
                    Check(status.Text == transmitStatus, "O status do player apareceu na aba Transmitir.");
                }

                Console.WriteLine(args.Length > 0
                    ? "OK: dois ciclos RTSP no player WPF, com remoção da janela nativa ao voltar a Transmitir."
                    : "OK: tema, seleção de áudio, janela compacta, validação de convite e troca de abas.");
            }
            catch (Exception ex)
            {
                result = 2;
                Console.Error.WriteLine(ex);
            }
            finally { window.Close(); }
        };
        app.Run(window);
        return result;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Render(Window window, string path)
    {
        window.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = File.Create(Path.GetFullPath(path));
        png.Save(stream);
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);
}

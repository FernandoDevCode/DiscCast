using DiscCast.Contracts;
using DiscCast.Services;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace DiscCast;

public partial class MainWindow : Window
{
    private readonly IBroadcastSession session = new BroadcastSession();
    private readonly List<VideoSource> sources = [];
    private LibVLC? vlc;
    private MediaPlayer? player;
    private VideoView? videoView;
    private AudioRoutingLease? playbackLease;
    private bool initialized, closing, closed, fullscreen;
    private string transmitStatus = "Use a mesma rede local ou VPN privada para compartilhar.";
    private string watchStatus = "Use um convite de alguém na mesma rede local ou VPN privada.";
    private WindowState oldState;
    private WindowStyle oldStyle;

    public MainWindow()
    {
        InitializeComponent();
        initialized = true;
        session.Changed += Session_Changed;
        RefreshLists();
        UpdateAudioSettings();
        UpdateStatus();
    }
    private void Ui(Action action)
    {
        if (!Dispatcher.HasShutdownStarted)
            _ = Dispatcher.BeginInvoke(() => { if (!closing) action(); });
    }
    private void UpdateStatus() => StatusText.Text = WatchTab.IsChecked == true ? watchStatus : transmitStatus;
    private void SetTransmitStatus(string message) { transmitStatus = message; UpdateStatus(); }
    private void SetWatchStatus(string message) { watchStatus = message; UpdateStatus(); }
    private void Session_Changed(BroadcastStatus status) => Ui(() =>
    {
        bool busy = status.State is BroadcastState.Starting or BroadcastState.Live or BroadcastState.Stopping;
        bool live = status.State == BroadcastState.Live;
        SettingsPanel.IsEnabled = !busy;
        SettingsScroll.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        LivePanel.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        StartButton.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
        StopButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = status.State != BroadcastState.Stopping;
        StopButton.Content = status.State == BroadcastState.Starting ? "Cancelar início" : "Encerrar transmissão";
        StateLabel.Text = status.State switch
        {
            BroadcastState.Starting => "Preparando transmissão…",
            BroadcastState.Stopping => "Encerrando transmissão…",
            BroadcastState.Failed => "Transmissão interrompida",
            _ => "Pronto para transmitir"
        };
        StateLabel.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        RoomBox.Text = status.RoomAddress ?? "";
        CopyButton.Visibility = RoomBox.Visibility = live ? Visibility.Visible : Visibility.Collapsed;
        CopyButton.Content = "Copiar convite";
        if (live)
        {
            LiveSource.Text = (SourceBox.SelectedItem as VideoSource)?.Label;
            string audioLabel = "Sem áudio";
            if (status.HasAudio)
                audioLabel = SharesSystemAudio ? "PC sem Discord" : (ProcessBox.SelectedItem as AudioApplication)?.Name ?? "Áudio do aplicativo";
            LiveDetails.Text = $"{SelectedProfile.Label}  ·  {audioLabel}";
        }
        SetTransmitStatus(status.Message);
    });
    private void ShowTransmit_Click(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        if (player != null)
        {
            StopPlayback();
            SetWatchStatus("Reprodução encerrada. Use Conectar para voltar à sala.");
        }
        WatchPanel.Visibility = Visibility.Collapsed;
        TransmitPanel.Visibility = Visibility.Visible;
        RootGrid.MaxWidth = 1080;
        UpdateStatus();
    }
    private void ShowWatch_Click(object sender, RoutedEventArgs e)
    {
        if (!initialized) return;
        TransmitPanel.Visibility = Visibility.Collapsed;
        WatchPanel.Visibility = Visibility.Visible;
        RootGrid.MaxWidth = double.PositiveInfinity;
        UpdateStatus();
        if (player == null) WatchAddress.Focus();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshLists();
    private void RefreshLists()
    {
        string? previousSource = (SourceBox.SelectedItem as VideoSource)?.Input;
        int? previousAudio = (ProcessBox.SelectedItem as AudioApplication)?.ProcessId;
        sources.Clear();
        int index = 1;
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            sources.Add(new($"Monitor {index++} · {b.Width} × {b.Height}" + (screen.Primary ? " · Principal" : ""), VideoSourceKind.Monitor, $"desktop|{b.X}|{b.Y}|{b.Width}|{b.Height}", b.Width, b.Height));
        }
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLength(handle) == 0) return true;
            GetWindowThreadProcessId(handle, out uint pid);
            if (pid == Environment.ProcessId) return true;
            var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
            if (title.Length == 0) return true;
            GetWindowRect(handle, out var rect);
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            if (w > 100 && h > 100) sources.Add(new(title.ToString(), VideoSourceKind.Window, $"hwnd=0x{handle.ToInt64():X}", w, h, (int)pid));
            return true;
        }, IntPtr.Zero);
        PopulateSources(previousSource);
        try
        {
            var apps = ProcessCatalog.Applications();
            ProcessBox.ItemsSource = apps;
            var source = SourceBox.SelectedItem as VideoSource;
            ProcessBox.SelectedItem = apps.FirstOrDefault(p => p.ProcessId == previousAudio)
                ?? apps.FirstOrDefault(p => p.ProcessId == source?.ProcessId);
        }
        catch (Exception ex) { SetTransmitStatus("Lista de aplicativos indisponível: " + ex.Message); }
        UpdateAudioSettings();
    }
    private void PopulateSources(string? previous = null)
    {
        var kind = WindowSource.IsChecked == true ? VideoSourceKind.Window : VideoSourceKind.Monitor;
        var filtered = sources.Where(s => s.Kind == kind).ToArray();
        SourceBox.ItemsSource = filtered;
        SourceBox.SelectedItem = filtered.FirstOrDefault(s => s.Input == previous);
        if (SourceBox.SelectedIndex < 0 && filtered.Length > 0) SourceBox.SelectedIndex = 0;
        SourceBox.IsEnabled = filtered.Length > 0;
        SourceHint.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        SourceHint.Text = kind == VideoSourceKind.Window
            ? "Abra a janela que deseja compartilhar e clique em Atualizar fontes."
            : "Nenhum monitor encontrado. Clique em Atualizar fontes para tentar novamente.";
        UpdateStartAvailability();
    }
    private void SourceKind_Checked(object sender, RoutedEventArgs e) { if (initialized) PopulateSources(); }
    private void Source_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || SourceBox.SelectedItem is not VideoSource source) return;
        var previous = e.RemovedItems.OfType<VideoSource>().FirstOrDefault();
        var selectedAudio = ProcessBox.SelectedItem as AudioApplication;
        if (source.ProcessId > 0 && (selectedAudio == null || selectedAudio.ProcessId == previous?.ProcessId)
            && ProcessBox.ItemsSource is IEnumerable<AudioApplication> apps)
        {
            var match = apps.FirstOrDefault(a => a.ProcessId == source.ProcessId);
            ProcessBox.SelectedItem = match;
        }
        UpdateStartAvailability();
    }
    private void AudioSettings_Changed(object sender, RoutedEventArgs e) { if (initialized) UpdateAudioSettings(); }
    private void AudioProcess_Changed(object sender, SelectionChangedEventArgs e) { if (initialized) UpdateAudioSettings(); }
    private void UpdateAudioSettings()
    {
        ProcessBox.Visibility = SharesAppAudio ? Visibility.Visible : Visibility.Collapsed;
        AudioHint.Text = SharesSystemAudio
            ? "Mantenha uma instância do Discord desktop aberta. A reprodução no DiscCast fica bloqueada neste PC durante a captura."
            : SharesAppAudio && ProcessBox.SelectedItem == null
                ? "Escolha um aplicativo aberto. Use Atualizar fontes se ele não aparecer."
                : "";
        AudioHint.Visibility = AudioHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStartAvailability();
    }
    private bool SharesAppAudio => AudioModeBox.SelectedIndex == 1;
    private bool SharesSystemAudio => AudioModeBox.SelectedIndex == 2;
    private void UpdateStartAvailability() =>
        StartButton.IsEnabled = SourceBox.SelectedItem != null && (!SharesAppAudio || ProcessBox.SelectedItem != null);
    private StreamProfile SelectedProfile => QualityBox.SelectedIndex == 1 ? StreamProfile.High : StreamProfile.Standard;
    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (!StartButton.IsEnabled || SourceBox.SelectedItem is not VideoSource source) return;
        var app = ProcessBox.SelectedItem as AudioApplication;
        StartButton.IsEnabled = false;
        bool system = SharesSystemAudio;
        await session.StartAsync(new(source, new(SharesAppAudio || system, system ? AudioMode.SystemExceptDiscord : AudioMode.Application, app?.ProcessId), SelectedProfile));
        if (!closing) UpdateStartAvailability();
    }
    private async void Stop_Click(object sender, RoutedEventArgs e) => await session.StopAsync();
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RoomBox.Text)) return;
        try { System.Windows.Clipboard.SetText(RoomBox.Text); CopyButton.Content = "Convite copiado"; }
        catch (ExternalException) { SetTransmitStatus("Não foi possível copiar. Selecione o convite e pressione Ctrl+C."); }
    }
    private void WatchAddress_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (initialized) ConnectButton.IsEnabled = !string.IsNullOrWhiteSpace(WatchAddress.Text);
    }
    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = System.Windows.Clipboard.GetText().Trim();
            if (text.Length == 0) { SetWatchStatus("Copie o convite recebido e clique em Colar novamente."); return; }
            WatchAddress.Text = text;
            WatchAddress.Focus();
            WatchAddress.CaretIndex = WatchAddress.Text.Length;
        }
        catch (ExternalException) { SetWatchStatus("Não foi possível colar. Tente Ctrl+V no campo do convite."); }
    }
    private void WatchAddress_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        e.Handled = true;
        Connect();
    }
    private void Connect_Click(object sender, RoutedEventArgs e) => Connect();
    private void Connect()
    {
        if (WatchTab.IsChecked != true || player != null || closing) return;
        if (!Uri.TryCreate(WatchAddress.Text.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "rtsp" || uri.UserInfo.Length == 0 || string.IsNullOrEmpty(uri.Host))
        {
            SetWatchStatus("Cole o convite RTSP completo da sala e tente novamente.");
            WatchAddress.Focus();
            return;
        }
        try
        {
            playbackLease = AudioRoutingLease.Acquire(systemCapture: false);
            if (vlc == null)
            {
                Core.Initialize();
                vlc = new LibVLC("--rtsp-tcp", "--network-caching=300", "--live-caching=300");
            }
            var current = new MediaPlayer(vlc) { Volume = (int)VolumeSlider.Value, EnableKeyInput = false, EnableMouseInput = false };
            player = current;
            current.Playing += (_, _) => Ui(() =>
            {
                if (player != current) return;
                if (!fullscreen) WatchControls.Visibility = Visibility.Visible;
                DisconnectButton.Content = "Desconectar";
                SetWatchStatus("Assistindo à transmissão.");
            });
            current.EncounteredError += (_, _) => Ui(() => PlaybackEnded(current,
                "Não foi possível conectar. Confira o convite, se a sala está ao vivo e o acesso à porta TCP 8554."));
            current.EndReached += (_, _) => Ui(() => PlaybackEnded(current, "A transmissão terminou ou a conexão caiu."));

            // Keep the native window out of the visual tree until Assistir needs it.
            videoView = new VideoView { MediaPlayer = current };
            VideoHost.Children.Add(videoView);
            PlayerPlaceholder.Visibility = Visibility.Collapsed;
            WatchAddress.IsReadOnly = true;
            PasteButton.IsEnabled = false;
            ConnectButton.Visibility = Visibility.Collapsed;
            DisconnectButton.Visibility = Visibility.Visible;
            DisconnectButton.Content = "Cancelar conexão";
            MuteButton.Content = "Silenciar";
            using var media = new Media(vlc, uri);
            if (!current.Play(media)) throw new InvalidOperationException("O player recusou a transmissão.");
            SetWatchStatus("Conectando à sala…");
        }
        catch (Exception ex) { StopPlayback(); SetWatchStatus(ex.Message); }
    }
    private void PlaybackEnded(MediaPlayer current, string message)
    {
        if (player != current) return;
        StopPlayback();
        SetWatchStatus(message);
    }
    private void StopPlayback()
    {
        var current = player;
        player = null;
        current?.Stop();
        if (videoView != null)
        {
            videoView.MediaPlayer = null;
            VideoHost.Children.Remove(videoView);
            videoView.Dispose();
            videoView = null;
        }
        current?.Dispose();
        playbackLease?.Dispose();
        playbackLease = null;
        SetFullscreen(false);
        PlayerPlaceholder.Visibility = Visibility.Visible;
        WatchControls.Visibility = Visibility.Collapsed;
        WatchAddress.IsReadOnly = false;
        PasteButton.IsEnabled = true;
        ConnectButton.Visibility = Visibility.Visible;
        DisconnectButton.Visibility = Visibility.Collapsed;
    }
    private void Disconnect_Click(object sender, RoutedEventArgs e) { StopPlayback(); SetWatchStatus("Você saiu da sala."); }
    private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (player != null) player.Volume = (int)e.NewValue;
        if (VolumeLabel != null) VolumeLabel.Text = $"{(int)e.NewValue}%";
    }
    private void Mute_Click(object sender, RoutedEventArgs e) { if (player == null) return; player.Mute = !player.Mute; MuteButton.Content = player.Mute ? "Ativar som" : "Silenciar"; }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) { if (player != null) SetFullscreen(!fullscreen); }
    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (fullscreen && e.Key == System.Windows.Input.Key.Escape) { SetFullscreen(false); e.Handled = true; }
    }
    private void SetFullscreen(bool enabled)
    {
        if (enabled == fullscreen) return;
        if (enabled) { oldState = WindowState; oldStyle = WindowStyle; }
        fullscreen = enabled;
        RootGrid.Margin = enabled ? new Thickness(0) : new Thickness(24, 18, 24, 18);
        RootGrid.MaxWidth = WatchTab.IsChecked == true ? double.PositiveInfinity : 1080;
        HeaderPanel.Visibility = WatchTop.Visibility = WatchControls.Visibility = StatusPanel.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        WindowStyle = enabled ? WindowStyle.None : oldStyle;
        WindowState = enabled ? WindowState.Maximized : oldState;
    }
    private void NetworkHelp_Click(object sender, RoutedEventArgs e) => System.Windows.MessageBox.Show(this,
        "Use a mesma rede local ou uma VPN privada nos dois PCs.\n\nSe o Firewall do Windows bloquear a conexão, permita o MediaMTX apenas na rede privada ou crie uma regra de entrada para TCP 8554. O DiscCast não altera o firewall.\n\nO convite contém a senha da sala. Não encaminhe a porta no roteador nem publique o convite na Internet.", "Conectar pela rede", MessageBoxButton.OK, MessageBoxImage.Information);
    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (closed) return;
        e.Cancel = true;
        if (closing) return;
        closing = true;
        IsEnabled = false;
        try { StopPlayback(); await session.DisposeAsync(); vlc?.Dispose(); }
        finally { closed = true; _ = Dispatcher.BeginInvoke((Action)Close); }
    }
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        int dark = 1;
        _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 20, ref dark, sizeof(int));
    }
    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr param);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out WindowRect rect);
    [StructLayout(LayoutKind.Sequential)] private struct WindowRect { public int Left, Top, Right, Bottom; }
}

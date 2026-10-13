using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--launcher-version", StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("Velkhara Launcher 3.0.0", "Versao do launcher");
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

internal sealed class LauncherForm : Form
{
    private const string Repository = "brunoian1/VelkharaVTC";
    private const string ReleaseTag = "client-latest";
    private const string PackageName = "VTC.VELKHARA.zip";
    private const string VersionName = "client-version.txt";
    private const string SettingsName = "launcher-renderer.txt";
    private const string DirectXExecutable = "bin\\otclient_dx.exe";
    private const string OpenGlExecutable = "bin\\otclient_gl.exe";

    private static readonly HashSet<string> PreservedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "otclientrc.lua", "otclient.log", "otclientv8.log", SettingsName
    };

    private static readonly HttpClient Http = CreateHttpClient();
    private readonly string root = AppContext.BaseDirectory;
    private readonly RadioButton directX = new() { Text = "DirectX 9 (recomendado)", AutoSize = true };
    private readonly RadioButton openGl = new() { Text = "OpenGL", AutoSize = true };
    private readonly Label status = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label version = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
    private readonly LauncherProgressBar progress = new();
    private readonly Label downloadInfo = new() { AutoSize = false, ForeColor = Color.FromArgb(194, 181, 160), TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button pauseButton = new() { Text = "Ⅱ", Visible = false, TabStop = false };
    private readonly Button updateButton = new() { Text = "PROCURAR ATUALIZACAO" };
    private readonly Button playButton = new() { Text = "JOGAR" };
    private readonly Button rendererButton = new() { Text = "DX9  ▼" };
    private readonly ContextMenuStrip rendererMenu = new();
    private bool busy;
    private bool downloadPaused;

    public LauncherForm()
    {
        Text = "Velkhara OT";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(900, 530);
        MinimumSize = new Size(900, 530);
        MaximumSize = new Size(900, 530);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 14, 12);
        BackgroundImage = null;
        ForeColor = Color.WhiteSmoke;
        Font = new Font("Segoe UI", 9.5F);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;

        var header = new Panel { BackColor = Color.FromArgb(25, 22, 19), Location = new Point(0, 0), Size = new Size(900, 44) };
        var mark = new Label
        {
            Text = "V",
            Font = new Font("Georgia", 13F, FontStyle.Bold),
            ForeColor = Color.FromArgb(205, 151, 72),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(10, 5),
            Size = new Size(30, 32)
        };
        var windowTitle = new Label { Text = "Velkhara OT Launcher", ForeColor = Color.FromArgb(220, 214, 203), BackColor = Color.Transparent, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(42, 0), Size = new Size(300, 42) };
        var minimize = CreateTitleButton("—", 730);
        var close = CreateTitleButton("×", 780);
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        close.Click += (_, _) => Close();
        header.Controls.AddRange([mark, windowTitle, minimize, close]);
        minimize.Location = new Point(800, 0);
        close.Location = new Point(850, 0);
        header.MouseDown += DragWindow;
        windowTitle.MouseDown += DragWindow;
        mark.MouseDown += DragWindow;

        var heroPanel = new Panel
        {
            Location = new Point(0, 44),
            Size = new Size(900, 366),
            BackgroundImage = LoadEmbeddedImage("VelkharaBackground.png"),
            BackgroundImageLayout = ImageLayout.Stretch,
            BackColor = Color.FromArgb(15, 14, 12)
        };
        var footerPanel = new Panel
        {
            Location = new Point(0, 410),
            Size = new Size(900, 120),
            BackColor = Color.FromArgb(20, 18, 15)
        };

        var logo = new PictureBox
        {
            Image = LoadEmbeddedLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Location = new Point(52, 24),
            Size = new Size(175, 112)
        };
        var title = new Label
        {
            Text = "Velkhara OT",
            Font = new Font("Segoe UI", 26F),
            ForeColor = Color.FromArgb(237, 229, 213),
            BackColor = Color.Transparent,
            AutoSize = false,
            Size = new Size(550, 52),
            Location = new Point(265, 62),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var official = new Label { Text = "CLIENT OFICIAL", Font = new Font("Segoe UI Semibold", 9F), ForeColor = Color.FromArgb(207, 151, 68), BackColor = Color.Transparent, AutoSize = false, Size = new Size(550, 22), Location = new Point(268, 40), TextAlign = ContentAlignment.MiddleLeft };
        var subtitle = new Label
        {
            Text = "Escolha o modo grafico e entre no jogo.",
            AutoSize = false,
            Size = new Size(550, 25),
            ForeColor = Color.FromArgb(164, 155, 143),
            BackColor = Color.Transparent,
            Location = new Point(268, 111),
            TextAlign = ContentAlignment.MiddleLeft
        };

        var graphicsLabel = new Label { Text = "MODO GRAFICO", Font = new Font("Segoe UI Semibold", 8.5F), ForeColor = Color.FromArgb(165, 174, 187), BackColor = Color.Transparent, AutoSize = true, Location = new Point(66, 190), Visible = false };
        StyleRendererButton(directX, "DirectX 9\r\nMelhor compatibilidade", new Point(66, 211));
        StyleRendererButton(openGl, "OpenGL\r\nModo alternativo", new Point(422, 211));
        directX.Visible = false;
        openGl.Visible = false;
        RoundControl(directX, 10);
        RoundControl(openGl, 10);
        AddRoundedBorder(directX, 10, () => directX.Checked ? Color.FromArgb(65, 181, 235) : Color.FromArgb(68, 78, 91), () => directX.Checked ? 2F : 1F);
        AddRoundedBorder(openGl, 10, () => openGl.Checked ? Color.FromArgb(65, 181, 235) : Color.FromArgb(68, 78, 91), () => openGl.Checked ? 2F : 1F);

        var updatePanel = new Panel { BackColor = Color.FromArgb(24, 21, 18), Location = new Point(52, 246), Size = new Size(796, 82) };
        RoundControl(updatePanel, 10);
        AddRoundedBorder(updatePanel, 10, () => Color.FromArgb(73, 59, 42), () => 1F);
        var statusDot = new Panel { BackColor = Color.FromArgb(211, 151, 55), Location = new Point(18, 22), Size = new Size(9, 9) };
        status.Location = new Point(42, 10);
        status.Size = new Size(720, 28);
        status.Font = new Font("Segoe UI Semibold", 9.5F);
        status.ForeColor = Color.FromArgb(226, 219, 207);
        version.Location = new Point(22, 40);
        version.Size = new Size(740, 20);
        version.Font = new Font("Segoe UI", 8.5F);
        version.ForeColor = Color.FromArgb(153, 142, 127);
        version.Text = "Verificando versao instalada...";
        updatePanel.Controls.AddRange([statusDot, status, version]);

        playButton.Location = new Point(703, 15);
        playButton.Size = new Size(145, 44);
        playButton.Text = "START GAME";
        playButton.BackColor = Color.FromArgb(181, 119, 43);
        playButton.ForeColor = Color.FromArgb(255, 246, 224);
        playButton.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        playButton.FlatStyle = FlatStyle.Flat;
        playButton.FlatAppearance.BorderSize = 0;
        playButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(205, 139, 53);
        playButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(153, 94, 31);
        playButton.Cursor = Cursors.Hand;

        rendererButton.Location = new Point(703, 64);
        rendererButton.Size = new Size(145, 26);
        rendererButton.BackColor = Color.FromArgb(91, 60, 31);
        rendererButton.ForeColor = Color.FromArgb(244, 222, 184);
        rendererButton.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
        rendererButton.FlatStyle = FlatStyle.Flat;
        rendererButton.FlatAppearance.BorderSize = 0;
        rendererButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(112, 75, 38);
        rendererButton.FlatAppearance.MouseDownBackColor = Color.FromArgb(72, 46, 25);
        rendererButton.Cursor = Cursors.Hand;
        rendererButton.TabStop = false;

        rendererMenu.ShowImageMargin = false;
        rendererMenu.BackColor = Color.FromArgb(28, 24, 20);
        rendererMenu.ForeColor = Color.FromArgb(235, 225, 208);
        rendererMenu.RenderMode = ToolStripRenderMode.System;
        var dx9MenuItem = rendererMenu.Items.Add("DirectX 9");
        var openGlMenuItem = rendererMenu.Items.Add("OpenGL");
        dx9MenuItem.Click += (_, _) => SelectRenderer(false);
        openGlMenuItem.Click += (_, _) => SelectRenderer(true);
        rendererButton.Click += (_, _) => rendererMenu.Show(rendererButton, new Point(0, rendererButton.Height));

        progress.Location = new Point(52, 20);
        progress.Size = new Size(570, 18);
        RoundControl(progress, 5);

        pauseButton.Location = new Point(630, 16);
        pauseButton.Size = new Size(30, 25);
        pauseButton.BackColor = Color.FromArgb(20, 31, 42);
        pauseButton.ForeColor = Color.White;
        pauseButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        pauseButton.FlatStyle = FlatStyle.Flat;
        pauseButton.FlatAppearance.BorderSize = 0;
        pauseButton.Cursor = Cursors.Hand;
        pauseButton.Click += (_, _) =>
        {
            downloadPaused = !downloadPaused;
            pauseButton.Text = downloadPaused ? "▶" : "Ⅱ";
            status.Text = downloadPaused ? "Download pausado." : "Baixando atualizacao...";
        };

        downloadInfo.Location = new Point(52, 43);
        downloadInfo.Size = new Size(608, 22);
        downloadInfo.Font = new Font("Segoe UI", 8.5F);
        downloadInfo.Text = "Cliente pronto para jogar";

        updateButton.Location = new Point(52, 76);
        updateButton.Size = new Size(205, 26);
        updateButton.Text = "VERIFICAR UPDATE";
        StyleSecondaryButton(updateButton);
        updateButton.Font = new Font("Segoe UI Semibold", 7.5F);
        RoundControl(updateButton, 6);
        AddRoundedBorder(updateButton, 6, () => Color.FromArgb(71, 82, 99), () => 1F);

        heroPanel.Controls.AddRange([logo, title, official, subtitle, updatePanel]);
        footerPanel.Controls.AddRange([progress, downloadInfo, pauseButton, updateButton, playButton, rendererButton]);
        Controls.AddRange([heroPanel, footerPanel, header, graphicsLabel, directX, openGl]);
        header.BringToFront();
        playButton.TabStop = false;

        var savedRenderer = ReadText(SettingsName);
        var useSavedOpenGl = savedRenderer.Equals("opengl", StringComparison.OrdinalIgnoreCase);
        directX.Checked = !useSavedOpenGl;
        openGl.Checked = useSavedOpenGl;
        rendererButton.Text = useSavedOpenGl ? "MODO: OPENGL  ▼" : "MODO: DX9  ▼";
        directX.CheckedChanged += (_, _) => RefreshExecutableState();
        openGl.CheckedChanged += (_, _) => RefreshExecutableState();
        updateButton.Click += async (_, _) => await CheckForUpdates(true);
        playButton.Click += (_, _) => StartGame();
        Shown += async (_, _) => await CheckForUpdates(false);
        RefreshExecutableState();
    }

    private void SelectRenderer(bool useOpenGl)
    {
        openGl.Checked = useOpenGl;
        directX.Checked = !useOpenGl;
        rendererButton.Text = useOpenGl ? "MODO: OPENGL  ▼" : "MODO: DX9  ▼";
        File.WriteAllText(Path.Combine(root, SettingsName), useOpenGl ? "opengl" : "directx9");
        RefreshExecutableState();
    }

    private static void StyleRendererButton(RadioButton button, string text, Point location)
    {
        button.Text = text;
        button.Appearance = Appearance.Button;
        button.AutoSize = false;
        button.Location = location;
        button.Size = new Size(346, 72);
        button.Padding = new Padding(20, 5, 0, 0);
        button.TextAlign = ContentAlignment.MiddleLeft;
        button.Font = new Font("Segoe UI Semibold", 9.5F);
        button.ForeColor = Color.FromArgb(213, 217, 224);
        button.BackColor = Color.FromArgb(39, 49, 60);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(104, 119, 132);
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.CheckedBackColor = Color.FromArgb(29, 63, 82);
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 33, 42);
        button.Cursor = Cursors.Hand;
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.BackColor = Color.FromArgb(20, 24, 32);
        button.ForeColor = Color.FromArgb(174, 180, 191);
        button.Font = new Font("Segoe UI Semibold", 8.5F);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderColor = Color.FromArgb(54, 61, 74);
        button.FlatAppearance.BorderSize = 0;
        button.Cursor = Cursors.Hand;
    }

    private static Image? LoadEmbeddedLogo()
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream("VelkharaLogo.png");
        if (stream is null) return null;
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Image? LoadEmbeddedImage(string name)
    {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(name);
        if (stream is null) return null;
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static Button CreateWindowButton(string text, int x)
    {
        var button = new Button { Text = text, Location = new Point(x, 4), Size = new Size(44, 40), FlatStyle = FlatStyle.Flat, BackColor = Color.Transparent, ForeColor = Color.FromArgb(137, 218, 247), Font = new Font("Segoe UI", 16F), TabStop = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(48, 70, 85);
        return button;
    }

    private static Button CreateTitleButton(string text, int x)
    {
        var button = new Button { Text = text, Location = new Point(x, 0), Size = new Size(50, 42), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 22, 19), ForeColor = Color.FromArgb(190, 181, 168), Font = new Font("Segoe UI", 13F), TabStop = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = text == "×" ? Color.FromArgb(225, 72, 72) : Color.FromArgb(225, 225, 225);
        return button;
    }

    private static void RoundControl(Control control, int radius)
    {
        control.Region?.Dispose();
        control.Region = RoundedRegion(new Rectangle(0, 0, control.Width, control.Height), radius);
    }

    private static void AddRoundedBorder(Control control, int radius, Func<Color> color, Func<float> width)
    {
        control.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var stroke = width();
            using var path = RoundedPath(new RectangleF(stroke / 2F, stroke / 2F, control.Width - stroke - 1F, control.Height - stroke - 1F), radius);
            using var pen = new Pen(color(), stroke);
            e.Graphics.DrawPath(pen, path);
        };
    }

    private static GraphicsPath RoundedPath(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2F;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Region RoundedRegion(Rectangle bounds, int radius)
    {
        using var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return new Region(path);
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private async Task CheckForUpdates(bool userRequested)
    {
        if (busy) return;
        SetBusy(true);
        status.Text = "Procurando atualizacoes...";
        progress.Value = 0;
        try
        {
            var remote = await GetRemoteVersion();
            var local = ReadText(VersionName);
            version.Text = "Versao instalada: " + FormatVersion(local);
            if (!string.Equals(local, remote, StringComparison.Ordinal))
                await InstallUpdate(remote);
            else
            {
                progress.Value = 100;
                status.Text = "Seu cliente esta atualizado.";
                if (userRequested) MessageBox.Show("Voce ja possui a versao mais recente.", "Atualizacao", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            status.Text = "Nao foi possivel verificar agora. Voce ainda pode jogar.";
            if (userRequested) MessageBox.Show(ex.Message, "Falha ao atualizar", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
            RefreshExecutableState();
        }
    }

    private async Task InstallUpdate(string remoteVersion)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "velkhara-update-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, PackageName);
        var extractPath = Path.Combine(tempRoot, "files");
        Directory.CreateDirectory(tempRoot);
        try
        {
            status.Text = "Nova versao encontrada. Baixando...";
            downloadPaused = false;
            pauseButton.Text = "Ⅱ";
            pauseButton.Visible = true;
            updateButton.Visible = false;
            downloadInfo.Text = "Preparando download...";
            await DownloadWithProgress(ReleaseUrl(PackageName) + "?v=" + Uri.EscapeDataString(remoteVersion), zipPath);
            pauseButton.Visible = false;
            status.Text = "Descompactando arquivos...";
            downloadInfo.Text = "Preparando os arquivos da atualizacao. Aguarde...";
            progress.IsIndeterminate = true;
            Directory.CreateDirectory(extractPath);
            await Task.Run(() => ExtractSafely(zipPath, extractPath));
            if (!File.Exists(Path.Combine(extractPath, DirectXExecutable)) && !File.Exists(Path.Combine(extractPath, OpenGlExecutable)))
                throw new InvalidDataException("O pacote nao contem um executavel valido do cliente.");
            status.Text = "Aplicando atualizacao...";
            downloadInfo.Text = "Substituindo os arquivos do cliente. Nao feche o launcher...";
            await Task.Run(() => CopyUpdate(extractPath, root));
            progress.IsIndeterminate = false;
            File.WriteAllText(Path.Combine(root, VersionName), remoteVersion + Environment.NewLine);
            progress.Value = 100;
            version.Text = "Versao instalada: " + FormatVersion(remoteVersion);
            status.Text = "Atualizacao instalada. Pronto para jogar!";
            downloadInfo.Text = "Atualizacao concluida";
        }
        finally
        {
            progress.IsIndeterminate = false;
            pauseButton.Visible = false;
            downloadPaused = false;
            updateButton.Visible = true;
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    private async Task DownloadWithProgress(string url, string destination)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync();
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        var buffer = new byte[131072];
        long received = 0;
        var timer = Stopwatch.StartNew();
        var lastUiUpdate = TimeSpan.Zero;
        while (!total.HasValue || received < total.Value)
        {
            var bytesToRead = total.HasValue
                ? (int)Math.Min(buffer.Length, total.Value - received)
                : buffer.Length;
            if (bytesToRead <= 0) break;

            var read = await input.ReadAsync(buffer.AsMemory(0, bytesToRead));
            if (read <= 0) break;

            while (downloadPaused) await Task.Delay(100);
            await output.WriteAsync(buffer.AsMemory(0, read));
            received += read;
            if (total > 0)
            {
                progress.Value = (int)Math.Min(100, received * 100 / total.Value);
                if (timer.Elapsed - lastUiUpdate >= TimeSpan.FromMilliseconds(250))
                {
                    var bytesPerSecond = received / Math.Max(0.1, timer.Elapsed.TotalSeconds);
                    var remaining = total.Value - received;
                    downloadInfo.Text = $"↓  {FormatBytes(remaining)} restantes  @  {FormatBytes((long)bytesPerSecond)}/s";
                    lastUiUpdate = timer.Elapsed;
                }
            }
        }

        await output.FlushAsync();
        if (total.HasValue && received != total.Value)
            throw new EndOfStreamException($"Download incompleto: recebido {received} de {total.Value} bytes.");

        progress.Value = 100;
        downloadInfo.Text = "Download concluido. Preparando instalacao...";
    }

    private void StartGame()
    {
        var executableName = openGl.Checked ? OpenGlExecutable : DirectXExecutable;
        var executable = Path.Combine(root, executableName);
        if (!File.Exists(executable))
        {
            MessageBox.Show($"O executavel {executableName} nao esta instalado.\n\nInclua-o no pacote do cliente ou escolha outro modo grafico.", "Executavel nao encontrado", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        File.WriteAllText(Path.Combine(root, SettingsName), openGl.Checked ? "opengl" : "directx9");
        Process.Start(new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = true });
        Close();
    }

    private void RefreshExecutableState()
    {
        if (busy) return;
        var name = openGl.Checked ? OpenGlExecutable : DirectXExecutable;
        var exists = File.Exists(Path.Combine(root, name));
        playButton.Enabled = exists;
        if (!exists) status.Text = $"{name} nao esta instalado.";
    }

    private void SetBusy(bool value)
    {
        busy = value;
        updateButton.Enabled = !value;
        playButton.Enabled = !value;
        directX.Enabled = !value;
        openGl.Enabled = !value;
        rendererButton.Enabled = !value;
        UseWaitCursor = false;
        Cursor = Cursors.Default;
        Application.UseWaitCursor = false;
        Cursor.Current = Cursors.Default;
    }

    private string ReadText(string name)
    {
        var path = Path.Combine(root, name);
        return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
    }

    private static string FormatVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "nao identificada";
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) && value.Length > 19)
            return "GitHub " + value.Substring(7, 12);
        return value.Length > 32 ? value[..32] : value;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return value.ToString(value >= 100 ? "0" : "0.0") + " " + units[unit];
    }

    private static void ExtractSafely(string zipPath, string destination)
    {
        var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!target.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Pacote de atualizacao invalido.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    private static void CopyUpdate(string source, string destination)
    {
        var launcherName = Path.GetFileName(Environment.ProcessPath ?? "VelkharaLauncher.exe");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (PreservedFiles.Contains(relative) || relative.Equals(launcherName, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    private static string ReleaseUrl(string file) => $"https://github.com/{Repository}/releases/download/{ReleaseTag}/{file}";

    private static async Task<string> GetRemoteVersion()
    {
        try
        {
            return (await Http.GetStringAsync(ReleaseUrl(VersionName) + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())).Trim();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Releases antigas podem conter apenas o ZIP. Nesse caso, a identidade
            // do proprio asset serve como versao e muda a cada novo upload.
            var apiUrl = $"https://api.github.com/repos/{Repository}/releases/tags/{ReleaseTag}";
            using var response = await Http.GetAsync(apiUrl + "?t=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);
            foreach (var asset in json.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (!string.Equals(asset.GetProperty("name").GetString(), PackageName, StringComparison.OrdinalIgnoreCase)) continue;
                if (asset.TryGetProperty("digest", out var digest) && !string.IsNullOrWhiteSpace(digest.GetString())) return digest.GetString()!;
                return asset.GetProperty("updated_at").GetString() ?? throw new InvalidDataException("Versao do pacote indisponivel.");
            }
            throw new FileNotFoundException("O pacote de atualizacao nao foi encontrado na release.");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("VelkharaLauncher", "3.0"));
        return client;
    }
}

internal sealed class LauncherProgressBar : Control
{
    private int currentValue;
    private int animationOffset;
    private readonly System.Windows.Forms.Timer animationTimer;
    private bool isIndeterminate;

    public bool IsIndeterminate
    {
        get => isIndeterminate;
        set
        {
            if (isIndeterminate == value) return;
            isIndeterminate = value;
            animationOffset = 0;
            if (value) animationTimer.Start();
            else animationTimer.Stop();
            Invalidate();
        }
    }

    public int Value
    {
        get => currentValue;
        set
        {
            currentValue = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public LauncherProgressBar()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(37, 29, 21);
        ForeColor = Color.FromArgb(218, 158, 65);
        Font = new Font("Segoe UI Semibold", 7F);
        Cursor = Cursors.Default;
        animationTimer = new System.Windows.Forms.Timer { Interval = 35 };
        animationTimer.Tick += (_, _) =>
        {
            animationOffset = (animationOffset + 7) % Math.Max(1, Width + 120);
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);

        if (isIndeterminate)
        {
            const int bandWidth = 110;
            var x = animationOffset - bandWidth;
            using var animatedFill = new LinearGradientBrush(
                new Rectangle(x, 0, bandWidth, Height),
                Color.FromArgb(133, 82, 31),
                Color.FromArgb(230, 173, 76),
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(animatedFill, x, 0, bandWidth, Height);
            using var animatedBorder = new Pen(Color.FromArgb(112, 79, 43));
            e.Graphics.DrawRectangle(animatedBorder, 0, 0, Width - 1, Height - 1);
            TextRenderer.DrawText(e.Graphics, "PROCESSANDO", Font, new Rectangle(8, 0, Width - 16, Height), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }

        var fillWidth = (int)Math.Round(Width * currentValue / 100D);
        if (fillWidth > 0)
        {
            using var fill = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(1, fillWidth), Height), Color.FromArgb(174, 105, 35), Color.FromArgb(231, 174, 74), LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(fill, 0, 0, fillWidth, Height);
            if (currentValue < 100)
            {
                using var marker = new SolidBrush(Color.White);
                e.Graphics.FillRectangle(marker, Math.Max(0, fillWidth - 2), 0, 3, Height);
            }
        }
        using var border = new Pen(Color.FromArgb(112, 79, 43));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        var text = currentValue + "%";
        TextRenderer.DrawText(e.Graphics, text, Font, new Rectangle(8, 0, Width - 16, Height), Color.White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var commandLine = args.Any(x => x.Equals("--package", StringComparison.OrdinalIgnoreCase));
        try
        {
            if (commandLine)
            {
                RunFromCommandLine();
                return 0;
            }

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) => WriteFatalLog(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                WriteFatalLog(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
            Application.Run(new PackagerForm());
            return 0;
        }
        catch (Exception ex)
        {
            var crashPath = Path.Combine(AppContext.BaseDirectory, "client-packager-crash.log");
            File.WriteAllText(crashPath, ex.ToString());
            if (!commandLine)
                MessageBox.Show(ex.Message + "\n\nDetalhes: " + crashPath, "Client Packager - erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static void RunFromCommandLine()
    {
        var configPath = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }
            .Select(root => Path.Combine(root, "client_packager_config.lua"))
            .FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("client_packager_config.lua não foi encontrado.");
        var configDir = Path.GetDirectoryName(configPath)!;
        var logPath = Path.Combine(configDir, "client-packager.log");
        var progress = new DirectProgress<PackagerProgress>(value =>
            File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] {value.Percent,3}% {value.Stage}: {value.Detail}{Environment.NewLine}"));
        var result = PackagerEngine.Run(PackagerConfigFile.Load(configPath), configDir, progress, CancellationToken.None);
        File.AppendAllText(logPath, $"[{DateTime.Now:HH:mm:ss}] Concluído: {result.Copied} copiados, {result.Skipped} ignorados.{Environment.NewLine}");
    }

    private static void WriteFatalLog(Exception exception)
    {
        var crashPath = Path.Combine(AppContext.BaseDirectory, "client-packager-crash.log");
        File.AppendAllText(crashPath, $"[{DateTime.Now:O}]\n{exception}\n\n");
        MessageBox.Show(exception.Message + "\n\nDetalhes: " + crashPath, "Client Packager - erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

internal sealed class DirectProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}

internal sealed class PackagerConfig
{
    public string Source { get; set; } = "otcv8-dev-master";
    public string Destination { get; set; } = "VTC VELKHARA";
    public bool ClearDestination { get; set; } = true;
    public bool PublishGitHub { get; set; } = true;
    public string GitHubRepository { get; set; } = "brunoian1/NewHorizonOTCV8";
    public string GitHubReleaseTag { get; set; } = "client-latest";
    public string GitHubReleaseTitle { get; set; } = "VTC Velkhara - Client Atualizado";
    public string GitHubReleaseUrl { get; set; } = "https://github.com/brunoian1/NewHorizonOTCV8/releases/tag/client-latest";
    public string ZipName { get; set; } = "NewHorizons.zip";
    public List<string> Items { get; set; } = [];
    public List<string> Excludes { get; set; } = [];
}

internal readonly record struct PackagerProgress(int Percent, string Stage, string Detail, int Copied, int Skipped);

internal sealed class PackagerForm : Form
{
    private static readonly Color Background = Color.FromArgb(15, 14, 12);
    private static readonly Color Surface = Color.FromArgb(24, 21, 18);
    private static readonly Color Surface2 = Color.FromArgb(31, 28, 24);
    private static readonly Color Border = Color.FromArgb(73, 59, 42);
    private static readonly Color Gold = Color.FromArgb(205, 151, 72);
    private static readonly Color TextPrimary = Color.FromArgb(237, 229, 213);
    private static readonly Color TextMuted = Color.FromArgb(164, 155, 143);
    private static readonly Color Blue = Color.FromArgb(42, 116, 157);

    private readonly string configPath;
    private readonly string configDir;
    private readonly TextBox source = Field();
    private readonly TextBox destination = Field();
    private readonly TextBox items = MultiField();
    private readonly TextBox excludes = MultiField();
    private readonly CheckBox clearDestination = Check("Limpar destino antes de copiar");
    private readonly CheckBox publishGitHub = Check("Publicar GitHub Release ao concluir");
    private readonly TextBox repository = Field();
    private readonly TextBox releaseTag = Field();
    private readonly TextBox releaseTitle = Field();
    private readonly TextBox releaseUrl = Field();
    private readonly TextBox zipName = Field();
    private readonly RichTextBox log = new();
    private readonly ProgressBar progress = new();
    private readonly Label stage = LabelOf("Pronto para empacotar", 10F, TextPrimary);
    private readonly Label counters = LabelOf("0 arquivos copiados  •  0 ignorados", 8.5F, TextMuted);
    private readonly Button start = ActionButton("START", Gold);
    private readonly Button cancel = ActionButton("CANCELAR", Color.FromArgb(83, 55, 39));
    private readonly Button save = SecondaryButton("SALVAR CONFIG");
    private readonly Button auth = SecondaryButton("CONECTAR GITHUB");
    private readonly Button openRelease = SecondaryButton("ABRIR RELEASE");
    private CancellationTokenSource? cancellation;
    private bool dragging;
    private Point dragOffset;

    public PackagerForm()
    {
        configPath = FindConfig();
        configDir = Path.GetDirectoryName(configPath)!;

        Text = "Velkhara Client Packager";
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(1040, 700);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Background;
        ForeColor = TextPrimary;
        Font = new Font("Segoe UI", 9F);
        FormBorderStyle = FormBorderStyle.None;

        var header = BuildHeader();
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 14, 18, 16),
            BackColor = Background,
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        body.Controls.Add(BuildSettings(), 0, 0);
        body.Controls.Add(BuildOperations(), 1, 0);

        Controls.Add(body);
        Controls.Add(header);
        header.BringToFront();

        LoadIntoForm(PackagerConfigFile.Load(configPath));
        UpdateGitHubState();
    }

    private Panel BuildHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 52, BackColor = Color.FromArgb(25, 22, 19) };
        var mark = LabelOf("V", 15F, Gold, FontStyle.Bold);
        mark.Font = new Font("Georgia", 15F, FontStyle.Bold);
        mark.TextAlign = ContentAlignment.MiddleCenter;
        mark.Location = new Point(12, 5);
        mark.Size = new Size(36, 40);

        var title = LabelOf("Velkhara Client Packager", 11F, TextPrimary, FontStyle.Bold);
        title.Location = new Point(50, 0);
        title.Size = new Size(330, 50);
        title.TextAlign = ContentAlignment.MiddleLeft;

        var subtitle = LabelOf("BUILD  •  PACKAGE  •  RELEASE", 8F, Gold, FontStyle.Bold);
        subtitle.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        subtitle.Location = new Point(ClientSize.Width - 380, 0);
        subtitle.Size = new Size(260, 50);
        subtitle.TextAlign = ContentAlignment.MiddleRight;

        var minimize = WindowButton("—");
        minimize.Dock = DockStyle.Right;
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        var close = WindowButton("×");
        close.Dock = DockStyle.Right;
        close.Click += (_, _) => Close();
        close.FlatAppearance.MouseOverBackColor = Color.FromArgb(170, 48, 48);
        minimize.FlatAppearance.MouseOverBackColor = Surface2;

        foreach (var control in new Control[] { panel, mark, title })
        {
            control.MouseDown += BeginDrag;
            control.MouseMove += MoveDrag;
            control.MouseUp += (_, _) => dragging = false;
        }

        panel.Controls.AddRange([mark, title, subtitle, minimize, close]);
        close.BringToFront();
        minimize.BringToFront();
        return panel;
    }

    private Control BuildSettings()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill, Appearance = TabAppearance.Normal };
        tabs.TabPages.Add(BuildPathsTab());
        tabs.TabPages.Add(BuildContentTab());
        tabs.TabPages.Add(BuildGitHubTab());
        tabs.SelectedIndexChanged += (_, _) => StyleTabs(tabs);
        StyleTabs(tabs);
        return tabs;
    }

    private TabPage BuildPathsTab()
    {
        var page = Page("Diretórios");
        var layout = FormGrid();
        AddField(layout, 0, "ORIGEM DO CLIENTE", source, BrowseFolder(source));
        AddField(layout, 1, "DESTINO DO PACOTE", destination, BrowseFolder(destination));
        AddField(layout, 2, "LINK PUBLICADO / DESTINO DO UPLOAD", releaseUrl);
        AddField(layout, 3, "REPOSITÓRIO GITHUB (USUÁRIO/REPOSITÓRIO)", repository);
        AddField(layout, 4, "TAG DA RELEASE", releaseTag);
        AddField(layout, 5, "NOME DO ARQUIVO ZIP", zipName);
        layout.Controls.Add(clearDestination, 0, 12);
        layout.SetColumnSpan(clearDestination, 3);
        layout.Controls.Add(publishGitHub, 0, 13);
        layout.SetColumnSpan(publishGitHub, 3);

        var hint = LabelOf("Todos os caminhos, endereços e o link final podem ser alterados aqui antes de clicar em START.", 8.5F, TextMuted);
        hint.Dock = DockStyle.Top;
        hint.Height = 30;
        layout.Controls.Add(hint, 0, 14);
        layout.SetColumnSpan(hint, 3);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildContentTab()
    {
        var page = Page("Conteúdo");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 2 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        split.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Controls.Add(LabelOf("ITENS INCLUÍDOS (um por linha)", 8F, Gold, FontStyle.Bold), 0, 0);
        split.Controls.Add(LabelOf("CAMINHOS IGNORADOS (um por linha)", 8F, Gold, FontStyle.Bold), 1, 0);
        items.Dock = DockStyle.Fill;
        excludes.Dock = DockStyle.Fill;
        items.Margin = new Padding(0, 0, 7, 0);
        excludes.Margin = new Padding(7, 0, 0, 0);
        split.Controls.Add(items, 0, 1);
        split.Controls.Add(excludes, 1, 1);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildGitHubTab()
    {
        var page = Page("GitHub / Release");
        var layout = FormGrid();
        AddField(layout, 0, "TÍTULO DA RELEASE", releaseTitle);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, FlowDirection = FlowDirection.LeftToRight };
        auth.Click += (_, _) => StartGitHubLogin();
        openRelease.Click += (_, _) => OpenReleasePage();
        buttons.Controls.AddRange([auth, openRelease]);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 3);
        page.Controls.Add(layout);
        return page;
    }

    private Control BuildOperations()
    {
        var outer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 0, 0), BackColor = Background };
        var card = new Panel { Dock = DockStyle.Fill, Padding = new Padding(16), BackColor = Surface };
        AddBorder(card);

        var title = LabelOf("PROCESSO DE PUBLICAÇÃO", 9F, Gold, FontStyle.Bold);
        title.Dock = DockStyle.Top;
        title.Height = 26;
        stage.Dock = DockStyle.Top;
        stage.Height = 30;
        counters.Dock = DockStyle.Top;
        counters.Height = 23;
        progress.Dock = DockStyle.Top;
        progress.Height = 15;
        progress.Style = ProgressBarStyle.Continuous;

        log.Dock = DockStyle.Fill;
        log.BackColor = Color.FromArgb(11, 11, 10);
        log.ForeColor = Color.FromArgb(194, 207, 188);
        log.BorderStyle = BorderStyle.None;
        log.ReadOnly = true;
        log.Font = new Font("Cascadia Mono", 8.5F);
        log.Margin = new Padding(0, 12, 0, 12);

        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 48,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 5, 0, 0)
        };
        start.Size = new Size(145, 36);
        cancel.Size = new Size(105, 36);
        save.Size = new Size(125, 36);
        cancel.Enabled = false;
        start.Click += async (_, _) => await StartPackaging();
        cancel.Click += (_, _) => cancellation?.Cancel();
        save.Click += (_, _) => SaveFromForm(true);
        actionBar.Controls.AddRange([start, cancel, save]);

        card.Controls.Add(log);
        card.Controls.Add(actionBar);
        card.Controls.Add(progress);
        card.Controls.Add(counters);
        card.Controls.Add(stage);
        card.Controls.Add(title);
        outer.Controls.Add(card);
        return outer;
    }

    private async Task StartPackaging()
    {
        PackagerConfig config;
        try
        {
            config = ReadForm();
            PackagerEngine.Validate(config, configDir);
            PackagerConfigFile.Save(configPath, config);
            LoadIntoForm(config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Configuração inválida", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        cancellation = new CancellationTokenSource();
        SetBusy(true);
        log.Clear();
        AppendLog("Velkhara Client Packager iniciado.");

        var reporter = new Progress<PackagerProgress>(value =>
        {
            progress.Value = Math.Clamp(value.Percent, 0, 100);
            stage.Text = value.Stage;
            counters.Text = $"{value.Copied:N0} arquivos copiados  •  {value.Skipped:N0} ignorados";
            if (!string.IsNullOrWhiteSpace(value.Detail)) AppendLog(value.Detail);
        });

        try
        {
            var result = await Task.Run(() => PackagerEngine.Run(config, configDir, reporter, cancellation.Token));
            progress.Value = 100;
            stage.Text = config.PublishGitHub ? "Publicado com sucesso" : "Pacote criado com sucesso";
            AppendLog($"Concluído: {result.Copied:N0} copiados, {result.Skipped:N0} ignorados.");
            if (!string.IsNullOrWhiteSpace(result.ReleaseUrl)) AppendLog("Release: " + result.ReleaseUrl);
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (OperationCanceledException)
        {
            stage.Text = "Operação cancelada";
            AppendLog("Cancelado pelo usuário.");
        }
        catch (Exception ex)
        {
            stage.Text = "Falha no empacotamento";
            AppendLog("ERRO: " + ex.Message);
            MessageBox.Show(ex.Message, "Falha ao publicar", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
            cancellation.Dispose();
            cancellation = null;
            UpdateGitHubState();
        }
    }

    private void SetBusy(bool value)
    {
        start.Enabled = !value;
        save.Enabled = !value;
        cancel.Enabled = value;
        UseWaitCursor = value;
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired) { BeginInvoke(() => AppendLog(message)); return; }
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        log.AppendText(line);
        File.AppendAllText(Path.Combine(configDir, "client-packager.log"), line);
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    private PackagerConfig ReadForm() => new()
    {
        Source = source.Text.Trim(),
        Destination = destination.Text.Trim(),
        ClearDestination = clearDestination.Checked,
        PublishGitHub = publishGitHub.Checked,
        GitHubRepository = repository.Text.Trim(),
        GitHubReleaseTag = releaseTag.Text.Trim(),
        GitHubReleaseTitle = releaseTitle.Text.Trim(),
        GitHubReleaseUrl = releaseUrl.Text.Trim(),
        ZipName = zipName.Text.Trim(),
        Items = Lines(items),
        Excludes = Lines(excludes)
    };

    private void LoadIntoForm(PackagerConfig value)
    {
        source.Text = value.Source;
        destination.Text = value.Destination;
        clearDestination.Checked = value.ClearDestination;
        publishGitHub.Checked = value.PublishGitHub;
        repository.Text = value.GitHubRepository;
        releaseTag.Text = value.GitHubReleaseTag;
        releaseTitle.Text = value.GitHubReleaseTitle;
        releaseUrl.Text = string.IsNullOrWhiteSpace(value.GitHubReleaseUrl)
            ? PackagerEngine.BuildReleaseUrl(value.GitHubRepository, value.GitHubReleaseTag)
            : value.GitHubReleaseUrl;
        zipName.Text = value.ZipName;
        items.Lines = value.Items.ToArray();
        excludes.Lines = value.Excludes.ToArray();
    }

    private void SaveFromForm(bool notify)
    {
        PackagerConfigFile.Save(configPath, ReadForm());
        if (notify)
        {
            stage.Text = "Configuração salva";
            AppendLog("Configuração atualizada em " + configPath);
        }
    }

    private void StartGitHubLogin()
    {
        var gh = PackagerEngine.FindGh(configDir);
        if (gh is null)
        {
            MessageBox.Show("gh.exe não foi encontrado. Coloque-o ao lado do ClientPackager.", "GitHub CLI");
            return;
        }
        Process.Start(new ProcessStartInfo(gh, "auth login --hostname github.com --web --git-protocol https") { UseShellExecute = true });
    }

    private void OpenReleasePage()
    {
        var url = releaseUrl.Text.Trim();
        if (url.Length == 0) url = PackagerEngine.BuildReleaseUrl(repository.Text.Trim(), releaseTag.Text.Trim());
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void UpdateGitHubState()
    {
        var gh = PackagerEngine.FindGh(configDir);
        auth.Text = gh is null ? "GH.EXE NÃO ENCONTRADO" : "CONECTAR GITHUB";
        auth.Enabled = gh is not null;
    }

    private Button BrowseFolder(TextBox target)
    {
        var button = SecondaryButton("...");
        button.Width = 42;
        button.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { ShowNewFolderButton = true };
            var current = PackagerEngine.ResolvePath(target.Text, configDir);
            if (Directory.Exists(current)) dialog.InitialDirectory = current;
            if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
        };
        return button;
    }

    private static List<string> Lines(TextBox box) => box.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static TabPage Page(string title) => new(title) { BackColor = Surface, ForeColor = TextPrimary, Padding = new Padding(0) };
    private static TableLayoutPanel FormGrid()
    {
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 3, RowCount = 15, AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        for (var i = 0; i < 12; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, i % 2 == 0 ? 25 : 39));
        return grid;
    }

    private static void AddField(TableLayoutPanel grid, int index, string caption, TextBox field, Button? browse = null)
    {
        var row = index * 2;
        var label = LabelOf(caption, 8F, Gold, FontStyle.Bold);
        label.Dock = DockStyle.Fill;
        grid.Controls.Add(label, 0, row);
        grid.SetColumnSpan(label, 3);
        field.Dock = DockStyle.Fill;
        grid.Controls.Add(field, 0, row + 1);
        if (browse is not null) grid.Controls.Add(browse, 2, row + 1);
    }

    private static TextBox Field() => new()
    {
        BackColor = Color.FromArgb(12, 12, 11),
        ForeColor = TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5F)
    };

    private static TextBox MultiField()
    {
        var box = Field();
        box.Multiline = true;
        box.ScrollBars = ScrollBars.Vertical;
        box.AcceptsReturn = true;
        box.WordWrap = false;
        box.Font = new Font("Cascadia Mono", 9F);
        return box;
    }

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, ForeColor = TextPrimary, FlatStyle = FlatStyle.Flat };
    private static Label LabelOf(string text, float size, Color color, FontStyle style = FontStyle.Regular) => new()
    {
        Text = text,
        ForeColor = color,
        BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, style),
        AutoSize = false
    };

    private static Button ActionButton(string text, Color color)
    {
        var button = new Button { Text = text, BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, .12F);
        return button;
    }

    private static Button SecondaryButton(string text)
    {
        var button = ActionButton(text, Color.FromArgb(31, 37, 45));
        button.ForeColor = Color.FromArgb(190, 196, 207);
        button.FlatAppearance.BorderColor = Color.FromArgb(65, 72, 84);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private static Button WindowButton(string text)
    {
        var button = new Button { Text = text, Size = new Size(50, 52), FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(25, 22, 19), ForeColor = TextMuted, Font = new Font("Segoe UI", 13F), TabStop = false };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = text == "×" ? Color.FromArgb(190, 55, 55) : Color.FromArgb(48, 43, 37);
        return button;
    }

    private static void AddBorder(Control control)
    {
        control.Paint += (_, e) =>
        {
            using var pen = new Pen(Border);
            e.Graphics.DrawRectangle(pen, 0, 0, control.Width - 1, control.Height - 1);
        };
    }

    private static void StyleTabs(TabControl tabs)
    {
        tabs.BackColor = Surface;
    }

    private void BeginDrag(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        dragging = true;
        dragOffset = e.Location;
    }

    private void MoveDrag(object? sender, MouseEventArgs e)
    {
        if (!dragging) return;
        var screen = ((Control)sender!).PointToScreen(e.Location);
        Location = new Point(screen.X - dragOffset.X, screen.Y - dragOffset.Y);
    }

    private static string FindConfig()
    {
        foreach (var root in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var candidate = Path.Combine(root, "client_packager_config.lua");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("client_packager_config.lua não foi encontrado.");
    }
}

internal readonly record struct PackagerResult(int Copied, int Skipped, string ReleaseUrl);

internal static class PackagerEngine
{
    public static void Validate(PackagerConfig config, string baseDir)
    {
        ApplyReleaseUrl(config);
        if (config.Items.Count == 0) throw new InvalidOperationException("Informe pelo menos um item para copiar.");
        var source = ResolvePath(config.Source, baseDir);
        var destination = ResolvePath(config.Destination, baseDir);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException("Origem não encontrada: " + source);
        if (SamePath(source, destination)) throw new InvalidOperationException("Origem e destino não podem ser a mesma pasta.");
        if (config.PublishGitHub && string.IsNullOrWhiteSpace(config.GitHubRepository)) throw new InvalidOperationException("Informe o repositório GitHub.");
        if (config.PublishGitHub && FindGh(baseDir) is null) throw new FileNotFoundException("gh.exe não foi encontrado ao lado do ClientPackager ou no PATH.");
        if (config.PublishGitHub && !IsGitHubAuthenticated(baseDir))
            throw new InvalidOperationException("GitHub ainda não está autenticado. Clique em CONECTAR GITHUB e conclua o login antes de iniciar.");
    }

    public static PackagerResult Run(PackagerConfig config, string baseDir, IProgress<PackagerProgress> progress, CancellationToken token)
    {
        Validate(config, baseDir);
        var sourceRoot = ResolvePath(config.Source, baseDir);
        var destinationRoot = ResolvePath(config.Destination, baseDir);
        var excludes = config.Excludes.Select(Normalize).ToList();
        var manifest = BuildManifest(config, sourceRoot, excludes, token);
        var total = Math.Max(manifest.Count, 1);
        var copied = 0;
        var skipped = 0;

        progress.Report(new(0, "Preparando destino", destinationRoot, copied, skipped));
        if (config.ClearDestination && Directory.Exists(destinationRoot)) ClearDirectory(destinationRoot, token);
        Directory.CreateDirectory(destinationRoot);

        foreach (var entry in manifest)
        {
            token.ThrowIfCancellationRequested();
            if (entry.Source is null)
            {
                skipped++;
                progress.Report(new(Percent(copied + skipped, total), "Copiando arquivos", "Ignorado: " + entry.Relative, copied, skipped));
                continue;
            }
            CopyFile(entry.Source, Path.Combine(destinationRoot, entry.Relative));
            copied++;
            if (copied % 50 == 0 || copied == manifest.Count)
                progress.Report(new(Percent(copied + skipped, total), "Copiando arquivos", entry.Relative, copied, skipped));
        }

        var releaseUrl = "";
        token.ThrowIfCancellationRequested();
        var versionPath = Path.Combine(destinationRoot, "client-version.txt");
        File.WriteAllText(versionPath, DateTime.UtcNow.ToString("yyyyMMddHHmmss") + Environment.NewLine);
        var zipPath = Path.Combine(baseDir, config.ZipName);
        progress.Report(new(92, "Criando pacote ZIP", zipPath, copied, skipped));
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(destinationRoot, zipPath, CompressionLevel.Optimal, false);

        if (config.PublishGitHub)
        {
            token.ThrowIfCancellationRequested();
            progress.Report(new(96, "Publicando GitHub Release", config.GitHubRepository, copied, skipped));
            Publish(config, zipPath, versionPath, baseDir, token);
            releaseUrl = config.GitHubReleaseUrl;
        }

        progress.Report(new(100, "Concluído", "", copied, skipped));
        return new(copied, skipped, releaseUrl);
    }

    private readonly record struct ManifestEntry(string? Source, string Relative);

    private static List<ManifestEntry> BuildManifest(PackagerConfig config, string sourceRoot, List<string> excludes, CancellationToken token)
    {
        var result = new List<ManifestEntry>();
        foreach (var item in config.Items)
        {
            token.ThrowIfCancellationRequested();
            var relative = Normalize(item);
            var full = Path.Combine(sourceRoot, relative);
            if (IsExcluded(relative, excludes)) { result.Add(new(null, relative)); continue; }
            if (File.Exists(full)) { result.Add(new(full, relative)); continue; }
            if (!Directory.Exists(full)) throw new FileNotFoundException("Item obrigatório não encontrado: " + full);
            foreach (var file in Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                var child = Path.GetRelativePath(sourceRoot, file);
                if (IsExcluded(child, excludes)) { result.Add(new(null, child)); continue; }
                result.Add(new(file, child));
            }
        }

        AddCurrentClientExecutable(result, sourceRoot, "otclient_dx.exe");
        AddCurrentClientExecutable(result, sourceRoot, "otclient_gl.exe");
        return result;
    }

    private static void AddCurrentClientExecutable(List<ManifestEntry> manifest, string sourceRoot, string fileName)
    {
        var currentExecutable = Path.Combine(sourceRoot, fileName);
        if (!File.Exists(currentExecutable)) return;

        var packagePath = Normalize(Path.Combine("bin", fileName));
        manifest.RemoveAll(entry => entry.Relative.Equals(packagePath, StringComparison.OrdinalIgnoreCase));
        manifest.Add(new(currentExecutable, packagePath));
    }

    private static void Publish(PackagerConfig config, string zipPath, string versionPath, string baseDir, CancellationToken token)
    {
        var gh = FindGh(baseDir) ?? throw new FileNotFoundException("gh.exe não encontrado.");
        if (Run(gh, ["auth", "status"], token, false) != 0)
            throw new InvalidOperationException("GitHub não autenticado. Use o botão CONECTAR GITHUB.");

        var exists = Run(gh, ["release", "view", config.GitHubReleaseTag, "--repo", config.GitHubRepository], token, false) == 0;
        if (!exists)
        {
            var creation = RunWithError(gh, ["release", "create", config.GitHubReleaseTag, "--repo", config.GitHubRepository, "--target", "main", "--title", config.GitHubReleaseTitle, "--notes", "Cliente atualizado automaticamente pelo ClientPackager.", "--latest"], token);
            if (creation.ExitCode != 0) throw new InvalidOperationException("Falha ao criar a release no GitHub: " + creation.Error);
        }
        var upload = RunWithError(gh, ["release", "upload", config.GitHubReleaseTag, zipPath, versionPath, "--repo", config.GitHubRepository, "--clobber"], token);
        if (upload.ExitCode != 0) throw new InvalidOperationException("Falha ao enviar o ZIP para o GitHub: " + upload.Error);
    }

    private static int Run(string executable, IEnumerable<string> arguments, CancellationToken token, bool output)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = !output, RedirectStandardError = !output };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível executar " + executable);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        process.WaitForExit();
        return process.ExitCode;
    }

    private static (int ExitCode, string Error) RunWithError(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Não foi possível executar " + executable);
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch { } });
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        var error = string.Join(" ", new[] { standardError, standardOutput }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
        return (process.ExitCode, error);
    }

    public static string? FindGh(string baseDir)
    {
        foreach (var candidate in new[]
        {
            Path.Combine(baseDir, "gh.exe"),
            Path.Combine(AppContext.BaseDirectory, "gh.exe"),
            "gh.exe"
        })
        {
            if (Path.IsPathRooted(candidate) && File.Exists(candidate)) return candidate;
            if (!Path.IsPathRooted(candidate))
            {
                var paths = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator);
                var match = paths.Select(x => Path.Combine(x, candidate)).FirstOrDefault(File.Exists);
                if (match is not null) return match;
            }
        }
        return null;
    }

    public static bool IsGitHubAuthenticated(string baseDir)
    {
        var gh = FindGh(baseDir);
        if (gh is null) return false;
        try { return Run(gh, ["auth", "status"], CancellationToken.None, false) == 0; }
        catch { return false; }
    }

    public static string BuildReleaseUrl(string repository, string tag) =>
        $"https://github.com/{repository.Trim().Trim('/')}/releases/tag/{tag.Trim()}";

    private static void ApplyReleaseUrl(PackagerConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.GitHubReleaseUrl))
        {
            config.GitHubReleaseUrl = BuildReleaseUrl(config.GitHubRepository, config.GitHubReleaseTag);
            return;
        }

        if (!Uri.TryCreate(config.GitHubReleaseUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O link da release precisa ser uma URL válida do GitHub.");

        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 5 || !parts[2].Equals("releases", StringComparison.OrdinalIgnoreCase) ||
            !parts[3].Equals("tag", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Use o formato: https://github.com/usuario/repositorio/releases/tag/tag");

        config.GitHubRepository = parts[0] + "/" + parts[1];
        config.GitHubReleaseTag = Uri.UnescapeDataString(parts[4]);
        config.GitHubReleaseUrl = BuildReleaseUrl(config.GitHubRepository, config.GitHubReleaseTag);
    }

    public static string ResolvePath(string path, string baseDir)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path ?? "");
        return Path.GetFullPath(Path.IsPathRooted(expanded) ? expanded : Path.Combine(baseDir, expanded));
    }

    private static void ClearDirectory(string path, CancellationToken token)
    {
        var resolved = Path.GetFullPath(path);
        if (resolved.Length < 6 || resolved == Path.GetPathRoot(resolved)) throw new InvalidOperationException("Destino inseguro para limpeza: " + resolved);
        foreach (var file in Directory.EnumerateFiles(resolved)) { token.ThrowIfCancellationRequested(); File.Delete(file); }
        foreach (var directory in Directory.EnumerateDirectories(resolved)) { token.ThrowIfCancellationRequested(); Directory.Delete(directory, true); }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, true);
    }

    private static bool IsExcluded(string path, IEnumerable<string> excludes)
    {
        var normalized = Normalize(path);
        return excludes.Any(exclude => normalized.Equals(exclude, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(exclude + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private static string Normalize(string path) => (path ?? "").Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
    private static bool SamePath(string left, string right) => Path.GetFullPath(left).TrimEnd('\\').Equals(Path.GetFullPath(right).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
    private static int Percent(int done, int total) => Math.Min(90, (int)Math.Round(done * 90D / Math.Max(total, 1)));
}

internal static class PackagerConfigFile
{
    public static PackagerConfig Load(string path)
    {
        var text = StripComments(File.ReadAllText(path));
        var config = new PackagerConfig
        {
            Source = ReadString(text, "source") ?? "otcv8-dev-master",
            Destination = ReadString(text, "destination") ?? "VTC VELKHARA",
            ClearDestination = ReadBool(text, "clearDestination") ?? true,
            PublishGitHub = ReadBool(text, "publishGitHub") ?? true,
            GitHubRepository = ReadString(text, "githubRepository") ?? "brunoian1/NewHorizonOTCV8",
            GitHubReleaseTag = ReadString(text, "githubReleaseTag") ?? "client-latest",
            GitHubReleaseTitle = ReadString(text, "githubReleaseTitle") ?? "VTC Velkhara - Client Atualizado",
            GitHubReleaseUrl = ReadString(text, "githubReleaseUrl") ?? "",
            ZipName = ReadString(text, "zipName") ?? "NewHorizons.zip",
            Items = ReadList(text, "items"),
            Excludes = ReadList(text, "exclude")
        };
        if (config.Items.Count == 0)
            config.Items.AddRange(["data", "layouts", "mods", "modules", "init.lua", "d3dcompiler_47.dll", "libEGL.dll", "libGLESv2.dll", "otclient_dx.exe", "otclient_gl.exe"]);
        if (string.IsNullOrWhiteSpace(config.GitHubReleaseUrl))
            config.GitHubReleaseUrl = PackagerEngine.BuildReleaseUrl(config.GitHubRepository, config.GitHubReleaseTag);
        return config;
    }

    public static void Save(string path, PackagerConfig config)
    {
        static string Q(string value) => "[[" + value.Replace("]]", "") + "]]";
        static string ListBlock(IEnumerable<string> values) => string.Join(Environment.NewLine, values.Select(x => "  " + Q(x) + ","));
        var text = $$"""
source = {{Q(config.Source)}}
destination = {{Q(config.Destination)}}

-- Valores abaixo podem ser alterados pelo ClientPackager_NEW.
clearDestination = {{config.ClearDestination.ToString().ToLowerInvariant()}}
publishGitHub = {{config.PublishGitHub.ToString().ToLowerInvariant()}}
githubRepository = {{Q(config.GitHubRepository)}}
githubReleaseTag = {{Q(config.GitHubReleaseTag)}}
githubReleaseTitle = {{Q(config.GitHubReleaseTitle)}}
githubReleaseUrl = {{Q(config.GitHubReleaseUrl)}}
zipName = {{Q(config.ZipName)}}

items = {
{{ListBlock(config.Items)}}
}

exclude = {
{{ListBlock(config.Excludes)}}
}
""";
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    private static string StripComments(string text) => string.Join('\n', text.Replace("\r\n", "\n").Split('\n').Select(line => line.Split("--", 2)[0]));
    private static string? ReadString(string text, string name)
    {
        var match = Regex.Match(text, @"\b" + Regex.Escape(name) + @"\s*=\s*(?:\[\[(.*?)\]\]|""(.*?)""|'(.*?)')", RegexOptions.Singleline);
        return match.Success ? match.Groups.Cast<Group>().Skip(1).FirstOrDefault(x => x.Success)?.Value.Trim() : null;
    }
    private static bool? ReadBool(string text, string name)
    {
        var match = Regex.Match(text, @"\b" + Regex.Escape(name) + @"\s*=\s*(true|false)", RegexOptions.IgnoreCase);
        return match.Success ? bool.Parse(match.Groups[1].Value) : null;
    }
    private static List<string> ReadList(string text, string name)
    {
        var match = Regex.Match(text, @"\b" + Regex.Escape(name) + @"\s*=\s*\{(.*?)\}", RegexOptions.Singleline);
        if (!match.Success) return [];
        return Regex.Matches(match.Groups[1].Value, @"\[\[(.*?)\]\]|""(.*?)""|'(.*?)'", RegexOptions.Singleline)
            .Select(item => item.Groups.Cast<Group>().Skip(1).First(x => x.Success).Value.Trim())
            .Where(x => x.Length > 0).ToList();
    }
}

using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Windows.Forms;

internal static class Program
{
    internal const string ProductName = "Tosun Flux";
    internal const string ProductVersion = "0.3.0";
    internal const string Publisher = "Tosun";
    internal const string UninstallKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Tosun Flux";

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Any(item => item.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                InstallerOperations.Uninstall();
                return;
            }

            Application.Run(new InstallerForm());
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox _installPath = new();
    private readonly Button _browseButton = new();
    private readonly Button _installButton = new();
    private readonly ProgressBar _progress = new();
    private readonly Label _status = new();
    private readonly Panel _content = new();
    private readonly Button _desktopShortcutButton = new();
    private readonly Button _startMenuShortcutButton = new();
    private readonly Button _launchButton = new();
    private readonly Button _closeButton = new();
    private string _selectedInstallRoot = string.Empty;

    private static readonly Color Ink = Color.FromArgb(30, 38, 58);
    private static readonly Color Muted = Color.FromArgb(102, 112, 134);
    private static readonly Color Accent = Color.FromArgb(104, 123, 232);
    private static readonly Color AccentDark = Color.FromArgb(76, 95, 198);

    public InstallerForm()
    {
        Text = $"{Program.ProductName} 설치";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 500);
        MinimumSize = new Size(640, 460);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        BuildLayout();
    }

    private void BuildLayout()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 118,
            BackColor = Color.FromArgb(241, 244, 255),
            Padding = new Padding(34, 22, 34, 16)
        };
        header.Controls.Add(new Label
        {
            Text = "TOSUN FLUX",
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = Ink
        });
        header.Controls.Add(new Label
        {
            Text = "통합 파일 변환기를 설치합니다",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Muted
        });
        header.Controls.Add(new Label
        {
            Text = $"게시자 {Program.Publisher}  ·  버전 {Program.ProductVersion}",
            Dock = DockStyle.Bottom,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Muted
        });
        Controls.Add(header);

        _content.Dock = DockStyle.Fill;
        _content.Padding = new Padding(34, 24, 34, 22);
        Controls.Add(_content);
        ShowInstallPage();
    }

    private void ShowInstallPage()
    {
        _content.Controls.Clear();
        _selectedInstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Program.ProductName);

        _content.Controls.Add(CreateLabel("설치 위치", 13, FontStyle.Bold, Ink, DockStyle.Top, 30));
        _content.Controls.Add(CreateLabel("Tosun Flux를 설치할 폴더를 선택하세요. 설치에는 관리자 권한이 필요합니다.", 9.5f, FontStyle.Regular, Muted, DockStyle.Top, 34));

        var pathRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 44,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 8, 0, 0)
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
        _installPath.Text = _selectedInstallRoot;
        _installPath.Dock = DockStyle.Fill;
        _installPath.Font = new Font("Segoe UI", 10f);
        _installPath.Margin = new Padding(0, 0, 10, 0);
        _installPath.BorderStyle = BorderStyle.FixedSingle;
        pathRow.Controls.Add(_installPath, 0, 0);
        ConfigureButton(_browseButton, "찾아보기", false);
        _browseButton.Click -= BrowseButtonClicked;
        _browseButton.Click += BrowseButtonClicked;
        pathRow.Controls.Add(_browseButton, 1, 0);
        _content.Controls.Add(pathRow);

        _content.Controls.Add(CreateLabel("기본 위치: C:\\Program Files\\Tosun Flux", 9f, FontStyle.Regular, Muted, DockStyle.Top, 42));

        var info = new Panel
        {
            Dock = DockStyle.Top,
            Height = 126,
            BackColor = Color.FromArgb(248, 249, 253),
            Padding = new Padding(18, 14, 18, 12)
        };
        info.Controls.Add(CreateLabel("설치 안내", 10.5f, FontStyle.Bold, Ink, DockStyle.Top, 24));
        info.Controls.Add(CreateLabel("• 설치 후 Windows의 프로그램 설치 및 제거 목록에 Tosun Flux가 등록됩니다.\n• 설치 완료 후 바탕화면과 시작 메뉴 바로가기를 원하는 항목만 추가할 수 있습니다.\n• 기존 설치 폴더를 선택하면 필요한 파일을 새 버전으로 교체합니다.", 9.5f, FontStyle.Regular, Muted, DockStyle.Fill));
        _content.Controls.Add(info);

        _progress.Dock = DockStyle.Bottom;
        _progress.Height = 12;
        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Visible = false;
        _content.Controls.Add(_progress);

        _status.Dock = DockStyle.Bottom;
        _status.Height = 28;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Font = new Font("Segoe UI", 9f);
        _status.ForeColor = Muted;
        _status.Visible = false;
        _content.Controls.Add(_status);

        ConfigureButton(_installButton, "설치", true);
        _installButton.Dock = DockStyle.Bottom;
        _installButton.Height = 48;
        _installButton.Margin = new Padding(0, 14, 0, 0);
        _installButton.Click -= InstallButtonClicked;
        _installButton.Click += InstallButtonClicked;
        _content.Controls.Add(_installButton);
    }

    private void BrowseButtonClicked(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Tosun Flux를 설치할 폴더를 선택하세요.",
            SelectedPath = _installPath.Text
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installPath.Text = dialog.SelectedPath;
    }

    private async void InstallButtonClicked(object? sender, EventArgs e)
    {
        var path = _installPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            MessageBox.Show("설치 위치를 입력하세요.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            path = Path.GetFullPath(path);
        }
        catch
        {
            MessageBox.Show("설치 위치가 올바르지 않습니다.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (path.Equals(Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("드라이브 루트가 아닌 설치 폴더를 선택하세요.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        SetInstallingState(true);
        var progress = new Progress<InstallProgress>(value =>
        {
            _progress.Value = Math.Clamp(value.Percent, 0, 100);
            _status.Text = value.Message;
        });

        try
        {
            await Task.Run(() => InstallerOperations.Install(path, progress));
            _selectedInstallRoot = path;
            ShowCompletePage();
        }
        catch (Exception error)
        {
            SetInstallingState(false);
            MessageBox.Show(error.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SetInstallingState(bool installing)
    {
        _installPath.Enabled = !installing;
        _browseButton.Enabled = !installing;
        _installButton.Enabled = !installing;
        _progress.Visible = installing;
        _status.Visible = installing;
        if (installing)
        {
            _progress.Value = 0;
            _status.Text = "설치를 준비하는 중...";
        }
    }

    private void ShowCompletePage()
    {
        _content.Controls.Clear();
        _content.Controls.Add(CreateLabel("설치가 완료되었습니다", 17, FontStyle.Bold, Ink, DockStyle.Top, 42));
        _content.Controls.Add(CreateLabel($"{Program.ProductName}가 다음 위치에 설치되었습니다.\n{_selectedInstallRoot}\n\n원하는 바로가기를 추가한 뒤 Tosun Flux를 실행할 수 있습니다.", 10.5f, FontStyle.Regular, Muted, DockStyle.Top, 98));

        ConfigureButton(_desktopShortcutButton, "바탕화면 바로가기 추가", false);
        ConfigureButton(_startMenuShortcutButton, "시작 메뉴 바로가기 추가", false);
        _desktopShortcutButton.Dock = DockStyle.Top;
        _desktopShortcutButton.Height = 42;
        _desktopShortcutButton.Margin = new Padding(0, 5, 0, 5);
        _startMenuShortcutButton.Dock = DockStyle.Top;
        _startMenuShortcutButton.Height = 42;
        _startMenuShortcutButton.Margin = new Padding(0, 5, 0, 5);
        _desktopShortcutButton.Click -= AddDesktopShortcutClicked;
        _desktopShortcutButton.Click += AddDesktopShortcutClicked;
        _startMenuShortcutButton.Click -= AddStartMenuShortcutClicked;
        _startMenuShortcutButton.Click += AddStartMenuShortcutClicked;
        _content.Controls.Add(_startMenuShortcutButton);
        _content.Controls.Add(_desktopShortcutButton);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 50,
            ColumnCount = 2,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ConfigureButton(_closeButton, "닫기", false);
        ConfigureButton(_launchButton, "Tosun Flux 실행", true);
        _closeButton.Dock = DockStyle.Fill;
        _launchButton.Dock = DockStyle.Fill;
        _closeButton.Margin = new Padding(0, 0, 8, 0);
        _launchButton.Margin = new Padding(8, 0, 0, 0);
        _closeButton.Click -= CloseButtonClicked;
        _closeButton.Click += CloseButtonClicked;
        _launchButton.Click -= LaunchButtonClicked;
        _launchButton.Click += LaunchButtonClicked;
        actions.Controls.Add(_closeButton, 0, 0);
        actions.Controls.Add(_launchButton, 1, 0);
        _content.Controls.Add(actions);
    }

    private void AddDesktopShortcutClicked(object? sender, EventArgs e)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tosun Flux.lnk");
        InstallerOperations.CreateApplicationShortcut(path, _selectedInstallRoot);
        _desktopShortcutButton.Text = "바탕화면 바로가기 추가됨";
        _desktopShortcutButton.Enabled = false;
    }

    private void AddStartMenuShortcutClicked(object? sender, EventArgs e)
    {
        InstallerOperations.CreateStartMenuShortcut(_selectedInstallRoot);
        _startMenuShortcutButton.Text = "시작 메뉴 바로가기 추가됨";
        _startMenuShortcutButton.Enabled = false;
    }

    private void LaunchButtonClicked(object? sender, EventArgs e)
    {
        InstallerOperations.LaunchApplication(_selectedInstallRoot);
        Close();
    }

    private void CloseButtonClicked(object? sender, EventArgs e) => Close();

    private static Label CreateLabel(string text, float size, FontStyle style, Color color, DockStyle dock = DockStyle.None, int height = 0)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            Dock = dock,
            Height = height,
            AutoSize = false
        };
    }

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(214, 219, 233);
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : Ink;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.Padding = new Padding(8, 0, 8, 0);
        button.MouseEnter += (_, _) => button.BackColor = primary ? AccentDark : Color.FromArgb(246, 247, 252);
        button.MouseLeave += (_, _) => button.BackColor = primary ? Accent : Color.White;
    }
}

internal readonly record struct InstallProgress(int Percent, string Message);

internal static class InstallerOperations
{
    public static void Install(string installRoot, IProgress<InstallProgress> progress)
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"Tosun Flux Install {Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            progress.Report(new InstallProgress(3, "설치 패키지를 확인하는 중..."));
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                ?? throw new InvalidOperationException("설치 패키지 데이터를 찾을 수 없습니다.");
            var archivePath = Path.Combine(temporaryRoot, "payload.zip");
            using (var archive = File.Create(archivePath))
                resource.CopyTo(archive);

            var extractedRoot = Path.Combine(temporaryRoot, "payload");
            Directory.CreateDirectory(extractedRoot);
            using var zip = ZipFile.OpenRead(archivePath);
            for (var index = 0; index < zip.Entries.Count; index++)
            {
                var entry = zip.Entries[index];
                var destination = Path.GetFullPath(Path.Combine(extractedRoot, entry.FullName));
                if (!destination.StartsWith(extractedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("설치 패키지 경로가 올바르지 않습니다.");
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, true);
                }
                progress.Report(new InstallProgress(5 + (index + 1) * 50 / Math.Max(zip.Entries.Count, 1), "설치 파일을 준비하는 중..."));
            }

            if (!File.Exists(Path.Combine(extractedRoot, "Tosun Flux.exe")))
                throw new InvalidOperationException("Tosun Flux 실행 파일을 설치 패키지에서 찾을 수 없습니다.");

            var files = Directory.EnumerateFiles(extractedRoot, "*", SearchOption.AllDirectories).ToArray();
            Directory.CreateDirectory(installRoot);
            for (var index = 0; index < files.Length; index++)
            {
                var relative = Path.GetRelativePath(extractedRoot, files[index]);
                var destination = Path.Combine(installRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(files[index], destination, true);
                progress.Report(new InstallProgress(55 + (index + 1) * 32 / Math.Max(files.Length, 1), "프로그램 파일을 설치하는 중..."));
            }

            var uninstaller = Path.Combine(installRoot, "Uninstall Tosun Flux.exe");
            if (!string.Equals(Path.GetFullPath(Environment.ProcessPath!), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(Environment.ProcessPath!, uninstaller, true);
            RegisterUninstaller(installRoot, uninstaller);
            progress.Report(new InstallProgress(100, "설치가 완료되었습니다."));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }
    }

    public static void CreateApplicationShortcut(string shortcutPath, string installRoot)
    {
        CreateShortcut(shortcutPath, Path.Combine(installRoot, "Tosun Flux.exe"), "Tosun Flux 파일 통합 변환기");
    }

    public static void CreateStartMenuShortcut(string installRoot)
    {
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", Program.ProductName);
        Directory.CreateDirectory(startMenu);
        CreateApplicationShortcut(Path.Combine(startMenu, "Tosun Flux.lnk"), installRoot);
        CreateShortcut(Path.Combine(startMenu, "Tosun Flux 제거.lnk"), Path.Combine(installRoot, "Uninstall Tosun Flux.exe"), "Tosun Flux 제거", "--uninstall");
    }

    public static void LaunchApplication(string installRoot)
    {
        Process.Start(new ProcessStartInfo(Path.Combine(installRoot, "Tosun Flux.exe"))
        {
            WorkingDirectory = installRoot,
            UseShellExecute = true
        });
    }

    public static void Uninstall()
    {
        var installRoot = Path.GetDirectoryName(Environment.ProcessPath!)!;
        var desktopShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tosun Flux.lnk");
        if (File.Exists(desktopShortcut))
            File.Delete(desktopShortcut);

        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", Program.ProductName);
        if (Directory.Exists(startMenu))
            Directory.Delete(startMenu, true);

        Registry.LocalMachine.DeleteSubKeyTree(Program.UninstallKeyPath, false);
        var script = $"timeout /t 2 /nobreak >nul & rmdir /s /q \"{installRoot}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {script}")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static void RegisterUninstaller(string installRoot, string uninstaller)
    {
        using var key = Registry.LocalMachine.CreateSubKey(Program.UninstallKeyPath);
        if (key is null)
            throw new InvalidOperationException("Windows 프로그램 설치 및 제거 목록에 등록할 수 없습니다.");
        key.SetValue("DisplayName", Program.ProductName);
        key.SetValue("DisplayVersion", Program.ProductVersion);
        key.SetValue("Publisher", Program.Publisher);
        key.SetValue("InstallLocation", installRoot);
        key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall");
        key.SetValue("DisplayIcon", uninstaller);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", EstimateInstallSize(installRoot), RegistryValueKind.DWord);
    }

    private static int EstimateInstallSize(string installRoot)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
            bytes += new FileInfo(file).Length;
        return (int)Math.Clamp(bytes / 1024, 1, int.MaxValue);
    }

    private static void CreateShortcut(string path, string target, string description, string? arguments = null)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 바로가기 기능을 사용할 수 없습니다.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target;
        shortcut.WorkingDirectory = Path.GetDirectoryName(target);
        shortcut.IconLocation = $"{target},0";
        shortcut.Description = description;
        if (arguments is not null)
            shortcut.Arguments = arguments;
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }
}

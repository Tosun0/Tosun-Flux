using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows.Forms;

internal static class Program
{
    internal const string ProductName = "Tosun Flux";
    internal const string ProductVersion = "1.0.2";
    internal const string Publisher = "Tosun Studio";
    internal const string UninstallKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Tosun Flux";
    internal const string AppPathKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths\\Tosun Flux.exe";

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

            WaitForPreviousApplication(args);
            if (args.Any(item => item.Equals("--silent", StringComparison.OrdinalIgnoreCase)))
            {
                var installRoot = InstallerOperations.GetInitialInstallRoot();
                InstallerOperations.Install(new InstallOptions(installRoot, true, true), new Progress<InstallProgress>(_ => { }));
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

    private static void WaitForPreviousApplication(string[] args)
    {
        var index = Array.FindIndex(args, item => item.Equals("--wait-for-pid", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out var processId))
            return;
        try
        {
            Process.GetProcessById(processId).WaitForExit(30000);
        }
        catch (ArgumentException)
        {
            // 이전 앱이 이미 종료된 경우 바로 설치 화면을 엽니다.
        }
    }
}

internal sealed class InstallerForm : Form
{
    private readonly Panel _content = new();
    private readonly TextBox _installPath = new();
    private readonly CheckBox _desktopShortcut = new();
    private readonly CheckBox _startMenuShortcut = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private readonly Button _installButton = new();
    private string _installedRoot = string.Empty;
    private InstallResult _installResult;

    private static readonly Color Ink = Color.FromArgb(28, 34, 50);
    private static readonly Color Muted = Color.FromArgb(92, 102, 122);
    private static readonly Color Accent = Color.FromArgb(105, 122, 229);
    private static readonly Color AccentDark = Color.FromArgb(79, 95, 199);
    private static readonly Color Surface = Color.FromArgb(246, 248, 253);

    public InstallerForm()
    {
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"{Program.ProductName} 설치";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(740, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        BuildShell();
        if (InstallerOperations.TryGetInstalledRoot(out var installedRoot, out var installedVersion))
            ShowMaintenancePage(installedRoot, installedVersion);
        else
            ShowInstallPage();
    }

    private void BuildShell()
    {
        var header = new Panel
        {
            Location = Point.Empty,
            Size = new Size(ClientSize.Width, 126),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.FromArgb(239, 242, 255)
        };
        header.Controls.Add(CreateLabel("TOSUN FLUX", new Point(36, 24), new Size(520, 40), 22, FontStyle.Bold, Ink));
        header.Controls.Add(CreateLabel("토순의 파일 컨버터 설치·유지 관리 프로그램", new Point(38, 67), new Size(520, 25), 10.5f, FontStyle.Regular, Muted));
        header.Controls.Add(CreateLabel($"게시자 {Program.Publisher}  ·  v{Program.ProductVersion}", new Point(38, 95), new Size(520, 20), 9, FontStyle.Regular, Muted));
        header.Controls.Add(CreateLabel("© 2026 Tosun Studio. All rights reserved.", new Point(38, 111), new Size(580, 15), 8.5f, FontStyle.Regular, Muted));

        using var appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (appIcon is not null)
        {
            header.Controls.Add(new PictureBox
            {
                Image = appIcon.ToBitmap(),
                Location = new Point(642, 27),
                Size = new Size(70, 70),
                SizeMode = PictureBoxSizeMode.Zoom,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            });
        }

        _content.Location = new Point(0, 126);
        _content.Size = new Size(ClientSize.Width, ClientSize.Height - 126);
        _content.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _content.BackColor = Color.White;
        Controls.Add(_content);
        Controls.Add(header);
    }

    private void ShowInstallPage()
    {
        _content.Controls.Clear();
        _installPath.Text = InstallerOperations.GetInitialInstallRoot();

        _content.Controls.Add(CreateLabel("설치 준비", new Point(36, 20), new Size(668, 32), 16, FontStyle.Bold, Ink));
        _content.Controls.Add(CreateLabel("설치 위치와 바로가기를 선택한 뒤 설치를 누르세요.", new Point(36, 56), new Size(668, 24), 10, FontStyle.Regular, Muted));
        _content.Controls.Add(CreateLabel("설치 위치", new Point(36, 94), new Size(200, 23), 10, FontStyle.Bold, Ink));

        _installPath.Location = new Point(36, 122);
        _installPath.Size = new Size(524, 34);
        _installPath.Font = new Font("Segoe UI", 10f);
        _installPath.BorderStyle = BorderStyle.FixedSingle;
        _content.Controls.Add(_installPath);

        var browseButton = new Button { Location = new Point(574, 120), Size = new Size(130, 38) };
        ConfigureButton(browseButton, "찾아보기", false);
        browseButton.Click += (_, _) => BrowseInstallFolder();
        _content.Controls.Add(browseButton);

        _content.Controls.Add(CreateLabel("바로가기", new Point(36, 185), new Size(200, 23), 10, FontStyle.Bold, Ink));
        ConfigureCheckBox(_desktopShortcut, "바탕화면에 Tosun Flux 바로가기 만들기", new Point(39, 216));
        ConfigureCheckBox(_startMenuShortcut, "시작 메뉴에 Tosun Flux 바로가기 만들기", new Point(39, 251));
        _desktopShortcut.Checked = true;
        _startMenuShortcut.Checked = true;
        _content.Controls.Add(_desktopShortcut);
        _content.Controls.Add(_startMenuShortcut);

        var notice = new Panel { Location = new Point(36, 298), Size = new Size(668, 73), BackColor = Surface };
        notice.Controls.Add(CreateLabel("설치가 끝나기 전에 앱·백엔드 상태와 Windows 등록을 확인합니다.", new Point(16, 13), new Size(636, 22), 9.5f, FontStyle.Bold, Ink));
        notice.Controls.Add(CreateLabel("기본 위치는 C:\\Program Files\\Tosun Flux이며 관리자 권한이 필요합니다.", new Point(16, 40), new Size(636, 20), 9, FontStyle.Regular, Muted));
        _content.Controls.Add(notice);

        _status.Location = new Point(36, 382);
        _status.Size = new Size(668, 22);
        _status.Text = "설치를 시작할 준비가 되었습니다.";
        _status.ForeColor = Muted;
        _content.Controls.Add(_status);

        _progress.Location = new Point(36, 411);
        _progress.Size = new Size(668, 12);
        _progress.Style = ProgressBarStyle.Continuous;
        _content.Controls.Add(_progress);

        _installButton.Location = new Point(36, 438);
        _installButton.Size = new Size(668, 46);
        ConfigureButton(_installButton, "설치", true);
        _installButton.Click += InstallButtonClicked;
        _content.Controls.Add(_installButton);
    }

    private void ShowMaintenancePage(string installedRoot, Version installedVersion)
    {
        _installedRoot = installedRoot;
        var targetVersion = Version.Parse(Program.ProductVersion);
        var requiresUpdate = installedVersion != targetVersion;
        Text = requiresUpdate ? $"{Program.ProductName} 업데이트" : $"{Program.ProductName} 유지 관리";
        _content.Controls.Clear();

        _content.Controls.Add(CreateLabel(
            requiresUpdate ? "업데이트할 수 있습니다" : "이미 최신 버전입니다",
            new Point(36, 22), new Size(668, 34), 17, FontStyle.Bold, Ink));
        _content.Controls.Add(CreateLabel(
            requiresUpdate
                ? $"Tosun Flux v{installedVersion}에서 v{targetVersion}으로 업데이트합니다."
                : "같은 버전을 다시 적용하거나 프로그램을 제거할 수 있습니다.",
            new Point(36, 61), new Size(668, 24), 10, FontStyle.Regular, Muted));

        var installed = new Panel { Location = new Point(36, 112), Size = new Size(668, 76), BackColor = Surface };
        installed.Controls.Add(CreateLabel($"현재 설치 위치  ·  v{installedVersion}", new Point(16, 11), new Size(636, 20), 9, FontStyle.Bold, Muted));
        installed.Controls.Add(CreateLabel(installedRoot, new Point(16, 36), new Size(636, 24), 10, FontStyle.Regular, Ink));
        _content.Controls.Add(installed);

        var notice = new Panel { Location = new Point(36, 215), Size = new Size(668, 76), BackColor = Surface };
        notice.Controls.Add(CreateLabel(
            requiresUpdate
                ? $"새 버전 v{targetVersion}을 현재 설치 위치에 적용합니다."
                : $"v{targetVersion} 파일을 현재 설치 위치에 다시 적용해 복구합니다.",
            new Point(16, 13), new Size(636, 22), 9.5f, FontStyle.Bold, Ink));
        notice.Controls.Add(CreateLabel("제거는 Windows의 설치된 앱 목록에서 실행하는 것과 같은 경로를 사용합니다.", new Point(16, 40), new Size(636, 20), 9, FontStyle.Regular, Muted));
        _content.Controls.Add(notice);

        var repairButton = new Button { Location = new Point(36, 324), Size = new Size(668, 48) };
        ConfigureButton(repairButton, requiresUpdate ? "업데이트" : "복구", true);
        repairButton.Click += RepairButtonClicked;
        _content.Controls.Add(repairButton);

        var removeButton = new Button { Location = new Point(36, 397), Size = new Size(321, 48) };
        var cancelButton = new Button { Location = new Point(383, 397), Size = new Size(321, 48) };
        ConfigureButton(removeButton, "Tosun Flux 제거", false);
        ConfigureButton(cancelButton, "취소", false);
        removeButton.Click += (_, _) => RemoveInstalledApplication();
        cancelButton.Click += (_, _) => Close();
        _content.Controls.Add(removeButton);
        _content.Controls.Add(cancelButton);
    }

    private void RemoveInstalledApplication()
    {
        InstallerOperations.Uninstall();
        if (!InstallerOperations.TryGetInstalledRoot(out _))
            Close();
    }
    private void BrowseInstallFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Tosun Flux를 설치할 폴더를 선택하세요.",
            SelectedPath = _installPath.Text,
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installPath.Text = dialog.SelectedPath;
    }

    private async void InstallButtonClicked(object? sender, EventArgs e)
    {
        if (!TryGetInstallRoot(out var installRoot))
            return;

        await InstallToPathAsync(installRoot, _desktopShortcut.Checked, _startMenuShortcut.Checked);
    }

    private async void RepairButtonClicked(object? sender, EventArgs e)
    {
        await InstallToPathAsync(_installedRoot, true, true);
    }

    private async Task InstallToPathAsync(string installRoot, bool createDesktopShortcut, bool createStartMenuShortcut)
    {
        SetInstalling(true);
        var progress = new Progress<InstallProgress>(value =>
        {
            _progress.Value = Math.Clamp(value.Percent, 0, 100);
            _status.Text = value.Message;
        });

        try
        {
            var options = new InstallOptions(installRoot, createDesktopShortcut, createStartMenuShortcut);
            _installResult = await Task.Run(() => InstallerOperations.Install(options, progress));
            _installedRoot = installRoot;
            ShowCompletePage();
        }
        catch (Exception error)
        {
            SetInstalling(false);
            _status.Text = "설치를 완료하지 못했습니다.";
            MessageBox.Show(this, error.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool TryGetInstallRoot(out string installRoot)
    {
        installRoot = _installPath.Text.Trim();
        try
        {
            installRoot = Path.GetFullPath(installRoot);
        }
        catch
        {
            MessageBox.Show(this, "설치 위치가 올바르지 않습니다.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(installRoot) || installRoot.Equals(Path.GetPathRoot(installRoot), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "드라이브 루트가 아닌 설치 폴더를 선택하세요.", Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        return true;
    }

    private void SetInstalling(bool installing)
    {
        foreach (Control control in _content.Controls)
            control.Enabled = !installing || control == _status || control == _progress;
        _installButton.Enabled = !installing;
        if (installing)
        {
            _progress.Value = 0;
            _status.Text = "설치를 준비하는 중...";
        }
    }

    private void ShowCompletePage()
    {
        _content.Controls.Clear();
        _content.Controls.Add(CreateLabel("✓", new Point(36, 25), new Size(70, 70), 36, FontStyle.Bold, Accent));
        _content.Controls.Add(CreateLabel("설치가 완료되었습니다", new Point(112, 30), new Size(592, 35), 17, FontStyle.Bold, Ink));
        _content.Controls.Add(CreateLabel("앱과 변환 백엔드 연결, Windows 등록까지 확인했습니다.", new Point(112, 69), new Size(592, 24), 10, FontStyle.Regular, Muted));

        var installed = new Panel { Location = new Point(36, 116), Size = new Size(668, 69), BackColor = Surface };
        installed.Controls.Add(CreateLabel("설치 위치", new Point(16, 10), new Size(636, 20), 9, FontStyle.Bold, Muted));
        installed.Controls.Add(CreateLabel(_installedRoot, new Point(16, 34), new Size(636, 24), 10, FontStyle.Regular, Ink));
        _content.Controls.Add(installed);

        _content.Controls.Add(CreateLabel("바로가기", new Point(36, 211), new Size(200, 23), 10, FontStyle.Bold, Ink));
        var desktopButton = new Button { Location = new Point(36, 242), Size = new Size(321, 44) };
        var startButton = new Button { Location = new Point(383, 242), Size = new Size(321, 44) };
        ConfigureButton(desktopButton, _installResult.DesktopShortcutCreated ? "바탕화면 바로가기 생성됨" : "바탕화면 바로가기 만들기", false);
        ConfigureButton(startButton, _installResult.StartMenuShortcutCreated ? "시작 메뉴 바로가기 생성됨" : "시작 메뉴 바로가기 만들기", false);
        desktopButton.Enabled = !_installResult.DesktopShortcutCreated;
        startButton.Enabled = !_installResult.StartMenuShortcutCreated;
        desktopButton.Click += (_, _) => CreateDesktopShortcut(desktopButton);
        startButton.Click += (_, _) => CreateStartMenuShortcut(startButton);
        _content.Controls.Add(desktopButton);
        _content.Controls.Add(startButton);

        _content.Controls.Add(CreateLabel("Windows 설정의 설치된 앱과 제어판의 프로그램 제거 목록에서 Tosun Flux를 제거할 수 있습니다.", new Point(36, 316), new Size(668, 48), 9.5f, FontStyle.Regular, Muted));

        var closeButton = new Button { Location = new Point(36, 418), Size = new Size(321, 50) };
        var launchButton = new Button { Location = new Point(383, 418), Size = new Size(321, 50) };
        ConfigureButton(closeButton, "닫기", false);
        ConfigureButton(launchButton, "Tosun Flux 실행", true);
        closeButton.Click += (_, _) => Close();
        launchButton.Click += (_, _) => LaunchInstalledApplication();
        _content.Controls.Add(closeButton);
        _content.Controls.Add(launchButton);
    }

    private void CreateDesktopShortcut(Button button)
    {
        try
        {
            InstallerOperations.CreateDesktopShortcut(_installedRoot);
            button.Text = "바탕화면 바로가기 생성됨";
            button.Enabled = false;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateStartMenuShortcut(Button button)
    {
        try
        {
            InstallerOperations.CreateStartMenuShortcut(_installedRoot);
            button.Text = "시작 메뉴 바로가기 생성됨";
            button.Enabled = false;
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LaunchInstalledApplication()
    {
        try
        {
            InstallerOperations.LaunchApplication(_installedRoot);
            Close();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, error.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Label CreateLabel(string text, Point location, Size size, float fontSize, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            Location = location,
            Size = size,
            Font = new Font("Segoe UI", fontSize, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text, Point location)
    {
        checkBox.Text = text;
        checkBox.Location = location;
        checkBox.Size = new Size(650, 26);
        checkBox.Font = new Font("Segoe UI", 9.5f);
        checkBox.ForeColor = Ink;
        checkBox.UseVisualStyleBackColor = true;
    }

    private static void ConfigureButton(Button button, string text, bool primary)
    {
        button.Text = text;
        button.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(211, 216, 231);
        button.BackColor = primary ? Accent : Color.White;
        button.ForeColor = primary ? Color.White : Ink;
        button.Cursor = Cursors.Hand;
        button.UseVisualStyleBackColor = false;
        button.MouseEnter += (_, _) => button.BackColor = primary ? AccentDark : Color.FromArgb(244, 246, 251);
        button.MouseLeave += (_, _) => button.BackColor = primary ? Accent : Color.White;
    }
}

internal readonly record struct InstallOptions(string InstallRoot, bool CreateDesktopShortcut, bool CreateStartMenuShortcut);
internal readonly record struct InstallResult(bool DesktopShortcutCreated, bool StartMenuShortcutCreated);
internal readonly record struct InstallProgress(int Percent, string Message);

internal static class InstallerOperations
{
    private const string AppExeName = "Tosun Flux.exe";
    private const string BackendRelativePath = @"backend\TosunFluxBackend\TosunFluxBackend.exe";

    public static bool TryGetInstalledRoot(out string installRoot)
    {
        return TryGetInstalledRoot(out installRoot, out _);
    }

    public static bool TryGetInstalledRoot(out string installRoot, out Version installedVersion)
    {
        using var key = Registry.LocalMachine.OpenSubKey(Program.UninstallKeyPath);
        installRoot = key?.GetValue("InstallLocation") as string ?? string.Empty;
        var versionText = key?.GetValue("DisplayVersion") as string;
        installedVersion = Version.TryParse(versionText, out var parsedVersion)
            ? parsedVersion
            : new Version(0, 0, 0);
        return installRoot.Length > 0;
    }
    public static string GetInitialInstallRoot()
    {
        using var key = Registry.LocalMachine.OpenSubKey(Program.UninstallKeyPath);
        return key?.GetValue("InstallLocation") as string is { Length: > 0 } installedPath
            ? installedPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Program.ProductName);
    }

    public static InstallResult Install(InstallOptions options, IProgress<InstallProgress> progress)
    {
        EnsureApplicationIsClosed(options.InstallRoot);
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"Tosun Flux Install {Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            var extractedRoot = ExtractPayload(temporaryRoot, progress);
            CopyPayload(extractedRoot, options.InstallRoot, progress);

            var appPath = Path.Combine(options.InstallRoot, AppExeName);
            var backendPath = Path.Combine(options.InstallRoot, BackendRelativePath);
            var uninstaller = Path.Combine(options.InstallRoot, "Uninstall Tosun Flux.exe");
            if (!File.Exists(appPath) || !File.Exists(backendPath))
                throw new InvalidOperationException("설치된 앱 또는 변환 백엔드 파일을 찾을 수 없습니다.");

            progress.Report(new InstallProgress(88, "변환 백엔드 연결을 확인하는 중..."));
            VerifyBackend(backendPath);
            File.Copy(Environment.ProcessPath!, uninstaller, true);

            progress.Report(new InstallProgress(93, "Windows에 앱을 등록하는 중..."));
            RegisterWindowsApp(options.InstallRoot, appPath, uninstaller);
            DeleteFile(Path.Combine(GetStartMenuFolder(), "Tosun Flux 제거.lnk"));

            var desktopCreated = false;
            var startMenuCreated = false;
            if (options.CreateDesktopShortcut)
            {
                CreateDesktopShortcut(options.InstallRoot);
                desktopCreated = true;
            }
            if (options.CreateStartMenuShortcut)
            {
                CreateStartMenuShortcut(options.InstallRoot);
                startMenuCreated = true;
            }

            progress.Report(new InstallProgress(100, "설치가 완료되었습니다."));
            return new InstallResult(desktopCreated, startMenuCreated);
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot))
                    Directory.Delete(temporaryRoot, true);
            }
            catch
            {
                // 임시 폴더 정리 실패가 완료된 설치를 되돌리지는 않습니다.
            }
        }
    }

    public static void CreateDesktopShortcut(string installRoot)
    {
        var desktop = GetDesktopDirectory();
        CreateApplicationShortcut(Path.Combine(desktop, "Tosun Flux.lnk"), installRoot);
    }

    public static void CreateStartMenuShortcut(string installRoot)
    {
        var startMenu = GetStartMenuFolder();
        Directory.CreateDirectory(startMenu);
        CreateApplicationShortcut(Path.Combine(startMenu, "Tosun Flux.lnk"), installRoot);
    }

    public static void LaunchApplication(string installRoot)
    {
        var appPath = Path.Combine(installRoot, AppExeName);
        var backendPath = Path.Combine(installRoot, BackendRelativePath);
        if (!File.Exists(appPath) || !File.Exists(backendPath))
            throw new InvalidOperationException("설치된 앱과 변환 백엔드를 찾을 수 없습니다.");
        VerifyBackend(backendPath);
        // 관리자 권한 설치 프로그램의 자식 프로세스로 직접 실행하면
        // 탐색기에서 일반 사용자 권한으로 드래그앤드랍이 차단됩니다.
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{appPath}\"")
        {
            WorkingDirectory = installRoot,
            UseShellExecute = true
        });
    }

    public static void Uninstall()
    {
        if (MessageBox.Show("Tosun Flux와 설치된 구성 요소를 제거할까요?", "Tosun Flux 제거", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var installRoot = Path.GetDirectoryName(Environment.ProcessPath!)!;
        DeleteFile(Path.Combine(GetDesktopDirectory(), "Tosun Flux.lnk"));
        var startMenu = GetStartMenuFolder();
        if (Directory.Exists(startMenu))
            Directory.Delete(startMenu, true);
        Registry.LocalMachine.DeleteSubKeyTree(Program.UninstallKeyPath, false);
        Registry.LocalMachine.DeleteSubKeyTree(Program.AppPathKeyPath, false);

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{installRoot}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        MessageBox.Show("Tosun Flux 제거를 완료했습니다.", "Tosun Flux 제거", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string GetDesktopDirectory()
    {
        using var key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\User Shell Folders");
        var redirectedDesktop = key?.GetValue("Desktop") as string;
        if (!string.IsNullOrWhiteSpace(redirectedDesktop))
        {
            var expanded = Environment.ExpandEnvironmentVariables(redirectedDesktop);
            Directory.CreateDirectory(expanded);
            return expanded;
        }

        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        Directory.CreateDirectory(desktop);
        return desktop;
    }

    private static string ExtractPayload(string temporaryRoot, IProgress<InstallProgress> progress)
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
                Directory.CreateDirectory(destination);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, true);
            }
            progress.Report(new InstallProgress(5 + (index + 1) * 48 / Math.Max(zip.Entries.Count, 1), "설치 파일을 준비하는 중..."));
        }
        return extractedRoot;
    }

    private static void CopyPayload(string sourceRoot, string installRoot, IProgress<InstallProgress> progress)
    {
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        Directory.CreateDirectory(installRoot);
        for (var index = 0; index < files.Length; index++)
        {
            var relative = Path.GetRelativePath(sourceRoot, files[index]);
            var destination = Path.Combine(installRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(files[index], destination, true);
            progress.Report(new InstallProgress(53 + (index + 1) * 33 / Math.Max(files.Length, 1), "프로그램 파일을 설치하는 중..."));
        }
    }

    private static void VerifyBackend(string backendPath)
    {
        var startInfo = new ProcessStartInfo(backendPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(backendPath)!
        };
        startInfo.ArgumentList.Add("health");
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("변환 백엔드를 실행할 수 없습니다.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(true);
            throw new InvalidOperationException("변환 백엔드 응답 시간이 초과되었습니다.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim().Length > 0 ? error.Trim() : "변환 백엔드 상태 확인에 실패했습니다.");
        using var document = JsonDocument.Parse(output);
        if (document.RootElement.GetProperty("status").GetString() != "ok")
            throw new InvalidOperationException("변환 백엔드가 정상 상태를 반환하지 않았습니다.");
    }

    private static void EnsureApplicationIsClosed(string installRoot)
    {
        var appPath = Path.Combine(installRoot, AppExeName);
        foreach (var process in Process.GetProcessesByName("Tosun Flux"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, appPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("실행 중인 Tosun Flux를 종료한 뒤 다시 설치해 주세요.");
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static void RegisterWindowsApp(string installRoot, string appPath, string uninstaller)
    {
        using (var key = Registry.LocalMachine.CreateSubKey(Program.UninstallKeyPath))
        {
            if (key is null)
                throw new InvalidOperationException("Windows 프로그램 목록에 등록할 수 없습니다.");
            key.SetValue("DisplayName", Program.ProductName);
            key.SetValue("DisplayVersion", Program.ProductVersion);
            key.SetValue("Publisher", Program.Publisher);
            key.SetValue("InstallLocation", installRoot);
            key.SetValue("InstallSource", Path.GetDirectoryName(Environment.ProcessPath!) ?? string.Empty);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("UninstallString", $"\"{uninstaller}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstaller}\" --uninstall");
            key.SetValue("DisplayIcon", $"{appPath},0");
            key.SetValue("URLInfoAbout", "https://github.com/Tosun0/Tosun-Flux");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize", EstimateInstallSize(installRoot), RegistryValueKind.DWord);
        }

        using var appPathKey = Registry.LocalMachine.CreateSubKey(Program.AppPathKeyPath);
        if (appPathKey is null)
            throw new InvalidOperationException("Windows 앱 경로에 등록할 수 없습니다.");
        appPathKey.SetValue(string.Empty, appPath);
        appPathKey.SetValue("Path", installRoot);
    }

    private static int EstimateInstallSize(string installRoot)
    {
        long bytes = 0;
        foreach (var file in Directory.EnumerateFiles(installRoot, "*", SearchOption.AllDirectories))
            bytes += new FileInfo(file).Length;
        return (int)Math.Clamp(bytes / 1024, 1, int.MaxValue);
    }

    private static string GetStartMenuFolder()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", Program.ProductName);
    }

    private static void CreateApplicationShortcut(string shortcutPath, string installRoot)
    {
        CreateShortcut(shortcutPath, Path.Combine(installRoot, AppExeName), "Tosun Flux 파일 통합 변환기");
    }

    private static void CreateShortcut(string path, string target, string description, string? arguments = null)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows 바로가기 기능을 사용할 수 없습니다.");
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = Path.GetDirectoryName(target);
            shortcut.IconLocation = $"{Path.Combine(Path.GetDirectoryName(target)!, AppExeName)},0";
            shortcut.Description = description;
            shortcut.WindowStyle = 1;
            if (arguments is not null)
                shortcut.Arguments = arguments;
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null)
                Marshal.FinalReleaseComObject(shell);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}

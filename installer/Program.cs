using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class Program
{
    private const string ProductName = "Tosun Flux";
    private static readonly string InstallRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductName);

    [STAThread]
    private static void Main(string[] args)
    {
        try
        {
            if (args.Any(item => item.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                return;
            }

            Install();
            MessageBox.Show("Tosun Flux 설치가 완료되었습니다.", ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Process.Start(new ProcessStartInfo(Path.Combine(InstallRoot, "Tosun Flux.exe")) { WorkingDirectory = InstallRoot, UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void Install()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"Tosun Flux Install {Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                ?? throw new InvalidOperationException("설치 패키지 데이터를 찾을 수 없습니다.");
            var archivePath = Path.Combine(temporaryRoot, "payload.zip");
            using (var archive = File.Create(archivePath))
                resource.CopyTo(archive);
            var extractedRoot = Path.Combine(temporaryRoot, "payload");
            ZipFile.ExtractToDirectory(archivePath, extractedRoot, true);
            if (!File.Exists(Path.Combine(extractedRoot, "Tosun Flux.exe")))
                throw new InvalidOperationException("Tosun Flux 실행 파일을 설치 패키지에서 찾을 수 없습니다.");

            Directory.CreateDirectory(InstallRoot);
            CopyDirectory(extractedRoot, InstallRoot);
            File.Copy(Environment.ProcessPath!, Path.Combine(InstallRoot, "Uninstall Tosun Flux.exe"), true);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", ProductName);
            Directory.CreateDirectory(startMenu);
            CreateShortcut(Path.Combine(desktop, "Tosun Flux.lnk"), Path.Combine(InstallRoot, "Tosun Flux.exe"), "Tosun Flux 파일 통합 변환기");
            CreateShortcut(Path.Combine(startMenu, "Tosun Flux.lnk"), Path.Combine(InstallRoot, "Tosun Flux.exe"), "Tosun Flux 파일 통합 변환기");
            CreateShortcut(Path.Combine(startMenu, "Tosun Flux 제거.lnk"), Path.Combine(InstallRoot, "Uninstall Tosun Flux.exe"), "Tosun Flux 제거", "--uninstall");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }
    }

    private static void Uninstall()
    {
        File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Tosun Flux.lnk"));
        var startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", ProductName);
        if (Directory.Exists(startMenu))
            Directory.Delete(startMenu, true);
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallRoot}\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(source))
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
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

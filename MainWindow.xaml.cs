using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATASChinesePatch.Models;
using ATASChinesePatch.Services;
using Microsoft.Win32;

namespace ATASChinesePatch;

public partial class MainWindow : Window
{
    private readonly IndicatorFontPatcher _patcher = new();
    private string _lastOutputDirectory = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        AppendLog("ATAS Chinese Patch 已启动。");
        AppendLog("请选择一个 ATAS 自定义指标 DLL。常见位置：%APPDATA%\\ATAS\\Indicators");
    }

    private void SelectDllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 ATAS 自定义指标 DLL",
                Filter = "DLL 文件 (*.dll)|*.dll|所有文件 (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog(this) != true)
                return;

            DllPathTextBox.Text = dialog.FileName;
            _lastOutputDirectory = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
            AppendLog($"已选择 DLL：{dialog.FileName}");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var result = _patcher.Scan(DllPathTextBox.Text);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void PatchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (LooksLikePatchedOutput(DllPathTextBox.Text))
            {
                var continuePatched = MessageBox.Show(
                    this,
                    "当前文件看起来已经是补丁产物，建议选择原始 DLL。\n\n仍要继续吗？",
                    "ATAS Chinese Patch",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (continuePatched != MessageBoxResult.Yes)
                {
                    AppendLog("用户取消：当前文件看起来已经是补丁产物。");
                    return;
                }
            }

            var replacementFont = GetSelectedReplacementFont();
            var scanResult = _patcher.Scan(DllPathTextBox.Text);
            ApplyResult(scanResult);

            if (scanResult.DetectedFonts.Count == 0)
            {
                MessageBox.Show(
                    this,
                    "未发现可替换的硬编码字体，补丁不会生效。",
                    "ATAS Chinese Patch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                AppendLog("未生成补丁：没有发现可替换字体。");
                return;
            }

            var replacementPreview = string.Join(
                Environment.NewLine,
                scanResult.DetectedFonts
                    .Select(font => font.FontName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(font => font, StringComparer.Ordinal)
                    .Select(font => $"{font} -> {replacementFont}"));

            var confirmReplace = MessageBox.Show(
                this,
                $"将要替换以下硬编码字体：{Environment.NewLine}{Environment.NewLine}{replacementPreview}{Environment.NewLine}{Environment.NewLine}是否继续？",
                "确认生成补丁 DLL",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmReplace != MessageBoxResult.Yes)
            {
                AppendLog("用户取消：未确认替换列表。");
                return;
            }

            var confirmAtasClosed = MessageBox.Show(
                this,
                "请确认 ATAS 已关闭，否则 DLL 可能被占用或加载失败。\n\n确认继续生成补丁 DLL 吗？",
                "关闭 ATAS 提醒",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmAtasClosed != MessageBoxResult.Yes)
            {
                AppendLog("用户取消：未确认 ATAS 已关闭。");
                return;
            }

            var result = _patcher.Patch(DllPathTextBox.Text, replacementFont, OverwriteCheckBox.IsChecked == true);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void OpenOutputDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = _lastOutputDirectory;
            if (string.IsNullOrWhiteSpace(directory) && !string.IsNullOrWhiteSpace(DllPathTextBox.Text))
                directory = Path.GetDirectoryName(DllPathTextBox.Text) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                throw new InvalidOperationException("还没有可打开的输出目录。");

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });

            AppendLog($"已打开输出目录：{directory}");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private string GetSelectedReplacementFont()
    {
        if (ReplacementFontComboBox.SelectedItem is ComboBoxItem item && item.Content is string font)
            return font;

        return "SimSun";
    }

    private void ApplyResult(PatchResult result)
    {
        foreach (var log in result.Logs)
            AppendLog(log);

        if (!string.IsNullOrWhiteSpace(result.OutputPath))
            _lastOutputDirectory = Path.GetDirectoryName(result.OutputPath) ?? _lastOutputDirectory;
        else if (!string.IsNullOrWhiteSpace(result.InputPath))
            _lastOutputDirectory = Path.GetDirectoryName(result.InputPath) ?? _lastOutputDirectory;

        SaveOperationLog(result.Logs);
    }

    private void ShowError(Exception ex)
    {
        var message = ex is InvalidOperationException ? ex.Message : $"操作失败：{ex.Message}";
        AppendLog(message);
        SaveOperationLog([message]);
        MessageBox.Show(this, message, "ATAS Chinese Patch", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void SaveOperationLog(IEnumerable<string> logs)
    {
        try
        {
            var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logDirectory);

            var logPath = Path.Combine(logDirectory, $"atas-chinese-patch-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            var lines = logs.Select(log => $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {log}");
            File.WriteAllLines(logPath, lines);

            AppendLog($"操作日志已保存：{logPath}");
        }
        catch (Exception ex)
        {
            AppendLog($"日志保存失败：{ex.Message}");
        }
    }

    private static bool LooksLikePatchedOutput(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains(".CJKPatched.dll", StringComparison.OrdinalIgnoreCase);
    }
}

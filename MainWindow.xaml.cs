using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ATASChinesePatch.Models;
using ATASChinesePatch.Services;
using Forms = System.Windows.Forms;

namespace ATASChinesePatch;

public partial class MainWindow : Window
{
    private readonly IndicatorFontPatcher _patcher = new();
    private readonly ObservableCollection<DllCandidateRow> _candidateRows = [];
    private string _lastOutputDirectory = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        CandidateDataGrid.ItemsSource = _candidateRows;
        InstallPathTextBox.Text = @"C:\Program Files (x86)\ATAS Platform";
        DataPathTextBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ATAS");

        AppendLog("ATAS Chinese Patch 已启动。");
        AppendLog("请确认 ATAS 安装目录和数据目录，然后点击“扫描目录”。");
    }

    private void BrowseInstallPathButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolderInto(InstallPathTextBox, "选择 ATAS 安装目录");
    }

    private void BrowseDataPathButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseFolderInto(DataPathTextBox, "选择 ATAS 数据目录");
    }

    private async void ScanFoldersButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(isBusy: true, activeOperation: "scan");
            await ScanFoldersAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(isBusy: false);
        }
    }

    private async void PatchSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(isBusy: true, activeOperation: "patch");
            await PatchSelectedCandidatesAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(isBusy: false);
        }
    }

    private void OpenOutputDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = _lastOutputDirectory;
            if (string.IsNullOrWhiteSpace(directory))
                directory = _candidateRows.FirstOrDefault(row => !string.IsNullOrWhiteSpace(row.OutputPath))?.OutputDirectory ?? string.Empty;

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

    private void BrowseFolderInto(System.Windows.Controls.TextBox textBox, string description)
    {
        try
        {
            using var dialog = new Forms.FolderBrowserDialog
            {
                Description = description,
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(textBox.Text) ? textBox.Text : string.Empty
            };

            if (dialog.ShowDialog() != Forms.DialogResult.OK)
                return;

            textBox.Text = dialog.SelectedPath;
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task ScanFoldersAsync()
    {
        var roots = GetExistingScanRoots();
        if (roots.Count == 0)
            throw new InvalidOperationException("请至少填写一个存在的 ATAS 目录。");

        _candidateRows.Clear();
        var replacementFont = GetSelectedReplacementFont();

        var progress = new Progress<ScanProgress>(update =>
        {
            if (!string.IsNullOrWhiteSpace(update.Message))
                AppendLog(update.Message);

            if (update.Row is not null)
                _candidateRows.Add(update.Row);
        });

        var summary = await Task.Run(() => ScanFoldersCore(roots, replacementFont, progress));
        AppendLog(summary.Message);
        SaveOperationLog(summary.OperationLogs);

        if (summary.Candidates == 0)
        {
            System.Windows.MessageBox.Show(
                this,
                "没有发现包含可替换硬编码字体的 DLL。",
                "ATAS Chinese Patch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private ScanSummary ScanFoldersCore(
        IReadOnlyCollection<string> roots,
        string replacementFont,
        IProgress<ScanProgress> progress)
    {
        var scanned = 0;
        var skipped = 0;
        var candidates = 0;
        var operationLogs = new List<string>
        {
            "开始扫描 ATAS 目录。",
            $"替换字体：{replacementFont}"
        };

        foreach (var root in roots)
        {
            var rootMessage = $"开始扫描目录：{root}";
            progress.Report(new ScanProgress(Message: rootMessage));
            operationLogs.Add(rootMessage);

            foreach (var dllPath in EnumerateDllFilesSafe(root, operationLogs, message => progress.Report(new ScanProgress(Message: message))))
            {
                scanned++;

                if (LooksLikeGeneratedPatchFile(dllPath))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var result = _patcher.Scan(dllPath);
                    if (result.DetectedFonts.Count == 0)
                        continue;

                    candidates++;
                    var row = DllCandidateRow.FromResult(result);
                    var message = $"发现可修改 DLL：{dllPath} | 字体：{row.DetectedFontsText}";
                    progress.Report(new ScanProgress(message, row));
                    operationLogs.Add(message);
                }
                catch (InvalidOperationException)
                {
                    skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    var message = $"跳过 DLL：{dllPath} | {ex.Message}";
                    progress.Report(new ScanProgress(Message: message));
                    operationLogs.Add(message);
                }
            }
        }

        var summary = $"扫描完成：扫描 DLL {scanned} 个，发现可修改 {candidates} 个，跳过 {skipped} 个。";
        operationLogs.Add(summary);
        return new ScanSummary(summary, candidates, operationLogs);
    }

    private async Task PatchSelectedCandidatesAsync()
    {
        var selectedRows = _candidateRows.Where(row => row.IsSelected).ToList();
        if (selectedRows.Count == 0)
            throw new InvalidOperationException("请先扫描并勾选至少一个可修改 DLL。");

        var replacementFont = GetSelectedReplacementFont();
        var overwriteOriginal = OverwriteCheckBox.IsChecked == true;
        var confirmMessage =
            $"将对 {selectedRows.Count} 个 DLL 批量替换字体为 {replacementFont}。{Environment.NewLine}{Environment.NewLine}" +
            (overwriteOriginal
                ? "当前模式会先备份再覆盖原 DLL。Program Files 目录可能需要以管理员身份运行。"
                : "当前模式会在原目录生成 .CJKPatched.dll 文件。Program Files 目录可能需要以管理员身份运行。") +
            $"{Environment.NewLine}{Environment.NewLine}请确认 ATAS 已关闭。是否继续？";

        var confirm = System.Windows.MessageBox.Show(
            this,
            confirmMessage,
            "确认批量修改 DLL",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            AppendLog("用户取消：未确认批量修改。");
            return;
        }

        var progress = new Progress<PatchProgress>(update =>
        {
            if (!string.IsNullOrWhiteSpace(update.Status))
                update.Row.Status = update.Status;

            if (!string.IsNullOrWhiteSpace(update.OutputPath))
            {
                update.Row.OutputPath = update.OutputPath;
                _lastOutputDirectory = update.Row.OutputDirectory;
            }

            if (update.ReplacedCount.HasValue)
                update.Row.ReplacedCount = update.ReplacedCount.Value;

            if (!string.IsNullOrWhiteSpace(update.Message))
                AppendLog(update.Message);
        });

        var summary = await Task.Run(() => PatchSelectedCandidatesCore(selectedRows, replacementFont, overwriteOriginal, progress));
        AppendLog(summary.Message);
        SaveOperationLog(summary.OperationLogs);
    }

    private PatchSummary PatchSelectedCandidatesCore(
        IReadOnlyCollection<DllCandidateRow> selectedRows,
        string replacementFont,
        bool overwriteOriginal,
        IProgress<PatchProgress> progress)
    {
        var operationLogs = new List<string>
        {
            "开始批量修改 DLL。",
            $"替换字体：{replacementFont}",
            overwriteOriginal ? "模式：备份后覆盖原 DLL。" : "模式：生成 .CJKPatched.dll。"
        };
        var success = 0;
        var failed = 0;

        foreach (var row in selectedRows)
        {
            try
            {
                progress.Report(new PatchProgress(row, Status: "处理中"));
                var result = _patcher.Patch(row.Path, replacementFont, overwriteOriginal);
                success++;

                progress.Report(new PatchProgress(
                    row,
                    Status: overwriteOriginal ? "已覆盖" : "已生成",
                    OutputPath: result.OutputPath,
                    ReplacedCount: result.ReplacedCount,
                    Message: $"修改完成：{row.Path} | 替换 {result.ReplacedCount} 处 | 输出：{result.OutputPath}"));
                operationLogs.AddRange(result.Logs);
            }
            catch (Exception ex)
            {
                failed++;
                var message = $"修改失败：{row.Path} | {ex.Message}";
                progress.Report(new PatchProgress(row, Status: "失败", Message: message));
                operationLogs.Add(message);
            }
        }

        var summary = $"批量修改完成：成功 {success} 个，失败 {failed} 个。";
        operationLogs.Add(summary);
        return new PatchSummary(summary, operationLogs);
    }

    private List<string> GetExistingScanRoots()
    {
        return new[] { InstallPathTextBox.Text, DataPathTextBox.Text }
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Environment.ExpandEnvironmentVariables(path.Trim()))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> EnumerateDllFilesSafe(string root, ICollection<string> operationLogs, Action<string> reportLog)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var currentDirectory = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(currentDirectory, "*.dll");
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                var message = $"跳过目录文件枚举：{currentDirectory} | {ex.Message}";
                reportLog(message);
                operationLogs.Add(message);
                continue;
            }

            foreach (var file in files)
                yield return file;

            string[] subdirectories;
            try
            {
                subdirectories = Directory.GetDirectories(currentDirectory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                var message = $"跳过子目录枚举：{currentDirectory} | {ex.Message}";
                reportLog(message);
                operationLogs.Add(message);
                continue;
            }

            foreach (var subdirectory in subdirectories)
                pending.Push(subdirectory);
        }
    }

    private void SetBusy(bool isBusy, string? activeOperation = null)
    {
        ScanFoldersButton.IsEnabled = !isBusy;
        PatchSelectedButton.IsEnabled = !isBusy;
        InstallPathTextBox.IsEnabled = !isBusy;
        DataPathTextBox.IsEnabled = !isBusy;
        ReplacementFontComboBox.IsEnabled = !isBusy;
        OverwriteCheckBox.IsEnabled = !isBusy;
        CandidateDataGrid.IsEnabled = !isBusy;

        ScanFoldersButton.Content = isBusy && activeOperation == "scan" ? "扫描中..." : "扫描目录";
        PatchSelectedButton.Content = isBusy && activeOperation == "patch" ? "修改中..." : "修改已勾选";
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
        System.Windows.MessageBox.Show(this, message, "ATAS Chinese Patch", MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private static bool LooksLikeGeneratedPatchFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains(".CJKPatched.dll", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".CJKPatch.tmp.dll", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ScanProgress(string? Message = null, DllCandidateRow? Row = null);

    private sealed record PatchProgress(
        DllCandidateRow Row,
        string? Status = null,
        string? OutputPath = null,
        int? ReplacedCount = null,
        string? Message = null);

    private sealed record ScanSummary(string Message, int Candidates, IReadOnlyCollection<string> OperationLogs);

    private sealed record PatchSummary(string Message, IReadOnlyCollection<string> OperationLogs);

    private sealed class DllCandidateRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _status = "待修改";
        private string _outputPath = string.Empty;
        private int _replacedCount;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (_status == value)
                    return;

                _status = value;
                OnPropertyChanged(nameof(Status));
            }
        }

        public string Path { get; private init; } = string.Empty;
        public int DetectedCount { get; private init; }
        public string DetectedFontsText { get; private init; } = string.Empty;

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                if (_outputPath == value)
                    return;

                _outputPath = value;
                OnPropertyChanged(nameof(OutputPath));
                OnPropertyChanged(nameof(OutputDirectory));
            }
        }

        public string OutputDirectory => System.IO.Path.GetDirectoryName(OutputPath) ?? string.Empty;

        public int ReplacedCount
        {
            get => _replacedCount;
            set
            {
                if (_replacedCount == value)
                    return;

                _replacedCount = value;
                OnPropertyChanged(nameof(ReplacedCount));
            }
        }

        public static DllCandidateRow FromResult(PatchResult result)
        {
            var distinctFonts = result.DetectedFonts
                .Select(font => font.FontName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(font => font, StringComparer.Ordinal)
                .ToList();

            return new DllCandidateRow
            {
                Path = result.InputPath,
                DetectedCount = result.DetectedFonts.Count,
                DetectedFontsText = string.Join(", ", distinctFonts)
            };
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

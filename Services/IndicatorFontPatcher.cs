using System.IO;
using ATASChinesePatch.Models;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.IO;

namespace ATASChinesePatch.Services;

public sealed class IndicatorFontPatcher
{
    private static readonly HashSet<string> ReplaceableFonts = new(StringComparer.Ordinal)
    {
        "Roboto",
        "Arial",
        "Tahoma",
        "Segoe UI",
        "Consolas",
        "Verdana",
        "Calibri",
        "Helvetica",
        "Times New Roman",
        "Courier New"
    };

    private static readonly string[] SuspiciousFontKeywords =
    [
        "Font",
        "FontFamily",
        "Typeface",
        "RenderFont",
        "TextFormat",
        "Roboto",
        "Arial",
        "Tahoma",
        "Segoe",
        "Consolas",
        "Verdana",
        "Calibri",
        "Microsoft",
        "YaHei",
        "SimSun"
    ];

    public PatchResult Scan(string inputDllPath)
    {
        return Process(inputDllPath, replacementFont: null, overwriteOriginal: false);
    }

    public PatchResult Patch(string inputDllPath, string replacementFont, bool overwriteOriginal)
    {
        if (string.IsNullOrWhiteSpace(replacementFont))
            throw new InvalidOperationException("请选择替换字体。");

        return Process(inputDllPath, replacementFont, overwriteOriginal);
    }

    private static PatchResult Process(string inputDllPath, string? replacementFont, bool overwriteOriginal)
    {
        ValidateInputPath(inputDllPath);

        var result = new PatchResult
        {
            InputPath = inputDllPath
        };

        var isPatchMode = replacementFont is not null;
        result.Logs.Add(isPatchMode ? "开始生成补丁 DLL。" : "开始扫描 DLL。");
        result.Logs.Add($"输入文件：{inputDllPath}");

        using var module = LoadModule(inputDllPath);

        foreach (var type in module.GetTypes())
        {
            foreach (var method in type.Methods)
            {
                if (!method.HasBody)
                    continue;

                foreach (var instruction in method.Body.Instructions)
                {
                    if (instruction.OpCode != OpCodes.Ldstr || instruction.Operand is not string value)
                        continue;

                    result.TotalStringCount++;
                    AddSuspiciousFontString(result, value, type.FullName, method.FullName);

                    if (!ReplaceableFonts.Contains(value))
                        continue;

                    var detected = new DetectedFont
                    {
                        FontName = value,
                        TypeName = type.FullName,
                        MethodName = method.FullName
                    };

                    result.DetectedFonts.Add(detected);
                    result.Logs.Add($"发现可替换字体：{value} | 类型：{type.FullName} | 方法：{method.FullName}");

                    if (isPatchMode)
                    {
                        instruction.Operand = replacementFont;
                        result.ReplacedCount++;
                    }
                }
            }
        }

        result.Logs.Add($"扫描到字符串常量数量：{result.TotalStringCount}");
        result.Logs.Add($"发现可替换字体数量：{result.DetectedFonts.Count}");
        result.Logs.Add($"发现疑似字体相关字符串数量：{result.SuspiciousFontStrings.Count}");

        if (!isPatchMode)
        {
            result.Logs.Add("扫描完成，未修改 DLL。");
            return result;
        }

        if (result.DetectedFonts.Count == 0)
            throw new InvalidOperationException("未发现可替换的硬编码字体，补丁不会生效。");

        result.BackupPath = CreateBackup(inputDllPath);
        result.Logs.Add($"已创建临时备份：{result.BackupPath}");

        if (overwriteOriginal)
        {
            WriteOverOriginal(module, inputDllPath);
            result.OutputPath = inputDllPath;
        }
        else
        {
            result.OutputPath = GetPatchedOutputPath(inputDllPath);
            WriteModule(module, result.OutputPath);
        }

        DeleteSuccessfulBackup(result.BackupPath, result.Logs);

        result.Logs.Add(overwriteOriginal
            ? "已覆盖原 DLL。"
            : $"已生成补丁 DLL：{result.OutputPath}");
        result.Logs.Add($"替换数量：{result.ReplacedCount}");
        result.Logs.Add("操作完成。");

        return result;
    }

    private static ModuleDefMD LoadModule(string inputDllPath)
    {
        try
        {
            return ModuleDefMD.Load(inputDllPath);
        }
        catch (BadImageFormatException ex)
        {
            throw new InvalidOperationException("该文件不是有效的 .NET DLL，无法补丁。", ex);
        }
        catch (DataReaderException ex)
        {
            throw new InvalidOperationException("该文件不是有效的 .NET DLL，无法补丁。", ex);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"DLL 读取失败，文件可能被占用。请确认 ATAS 已关闭。详细信息：{ex.Message}", ex);
        }
    }

    private static void ValidateInputPath(string inputDllPath)
    {
        if (string.IsNullOrWhiteSpace(inputDllPath))
            throw new InvalidOperationException("请先选择 DLL 文件。");

        if (!File.Exists(inputDllPath))
            throw new InvalidOperationException("选择的 DLL 文件不存在。");

        var fileName = Path.GetFileName(inputDllPath);
        if (string.Equals(fileName, "ATAS.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安全限制：不允许修改 ATAS.exe。");

        if (!string.Equals(Path.GetExtension(inputDllPath), ".dll", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("请选择 ATAS 自定义指标 DLL 文件。");
    }

    private static string CreateBackup(string inputDllPath)
    {
        var inputDirectory = Path.GetDirectoryName(inputDllPath);
        if (string.IsNullOrWhiteSpace(inputDirectory))
            throw new InvalidOperationException("无法识别 DLL 所在目录。");

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var backupDirectory = Path.Combine(inputDirectory, "CJKPatch_Backups", timestamp);
        Directory.CreateDirectory(backupDirectory);

        var backupPath = Path.Combine(backupDirectory, Path.GetFileName(inputDllPath));
        try
        {
            File.Copy(inputDllPath, backupPath, overwrite: false);
            return backupPath;
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"DLL 备份失败，文件可能被占用。请确认 ATAS 已关闭。详细信息：{ex.Message}", ex);
        }
    }

    private static void DeleteSuccessfulBackup(string backupPath, ICollection<string> logs)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            return;

        try
        {
            if (!File.Exists(backupPath))
                return;

            var timestampDirectory = Path.GetDirectoryName(backupPath);
            var backupRootDirectory = !string.IsNullOrWhiteSpace(timestampDirectory)
                ? Path.GetDirectoryName(timestampDirectory)
                : null;

            File.Delete(backupPath);
            logs.Add($"修改成功，已删除临时备份：{backupPath}");

            DeleteDirectoryIfEmpty(timestampDirectory, logs);
            DeleteDirectoryIfEmpty(backupRootDirectory, logs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logs.Add($"修改已成功，但临时备份删除失败：{backupPath} | {ex.Message}");
        }
    }

    private static void DeleteDirectoryIfEmpty(string? directoryPath, ICollection<string> logs)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            return;

        if (Directory.EnumerateFileSystemEntries(directoryPath).Any())
            return;

        Directory.Delete(directoryPath);
        logs.Add($"已删除空备份目录：{directoryPath}");
    }

    private static string GetPatchedOutputPath(string inputDllPath)
    {
        var directory = Path.GetDirectoryName(inputDllPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("无法识别 DLL 所在目录。");

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(inputDllPath);
        return Path.Combine(directory, $"{fileNameWithoutExtension}.CJKPatched.dll");
    }

    private static void WriteOverOriginal(ModuleDefMD module, string inputDllPath)
    {
        var directory = Path.GetDirectoryName(inputDllPath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("无法识别 DLL 所在目录。");

        var temporaryPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(inputDllPath)}.CJKPatch.tmp.dll");

        try
        {
            WriteModule(module, temporaryPath);
            File.Copy(temporaryPath, inputDllPath, overwrite: true);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"DLL 覆盖失败，文件可能被占用。请确认 ATAS 已关闭。详细信息：{ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"DLL 覆盖失败：{ex.Message}", ex);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void WriteModule(ModuleDefMD module, string outputPath)
    {
        try
        {
            module.Write(outputPath);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"DLL 写入失败，文件可能被占用。请确认 ATAS 已关闭。详细信息：{ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"DLL 写入失败：{ex.Message}", ex);
        }
    }

    private static void AddSuspiciousFontString(PatchResult result, string value, string typeName, string methodName)
    {
        var matchedKeyword = SuspiciousFontKeywords.FirstOrDefault(keyword =>
            value.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (matchedKeyword is null)
            return;

        result.SuspiciousFontStrings.Add(new SuspiciousFontString
        {
            Value = value,
            MatchedKeyword = matchedKeyword,
            TypeName = typeName,
            MethodName = methodName
        });

        result.Logs.Add($"疑似字体相关字符串：{value} | 关键词：{matchedKeyword} | 类型：{typeName} | 方法：{methodName}");
    }
}

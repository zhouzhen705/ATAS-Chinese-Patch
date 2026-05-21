# ATAS Chinese Patch

[English README](README.en.md)

ATAS Chinese Patch 是一个外部 Windows 桌面 EXE 工具，用于修复 ATAS 8.x 自定义指标 DLL 中中文显示为方框的问题。

本工具不会修改 ATAS.exe，不会注入 ATAS 进程，不会 Hook Windows API，也不会处理授权、签名、DRM、反调试或反篡改逻辑。工具只处理用户手动选择的 .NET 自定义指标 DLL。

## 项目用途

某些 ATAS 自定义指标 DLL 在绘图时硬编码了 Roboto、Arial、Tahoma、Segoe UI、Consolas 等不适合中文显示的字体，导致中文显示为方框。本工具使用 dnlib 扫描 DLL 的 IL 字符串常量，找到精确匹配的字体名，并把它们替换为支持中文的字体。

## 适用场景

- ATAS 指标中的英文显示正常，但中文显示为方框。
- 指标 DLL 是 .NET DLL。
- 字体名是硬编码在 IL 字符串常量中的精确值。
- 用户希望把字体替换为 SimSun、Microsoft YaHei、Microsoft YaHei UI、SimHei、Noto Sans CJK SC 或 Source Han Sans SC。

## 不适用场景

- ATAS.exe 本体。
- 非 .NET DLL。
- 字体名不是硬编码字符串，而是运行时生成、配置读取或加密存储。
- DLL 被强签名、混淆、加壳或存在完整性校验。
- 中文方框问题不是字体导致，而是渲染引擎、系统字体或指标自身逻辑导致。

## 使用方法

1. 补丁前关闭 ATAS。
2. 启动 ATAS Chinese Patch。
3. 确认 ATAS 安装目录和 ATAS 数据目录。默认会填入：
   - `C:\Program Files (x86)\ATAS Platform`
   - `%APPDATA%\ATAS`
4. 点击“扫描目录”，工具会递归扫描这两个目录下的 `.dll` 文件。
5. 扫描结果中只会列出发现硬编码可替换字体的 DLL。
6. 检查候选 DLL 列表，取消勾选不想修改的 DLL。
7. 选择替换字体，默认是 SimSun。
8. 点击“修改已勾选”。
9. 再次确认 ATAS 已关闭。
10. 默认会在原 DLL 同级目录生成 `原文件名.CJKPatched.dll`，并自动备份原始 DLL。
11. 如需覆盖原 DLL，可勾选“备份后覆盖原 DLL”，工具仍会先备份再写回。修改 `Program Files` 下的 DLL 可能需要以管理员身份运行。

ATAS 指标 DLL 常见位置：

```text
%APPDATA%\ATAS\Indicators
```

## 先用测试 DLL 验证

建议先用项目内置的测试 DLL 验证流程，不要一开始就处理真实 ATAS 指标 DLL。

先构建测试类库：

```powershell
dotnet build .\TestIndicatorFontSamples\TestIndicatorFontSamples.csproj -c Release
```

测试 DLL 位置：

```text
TestIndicatorFontSamples\bin\Release\net10.0\TestIndicatorFontSamples.dll
```

打开 ATAS Chinese Patch 后，可以把其中一个扫描目录临时改为这个测试 DLL 所在文件夹。扫描日志应能看到：

- `Roboto`
- `Arial`
- `Segoe UI`
- 疑似字体相关字符串 `RenderFont`

点击“修改已勾选”后，默认会生成：

```text
TestIndicatorFontSamples.CJKPatched.dll
```

同时会创建备份目录：

```text
CJKPatch_Backups\yyyyMMdd_HHmmss
```

## 日志文件

每次扫描和补丁操作都会把日志写入程序目录下的 `logs` 文件夹。

日志文件名格式：

```text
atas-chinese-patch-yyyyMMdd-HHmmss.log
```

## 如何恢复备份

如果补丁后 ATAS 无法加载指标，可以从原 DLL 同级目录下的 `CJKPatch_Backups` 找到对应时间的备份。

恢复方式：

1. 关闭 ATAS。
2. 打开 `CJKPatch_Backups\yyyyMMdd_HHmmss`。
3. 找到备份的原始 DLL。
4. 将该 DLL 复制回指标目录。
5. 如果你之前选择了“备份后覆盖原 DLL”，请用备份文件覆盖当前 DLL。

## 如何运行

在项目根目录运行：

```powershell
dotnet run
```

或者先构建再运行：

```powershell
dotnet build
.\bin\Debug\net10.0-windows\ATASChinesePatch.exe
```

## 如何构建

需要安装 .NET 10 SDK。

```powershell
dotnet restore
dotnet build -c Release
```

## 如何发布单 EXE

发布 Windows x64 自包含单文件 EXE：

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

只需要复制这个 EXE：

```text
bin\Release\net10.0-windows\win-x64\publish\ATASChinesePatch.exe
```

不要复制 Debug 或普通 Release 目录里的 EXE 到其他电脑单独运行。

如果目标电脑是 32 位 Windows，请改用：

```powershell
dotnet publish -c Release -r win-x86 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

## 风险说明

- 工具会在写入前自动备份原始 DLL，备份目录格式为 `CJKPatch_Backups/yyyyMMdd_HHmmss`。
- 默认不覆盖原文件，而是生成新的 `.CJKPatched.dll` 文件。
- 如果 DLL 被强签名、混淆或不是 .NET DLL，可能无法补丁或补丁后无法加载。
- 如果 ATAS 未关闭，DLL 可能被占用，导致备份、写入或加载失败。
- 修改第三方 DLL 可能影响指标运行稳定性。使用前请确认你有权修改该自定义指标 DLL，并保留原始备份。

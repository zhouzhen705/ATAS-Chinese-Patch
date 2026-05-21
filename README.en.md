# ATAS Chinese Patch

[中文说明](README.md)

ATAS Chinese Patch is an external Windows desktop EXE tool for fixing Chinese text rendered as square boxes in ATAS 8.x custom indicator DLLs.

This tool does not modify ATAS.exe, inject into the ATAS process, hook Windows APIs, or bypass licensing, signing, DRM, anti-debugging, or anti-tamper logic. It only processes user-selected .NET custom indicator DLLs.

## Purpose

Some ATAS custom indicator DLLs hard-code fonts such as Roboto, Arial, Tahoma, Segoe UI, and Consolas. These fonts may not render Chinese text correctly, causing square boxes. This tool uses dnlib to scan IL string constants, finds exact font-name matches, and replaces them with Chinese-capable fonts.

## Suitable Use Cases

- English text in an ATAS indicator renders correctly, but Chinese text appears as square boxes.
- The indicator DLL is a .NET DLL.
- The font name is an exact hard-coded IL string constant.
- You want to replace the font with SimSun, Microsoft YaHei, Microsoft YaHei UI, SimHei, Noto Sans CJK SC, or Source Han Sans SC.

## Not Suitable For

- ATAS.exe itself.
- Non-.NET DLLs.
- Font names generated at runtime, read from configuration, or stored in encrypted/obfuscated form.
- DLLs with strong-name signing, obfuscation, packing, or integrity checks.
- Cases where the square-box issue is caused by the rendering engine, system fonts, or indicator logic rather than the hard-coded font.

## Usage

1. Close ATAS before patching.
2. Start ATAS Chinese Patch.
3. Confirm the ATAS installation folder and ATAS data folder. The defaults are:
   - `C:\Program Files (x86)\ATAS Platform`
   - `%APPDATA%\ATAS`
4. Click "Scan Folders". The tool recursively scans `.dll` files in both folders.
5. The result table only lists DLLs that contain replaceable hard-coded font names.
6. Review the candidate DLL list and uncheck any DLL you do not want to modify.
7. Choose the replacement font. The default is SimSun.
8. Click "Patch Selected".
9. Confirm that ATAS is closed.
10. By default, the tool creates `OriginalFileName.CJKPatched.dll` in the same folder and backs up the original DLL.
11. To overwrite the original DLL, enable "Backup then overwrite original DLL". The tool still backs up the original file first. Patching DLLs under `Program Files` may require running as administrator.

Common ATAS indicator DLL location:

```text
%APPDATA%\ATAS\Indicators
```

## Test With Sample DLL First

It is recommended to verify the workflow with the included sample DLL before patching a real ATAS indicator DLL.

Build the sample class library first:

```powershell
dotnet build .\TestIndicatorFontSamples\TestIndicatorFontSamples.csproj -c Release
```

Sample DLL path:

```text
TestIndicatorFontSamples\bin\Release\net10.0\TestIndicatorFontSamples.dll
```

Open ATAS Chinese Patch and temporarily set one scan folder to the folder containing this test DLL. The scan log should show:

- `Roboto`
- `Arial`
- `Segoe UI`
- Suspicious font-related string `RenderFont`

After clicking "Patch Selected", the default output is:

```text
TestIndicatorFontSamples.CJKPatched.dll
```

A backup folder will also be created:

```text
CJKPatch_Backups\yyyyMMdd_HHmmss
```

## Log Files

Each scan and patch operation writes a log file to the `logs` folder under the application directory.

Log file name format:

```text
atas-chinese-patch-yyyyMMdd-HHmmss.log
```

## Restore From Backup

If ATAS cannot load the indicator after patching, restore the original DLL from the timestamped folder under `CJKPatch_Backups`.

Restore steps:

1. Close ATAS.
2. Open `CJKPatch_Backups\yyyyMMdd_HHmmss`.
3. Find the backed-up original DLL.
4. Copy it back to the indicator folder.
5. If you used "Backup then overwrite original DLL", overwrite the current DLL with the backup file.

## Run

Run from the project root:

```powershell
dotnet run
```

Or build first and run the generated executable:

```powershell
dotnet build
.\bin\Debug\net10.0-windows\ATASChinesePatch.exe
```

## Build

.NET 10 SDK is required.

```powershell
dotnet restore
dotnet build -c Release
```

## Publish Single EXE

Publish a Windows x64 self-contained single-file EXE:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

Only copy this EXE:

```text
bin\Release\net10.0-windows\win-x64\publish\ATASChinesePatch.exe
```

Do not copy the EXE from the Debug folder or the normal Release build folder as a standalone executable for another computer.

If the target computer is 32-bit Windows, use:

```powershell
dotnet publish -c Release -r win-x86 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:DebugType=None /p:DebugSymbols=false
```

## Risks

- The tool automatically backs up the original DLL before writing. Backup folders use the `CJKPatch_Backups/yyyyMMdd_HHmmss` format.
- By default, the original file is not overwritten. A new `.CJKPatched.dll` file is generated instead.
- If the DLL is strong-name signed, obfuscated, or not a .NET DLL, patching may fail or the patched DLL may not load.
- If ATAS is not closed, the DLL may be locked, causing backup, write, or load failures.
- Modifying third-party DLLs may affect indicator stability. Make sure you have permission to modify the custom indicator DLL and keep the original backup.

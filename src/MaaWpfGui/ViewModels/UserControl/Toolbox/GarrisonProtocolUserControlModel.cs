// <copyright file="GarrisonProtocolUserControlModel.cs" company="MaaAssistantArknights">
// Part of the MaaWpfGui project, maintained by the MaaAssistantArknights team (Maa Team)
// Copyright (C) 2021-2026 MaaAssistantArknights Contributors
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License v3.0 only as published by
// the Free Software Foundation, either version 3 of the License, or
// any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY
// </copyright>

#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using JetBrains.Annotations;
using MaaWpfGui.Configuration.Factory;
using MaaWpfGui.Configuration.Single.Settings;
using MaaWpfGui.Constants;
using MaaWpfGui.Helper;
using MaaWpfGui.States;
using MaaWpfGui.ViewModels.UI;
using MaaWpfGui.ViewModels.UserControl.Settings;
using Serilog;
using Stylet;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace MaaWpfGui.ViewModels.UserControl.Toolbox;

/// <summary>
/// 工具箱 ｢ 卫戍协议试跑 ｣ ：把 MAA 连接信息交给 GarrisonFarm 的实机脚本。
/// </summary>
public class GarrisonProtocolUserControlModel : PropertyChangedBase
{
    private const int StopWaitMs = 15_000;
    private const int MaxGameCount = 20;
    private static readonly ILogger _logger = Log.ForContext<GarrisonProtocolUserControlModel>();
    private readonly Lock _processLock = new();
    private Process? _process;
    private bool _stoppingProcess;

    static GarrisonProtocolUserControlModel()
    {
        Instance = new();
    }

    public static GarrisonProtocolUserControlModel Instance { get; }

    private GarrisonProtocolUserControlModel()
    {
    }

    private static GarrisonProtocol Config => ConfigFactory.CurrentConfig.Toolbox.MiniGame.GarrisonProtocol;

    /// <summary>
    /// Gets or sets GarrisonFarm 仓库根目录。
    /// </summary>
    public string FarmDir
    {
        get; set {
            SetAndNotify(ref field, value);
            Config.FarmDir = value;
        }
    } = Config.FarmDir;

    /// <summary>
    /// Gets or sets 可选模型路径。
    /// </summary>
    public string ModelPath
    {
        get; set {
            SetAndNotify(ref field, value);
            Config.ModelPath = value;
        }
    } = Config.ModelPath;

    /// <summary>
    /// Gets or sets Python 可执行文件。
    /// </summary>
    public string PythonPath
    {
        get; set {
            SetAndNotify(ref field, value);
            Config.PythonPath = value;
        }
    } = string.IsNullOrWhiteSpace(Config.PythonPath) ? "python" : Config.PythonPath;

    /// <summary>
    /// Gets or sets 试跑局数。
    /// </summary>
    public int GameCount
    {
        get; set {
            var clamped = Math.Clamp(value, 1, MaxGameCount);
            SetAndNotify(ref field, clamped);
            Config.GameCount = clamped;
        }
    } = Math.Clamp(Config.GameCount <= 0 ? 1 : Config.GameCount, 1, MaxGameCount);

    /// <summary>
    /// Gets or sets 开局必须出现的附加盟约。
    /// </summary>
    public string NeedBonds
    {
        get; set {
            SetAndNotify(ref field, value);
            Config.NeedBonds = value;
        }
    } = Config.NeedBonds;

    /// <summary>
    /// Gets a value indicating whether 实机脚本进程仍在跑。
    /// </summary>
    public bool IsRunning
    {
        get {
            lock (_processLock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>
    /// 选择 GarrisonFarm 目录。
    /// </summary>
    [UsedImplicitly]
    public void SelectFarmDir()
    {
        using var dialog = new FolderBrowserDialog();
        dialog.SelectedPath = Directory.Exists(FarmDir) ? FarmDir : PathsHelper.BaseDir;
        dialog.ShowNewFolderButton = false;
        if (dialog.ShowDialog() != DialogResult.OK)
        {
            return;
        }

        FarmDir = dialog.SelectedPath;
    }

    /// <summary>
    /// 选择可选模型文件。
    /// </summary>
    [UsedImplicitly]
    public void SelectModelPath()
    {
        var dialog = new OpenFileDialog {
            Filter = "GarrisonFarm model|*.json;*.onnx;*.pt|All|*.*",
        };
        var startDir = !string.IsNullOrWhiteSpace(ModelPath)
            ? Path.GetDirectoryName(ModelPath)
            : (Directory.Exists(Path.Combine(FarmDir, "models")) ? Path.Combine(FarmDir, "models") : FarmDir);
        if (!string.IsNullOrEmpty(startDir) && Directory.Exists(startDir))
        {
            dialog.InitialDirectory = startDir;
        }

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ModelPath = dialog.FileName;
    }

    /// <summary>
    /// 校验试跑前置条件。
    /// </summary>
    /// <param name="error">失败时的界面文案。</param>
    /// <returns>是否可以启动。</returns>
    public bool TryValidate(out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(FarmDir)
            || !File.Exists(Path.Combine(FarmDir, "rl_live.py"))
            || !File.Exists(Path.Combine(FarmDir, "garrison_farm.py")))
        {
            error = LocalizationHelper.GetString("MiniGame@GarrisonProtocol@FarmMissing");
            return false;
        }

        if (FindLauncherScript() is null)
        {
            error = LocalizationHelper.GetString("MiniGame@GarrisonProtocol@LauncherMissing");
            return false;
        }

        if (string.IsNullOrWhiteSpace(SettingsViewModel.ConnectSettings.AdbPath)
            || string.IsNullOrWhiteSpace(SettingsViewModel.ConnectSettings.ConnectAddress))
        {
            error = LocalizationHelper.GetString("MiniGame@GarrisonProtocol@AdbMissing");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 启动 GarrisonFarm 实机脚本；调用方须已 <see cref="RunningState.BeginRun"/>。
    /// </summary>
    /// <returns>进程是否拉起。</returns>
    public bool StartProcess()
    {
        if (!TryValidate(out var error))
        {
            Instances.TaskQueueViewModel.AddLog(error, UiLogColor.Error);
            return false;
        }

        var launcher = FindLauncherScript()!;
        var python = string.IsNullOrWhiteSpace(PythonPath) ? "python" : PythonPath.Trim();
        var farm = Path.GetFullPath(FarmDir.Trim());
        var net = ResolveNetPath(farm, ModelPath);
        var stopFile = Path.Combine(farm, "rl_stop");
        try
        {
            if (File.Exists(stopFile))
            {
                File.Delete(stopFile);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to remove leftover rl_stop");
        }

        var startInfo = new ProcessStartInfo {
            FileName = python,
            WorkingDirectory = farm,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.Environment["GF_MAA"] = PathsHelper.BaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        startInfo.Environment["GF_ADB"] = SettingsViewModel.ConnectSettings.AdbPath;
        startInfo.Environment["GF_ADDRESS"] = SettingsViewModel.ConnectSettings.ConnectAddress;
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";
        if (string.Equals(Path.GetFileNameWithoutExtension(python), "py", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-3");
        }

        startInfo.ArgumentList.Add(launcher);
        startInfo.ArgumentList.Add("--farm");
        startInfo.ArgumentList.Add(farm);
        startInfo.ArgumentList.Add("--maa");
        startInfo.ArgumentList.Add(PathsHelper.BaseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        startInfo.ArgumentList.Add("--adb");
        startInfo.ArgumentList.Add(SettingsViewModel.ConnectSettings.AdbPath);
        startInfo.ArgumentList.Add("--address");
        startInfo.ArgumentList.Add(SettingsViewModel.ConnectSettings.ConnectAddress);
        if (!string.IsNullOrEmpty(net))
        {
            startInfo.ArgumentList.Add("--net");
            startInfo.ArgumentList.Add(net);
        }

        startInfo.ArgumentList.Add("games");
        startInfo.ArgumentList.Add(GameCount.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(NeedBonds))
        {
            startInfo.ArgumentList.Add(NeedBonds.Trim());
        }

        Process process;
        try
        {
            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => AppendProcessLog(e.Data, UiLogColor.Trace);
            process.ErrorDataReceived += (_, e) => AppendProcessLog(e.Data, UiLogColor.Warning);
            process.Exited += (_, _) => Execute.OnUIThread(OnProcessExited);
            if (!process.Start())
            {
                Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetString("MiniGame@GarrisonProtocol@StartFailed"), UiLogColor.Error);
                process.Dispose();
                return false;
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to start GarrisonFarm trial");
            var msg = LocalizationHelper.GetString("MiniGame@GarrisonProtocol@PythonMissing");
            Instances.TaskQueueViewModel.AddLog($"{msg}\n{ex.Message}", UiLogColor.Error);
            return false;
        }

        lock (_processLock)
        {
            _stoppingProcess = false;
            _process = process;
        }

        Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetString("MiniGame@GarrisonProtocol@Started"), UiLogColor.Info);
        Instances.TaskQueueViewModel.AddLog(
            string.IsNullOrEmpty(net)
                ? LocalizationHelper.GetString("MiniGame@GarrisonProtocol@RuleFallback")
                : string.Format(LocalizationHelper.GetString("MiniGame@GarrisonProtocol@UsingModel"), net),
            UiLogColor.Trace);
        _logger.Information("GarrisonFarm trial started, pid={Pid}, net={Net}", process.Id, net ?? "rule");
        return true;
    }

    /// <summary>
    /// 写入 rl_stop，超时后结束进程树，并收口运行态。
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task StopAsync()
    {
        Process? process;
        lock (_processLock)
        {
            process = _process;
            _stoppingProcess = true;
        }

        RunningState.Instance.SetStopping(true);
        Instances.TaskQueueViewModel.AddLog(LocalizationHelper.GetString("Stopping"), splitMode: TaskQueueViewModel.LogCardSplitMode.Both);

        var farm = FarmDir.Trim();
        if (!string.IsNullOrEmpty(farm))
        {
            try
            {
                await File.WriteAllTextAsync(Path.Combine(farm, "rl_stop"), "stop").ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to write rl_stop");
            }
        }

        try
        {
            if (process is { HasExited: false })
            {
                var exited = await WaitForExitAsync(process, StopWaitMs).ConfigureAwait(true);
                if (!exited)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning(ex, "Failed to kill GarrisonFarm process tree");
                    }

                    await WaitForExitAsync(process, 3000).ConfigureAwait(true);
                }
            }
        }
        catch (InvalidOperationException)
        {
            // Exited 回调可能已 Dispose
        }

        Instances.TaskQueueViewModel.SetStopped();
    }

    private void OnProcessExited()
    {
        int? code = null;
        lock (_processLock)
        {
            if (_process != null)
            {
                try
                {
                    code = _process.ExitCode;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to read GarrisonFarm exit code");
                }

                _process.Dispose();
                _process = null;
            }
        }

        if (code is { } exitCode)
        {
            Instances.TaskQueueViewModel.AddLog(
                string.Format(LocalizationHelper.GetString("MiniGame@GarrisonProtocol@Exited"), exitCode),
                exitCode == 0 ? UiLogColor.Trace : UiLogColor.Warning);
        }

        bool stopping;
        lock (_processLock)
        {
            stopping = _stoppingProcess;
            _stoppingProcess = false;
        }

        if (!stopping && !RunningState.Instance.GetIdle())
        {
            Instances.TaskQueueViewModel.SetStopped();
        }
    }

    private static void AppendProcessLog(string? line, string color)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        Instances.TaskQueueViewModel.AddLog(line.TrimEnd(), color);
    }

    private static async Task<bool> WaitForExitAsync(Process process, int timeoutMs)
    {
        try
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    /// <summary>
    /// 解析启动器脚本路径（安装目录或源码树）。
    /// </summary>
    /// <returns>存在则返回绝对路径。</returns>
    public static string? FindLauncherScript()
    {
        foreach (var candidate in new[]
        {
            Path.Combine(PathsHelper.BaseDir, "GarrisonProtocol", "run_trial.py"),
            Path.Combine(PathsHelper.BaseDir, "tools", "GarrisonProtocol", "run_trial.py"),
        })
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var dir = new DirectoryInfo(PathsHelper.BaseDir);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "tools", "GarrisonProtocol", "run_trial.py");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 GF_NET：用户指定优先，否则 GarrisonFarm/models/ppo_019；都没有则规则表。
    /// </summary>
    /// <param name="farmDir">GarrisonFarm 根目录。</param>
    /// <param name="modelPath">用户填写的模型路径，可空。</param>
    /// <returns>无扩展名或 .onnx 路径；没有模型时返回 null。</returns>
    public static string? ResolveNetPath(string farmDir, string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            return NormalizeNetPath(modelPath.Trim());
        }

        var fallback = Path.Combine(farmDir, "models", "ppo_019");
        return NetExists(fallback) ? fallback : null;
    }

    private static string NormalizeNetPath(string path)
    {
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".pt", StringComparison.OrdinalIgnoreCase))
        {
            return path[..^Path.GetExtension(path).Length];
        }

        return path;
    }

    private static bool NetExists(string net)
    {
        return File.Exists(net)
               || File.Exists(net + ".json")
               || File.Exists(net + ".onnx")
               || File.Exists(Path.ChangeExtension(net, ".onnx"));
    }
}

// <copyright file="GarrisonProtocol.cs" company="MaaAssistantArknights">
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
using MaaWpfGui.Models;
using static MaaWpfGui.Configuration.Factory.ConfigFactory;

namespace MaaWpfGui.Configuration.Single.Settings;

/// <summary>
/// 工具箱 ｢ 卫戍协议试跑 ｣ 实验入口设置。
/// </summary>
public class GarrisonProtocol : NotifyPropertyChangedWithValue
{
    public void EventBinding(string prefix)
    {
        PropertyChanged += Handler.OnPropertyChangedFactory(prefix);
    }

    /// <summary>
    /// Gets or sets GarrisonFarm 仓库根目录。
    /// </summary>
    public string FarmDir { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets 可选模型路径（无扩展名权重、json 或 onnx）。
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets Python 可执行文件。
    /// </summary>
    public string PythonPath { get; set; } = "python";

    /// <summary>
    /// Gets or sets 试跑局数。
    /// </summary>
    public int GameCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets 开局必须出现的附加盟约（逗号分隔，简中 OCR 原文）。
    /// </summary>
    public string NeedBonds { get; set; } = string.Empty;
}

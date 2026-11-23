# 卫戍协议试跑（实验）

MAA 工具箱实验入口：把连接信息交给用户自己准备的
[GarrisonFarm](https://github.com/UnknownGlyph/GarrisonFarm) 实机脚本
`rl_live.py`，用来打 ｢ 卫戍协议 ｣ 规程。

成功标准是打完一局并留下日志，**不保证通关**。作者的 `ppo_019` 权重为可选可替换项，不进本仓库、不进安装包。

## 准备

1. 自行克隆 GarrisonFarm，并按其说明安装 Python 3.11 依赖（`numpy`、`opencv-python`、`rapidocr-onnxruntime`、`maafw` 等）。
2. 可选：从该仓库下载 `models/ppo_019`（无扩展名权重 + 同名 `.json`）。没有模型时启动器会回退到作者规则表（领袖核心盟约打分、第 2/4/6/8 回合商店升级、卖出保护）。
3. 在 MAA 设置里配好 ADB 与地址。本入口**不会**再走 MaaCore 任务链，也**不要**与一键长草同时开（会抢 ADB）。
4. 脚本按简中界面 OCR。请用简中客户端，并停在卫戍协议活动页再点开始。

## 工具箱

打开 MAA → 工具箱 → 实验 → ｢ 卫戍协议试跑 ｣：

- GarrisonFarm 目录：指向你克隆下来的仓库根（里面要有 `rl_live.py`）
- 模型路径：可空；空则尝试 `models/ppo_019`，再没有就走规则表
- Python：默认 `python`
- 局数：默认 1

停止时会在 GarrisonFarm 目录写入 `rl_stop`，脚本在检查点退出；超时则结束 Python 进程树。

## 命令行

也可不经 GUI：

```text
python tools/GarrisonProtocol/run_trial.py --farm <GarrisonFarm> --maa <MAA目录> --adb <adb> --address <设备> [--net <ppo_019>] games 1
```

`--dry-run` 只检查目录与计划，不操作模拟器。

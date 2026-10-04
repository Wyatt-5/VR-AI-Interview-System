# VR 智能 AI 面试项目

这是 2026-10-04 整理的 GitHub 交付版本，包含 Unity 工程、Python 后端、首次安装脚本、日常启动脚本和完整交接文档。

## 当前状态

- Unity 实际工程版本：`2022.3.53f1c1`（中国版分支；Unity Hub 路径可能显示为 `2022.3.53f1`）。
- 主场景：`unity项目/Assets/Scenes/MainScene_AIInterview_VoiceTest_v1.unity`。
- Unity 批处理交付验证已通过：无编译错误、无 Missing Script；四个面试官动画、纯语音、成熟男声、桌面计时器、问题 UI、结果 UI、Build Settings 与 OpenXR 引用完整。
- Python 后端语法检查、依赖检查、启动与 `/health` 健康检查均已通过。

## 第一次使用

1. 必须通过 Git + Git LFS 获取项目，不能直接用 GitHub 网页逐个上传大动画。
2. 双击 `首次安装后端环境.bat`。
3. 在 `后端/.env` 填入你自己的 `DEEPSEEK_API_KEY`，不要提交该文件。
4. 双击 `启动后端.bat`，保持黑色窗口开启。
5. 后端显示 `Application startup complete` 后，再通过 Unity Hub 打开 `unity项目`。
6. 打开主场景并点击 Play。

完整步骤见 [新电脑部署与启动说明.md](新电脑部署与启动说明.md)。接手阶段的变更位置见 [接手后修改记录.md](接手后修改记录.md)。

## 目录

```text
.
├─ unity项目/                  Unity 工程（Assets、Packages、ProjectSettings）
├─ 后端/                       FastAPI、AI、ASR 与 TTS 后端
├─ 首次安装后端环境.bat         新电脑首次创建 Python 环境并安装依赖
├─ 启动后端.bat                 日常一键启动后端
├─ 新电脑部署与启动说明.md       完整部署、启动与排错说明
├─ 接手后修改记录.md             功能、修复及文件位置
└─ 历史交接资料_仅供参考/        原始资料；目录名和部分内容已过时
```

## GitHub 注意事项

四个必要动画单文件均超过 GitHub 普通文件 100 MB 上限，已在 `.gitattributes` 中配置 Git LFS。上传前必须安装 Git LFS，并确认 `git lfs ls-files` 能看到四个 `.anim` 文件。

项目内含第三方模型、贴图和字体，但原交接资料中没有完整许可证。授权确认前请使用私有仓库，不要直接公开发布。

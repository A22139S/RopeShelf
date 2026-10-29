# 绳间 · RopeShelf

Windows 上的本地截图晾衣绳。截图挂在屏幕上沿，移入展开，点一下复制，拖出去分享。

![程序生成的演示图片：截图卡片挂在绳上](docs/images/preview.png)

**Windows 11 x64 · 0.1.1 预览版 · 离线使用 · MIT**

灵感来自 [Tendedero](https://github.com/alejandrobujan/tendedero)。RopeShelf 使用独立的 C# / WPF 实现、名称及原创图标，由本项目独立维护。Tendedero 的名称、图标及文档图片不随本项目分发，本项目未获得其作者的官方背书。

## 使用

从本仓库 **Releases** 下载 `RopeShelf-0.1.1-win-x64.zip`，解压全部文件后运行 `RopeShelf.exe`。若当前没有发布附件，可以按下面的命令从源码构建。便携包自带 .NET 运行环境，源码构建需要 SDK。

1. 使用现有截图工具截图，或在画图等应用中复制图片像素。
2. 在主屏顶部中央停留约 **300ms**，或按 **Ctrl+Alt+T** 展开。
3. 操作卡片，移开指针后绳子自动收起。没有图片时不显示空绳子。

| 操作 | 效果 |
|---|---|
| 单击卡片 | 复制完整图片；等待系统双击判定间隔后执行 |
| 双击 | 用默认应用打开图片 |
| 中键 | 大图预览，Esc 关闭 |
| 滚轮 | 横向浏览 |
| 拖入接收应用 | 交给接收方，卡片保留 |
| 拖入 Explorer 文件夹 | 保存成功并确认后取下卡片 |
| 右键 | 复制、打开、另存、打开所在文件夹、取下 |
| 托盘图标／菜单 | 展开收起、设置、暂停剪贴板收图、退出 |

系统截图快捷键保持原样，程序不内置截图工具。开机启动默认关闭，可在设置中开启。

## 功能与边界

- 接收剪贴板图片及系统截图目录中的新图片，允许添加监视目录；不导入目录旧图。
- 过滤文字、文件列表、可识别的隐私标记及自身复制事件，短时间内按像素内容去重。
- 主屏顶部单行卡片，新图片短暂展示约1.5秒；指针、菜单或拖放操作会保持展开。
- 全屏时继续收图，抑制自动展开和悬停唤出，快捷键仍可主动请求展开。
- 默认保留最新30张，重启恢复；外部文件只取下卡片，不删除原图。
- 应用收件箱图片取下时请求移入回收站；回收站未确认接收时保留或恢复文件并报告失败，此时容量可能暂时超过30张。
- 半透明玻璃卡片、木夹和轻微摆动；系统关闭动画时减少动画。玻璃材质不是实时桌面背景模糊。

首版支持 **Windows 11 x64、主显示器**。不包含标注、视频处理、长期历史搜索或自动更新。

## 隐私与本地文件

程序没有网络调用、账号、遥测或自动更新。安装 SDK、恢复构建依赖和 GitHub 操作不属于程序运行时。

默认数据目录为 `%LOCALAPPDATA%\RopeShelf`，可从托盘菜单打开：

| 目录／文件 | 用途 |
|---|---|
| `settings.json`、`items.json` | 设置及卡片索引，含本地图片路径 |
| `Inbox/` | 剪贴板图片的应用自管副本 |
| `Thumbnails/` | 可重建缩略图缓存 |
| `Transfers/` | 拖放交接副本及租约 |
| `Recovered/` | 保留的回收中断备份，可能不存在 |
| `logs/` | 本地错误和诊断记录，可能包含文件路径 |

拖放副本保留七天，避免接收应用稍后读取时文件已失效。启动时仅清理过期、有本应用记录且无混入其他文件的暂存目录；未登记目录保留。接收方应将需要长期保留的图片另行保存。不要通过清空 `Inbox` 来取下卡片。

更新或替换程序文件不会清除数据。报告问题时请删去日志中的私人路径，不要上传收件箱、剪贴板或真实截图。

## 验证状态

2026-10-29，维护者反馈手动验收项目全部通过；这是人工反馈，并非已公开的逐项机器日志。自动检查包含21项基础检查、6项窗口检查和4项剪贴板检查。另有30张合成4K图片的恢复采样。

**已知差异：** 开发执行环境中的回收站成功路径探测仍失败，测试文件已保留或恢复；窗口焦点／可见性检查曾出现一次失败后重跑通过。两项记录均保留，不能用人工反馈改写失败结果。对不熟悉的系统环境建议先使用生成图片试用。

详情见 [验证记录](docs/validation.md)、[手动验收](docs/manual-acceptance.md) 和 [回收站诊断](docs/recycle-diagnosis.md)。当前二进制未签名。

## 从源码构建

需要 Windows x64、**.NET 10 SDK 10.0.400 或更新的兼容版本**。本项目没有第三方 NuGet 应用依赖。

```powershell
git clone https://github.com/A22139S/RopeShelf.git
cd RopeShelf
./scripts/build.ps1
```

构建脚本将 CLI 配置和 NuGet 缓存放入项目 `artifacts/`。首次构建恢复依赖需要联网。

生成自包含便携 ZIP：

```powershell
./scripts/release.ps1
```

结果位于 `artifacts/releases/`，包括 ZIP 和 SHA-256 校验文件。脚本附带运行时许可、项目许可和使用说明，不包含 PDB、测试图片或日志。

额外检查使用程序生成的图片：

```powershell
$test = './tests/RopeShelf.Tests/bin/Release/net10.0-windows/RopeShelf.Tests.exe'
& $test ./artifacts/tests --window
& $test ./artifacts/tests --clipboard
& $test ./artifacts/tests --recycle
& $test ./artifacts/tests --large-prepare
& $test ./artifacts/tests --large-check
```

`--window` 会创建测试窗口；`--clipboard` 临时写入生成图片并尝试恢复原剪贴板；`--recycle` 操作生成的测试文件，失败时不能记作通过。大图检查先生成30张4K PNG，再测首次及同一进程内的缓存恢复。桌面集成检查不在无交互 CI 中冒充真实用户环境验收。

仅渲染合成演示，不读取剪贴板或监视用户目录：

```powershell
./src/RopeShelf.App/bin/Release/net10.0-windows/RopeShelf.exe --demo --no-input --data-root ./artifacts/demo --render-preview ./artifacts/preview.png
```

## 项目结构

```text
src/RopeShelf.Core/       卡片模型、文件归属及展开状态机
src/RopeShelf.Windows/    剪贴板、目录、存储、Shell及Win32接口
src/RopeShelf.App/        WPF浮层、卡片、设置及托盘
tests/                   自动检查及独立拖放验证台
scripts/                 构建、打包、隐私扫描及原创图标生成
docs/                    需求、验证、路线图及说明
```

欢迎提交可复现问题或改进建议，见 [贡献说明](CONTRIBUTING.md) 和 [后续方向](docs/roadmap.md)。

## 许可与致谢

RopeShelf 源码采用 [MIT](LICENSE)。随包运行时及其第三方组件保留各自的许可与声明，见 [第三方说明](THIRD_PARTY_NOTICES.md)。

感谢 Alejandro Buján 的 Tendedero 带来的交互灵感。上游代码采用 MIT，但名称、图标及相关图稿另有限制，详见其 [LICENSE](https://github.com/alejandrobujan/tendedero/blob/main/LICENSE)。

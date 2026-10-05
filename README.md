# ComicReader

一个类似 [Rulia](https://github.com/RuliaReader/Rulia) 的现代化 Windows 漫画阅读器：**直接读取压缩包**，无需解压，开箱即用。

[![build-release](https://github.com/hohenghe/comic-reader/actions/workflows/build-release.yml/badge.svg)](https://github.com/hohenghe/comic-reader/actions/workflows/build-release.yml)

## 功能特性

- **压缩包直读**：CBZ / ZIP、CBR / RAR、CB7 / 7Z、CBT / TAR，以及图片文件夹
- **流式读取**：按页从压缩包中读取，不整包解压，不占用额外磁盘空间
- **三种阅读模式**：单页翻页 / 双页跨页 / 连续竖向滚动（长条webtoon友好）
- **阅读方向**：从左到右、从右到左（日漫），自动识别 ComicInfo.xml 中的 `Manga` 标记
- **缩放与平移**：适应高度 / 适应宽度 / 原始尺寸，Ctrl+滚轮缩放，拖拽平移
- **缩略图侧栏**：快速跳页
- **阅读进度**：自动记忆每本书的页码，关闭后可从上次位置继续
- **书库管理**：添加文件夹、递归扫描、封面网格、搜索、收藏、最近阅读
- **加密压缩包**：密码提示与记忆（支持 ZIP/RAR/7Z 加密包，取决于 SharpCompress 支持范围）
- **元数据**：读取 ComicInfo.xml（标题、作者、页码等）
- **自然排序**：`page2` 排在 `page10` 前面
- **便携存储**：设置、书库、封面缓存全部保存在 exe 旁边的 `data/` 目录，不写注册表
- **深色 / 浅色主题**，中文界面
- **拖拽打开**：把压缩包拖进窗口即可阅读

## 下载使用

前往 [Releases](../../releases) 下载 `ComicReader-win-x64.zip`，解压后双击 `ComicReader.exe` 即可运行（自包含 .NET 运行时，无需额外安装）。

> 说明：exe 未做代码签名，Windows SmartScreen 可能提示"未知发布者"，选择"仍要运行"即可。

## 快捷键

| 按键 | 功能 |
| --- | --- |
| `←` / `→` | 上一页 / 下一页（自动跟随阅读方向） |
| `Space` / `PageDown` | 下一页 |
| `PageUp` | 上一页 |
| `Home` / `End` | 第一页 / 最后一页 |
| `Ctrl + 滚轮` | 缩放 |
| `+` / `-` / `0` | 放大 / 缩小 / 恢复适应 |
| `滚轮` | 翻页（单页/双页模式） |
| `F` / `F11` | 全屏 |
| `T` | 缩略图侧栏 |
| `Esc` | 退出全屏 / 返回书库 |
| 鼠标左键点击左右两侧 | 翻页 |
| 鼠标左键点击中间 | 显示 / 隐藏工具栏 |

## 从源码构建

环境要求：Windows 10/11 + [.NET 10 SDK](https://dotnet.microsoft.com/download)

```powershell
# 运行单元测试
dotnet test tests/ComicReader.Core.Tests -c Release

# 本地调试运行
dotnet run --project src/ComicReader.App

# 发布自包含单目录版本（免安装 .NET）
dotnet publish src/ComicReader.App -c Release -r win-x64 --self-contained true -o dist/ComicReader-win-x64
```

生成测试样本（可选的示例漫画）：

```powershell
powershell -ExecutionPolicy Bypass -File tools/make-samples.ps1
```

推送 `v*` 标签后，GitHub Actions 会自动运行测试、构建并把 `ComicReader-win-x64.zip` 附加到 Release：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

## 项目结构

```
src/ComicReader.Core/     归档读取、自然排序、ComicInfo 解析、书库模型（无 UI 依赖）
src/ComicReader.App/      WPF 界面（书库 / 阅读器 / 设置）、图像缓存、便携存储
tests/ComicReader.Core.Tests/  xUnit 单元测试
tools/                    图标与测试样本生成脚本
.github/workflows/        CI：打 tag 自动构建并发布 exe
```

## 技术栈

- .NET 10 + WPF
- [SharpCompress](https://github.com/adamhathcock/sharpcompress)：纯托管 ZIP / RAR / 7Z / TAR 读取，无需外部解压程序
- CommunityToolkit.Mvvm

## 已知限制

- 图片格式支持 PNG / JPEG / GIF / BMP / TIFF；WebP / AVIF 取决于系统 WIC 编解码器（后续版本计划内置支持）
- 加密 RAR5 / 7z 的兼容性取决于 SharpCompress，个别压缩包可能无法读取
- PDF / EPUB 暂不支持（规划中）

## 许可证

[MIT](LICENSE)

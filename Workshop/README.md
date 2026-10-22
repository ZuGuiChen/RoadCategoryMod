# 发布材料与上传指引（v1.0.0）

本目录是创意工坊发布用的材料，不参与编译。

## 目录内容

| 文件 | 用途 |
|---|---|
| `preview.png` | 创意工坊封面图（512×512，带 v1.0.0 角标） |
| `Thumbnail.png` | 模组缩略图（256×256），随模组文件夹一起发布 |
| `Description_zh.txt` | 工坊描述（简体中文，BBCode） |
| `Description_en.txt` | Workshop description (English, BBCode) |
| `UpdateNotes_v1.0.0.md` | 首次发布公告（可贴到工坊更新说明） |
| `screenshots/01_category_icons.png` | 分类图标一览（64px 与 24px 对照） |
| `screenshots/02_settings.png` | 模组设置页截图（旧版界面，发布前建议用 v1.0.0 重拍） |
| `screenshots/03_*.png` | 建议补充：进入城市后截「道路面板 10 个分类页」「分类管理器」「UnifiedUI 工具栏」 |

## 发布包结构

```
RoadCategoryMod\
├── RoadCategoryMod.dll    必需（v1.0.0）
├── RoadCategoryMod.pdb    可选（调试用）
├── Thumbnail.png          模组缩略图
└── Icons\                 必需（默认图标，首次运行会复制到用户目录）
    ├── category_10.png … category_90.png
    ├── float_button.png
    └── category_tag.png
```

构建产物（`bin\Release`）已经包含 `Icons\`；把 `Workshop\Thumbnail.png` 复制进去即可作为发布包。
本仓库根目录的 `Release\RoadCategoryMod-v1.0.0.zip` 就是按此结构打好的包。

## 上传到创意工坊

### 方式 A：游戏内「内容管理器 → 发布」（推荐，v1.0.0 已用此方式准备）

游戏点发布会把**整个模组文件夹**复制到：

```
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\WorkshopStagingArea\<GUID>\
├── Content\               ← 会被上传的内容（模组文件夹的完整副本）
└── PreviewImage.png       ← 工坊封面图（默认是蓝图占位图，需要替换）
```

> ⚠️ 因为它复制**整个文件夹**，所以不要把旧版本备份（`*.bak`）放在模组目录里，
> 否则会一起被上传（体积变大、还可能让玩家困惑）。
> v1.0.0 发布时已把 `.bak` 移到 `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RoadCategoryMod\_old_builds\`。

发布前对 staging 目录做的整理（已做，可作为以后发布的检查项）：

1. 删除 `Content\*.bak`（旧版本备份）
2. 用 `preview_16x9_small.png`（644×360）替换 `PreviewImage.png`（游戏默认是蓝图占位图）
3. 确认 `Content\RoadCategoryMod.dll` 的版本 = **1.0.0.0**，且含 `Icons\`（12 个 PNG）与 `Thumbnail.png`
4. 再回游戏点「发布」

> 若中途取消并重新打开发布对话框，游戏会重新复制一遍模组目录：
> 模组目录现在是干净的，重新复制后只需再次替换 `PreviewImage.png` 即可。

### 方式 B：Steamworks 上传工具

1. **先关游戏**（DLL 被占用时无法覆盖/上传）
2. 确认 `bin\Release`（或 `Release\RoadCategoryMod-v1.0.0.zip` 解压后）内容完整，版本号为 **1.0.0.0**
3. 用 Steamworks 上传工具（AppID `255710`）把 `RoadCategoryMod\` 文件夹作为物品内容上传

### 发布后填写工坊页面

* **标题**：精细道路分类 / Fine Road Categories（游戏内发布会用模组名作标题，可在页面改）
* **描述**：粘贴 `Description_zh.txt`（英文页面用 `Description_en.txt`）
* **封面**：若页面显示的是蓝图占位图，改成 `preview_16x9.png` 或 `preview.png`
* **截图**：`screenshots/` 目录里的图片（建议再补 2–3 张游戏内截图）
* **标签**：Mod；描述开头已写明「需要 CitiesHarmony」
* **可见性**：确认设为「公开」
   * **封面**：`preview.png`
   * **截图**：`screenshots/` 目录里的图片（建议再补 2–3 张游戏内截图）
   * **标签**：Mod；描述开头已写明「需要 CitiesHarmony」
5. 发布后在自己的存档里跑一遍第 6 步的检查清单。

## 发布前检查清单

1. `Properties\AssemblyInfo.cs` 版本号 = **1.0.0.0**
2. `CHANGELOG.md` 顶部有 v1.0.0 公开版本条目
3. Release 构建 0 错误 0 警告，`bin\Release\Icons\` 有 12 个 PNG
4. 关闭游戏后部署到 `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RoadCategoryMod\`
5. 启动游戏 → 进城市 → 检查：
   - 道路面板出现 10 个分类页、图标正常
   - 圆形浮动按钮可拖动、大小可调
   - 管理器打开无卡顿，搜索/筛选/重置正常，日志里 `行控件 13 个`
   - 设置页各分组正常，切语言后 tab tooltip 跟着变
   - 启用 UnifiedUI 时，UUI 面板里其它模组图标仍在（回归点）
6. 关掉其它替换道路面板的模组（如 Better Road Toolbar）再测一次

## 源码仓库（已发布）

* 仓库：<https://github.com/ZuGuiChen/RoadCategoryMod>（公开）
* 首个版本：<https://github.com/ZuGuiChen/RoadCategoryMod/releases/tag/v1.0.0>（附 `RoadCategoryMod-v1.0.0.zip`）
* 账号：`ZuGuiChen`，使用 `gh`（GitHub CLI）创建与推送，已登录

### 以后提交更新

本机没有把 Git 加到 PATH，可以直接用 Visual Studio 自带的 git：

```powershell
$git = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe"
& $git add -A
& $git commit -m "v1.0.1: 修复 xxx"
& $git push
```

（装一个 Git for Windows 后就可以直接用 `git` 命令。）

打新版本 Release（会自动创建 tag 并上传发布包）：

```powershell
& "C:\Program Files\GitHub CLI\gh.exe" release create v1.0.1 "Release\RoadCategoryMod-v1.0.1.zip" --title "v1.0.1" --notes "更新说明"
```

> 注意：`bin/`、`obj/`、`.vs/`、`packages/`、`Release/` 已在 `.gitignore` 中排除；
> 游戏 DLL（`Assembly-CSharp.dll` 等）不在仓库里，编译时需要本机安装游戏与 CitiesHarmony。

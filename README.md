# 精细道路分类 / Fine Road Categories

《都市天际线 1》模组：把原版只有 4 类（小 / 中 / 大 / 高速）的道路面板，细分成 **10 个实用分类**，并提供一个可手动调整分类的管理器。

> 当前版本：**v1.0.0**（首个公开版本）· 游戏版本：Cities: Skylines 1.21 · 需要 CitiesHarmony
> 更新日志：[CHANGELOG.md](CHANGELOG.md)

---

## 功能

| 功能 | 说明 |
|---|---|
| 10 类细分 | 乡村公路 / 社区小路 / 小型道路 / 中型道路 / 主干道 / 大型道路 / 高速公路 / 巴士专用道 / 步行道路 / 其他道路 |
| 自动分类 | 按机动车道数、中心线、中央分隔带、公交专用道、道路名称等特征判断；支持创意工坊道路资产 |
| 分类管理器 | 缩略图 + 名称 + 车道数列表，搜索 / 分类筛选 / 逐条手动分类 / 单条重置 / 全部重置 |
| 手动分类持久化 | 保存在 `custom_categories.xml`，重启游戏后道路面板按你的设置显示 |
| 自定义图标 | 10 个分类 + 浮动按钮图标可换成自己的 PNG；模组自带一套默认图标 |
| 圆形浮动按钮 | 可拖动、28–64 像素、位置自动保存、可完全关闭 |
| UnifiedUI（可选） | 勾选后把入口放进 UnifiedUI 工具栏 |
| 9 种语言 | 简体中文 / English / Deutsch / Español / Français / 한국어 / Polski / Português / Русский，可跟随游戏语言 |
| 热键 | `Ctrl+Shift+Y` 打开道路总览窗口，可改为打开分类管理器 |
| 性能 | 管理器使用行控件池 + 可见区虚拟化，道路资产再多也保持恒定开销 |

---

## 前置与兼容性

### 必需

* **CitiesHarmony 2.2+**（Steam 创意工坊 id `2040656402`）——本模组使用 Harmony 补丁替换道路面板分类。

### 可选

* **UnifiedUI** —— 仅当你勾选「将图标加入 UnifiedUI」时才需要；未安装时该选项会自动提示不可用，不影响模组加载。

### 不兼容 / 需二选一

* 其它**同样替换道路面板父级分类**的模组，例如 **Better Road Toolbar**。这类模组与本模组功能重叠，同时启用会互相覆盖，只能保留一个。

### 已验证可共存（作者长期使用环境）

Find It 2、Move It、Traffic Manager: President Edition、Intersection Marking Tool、Network Anarchy / Road Anarchy、Node Controller Renewal、Quay Tools、Loading Screen Mod Revisited、FPS Booster、Skyve、Lumina、Network Multitool、Picker、Prop Control 等。
（这些模组不接管道路面板的父级分类，因此不会冲突。）

### 需要注意

* 本模组只影响 **UI 分类**，不修改道路本身的行为、容量、限速或存档数据；移除模组不会破坏城市。
* 手动分类改动后，道路面板的分类**需要重启游戏**才会重新排列（道路左下角分类标签弹窗里的改动会立即刷新）。
* 分类图标需要重启游戏才会重新加载。

---

## 安装

### 创意工坊（推荐）

订阅后由 Steam 自动安装。

### 手动安装

1. 关闭游戏；
2. 把发布包解压到：
   `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\RoadCategoryMod\`
3. 目录结构应为：

```
RoadCategoryMod\
├── RoadCategoryMod.dll
├── Thumbnail.png
└── Icons\
    ├── category_10.png … category_90.png
    ├── float_button.png
    └── category_tag.png
```

4. 启动游戏，在「内容管理器 → 模组」中启用本模组，并确保 **CitiesHarmony** 已启用。

---

## 使用

1. 进入城市后，屏幕左下角出现圆形浮动按钮，点击打开 **道路分类管理器**；
2. 在列表里用搜索 / 分类筛选找到道路，用下拉框手动指定分类（★ 表示手动分类），点「保存配置」；
3. 设置项在 **选项 → 模组设置 → 精细道路分类**：
   * 界面语言（自动跟随游戏 / 9 种语言）
   * 浮动按钮（开关、大小、重置位置）
   * 管理器（输入时实时过滤 / 仅回车·按钮触发）
   * 界面集成（加入 UnifiedUI）
   * 热键（开关、打开哪个窗口）
   * 自定义图标（说明、打开图标文件夹）
   * 手动分类数据（条数、一键清除）

---

## 自定义图标

图标目录：`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RoadCategoryMod\Icons\`

| 文件名 | 用途 |
|---|---|
| `category_10.png` … `category_90.png` | 10 个分类图标 |
| `float_button.png` | 浮动按钮图标 |
| `category_tag.png` | 分类标签图标 |

* 建议 64×64 PNG、透明背景；
* 也可用简写命名：`rural / community / small / medium / arterial / large / highway / bus / pedestrian / other`；
* 修改后需要重启游戏。

首次运行时，若上述目录为空，模组会把内置图标复制进去（不会覆盖已有自定义图标）。

---

## 配置文件

| 文件 | 位置 | 内容 |
|---|---|---|
| `RoadCategoryModSettings.xml` | `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities_ Skylines\`（注意目录名中间有空格） | 界面语言、浮动按钮位置/大小、热键、UnifiedUI 开关、搜索模式 |
| `custom_categories.xml` | `%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RoadCategoryMod\` | 手动分类覆盖 |
| `custom_icons.xml` | 同上 | 分类图标使用哪个游戏 sprite（可选，进阶） |

---

## 从源码构建

环境要求：

* Windows + **Visual Studio 2019/2022**（含「.NET 桌面开发」与 **.NET Framework 3.5 开发工具**）
* 已安装《城市：天际线》与 **CitiesHarmony**（用于引用 `Assembly-CSharp.dll`、`UnityEngine.dll`、`CitiesHarmony.Harmony.dll`）

```powershell
# 1) 默认路径（本项目作者的 Steam 路径）直接构建
msbuild RoadCategoryMod.csproj /p:Configuration=Release

# 2) 路径不同时用参数覆盖
msbuild RoadCategoryMod.csproj /p:Configuration=Release `
  /p:GameDir="C:\Program Files (x86)\Steam\steamapps\common\Cities_Skylines" `
  /p:HarmonyDir="C:\Program Files (x86)\Steam\steamapps\workshop\content\255710\2040656402"

# 3) 或使用脚本（构建 + 可选部署到本地模组目录）
.\build.ps1 -Deploy
```

也可用环境变量：`CITIES_SKYLINES_DIR`、`CITIES_HARMONY_DIR`。

构建产物在 `bin\Release\`：`RoadCategoryMod.dll`、`RoadCategoryMod.pdb`、`Icons\`（csproj 里的 `CopyDefaultIcons` 目标会自动复制图标）。

---

## 目录结构

```
.
├── RoadCategoryMod.csproj      项目文件（游戏路径可用 -p:GameDir 覆盖）
├── build.ps1                   一键构建 / 部署脚本
├── RoadCategoryMod.cs          模组入口（IUserMod / LoadingExtension）
├── Localization.cs             9 种语言文本表（88 键）
├── ModSettings.cs              设置持久化
├── ModSettingsUI.cs            「选项 → 模组设置」页面
├── RoadClassifier.cs           自动分类规则
├── RoadCategoryConfig.cs       手动分类配置
├── RoadCategoryManagerUI.cs    分类管理器（行控件池 + 虚拟化）
├── RoadGroupTabs.cs            道路面板分类 tab 的图标 / tooltip
├── CustomIconLoader.cs         自定义 PNG 图标加载
├── UiIcons.cs                  圆形按钮底图 / UnifiedUI 图标生成
├── UnifiedUIIntegration.cs     UnifiedUI 集成（反射，可选）
├── SearchFilterState.cs        搜索状态机（可单测）
├── AssemblyVersionPicker.cs    多份 UnifiedUILib 副本选择（可单测）
├── CategoryMarker.cs           RCM/xx 标记解析（可单测）
├── HotkeyListener.cs           Ctrl+Shift+Y
├── IndependentWindow.cs        浮动按钮 / 道路总览窗口
├── CollectAssetsPatch.cs       替换道路面板父级分类
├── PopulateGroupsPatch.cs      分类 tab 图标 / tooltip
├── IsCategoryValidPatch.cs     道路归属判断
├── CreateButtonPatch.cs        道路按钮上的分类小图标
├── CategorySelectPopup.cs      分类选择弹窗
├── IconConfig.cs               sprite 图标映射（可选）
├── Icons\                      默认图标（随模组发布）
├── Workshop\                   创意工坊发布材料
└── CHANGELOG.md                开发日志
```

---

## 已知限制

* 与同类"替换道路面板分类"的模组不兼容（见上文）。
* 手动分类 / 图标修改需要重启游戏才会在道路面板生效。
* 管理器快速拖动滚动条时，行内容可能出现 1 帧延迟（肉眼基本不可见）。
* 内置默认图标仅在图标目录为空时复制。

---

## 版本与更新

版本号规则：`主版本.次版本.修订号`。首个公开版本为 **v1.0.0**（2026-10-22），发布前的内部迭代记录在 [CHANGELOG.md](CHANGELOG.md) 中标注为 `[内部开发版]`。

---

## 许可

[MIT](LICENSE)

## 致谢

* 豆包 AI（Doubao AI）—— 初始框架与功能开发
* DeepSeek AI（DeepSeek Harness）—— 多语言、设置页、性能优化、UnifiedUI 集成、图标与发布整理
* Cities: Skylines 模组社区（Find It 2 / Better Road Toolbar 等提供的实现思路）

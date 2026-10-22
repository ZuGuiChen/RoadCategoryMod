using System;
using System.Collections.Generic;
using ColossalFramework.Globalization;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 模组多语言支持。
    ///
    /// 设计要点：
    /// 1. 语言代码与游戏官方语言文件保持一致（zh / en / de / es / fr / ko / pl / pt / ru）；
    /// 2. 玩家可在游戏“选项 - 模组设置”中选择语言，也可选择“自动”（跟随游戏语言）；
    /// 3. 查找顺序：当前语言 -&gt; 英文 -&gt; 中文 -&gt; key 本身，任何缺失都不会抛异常；
    /// 4. 切换语言会触发 LanguageChanged 事件，设置页与游戏内面板会即时刷新。
    /// </summary>
    public static class Localization
    {
        /// <summary>跟随游戏语言的设置值。</summary>
        public const string AutoCode = "auto";

        /// <summary>兜底语言。</summary>
        public const string FallbackCode = "en";

        /// <summary>游戏官方支持的 9 种界面语言。</summary>
        public static readonly string[] SupportedCodes = new string[]
        {
            "zh", "en", "de", "es", "fr", "ko", "pl", "pt", "ru"
        };

        /// <summary>语言自身的显示名称（不随界面语言变化）。</summary>
        private static readonly Dictionary<string, string> NativeNames = new Dictionary<string, string>
        {
            { "zh", "简体中文" },
            { "en", "English" },
            { "de", "Deutsch" },
            { "es", "Español" },
            { "fr", "Français" },
            { "ko", "한국어" },
            { "pl", "Polski" },
            { "pt", "Português (Brasil)" },
            { "ru", "Русский" }
        };

        private static Dictionary<string, Dictionary<string, string>> _table;
        private static string _effectiveCode = FallbackCode;
        private static bool _initialized;
        private static bool _refreshing;
        private static bool _detecting;

        /// <summary>界面语言变化后触发（设置页重建、游戏内面板刷新）。</summary>
        public static event Action LanguageChanged;

        /// <summary>当前实际生效的语言代码。</summary>
        public static string CurrentCode
        {
            get
            {
                EnsureInitialized();
                return _effectiveCode;
            }
        }

        /// <summary>玩家在设置中选择的语言代码（可能是 auto）。</summary>
        public static string SettingCode
        {
            get
            {
                string code = ModSettings.Current.Language;
                if (string.IsNullOrEmpty(code))
                    return AutoCode;
                code = code.Trim().ToLowerInvariant();
                if (code == AutoCode || !IsSupported(code))
                    return AutoCode;
                return code;
            }
        }

        /// <summary>设置页下拉框的选项代码（auto + 9 种语言）。</summary>
        public static string[] LanguageOptionCodes
        {
            get
            {
                var list = new List<string>();
                list.Add(AutoCode);
                list.AddRange(SupportedCodes);
                return list.ToArray();
            }
        }

        /// <summary>判断语言代码是否为受支持的语言。</summary>
        public static bool IsSupported(string code)
        {
            if (string.IsNullOrEmpty(code))
                return false;
            for (int i = 0; i < SupportedCodes.Length; i++)
            {
                if (string.Compare(SupportedCodes[i], code, StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }

        /// <summary>语言的下拉框显示名称；auto 使用当前界面语言的“自动”文案。</summary>
        public static string GetLanguageOptionLabel(string code)
        {
            if (string.IsNullOrEmpty(code) || code == AutoCode)
                return Get("SET_LANGUAGE_AUTO");
            return GetLanguageNativeName(code);
        }

        /// <summary>语言的自称（如 Deutsch、Français）。</summary>
        public static string GetLanguageNativeName(string code)
        {
            if (string.IsNullOrEmpty(code))
                return string.Empty;
            string name;
            if (NativeNames.TryGetValue(code.ToLowerInvariant(), out name))
                return name;
            return code;
        }

        /// <summary>取一条本地化文本。</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            EnsureInitialized();

            string value;
            Dictionary<string, string> dict = GetDictionary(_effectiveCode);
            if (dict != null && dict.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                return value;

            dict = GetDictionary(FallbackCode);
            if (dict != null && dict.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                return value;

            dict = GetDictionary("zh");
            if (dict != null && dict.TryGetValue(key, out value) && !string.IsNullOrEmpty(value))
                return value;

            return key;
        }

        /// <summary>取一条带占位符的本地化文本并格式化。</summary>
        public static string Format(string key, params object[] args)
        {
            string pattern = Get(key);
            if (args == null || args.Length == 0)
                return pattern;
            try
            {
                return string.Format(pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }

        /// <summary>分类显示名称（用于面板、下拉框、tooltip）。</summary>
        public static string GetCategoryName(RoadCategory cat)
        {
            switch (cat)
            {
                case RoadCategory.RuralRoads: return Get("CAT_RURAL");
                case RoadCategory.CommunityRoads: return Get("CAT_COMMUNITY");
                case RoadCategory.SmallRoads: return Get("CAT_SMALL");
                case RoadCategory.MediumRoads: return Get("CAT_MEDIUM");
                case RoadCategory.ArterialRoads: return Get("CAT_ARTERIAL");
                case RoadCategory.LargeRoads: return Get("CAT_LARGE");
                case RoadCategory.Highways: return Get("CAT_HIGHWAY");
                case RoadCategory.BusRoads: return Get("CAT_BUS");
                case RoadCategory.PedestrianRoads: return Get("CAT_PEDESTRIAN");
                case RoadCategory.OtherRoads: return Get("CAT_OTHER");
                default: return cat.ToString();
            }
        }

        /// <summary>
        /// 改变界面语言：写入设置、立即重新解析语言，并通知界面刷新。
        /// </summary>
        public static void SetLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
                code = AutoCode;
            code = code.Trim().ToLowerInvariant();
            if (code != AutoCode && !IsSupported(code))
                code = AutoCode;

            ModSettings settings = ModSettings.Current;
            settings.Language = code;
            settings.Save();

            _initialized = false;
            EnsureInitialized();

            FireLanguageChanged();
        }

        private static void FireLanguageChanged()
        {
            Action handler = LanguageChanged;
            if (handler == null)
                return;

            try
            {
                handler();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] LanguageChanged handler failed: " + e.Message);
            }
        }

        /// <summary>
        /// 重新解析语言（例如打开设置页、进入城市、玩家庭改了游戏语言后）。
        /// 实际生效语言发生变化时会触发 LanguageChanged，让界面跟着刷新。
        ///
        /// 注意：这里绝不允许重入。游戏在首次构造 LocaleManager 的过程中就会
        /// 触发 locale 事件，若在此时再去访问 LocaleManager.instance，Mono 会
        /// 递归创建新的 LocaleManager（v1.1.0 首版正是因此卡死在 logo）。
        /// </summary>
        public static void Refresh()
        {
            if (_refreshing)
                return;

            _refreshing = true;
            try
            {
                string before = _effectiveCode;
                _initialized = false;
                EnsureInitialized();

                if (_effectiveCode != before)
                    FireLanguageChanged();
            }
            finally
            {
                _refreshing = false;
            }
        }

        private static Dictionary<string, string> GetDictionary(string code)
        {
            if (_table == null)
            {
                try
                {
                    _table = CreateTable();
                }
                catch (Exception e)
                {
                    Debug.LogError("[RoadCategoryMod] 语言表初始化失败: " + e.Message);
                    return null;
                }
            }

            Dictionary<string, string> dict;
            if (_table.TryGetValue(code, out dict))
                return dict;
            return null;
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;
            _initialized = true;

            try
            {
                if (_table == null)
                    _table = CreateTable();

                string setting = SettingCode;
                if (setting == AutoCode)
                {
                    // 自动模式：跟随游戏语言
                    string detected;
                    if (TryDetectGameLanguage(out detected))
                        _effectiveCode = detected;
                    else if (string.IsNullOrEmpty(_effectiveCode))
                        _effectiveCode = FallbackCode;
                }
                else
                {
                    _effectiveCode = setting;
                }

                if (!IsSupported(_effectiveCode))
                    _effectiveCode = FallbackCode;
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] Localization init failed: " + e.Message);
                _effectiveCode = FallbackCode;
            }
        }

        /// <summary>
        /// 读取游戏当前语言（zh / en / de / ...）。
        /// 返回值：
        /// true  - 已确定游戏语言（受支持则返回该代码；不受支持则返回兜底语言 en）；
        /// false - 无法确定（语言管理器尚未就绪），调用方应保持当前语言。
        ///
        /// 安全约束：只读取“已经存在”的 LocaleManager 实例，绝不在这里触发构造，
        /// 否则会在游戏初始化语言的过程中递归创建实例（导致启动卡死）。
        /// </summary>
        private static bool TryDetectGameLanguage(out string code)
        {
            code = null;

            if (_detecting)
                return false;

            _detecting = true;
            try
            {
                // exists 只是静态字段判空，不会创建实例
                if (!LocaleManager.exists)
                    return false;

                string raw = LocaleManager.instance.language;
                if (string.IsNullOrEmpty(raw))
                    return false;

                raw = raw.Trim().ToLowerInvariant();
                int cut = raw.IndexOf('-');
                if (cut < 0)
                    cut = raw.IndexOf('_');
                if (cut > 0)
                    raw = raw.Substring(0, cut);

                if (raw == "zh" || raw == "chs" || raw == "zhcn" || raw == "zhhans")
                {
                    code = "zh";
                    return true;
                }

                // 游戏语言不在支持列表时统一使用英文
                code = IsSupported(raw) ? raw : FallbackCode;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _detecting = false;
            }
        }

        // ==================== 语言数据 ====================

        private static Dictionary<string, Dictionary<string, string>> CreateTable()
        {
            var table = new Dictionary<string, Dictionary<string, string>>();
            table["zh"] = Chinese();
            table["en"] = English();
            table["de"] = German();
            table["es"] = Spanish();
            table["fr"] = French();
            table["ko"] = Korean();
            table["pl"] = Polish();
            table["pt"] = Portuguese();
            table["ru"] = Russian();
            return table;
        }

        private static Dictionary<string, string> Chinese()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "精细道路分类" },
                { "MOD_DESCRIPTION", "将道路面板细分为 10 个分类 · 手动分类管理器 · 自定义图标" },

                { "SET_GROUP_OVERVIEW", "概览" },
                { "SET_OVERVIEW_LINE1", "版本 v{0}" },
                { "SET_OVERVIEW_LINE2", "进入游戏后，点击浮动按钮即可打开道路分类管理器。" },
                { "SET_OVERVIEW_LINE3", "也可以点击下面的按钮直接打开。" },
                { "SET_BTN_OPEN_MANAGER", "打开道路分类管理器" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "管理器仅在游戏内可用，请先进入城市。" },
                { "SET_BTN_OPEN_CONFIG", "打开配置文件夹" },

                { "SET_GROUP_LANGUAGE", "界面语言" },
                { "SET_LANGUAGE_LABEL", "语言" },
                { "SET_LANGUAGE_AUTO", "自动（跟随游戏）" },
                { "SET_LANGUAGE_CURRENT", "当前使用：{0}" },
                { "SET_LANGUAGE_HINT", "切换后本页面会立即刷新。" },

                { "SET_GROUP_FLOATING", "浮动按钮" },
                { "SET_FLOATING_ENABLE", "启用浮动按钮" },
                { "SET_FLOATING_IN_GAME_ONLY", "浮动按钮仅在游戏内显示。" },
                { "SET_FLOATING_SIZE", "按钮大小" },
                { "SET_BTN_RESET_POSITION", "重置按钮位置" },
                { "SET_FLOATING_POSITION_RESET", "浮动按钮位置已重置。" },
                { "SET_FLOATING_HINT1", "拖动按钮可移动位置，位置会自动保存。" },
                { "SET_FLOATING_HINT2", "仅在游戏内生效。" },

                { "SET_GROUP_INTEGRATION", "界面集成" },
                { "SET_UUI_ENABLE", "将图标加入 UnifiedUI" },
                { "SET_UUI_HINT", "需要订阅 UnifiedUI 模组；加入后可从 UnifiedUI 工具栏直接打开管理器。" },
                { "SET_UUI_UNAVAILABLE", "未检测到 UnifiedUI，功能不可用。" },
                { "SET_UUI_PENDING", "正在等待 UnifiedUI 面板创建…（通常几秒内生效）" },
                { "SET_UUI_ENABLED", "已加入 UnifiedUI 工具栏。" },
                { "SET_UUI_DISABLED", "已从 UnifiedUI 移除。" },
                { "SET_UUI_IN_GAME_ONLY", "请在游戏内切换该选项。" },
                { "SET_UUI_FAILED", "加入 UnifiedUI 失败，请查看日志。" },
                { "SET_UUI_RESTART_HINT", "按钮可能仍会显示，重启游戏后生效。" },

                { "SET_GROUP_MANAGER", "管理器" },
                { "SET_MANAGER_LIVE_SEARCH", "输入时实时过滤" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "关闭后只有按回车或点「搜索」按钮才会过滤列表。" },
                { "MANAGER_SEARCH_BUTTON", "搜索" },

                { "SET_GROUP_HOTKEY", "热键" },
                { "SET_HOTKEY_ENABLE", "启用热键 Ctrl+Shift+Y" },
                { "SET_HOTKEY_TARGET", "热键打开" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "道路总览窗口" },
                { "SET_HOTKEY_TARGET_MANAGER", "道路分类管理器" },
                { "SET_HOTKEY_HINT", "热键仅在游戏内生效。" },

                { "SET_GROUP_ICONS", "自定义图标" },
                { "SET_ICONS_LINE1", "将 64×64 的 PNG 图标放入图标文件夹：" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png（10 个分类）" },
                { "SET_ICONS_LINE3", "也可命名为：rural、community、small、medium、" },
                { "SET_ICONS_LINE4", "arterial、large、highway、bus、pedestrian、other" },
                { "SET_ICONS_LINE5", "float_button.png 为浮动按钮图标。" },
                { "SET_ICONS_LINE6", "修改后需重启游戏才能生效。" },
                { "SET_BTN_OPEN_ICONS", "打开图标文件夹" },

                { "SET_GROUP_DATA", "手动分类数据" },
                { "SET_DATA_COUNT", "已保存的手动分类：{0} 条" },
                { "SET_DATA_CLEAR", "清除所有手动分类" },
                { "SET_DATA_CLEAR_CONFIRM", "再次点击确认清除（共 {0} 条）" },
                { "SET_DATA_CLEARED", "已清除所有手动分类。" },
                { "SET_DATA_CLEARED_NOTHING", "没有需要清除的手动分类。" },
                { "SET_DATA_HINT", "清除后道路恢复为自动分类。" },

                { "MANAGER_TITLE", "道路分类管理器" },
                { "MANAGER_SEARCH", "搜索..." },
                { "MANAGER_FILTER_ALL", "全部分类" },
                { "MANAGER_SAVE", "保存配置" },
                { "MANAGER_RESET_ALL", "重置全部" },
                { "MANAGER_REFRESH", "刷新" },
                { "MANAGER_READY", "就绪" },
                { "MANAGER_STATUS", "共 {0} 条道路，显示 {1} 条 | ★ = 手动分类" },
                { "MANAGER_SAVED", "配置已保存，重启游戏后道路面板分类生效。" },
                { "MANAGER_RESET_ALL_DONE", "已重置所有手动分类。" },
                { "MANAGER_RESET_ONE", "已重置：{0}" },
                { "MANAGER_SET", "已设置：{0} → {1}（点击“保存配置”生效）" },
                { "MANAGER_LANES", "{0} 车道" },
                { "MANAGER_MANUAL_TAG", "手动分类" },
                { "MANAGER_ROW_RESET", "重置" },

                { "POPUP_TITLE", "选择道路分类" },
                { "POPUP_RESTORE_AUTO", "恢复自动分类" },

                { "ROAD_TOOLTIP_MANUAL", "★ 手动：{0}（点击修改）" },
                { "ROAD_TOOLTIP_AUTO", "自动：{0}（点击修改）" },

                { "FLOAT_BUTTON_TOOLTIP", "道路分类管理器" },
                { "LEGACY_TITLE", "道路分类总览" },

                { "CAT_RURAL", "乡村公路" },
                { "CAT_COMMUNITY", "社区小路" },
                { "CAT_SMALL", "小型道路" },
                { "CAT_MEDIUM", "中型道路" },
                { "CAT_ARTERIAL", "主干道" },
                { "CAT_LARGE", "大型道路" },
                { "CAT_HIGHWAY", "高速公路" },
                { "CAT_BUS", "巴士专用道" },
                { "CAT_PEDESTRIAN", "步行道路" },
                { "CAT_OTHER", "其他道路" }
            };
        }

        private static Dictionary<string, string> English()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Fine Road Categories" },
                { "MOD_DESCRIPTION", "Split the road panel into 10 fine categories · Manual category manager · Custom icons" },

                { "SET_GROUP_OVERVIEW", "Overview" },
                { "SET_OVERVIEW_LINE1", "Version {0}" },
                { "SET_OVERVIEW_LINE2", "In game, the floating button opens the road category manager." },
                { "SET_OVERVIEW_LINE3", "You can also use the button below." },
                { "SET_BTN_OPEN_MANAGER", "Open road category manager" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "The manager is only available in game. Enter a city first." },
                { "SET_BTN_OPEN_CONFIG", "Open config folder" },

                { "SET_GROUP_LANGUAGE", "Language" },
                { "SET_LANGUAGE_LABEL", "Language" },
                { "SET_LANGUAGE_AUTO", "Auto (follow game)" },
                { "SET_LANGUAGE_CURRENT", "Currently using: {0}" },
                { "SET_LANGUAGE_HINT", "This page refreshes immediately after switching." },

                { "SET_GROUP_FLOATING", "Floating button" },
                { "SET_FLOATING_ENABLE", "Enable floating button" },
                { "SET_FLOATING_IN_GAME_ONLY", "The floating button only shows in game." },
                { "SET_FLOATING_SIZE", "Button size" },
                { "SET_BTN_RESET_POSITION", "Reset button position" },
                { "SET_FLOATING_POSITION_RESET", "Floating button position reset." },
                { "SET_FLOATING_HINT1", "Drag the button to move it; the position is saved automatically." },
                { "SET_FLOATING_HINT2", "In-game only." },

                { "SET_GROUP_INTEGRATION", "Integration" },
                { "SET_UUI_ENABLE", "Add icon to UnifiedUI" },
                { "SET_UUI_HINT", "Requires the UnifiedUI mod. When enabled, the manager can be opened from the UnifiedUI toolbar." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI not detected; this option is unavailable." },
                { "SET_UUI_PENDING", "Waiting for the UnifiedUI panel… (usually a few seconds)" },
                { "SET_UUI_ENABLED", "Added to the UnifiedUI toolbar." },
                { "SET_UUI_DISABLED", "Removed from UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Change this option while in game." },
                { "SET_UUI_FAILED", "Failed to add to UnifiedUI; see the log." },
                { "SET_UUI_RESTART_HINT", "The button may still be visible; restart the game to apply." },

                { "SET_GROUP_MANAGER", "Manager" },
                { "SET_MANAGER_LIVE_SEARCH", "Filter while typing" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "When off, only Enter or the Search button filters the list." },
                { "MANAGER_SEARCH_BUTTON", "Search" },

                { "SET_GROUP_HOTKEY", "Hotkey" },
                { "SET_HOTKEY_ENABLE", "Enable hotkey Ctrl+Shift+Y" },
                { "SET_HOTKEY_TARGET", "Hotkey opens" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Road overview window" },
                { "SET_HOTKEY_TARGET_MANAGER", "Road category manager" },
                { "SET_HOTKEY_HINT", "The hotkey only works in game." },

                { "SET_GROUP_ICONS", "Custom icons" },
                { "SET_ICONS_LINE1", "Put 64×64 PNG files in the icon folder:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 categories)" },
                { "SET_ICONS_LINE3", "or name them: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png is the floating button icon." },
                { "SET_ICONS_LINE6", "Restart the game to apply changes." },
                { "SET_BTN_OPEN_ICONS", "Open icon folder" },

                { "SET_GROUP_DATA", "Manual categories" },
                { "SET_DATA_COUNT", "Saved manual categories: {0}" },
                { "SET_DATA_CLEAR", "Clear all manual categories" },
                { "SET_DATA_CLEAR_CONFIRM", "Click again to confirm ({0})" },
                { "SET_DATA_CLEARED", "All manual categories cleared." },
                { "SET_DATA_CLEARED_NOTHING", "There is nothing to clear." },
                { "SET_DATA_HINT", "Cleared roads fall back to automatic categories." },

                { "MANAGER_TITLE", "Road Category Manager" },
                { "MANAGER_SEARCH", "Search..." },
                { "MANAGER_FILTER_ALL", "All categories" },
                { "MANAGER_SAVE", "Save" },
                { "MANAGER_RESET_ALL", "Reset all" },
                { "MANAGER_REFRESH", "Refresh" },
                { "MANAGER_READY", "Ready" },
                { "MANAGER_STATUS", "{0} roads, showing {1} | ★ = manual" },
                { "MANAGER_SAVED", "Saved. The road panel applies the new categories after a restart." },
                { "MANAGER_RESET_ALL_DONE", "All manual categories reset." },
                { "MANAGER_RESET_ONE", "Reset: {0}" },
                { "MANAGER_SET", "Set: {0} → {1} (click Save to apply)" },
                { "MANAGER_LANES", "{0} lanes" },
                { "MANAGER_MANUAL_TAG", "Manual category" },
                { "MANAGER_ROW_RESET", "Reset" },

                { "POPUP_TITLE", "Choose road category" },
                { "POPUP_RESTORE_AUTO", "Restore automatic" },

                { "ROAD_TOOLTIP_MANUAL", "★ Manual: {0} (click to change)" },
                { "ROAD_TOOLTIP_AUTO", "Automatic: {0} (click to change)" },

                { "FLOAT_BUTTON_TOOLTIP", "Road Category Manager" },
                { "LEGACY_TITLE", "Road overview" },

                { "CAT_RURAL", "Rural roads" },
                { "CAT_COMMUNITY", "Community roads" },
                { "CAT_SMALL", "Small roads" },
                { "CAT_MEDIUM", "Medium roads" },
                { "CAT_ARTERIAL", "Arterial roads" },
                { "CAT_LARGE", "Large roads" },
                { "CAT_HIGHWAY", "Highways" },
                { "CAT_BUS", "Bus roads" },
                { "CAT_PEDESTRIAN", "Pedestrian roads" },
                { "CAT_OTHER", "Other roads" }
            };
        }

        private static Dictionary<string, string> German()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Feine Straßenkategorien" },
                { "MOD_DESCRIPTION", "Teilt das Straßenmenü in 10 Kategorien · Manueller Kategorie-Manager · Eigene Symbole" },

                { "SET_GROUP_OVERVIEW", "Übersicht" },
                { "SET_OVERVIEW_LINE1", "Version {0}" },
                { "SET_OVERVIEW_LINE2", "Im Spiel öffnet die Schaltfläche den Straßenkategorie-Manager." },
                { "SET_OVERVIEW_LINE3", "Alternativ die Schaltfläche unten verwenden." },
                { "SET_BTN_OPEN_MANAGER", "Straßenkategorie-Manager öffnen" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "Der Manager ist nur im Spiel verfügbar. Lade zuerst eine Stadt." },
                { "SET_BTN_OPEN_CONFIG", "Konfigurationsordner öffnen" },

                { "SET_GROUP_LANGUAGE", "Sprache" },
                { "SET_LANGUAGE_LABEL", "Sprache" },
                { "SET_LANGUAGE_AUTO", "Automatisch (wie Spiel)" },
                { "SET_LANGUAGE_CURRENT", "Aktuell: {0}" },
                { "SET_LANGUAGE_HINT", "Diese Seite wird nach dem Wechsel sofort aktualisiert." },

                { "SET_GROUP_FLOATING", "Schaltfläche" },
                { "SET_FLOATING_ENABLE", "Schaltfläche aktivieren" },
                { "SET_FLOATING_IN_GAME_ONLY", "Die Schaltfläche erscheint nur im Spiel." },
                { "SET_FLOATING_SIZE", "Größe der Schaltfläche" },
                { "SET_BTN_RESET_POSITION", "Position zurücksetzen" },
                { "SET_FLOATING_POSITION_RESET", "Position der Schaltfläche zurückgesetzt." },
                { "SET_FLOATING_HINT1", "Ziehen verschiebt die Schaltfläche; die Position wird gespeichert." },
                { "SET_FLOATING_HINT2", "Nur im Spiel wirksam." },

                { "SET_GROUP_INTEGRATION", "Integration" },
                { "SET_UUI_ENABLE", "Symbol zu UnifiedUI hinzufügen" },
                { "SET_UUI_HINT", "Erfordert die Mod UnifiedUI. Danach lässt sich der Manager über die UnifiedUI-Leiste öffnen." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI nicht gefunden; Option nicht verfügbar." },
                { "SET_UUI_PENDING", "Warte auf das UnifiedUI-Panel… (meist wenige Sekunden)" },
                { "SET_UUI_ENABLED", "Zur UnifiedUI-Leiste hinzugefügt." },
                { "SET_UUI_DISABLED", "Aus UnifiedUI entfernt." },
                { "SET_UUI_IN_GAME_ONLY", "Diese Option nur im Spiel ändern." },
                { "SET_UUI_FAILED", "Hinzufügen zu UnifiedUI fehlgeschlagen; siehe Log." },
                { "SET_UUI_RESTART_HINT", "Die Schaltfläche ist evtl. noch sichtbar; Neustart erforderlich." },

                { "SET_GROUP_MANAGER", "Manager" },
                { "SET_MANAGER_LIVE_SEARCH", "Beim Tippen filtern" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Wenn deaktiviert, filtern nur Eingabetaste oder „Suchen“." },
                { "MANAGER_SEARCH_BUTTON", "Suchen" },

                { "SET_GROUP_HOTKEY", "Tastenkürzel" },
                { "SET_HOTKEY_ENABLE", "Tastenkürzel Strg+Umschalt+Y aktivieren" },
                { "SET_HOTKEY_TARGET", "Kürzel öffnet" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Straßenübersicht" },
                { "SET_HOTKEY_TARGET_MANAGER", "Straßenkategorie-Manager" },
                { "SET_HOTKEY_HINT", "Das Kürzel funktioniert nur im Spiel." },

                { "SET_GROUP_ICONS", "Eigene Symbole" },
                { "SET_ICONS_LINE1", "Lege 64×64-PNG-Dateien im Symbolordner ab:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 Kategorien)" },
                { "SET_ICONS_LINE3", "oder benenne sie: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png ist das Symbol der Schaltfläche." },
                { "SET_ICONS_LINE6", "Für Änderungen das Spiel neu starten." },
                { "SET_BTN_OPEN_ICONS", "Symbolordner öffnen" },

                { "SET_GROUP_DATA", "Manuelle Kategorien" },
                { "SET_DATA_COUNT", "Gespeicherte manuelle Kategorien: {0}" },
                { "SET_DATA_CLEAR", "Alle manuellen Kategorien löschen" },
                { "SET_DATA_CLEAR_CONFIRM", "Erneut klicken zum Bestätigen ({0})" },
                { "SET_DATA_CLEARED", "Alle manuellen Kategorien gelöscht." },
                { "SET_DATA_CLEARED_NOTHING", "Es gibt nichts zu löschen." },
                { "SET_DATA_HINT", "Gelöschte Straßen werden wieder automatisch einsortiert." },

                { "MANAGER_TITLE", "Straßenkategorie-Manager" },
                { "MANAGER_SEARCH", "Suchen..." },
                { "MANAGER_FILTER_ALL", "Alle Kategorien" },
                { "MANAGER_SAVE", "Speichern" },
                { "MANAGER_RESET_ALL", "Alles zurücksetzen" },
                { "MANAGER_REFRESH", "Aktualisieren" },
                { "MANAGER_READY", "Bereit" },
                { "MANAGER_STATUS", "{0} Straßen, {1} angezeigt | ★ = manuell" },
                { "MANAGER_SAVED", "Gespeichert. Nach einem Neustart nutzt das Straßenmenü die neuen Kategorien." },
                { "MANAGER_RESET_ALL_DONE", "Alle manuellen Kategorien zurückgesetzt." },
                { "MANAGER_RESET_ONE", "Zurückgesetzt: {0}" },
                { "MANAGER_SET", "Gesetzt: {0} → {1} (zum Anwenden speichern)" },
                { "MANAGER_LANES", "{0} Spuren" },
                { "MANAGER_MANUAL_TAG", "Manuelle Kategorie" },
                { "MANAGER_ROW_RESET", "Zurücksetzen" },

                { "POPUP_TITLE", "Straßenkategorie wählen" },
                { "POPUP_RESTORE_AUTO", "Automatisch wiederherstellen" },

                { "ROAD_TOOLTIP_MANUAL", "★ Manuell: {0} (zum Ändern klicken)" },
                { "ROAD_TOOLTIP_AUTO", "Automatisch: {0} (zum Ändern klicken)" },

                { "FLOAT_BUTTON_TOOLTIP", "Straßenkategorie-Manager" },
                { "LEGACY_TITLE", "Straßenübersicht" },

                { "CAT_RURAL", "Landstraßen" },
                { "CAT_COMMUNITY", "Wohnstraßen" },
                { "CAT_SMALL", "Kleine Straßen" },
                { "CAT_MEDIUM", "Mittlere Straßen" },
                { "CAT_ARTERIAL", "Hauptstraßen" },
                { "CAT_LARGE", "Große Straßen" },
                { "CAT_HIGHWAY", "Autobahnen" },
                { "CAT_BUS", "Busspuren" },
                { "CAT_PEDESTRIAN", "Fußgängerstraßen" },
                { "CAT_OTHER", "Sonstige Straßen" }
            };
        }

        private static Dictionary<string, string> Spanish()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Categorías de calles refinadas" },
                { "MOD_DESCRIPTION", "Divide el panel de calles en 10 categorías · Gestor manual · Iconos personalizados" },

                { "SET_GROUP_OVERVIEW", "Resumen" },
                { "SET_OVERVIEW_LINE1", "Versión {0}" },
                { "SET_OVERVIEW_LINE2", "En el juego, el botón flotante abre el gestor de categorías." },
                { "SET_OVERVIEW_LINE3", "También puedes usar el botón de abajo." },
                { "SET_BTN_OPEN_MANAGER", "Abrir gestor de categorías de calles" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "El gestor solo está disponible en el juego. Entra en una ciudad primero." },
                { "SET_BTN_OPEN_CONFIG", "Abrir carpeta de configuración" },

                { "SET_GROUP_LANGUAGE", "Idioma" },
                { "SET_LANGUAGE_LABEL", "Idioma" },
                { "SET_LANGUAGE_AUTO", "Automático (según el juego)" },
                { "SET_LANGUAGE_CURRENT", "En uso: {0}" },
                { "SET_LANGUAGE_HINT", "Esta página se actualiza al cambiar el idioma." },

                { "SET_GROUP_FLOATING", "Botón flotante" },
                { "SET_FLOATING_ENABLE", "Activar botón flotante" },
                { "SET_FLOATING_IN_GAME_ONLY", "El botón flotante solo aparece en el juego." },
                { "SET_FLOATING_SIZE", "Tamaño del botón" },
                { "SET_BTN_RESET_POSITION", "Restablecer posición" },
                { "SET_FLOATING_POSITION_RESET", "Posición del botón restablecida." },
                { "SET_FLOATING_HINT1", "Arrastra el botón para moverlo; la posición se guarda sola." },
                { "SET_FLOATING_HINT2", "Solo se aplica en el juego." },

                { "SET_GROUP_INTEGRATION", "Integración" },
                { "SET_UUI_ENABLE", "Añadir icono a UnifiedUI" },
                { "SET_UUI_HINT", "Requiere el mod UnifiedUI. Al activarlo, el gestor se abre desde la barra de UnifiedUI." },
                { "SET_UUI_UNAVAILABLE", "No se detectó UnifiedUI; opción no disponible." },
                { "SET_UUI_PENDING", "Esperando el panel de UnifiedUI… (suele tardar unos segundos)" },
                { "SET_UUI_ENABLED", "Añadido a la barra de UnifiedUI." },
                { "SET_UUI_DISABLED", "Eliminado de UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Cambia esta opción dentro del juego." },
                { "SET_UUI_FAILED", "No se pudo añadir a UnifiedUI; revisa el registro." },
                { "SET_UUI_RESTART_HINT", "El botón puede seguir visible; reinicia el juego." },

                { "SET_GROUP_MANAGER", "Gestor" },
                { "SET_MANAGER_LIVE_SEARCH", "Filtrar al escribir" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Si está desactivado, solo Enter o el botón Buscar filtran la lista." },
                { "MANAGER_SEARCH_BUTTON", "Buscar" },

                { "SET_GROUP_HOTKEY", "Tecla rápida" },
                { "SET_HOTKEY_ENABLE", "Activar tecla rápida Ctrl+Mayús+Y" },
                { "SET_HOTKEY_TARGET", "La tecla abre" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Ventana de resumen de calles" },
                { "SET_HOTKEY_TARGET_MANAGER", "Gestor de categorías" },
                { "SET_HOTKEY_HINT", "La tecla rápida solo funciona en el juego." },

                { "SET_GROUP_ICONS", "Iconos personalizados" },
                { "SET_ICONS_LINE1", "Coloca PNG de 64×64 en la carpeta de iconos:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 categorías)" },
                { "SET_ICONS_LINE3", "o nómbralos: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png es el icono del botón flotante." },
                { "SET_ICONS_LINE6", "Reinicia el juego para aplicar los cambios." },
                { "SET_BTN_OPEN_ICONS", "Abrir carpeta de iconos" },

                { "SET_GROUP_DATA", "Categorías manuales" },
                { "SET_DATA_COUNT", "Categorías manuales guardadas: {0}" },
                { "SET_DATA_CLEAR", "Borrar todas las categorías manuales" },
                { "SET_DATA_CLEAR_CONFIRM", "Haz clic de nuevo para confirmar ({0})" },
                { "SET_DATA_CLEARED", "Se borraron todas las categorías manuales." },
                { "SET_DATA_CLEARED_NOTHING", "No hay nada que borrar." },
                { "SET_DATA_HINT", "Las calles vuelven a la clasificación automática." },

                { "MANAGER_TITLE", "Gestor de categorías de calles" },
                { "MANAGER_SEARCH", "Buscar..." },
                { "MANAGER_FILTER_ALL", "Todas las categorías" },
                { "MANAGER_SAVE", "Guardar" },
                { "MANAGER_RESET_ALL", "Restablecer todo" },
                { "MANAGER_REFRESH", "Actualizar" },
                { "MANAGER_READY", "Listo" },
                { "MANAGER_STATUS", "{0} calles, {1} mostradas | ★ = manual" },
                { "MANAGER_SAVED", "Guardado. El panel de calles usará las nuevas categorías tras reiniciar." },
                { "MANAGER_RESET_ALL_DONE", "Todas las categorías manuales restablecidas." },
                { "MANAGER_RESET_ONE", "Restablecido: {0}" },
                { "MANAGER_SET", "Definido: {0} → {1} (pulsa Guardar)" },
                { "MANAGER_LANES", "{0} carriles" },
                { "MANAGER_MANUAL_TAG", "Categoría manual" },
                { "MANAGER_ROW_RESET", "Restablecer" },

                { "POPUP_TITLE", "Elegir categoría de calle" },
                { "POPUP_RESTORE_AUTO", "Restaurar automático" },

                { "ROAD_TOOLTIP_MANUAL", "★ Manual: {0} (clic para cambiar)" },
                { "ROAD_TOOLTIP_AUTO", "Automática: {0} (clic para cambiar)" },

                { "FLOAT_BUTTON_TOOLTIP", "Gestor de categorías de calles" },
                { "LEGACY_TITLE", "Resumen de calles" },

                { "CAT_RURAL", "Carreteras rurales" },
                { "CAT_COMMUNITY", "Calles residenciales" },
                { "CAT_SMALL", "Calles pequeñas" },
                { "CAT_MEDIUM", "Calles medianas" },
                { "CAT_ARTERIAL", "Avenidas" },
                { "CAT_LARGE", "Calles grandes" },
                { "CAT_HIGHWAY", "Autopistas" },
                { "CAT_BUS", "Carriles de bus" },
                { "CAT_PEDESTRIAN", "Calles peatonales" },
                { "CAT_OTHER", "Otras calles" }
            };
        }

        private static Dictionary<string, string> French()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Catégories de routes détaillées" },
                { "MOD_DESCRIPTION", "Sépare le panneau des routes en 10 catégories · Gestion manuelle · Icônes personnalisées" },

                { "SET_GROUP_OVERVIEW", "Aperçu" },
                { "SET_OVERVIEW_LINE1", "Version {0}" },
                { "SET_OVERVIEW_LINE2", "En jeu, le bouton flottant ouvre le gestionnaire de catégories." },
                { "SET_OVERVIEW_LINE3", "Vous pouvez aussi utiliser le bouton ci-dessous." },
                { "SET_BTN_OPEN_MANAGER", "Ouvrir le gestionnaire de catégories" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "Le gestionnaire n'est disponible qu'en jeu. Chargez d'abord une ville." },
                { "SET_BTN_OPEN_CONFIG", "Ouvrir le dossier de configuration" },

                { "SET_GROUP_LANGUAGE", "Langue" },
                { "SET_LANGUAGE_LABEL", "Langue" },
                { "SET_LANGUAGE_AUTO", "Automatique (comme le jeu)" },
                { "SET_LANGUAGE_CURRENT", "Langue actuelle : {0}" },
                { "SET_LANGUAGE_HINT", "La page est actualisée immédiatement après le changement." },

                { "SET_GROUP_FLOATING", "Bouton flottant" },
                { "SET_FLOATING_ENABLE", "Activer le bouton flottant" },
                { "SET_FLOATING_IN_GAME_ONLY", "Le bouton flottant n'apparaît qu'en jeu." },
                { "SET_FLOATING_SIZE", "Taille du bouton" },
                { "SET_BTN_RESET_POSITION", "Réinitialiser la position" },
                { "SET_FLOATING_POSITION_RESET", "Position du bouton réinitialisée." },
                { "SET_FLOATING_HINT1", "Faites glisser le bouton pour le déplacer ; la position est enregistrée." },
                { "SET_FLOATING_HINT2", "Uniquement en jeu." },

                { "SET_GROUP_INTEGRATION", "Intégration" },
                { "SET_UUI_ENABLE", "Ajouter l'icône à UnifiedUI" },
                { "SET_UUI_HINT", "Nécessite le mod UnifiedUI. Une fois activé, le gestionnaire s'ouvre depuis la barre UnifiedUI." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI introuvable ; option indisponible." },
                { "SET_UUI_PENDING", "En attente du panneau UnifiedUI… (quelques secondes)" },
                { "SET_UUI_ENABLED", "Ajouté à la barre UnifiedUI." },
                { "SET_UUI_DISABLED", "Retiré de UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Modifiez cette option en jeu." },
                { "SET_UUI_FAILED", "Échec de l'ajout à UnifiedUI ; voir le journal." },
                { "SET_UUI_RESTART_HINT", "Le bouton peut rester visible ; redémarrez le jeu." },

                { "SET_GROUP_MANAGER", "Gestionnaire" },
                { "SET_MANAGER_LIVE_SEARCH", "Filtrer pendant la saisie" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Désactivé : seuls Entrée ou le bouton Rechercher filtrent la liste." },
                { "MANAGER_SEARCH_BUTTON", "Rechercher" },

                { "SET_GROUP_HOTKEY", "Raccourci" },
                { "SET_HOTKEY_ENABLE", "Activer le raccourci Ctrl+Maj+Y" },
                { "SET_HOTKEY_TARGET", "Le raccourci ouvre" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Fenêtre d'aperçu des routes" },
                { "SET_HOTKEY_TARGET_MANAGER", "Gestionnaire de catégories" },
                { "SET_HOTKEY_HINT", "Le raccourci ne fonctionne qu'en jeu." },

                { "SET_GROUP_ICONS", "Icônes personnalisées" },
                { "SET_ICONS_LINE1", "Placez des PNG 64×64 dans le dossier des icônes :" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 catégories)" },
                { "SET_ICONS_LINE3", "ou nommez-les : rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png est l'icône du bouton flottant." },
                { "SET_ICONS_LINE6", "Redémarrez le jeu pour appliquer." },
                { "SET_BTN_OPEN_ICONS", "Ouvrir le dossier des icônes" },

                { "SET_GROUP_DATA", "Catégories manuelles" },
                { "SET_DATA_COUNT", "Catégories manuelles enregistrées : {0}" },
                { "SET_DATA_CLEAR", "Effacer toutes les catégories manuelles" },
                { "SET_DATA_CLEAR_CONFIRM", "Cliquez à nouveau pour confirmer ({0})" },
                { "SET_DATA_CLEARED", "Toutes les catégories manuelles ont été effacées." },
                { "SET_DATA_CLEARED_NOTHING", "Rien à effacer." },
                { "SET_DATA_HINT", "Les routes effacées reviennent à la classification automatique." },

                { "MANAGER_TITLE", "Gestionnaire de catégories de routes" },
                { "MANAGER_SEARCH", "Rechercher..." },
                { "MANAGER_FILTER_ALL", "Toutes les catégories" },
                { "MANAGER_SAVE", "Enregistrer" },
                { "MANAGER_RESET_ALL", "Tout réinitialiser" },
                { "MANAGER_REFRESH", "Actualiser" },
                { "MANAGER_READY", "Prêt" },
                { "MANAGER_STATUS", "{0} routes, {1} affichées | ★ = manuel" },
                { "MANAGER_SAVED", "Enregistré. Le panneau des routes utilisera les nouvelles catégories après redémarrage." },
                { "MANAGER_RESET_ALL_DONE", "Toutes les catégories manuelles ont été réinitialisées." },
                { "MANAGER_RESET_ONE", "Réinitialisé : {0}" },
                { "MANAGER_SET", "Défini : {0} → {1} (cliquez sur Enregistrer)" },
                { "MANAGER_LANES", "{0} voies" },
                { "MANAGER_MANUAL_TAG", "Catégorie manuelle" },
                { "MANAGER_ROW_RESET", "Réinit." },

                { "POPUP_TITLE", "Choisir la catégorie de route" },
                { "POPUP_RESTORE_AUTO", "Rétablir l'automatique" },

                { "ROAD_TOOLTIP_MANUAL", "★ Manuel : {0} (cliquer pour changer)" },
                { "ROAD_TOOLTIP_AUTO", "Automatique : {0} (cliquer pour changer)" },

                { "FLOAT_BUTTON_TOOLTIP", "Gestionnaire de catégories de routes" },
                { "LEGACY_TITLE", "Aperçu des routes" },

                { "CAT_RURAL", "Routes rurales" },
                { "CAT_COMMUNITY", "Rues résidentielles" },
                { "CAT_SMALL", "Petites rues" },
                { "CAT_MEDIUM", "Rues moyennes" },
                { "CAT_ARTERIAL", "Axes routiers" },
                { "CAT_LARGE", "Grandes rues" },
                { "CAT_HIGHWAY", "Autoroutes" },
                { "CAT_BUS", "Voies de bus" },
                { "CAT_PEDESTRIAN", "Rues piétonnes" },
                { "CAT_OTHER", "Autres routes" }
            };
        }

        private static Dictionary<string, string> Korean()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "세부 도로 분류" },
                { "MOD_DESCRIPTION", "도로 패널을 10개 분류로 세분화 · 수동 분류 관리자 · 사용자 지정 아이콘" },

                { "SET_GROUP_OVERVIEW", "개요" },
                { "SET_OVERVIEW_LINE1", "버전 {0}" },
                { "SET_OVERVIEW_LINE2", "게임 내에서는 플로팅 버튼으로 분류 관리자를 엽니다." },
                { "SET_OVERVIEW_LINE3", "아래 버튼으로도 열 수 있습니다." },
                { "SET_BTN_OPEN_MANAGER", "도로 분류 관리자 열기" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "관리자는 게임 내에서만 사용할 수 있습니다. 먼저 도시에 진입하세요." },
                { "SET_BTN_OPEN_CONFIG", "설정 폴더 열기" },

                { "SET_GROUP_LANGUAGE", "언어" },
                { "SET_LANGUAGE_LABEL", "언어" },
                { "SET_LANGUAGE_AUTO", "자동 (게임 설정 따름)" },
                { "SET_LANGUAGE_CURRENT", "현재 언어: {0}" },
                { "SET_LANGUAGE_HINT", "언어를 바꾸면 이 페이지가 즉시 새로 고쳐집니다." },

                { "SET_GROUP_FLOATING", "플로팅 버튼" },
                { "SET_FLOATING_ENABLE", "플로팅 버튼 사용" },
                { "SET_FLOATING_IN_GAME_ONLY", "플로팅 버튼은 게임 내에서만 표시됩니다." },
                { "SET_FLOATING_SIZE", "버튼 크기" },
                { "SET_BTN_RESET_POSITION", "버튼 위치 초기화" },
                { "SET_FLOATING_POSITION_RESET", "플로팅 버튼 위치를 초기화했습니다." },
                { "SET_FLOATING_HINT1", "버튼을 끌어 이동할 수 있으며 위치는 자동 저장됩니다." },
                { "SET_FLOATING_HINT2", "게임 내에서만 적용됩니다." },

                { "SET_GROUP_INTEGRATION", "통합" },
                { "SET_UUI_ENABLE", "UnifiedUI에 아이콘 추가" },
                { "SET_UUI_HINT", "UnifiedUI 모드가 필요합니다. 켜면 UnifiedUI 도구 모음에서 관리자를 열 수 있습니다." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI를 찾을 수 없어 사용할 수 없습니다." },
                { "SET_UUI_PENDING", "UnifiedUI 패널을 기다리는 중… (보통 몇 초)" },
                { "SET_UUI_ENABLED", "UnifiedUI 도구 모음에 추가했습니다." },
                { "SET_UUI_DISABLED", "UnifiedUI에서 제거했습니다." },
                { "SET_UUI_IN_GAME_ONLY", "이 옵션은 게임 내에서 변경하세요." },
                { "SET_UUI_FAILED", "UnifiedUI 추가에 실패했습니다. 로그를 확인하세요." },
                { "SET_UUI_RESTART_HINT", "버튼이 남아 있을 수 있습니다. 게임을 다시 시작하세요." },

                { "SET_GROUP_MANAGER", "관리자" },
                { "SET_MANAGER_LIVE_SEARCH", "입력하는 동안 필터링" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "끄면 Enter 또는 검색 버튼으로만 목록을 필터링합니다." },
                { "MANAGER_SEARCH_BUTTON", "검색" },

                { "SET_GROUP_HOTKEY", "단축키" },
                { "SET_HOTKEY_ENABLE", "단축키 Ctrl+Shift+Y 사용" },
                { "SET_HOTKEY_TARGET", "단축키로 열기" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "도로 개요 창" },
                { "SET_HOTKEY_TARGET_MANAGER", "도로 분류 관리자" },
                { "SET_HOTKEY_HINT", "단축키는 게임 내에서만 작동합니다." },

                { "SET_GROUP_ICONS", "사용자 지정 아이콘" },
                { "SET_ICONS_LINE1", "64×64 PNG 파일을 아이콘 폴더에 넣으세요:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (분류 10개)" },
                { "SET_ICONS_LINE3", "또는 다음 이름을 사용하세요: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png 는 플로팅 버튼 아이콘입니다." },
                { "SET_ICONS_LINE6", "적용하려면 게임을 다시 시작하세요." },
                { "SET_BTN_OPEN_ICONS", "아이콘 폴더 열기" },

                { "SET_GROUP_DATA", "수동 분류 데이터" },
                { "SET_DATA_COUNT", "저장된 수동 분류: {0}개" },
                { "SET_DATA_CLEAR", "모든 수동 분류 삭제" },
                { "SET_DATA_CLEAR_CONFIRM", "한 번 더 눌러 확인 ({0})" },
                { "SET_DATA_CLEARED", "모든 수동 분류를 삭제했습니다." },
                { "SET_DATA_CLEARED_NOTHING", "삭제할 항목이 없습니다." },
                { "SET_DATA_HINT", "삭제한 도로는 자동 분류로 돌아갑니다." },

                { "MANAGER_TITLE", "도로 분류 관리자" },
                { "MANAGER_SEARCH", "검색..." },
                { "MANAGER_FILTER_ALL", "모든 분류" },
                { "MANAGER_SAVE", "저장" },
                { "MANAGER_RESET_ALL", "모두 초기화" },
                { "MANAGER_REFRESH", "새로 고침" },
                { "MANAGER_READY", "준비됨" },
                { "MANAGER_STATUS", "도로 {0}개, 표시 {1}개 | ★ = 수동" },
                { "MANAGER_SAVED", "저장했습니다. 새 분류는 게임을 다시 시작한 뒤 도로 패널에 적용됩니다." },
                { "MANAGER_RESET_ALL_DONE", "모든 수동 분류를 초기화했습니다." },
                { "MANAGER_RESET_ONE", "초기화: {0}" },
                { "MANAGER_SET", "설정: {0} → {1} (저장을 눌러 적용)" },
                { "MANAGER_LANES", "{0}차로" },
                { "MANAGER_MANUAL_TAG", "수동 분류" },
                { "MANAGER_ROW_RESET", "초기화" },

                { "POPUP_TITLE", "도로 분류 선택" },
                { "POPUP_RESTORE_AUTO", "자동 분류로 복원" },

                { "ROAD_TOOLTIP_MANUAL", "★ 수동: {0} (클릭하여 변경)" },
                { "ROAD_TOOLTIP_AUTO", "자동: {0} (클릭하여 변경)" },

                { "FLOAT_BUTTON_TOOLTIP", "도로 분류 관리자" },
                { "LEGACY_TITLE", "도로 개요" },

                { "CAT_RURAL", "시골 도로" },
                { "CAT_COMMUNITY", "주거지 도로" },
                { "CAT_SMALL", "소형 도로" },
                { "CAT_MEDIUM", "중형 도로" },
                { "CAT_ARTERIAL", "간선도로" },
                { "CAT_LARGE", "대형 도로" },
                { "CAT_HIGHWAY", "고속도로" },
                { "CAT_BUS", "버스 전용 차로" },
                { "CAT_PEDESTRIAN", "보행자 도로" },
                { "CAT_OTHER", "기타 도로" }
            };
        }

        private static Dictionary<string, string> Polish()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Szczegółowe kategorie dróg" },
                { "MOD_DESCRIPTION", "Dzieli panel dróg na 10 kategorii · Ręczny menedżer kategorii · Własne ikony" },

                { "SET_GROUP_OVERVIEW", "Przegląd" },
                { "SET_OVERVIEW_LINE1", "Wersja {0}" },
                { "SET_OVERVIEW_LINE2", "W grze pływający przycisk otwiera menedżer kategorii." },
                { "SET_OVERVIEW_LINE3", "Możesz też użyć przycisku poniżej." },
                { "SET_BTN_OPEN_MANAGER", "Otwórz menedżer kategorii dróg" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "Menedżer jest dostępny tylko w grze. Najpierw wejdź do miasta." },
                { "SET_BTN_OPEN_CONFIG", "Otwórz folder konfiguracji" },

                { "SET_GROUP_LANGUAGE", "Język" },
                { "SET_LANGUAGE_LABEL", "Język" },
                { "SET_LANGUAGE_AUTO", "Automatycznie (jak gra)" },
                { "SET_LANGUAGE_CURRENT", "Używany język: {0}" },
                { "SET_LANGUAGE_HINT", "Strona odświeży się od razu po zmianie." },

                { "SET_GROUP_FLOATING", "Pływający przycisk" },
                { "SET_FLOATING_ENABLE", "Włącz pływający przycisk" },
                { "SET_FLOATING_IN_GAME_ONLY", "Pływający przycisk jest widoczny tylko w grze." },
                { "SET_FLOATING_SIZE", "Rozmiar przycisku" },
                { "SET_BTN_RESET_POSITION", "Przywróć pozycję" },
                { "SET_FLOATING_POSITION_RESET", "Przywrócono pozycję pływającego przycisku." },
                { "SET_FLOATING_HINT1", "Przeciągnij przycisk, aby go przenieść; pozycja zapisuje się sama." },
                { "SET_FLOATING_HINT2", "Działa tylko w grze." },

                { "SET_GROUP_INTEGRATION", "Integracja" },
                { "SET_UUI_ENABLE", "Dodaj ikonę do UnifiedUI" },
                { "SET_UUI_HINT", "Wymaga moda UnifiedUI. Po włączeniu menedżer otwiera się z paska UnifiedUI." },
                { "SET_UUI_UNAVAILABLE", "Nie wykryto UnifiedUI; opcja niedostępna." },
                { "SET_UUI_PENDING", "Czekam na panel UnifiedUI… (zwykle kilka sekund)" },
                { "SET_UUI_ENABLED", "Dodano do paska UnifiedUI." },
                { "SET_UUI_DISABLED", "Usunięto z UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Zmieniaj tę opcję w grze." },
                { "SET_UUI_FAILED", "Nie udało się dodać do UnifiedUI; sprawdź log." },
                { "SET_UUI_RESTART_HINT", "Przycisk może być nadal widoczny; uruchom grę ponownie." },

                { "SET_GROUP_MANAGER", "Menedżer" },
                { "SET_MANAGER_LIVE_SEARCH", "Filtruj podczas pisania" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Po wyłączeniu listę filtrują tylko Enter lub przycisk Szukaj." },
                { "MANAGER_SEARCH_BUTTON", "Szukaj" },

                { "SET_GROUP_HOTKEY", "Skrót klawiszowy" },
                { "SET_HOTKEY_ENABLE", "Włącz skrót Ctrl+Shift+Y" },
                { "SET_HOTKEY_TARGET", "Skrót otwiera" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Okno przeglądu dróg" },
                { "SET_HOTKEY_TARGET_MANAGER", "Menedżer kategorii dróg" },
                { "SET_HOTKEY_HINT", "Skrót działa tylko w grze." },

                { "SET_GROUP_ICONS", "Własne ikony" },
                { "SET_ICONS_LINE1", "Umieść pliki PNG 64×64 w folderze ikon:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 kategorii)" },
                { "SET_ICONS_LINE3", "lub nazwij je: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png to ikona pływającego przycisku." },
                { "SET_ICONS_LINE6", "Aby zastosować, uruchom grę ponownie." },
                { "SET_BTN_OPEN_ICONS", "Otwórz folder ikon" },

                { "SET_GROUP_DATA", "Ręczne kategorie" },
                { "SET_DATA_COUNT", "Zapisane ręczne kategorie: {0}" },
                { "SET_DATA_CLEAR", "Usuń wszystkie ręczne kategorie" },
                { "SET_DATA_CLEAR_CONFIRM", "Kliknij ponownie, aby potwierdzić ({0})" },
                { "SET_DATA_CLEARED", "Usunięto wszystkie ręczne kategorie." },
                { "SET_DATA_CLEARED_NOTHING", "Nie ma nic do usunięcia." },
                { "SET_DATA_HINT", "Usunięte drogi wrócą do automatycznej klasyfikacji." },

                { "MANAGER_TITLE", "Menedżer kategorii dróg" },
                { "MANAGER_SEARCH", "Szukaj..." },
                { "MANAGER_FILTER_ALL", "Wszystkie kategorie" },
                { "MANAGER_SAVE", "Zapisz" },
                { "MANAGER_RESET_ALL", "Resetuj wszystko" },
                { "MANAGER_REFRESH", "Odśwież" },
                { "MANAGER_READY", "Gotowe" },
                { "MANAGER_STATUS", "Dróg: {0}, wyświetlono: {1} | ★ = ręcznie" },
                { "MANAGER_SAVED", "Zapisano. Panel dróg użyje nowych kategorii po ponownym uruchomieniu gry." },
                { "MANAGER_RESET_ALL_DONE", "Zresetowano wszystkie ręczne kategorie." },
                { "MANAGER_RESET_ONE", "Zresetowano: {0}" },
                { "MANAGER_SET", "Ustawiono: {0} → {1} (kliknij Zapisz)" },
                { "MANAGER_LANES", "Pasów: {0}" },
                { "MANAGER_MANUAL_TAG", "Ręczna kategoria" },
                { "MANAGER_ROW_RESET", "Resetuj" },

                { "POPUP_TITLE", "Wybierz kategorię drogi" },
                { "POPUP_RESTORE_AUTO", "Przywróć automatyczną" },

                { "ROAD_TOOLTIP_MANUAL", "★ Ręcznie: {0} (kliknij, aby zmienić)" },
                { "ROAD_TOOLTIP_AUTO", "Automatycznie: {0} (kliknij, aby zmienić)" },

                { "FLOAT_BUTTON_TOOLTIP", "Menedżer kategorii dróg" },
                { "LEGACY_TITLE", "Przegląd dróg" },

                { "CAT_RURAL", "Drogi wiejskie" },
                { "CAT_COMMUNITY", "Drogi osiedlowe" },
                { "CAT_SMALL", "Małe drogi" },
                { "CAT_MEDIUM", "Średnie drogi" },
                { "CAT_ARTERIAL", "Drogi główne" },
                { "CAT_LARGE", "Duże drogi" },
                { "CAT_HIGHWAY", "Autostrady" },
                { "CAT_BUS", "Pasy autobusowe" },
                { "CAT_PEDESTRIAN", "Drogi dla pieszych" },
                { "CAT_OTHER", "Inne drogi" }
            };
        }

        private static Dictionary<string, string> Portuguese()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Categorias de ruas detalhadas" },
                { "MOD_DESCRIPTION", "Divide o painel de ruas em 10 categorias · Gerenciador manual · Ícones personalizados" },

                { "SET_GROUP_OVERVIEW", "Visão geral" },
                { "SET_OVERVIEW_LINE1", "Versão {0}" },
                { "SET_OVERVIEW_LINE2", "No jogo, o botão flutuante abre o gerenciador de categorias." },
                { "SET_OVERVIEW_LINE3", "Você também pode usar o botão abaixo." },
                { "SET_BTN_OPEN_MANAGER", "Abrir gerenciador de categorias de ruas" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "O gerenciador só está disponível no jogo. Entre em uma cidade primeiro." },
                { "SET_BTN_OPEN_CONFIG", "Abrir pasta de configuração" },

                { "SET_GROUP_LANGUAGE", "Idioma" },
                { "SET_LANGUAGE_LABEL", "Idioma" },
                { "SET_LANGUAGE_AUTO", "Automático (seguir o jogo)" },
                { "SET_LANGUAGE_CURRENT", "Em uso: {0}" },
                { "SET_LANGUAGE_HINT", "A página é atualizada imediatamente após a troca." },

                { "SET_GROUP_FLOATING", "Botão flutuante" },
                { "SET_FLOATING_ENABLE", "Ativar botão flutuante" },
                { "SET_FLOATING_IN_GAME_ONLY", "O botão flutuante aparece apenas no jogo." },
                { "SET_FLOATING_SIZE", "Tamanho do botão" },
                { "SET_BTN_RESET_POSITION", "Redefinir posição" },
                { "SET_FLOATING_POSITION_RESET", "Posição do botão redefinida." },
                { "SET_FLOATING_HINT1", "Arraste o botão para mover; a posição é salva automaticamente." },
                { "SET_FLOATING_HINT2", "Só funciona no jogo." },

                { "SET_GROUP_INTEGRATION", "Integração" },
                { "SET_UUI_ENABLE", "Adicionar ícone ao UnifiedUI" },
                { "SET_UUI_HINT", "Requer o mod UnifiedUI. Quando ativado, o gerenciador abre pela barra do UnifiedUI." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI não detectado; opção indisponível." },
                { "SET_UUI_PENDING", "Aguardando o painel do UnifiedUI… (normalmente alguns segundos)" },
                { "SET_UUI_ENABLED", "Adicionado à barra do UnifiedUI." },
                { "SET_UUI_DISABLED", "Removido do UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Altere esta opção dentro do jogo." },
                { "SET_UUI_FAILED", "Falha ao adicionar ao UnifiedUI; veja o log." },
                { "SET_UUI_RESTART_HINT", "O botão pode continuar visível; reinicie o jogo." },

                { "SET_GROUP_MANAGER", "Gerenciador" },
                { "SET_MANAGER_LIVE_SEARCH", "Filtrar ao digitar" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Quando desativado, só Enter ou o botão Pesquisar filtram a lista." },
                { "MANAGER_SEARCH_BUTTON", "Pesquisar" },

                { "SET_GROUP_HOTKEY", "Atalho" },
                { "SET_HOTKEY_ENABLE", "Ativar atalho Ctrl+Shift+Y" },
                { "SET_HOTKEY_TARGET", "O atalho abre" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Janela de visão geral das ruas" },
                { "SET_HOTKEY_TARGET_MANAGER", "Gerenciador de categorias" },
                { "SET_HOTKEY_HINT", "O atalho só funciona no jogo." },

                { "SET_GROUP_ICONS", "Ícones personalizados" },
                { "SET_ICONS_LINE1", "Coloque PNGs 64×64 na pasta de ícones:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 categorias)" },
                { "SET_ICONS_LINE3", "ou nomeie como: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png é o ícone do botão flutuante." },
                { "SET_ICONS_LINE6", "Reinicie o jogo para aplicar." },
                { "SET_BTN_OPEN_ICONS", "Abrir pasta de ícones" },

                { "SET_GROUP_DATA", "Categorias manuais" },
                { "SET_DATA_COUNT", "Categorias manuais salvas: {0}" },
                { "SET_DATA_CLEAR", "Limpar todas as categorias manuais" },
                { "SET_DATA_CLEAR_CONFIRM", "Clique novamente para confirmar ({0})" },
                { "SET_DATA_CLEARED", "Todas as categorias manuais foram limpas." },
                { "SET_DATA_CLEARED_NOTHING", "Não há nada para limpar." },
                { "SET_DATA_HINT", "As ruas limpas voltam à classificação automática." },

                { "MANAGER_TITLE", "Gerenciador de categorias de ruas" },
                { "MANAGER_SEARCH", "Pesquisar..." },
                { "MANAGER_FILTER_ALL", "Todas as categorias" },
                { "MANAGER_SAVE", "Salvar" },
                { "MANAGER_RESET_ALL", "Redefinir tudo" },
                { "MANAGER_REFRESH", "Atualizar" },
                { "MANAGER_READY", "Pronto" },
                { "MANAGER_STATUS", "{0} ruas, {1} exibidas | ★ = manual" },
                { "MANAGER_SAVED", "Salvo. O painel de ruas usará as novas categorias após reiniciar o jogo." },
                { "MANAGER_RESET_ALL_DONE", "Todas as categorias manuais foram redefinidas." },
                { "MANAGER_RESET_ONE", "Redefinido: {0}" },
                { "MANAGER_SET", "Definido: {0} → {1} (clique em Salvar)" },
                { "MANAGER_LANES", "{0} faixas" },
                { "MANAGER_MANUAL_TAG", "Categoria manual" },
                { "MANAGER_ROW_RESET", "Redefinir" },

                { "POPUP_TITLE", "Escolher categoria da rua" },
                { "POPUP_RESTORE_AUTO", "Restaurar automático" },

                { "ROAD_TOOLTIP_MANUAL", "★ Manual: {0} (clique para alterar)" },
                { "ROAD_TOOLTIP_AUTO", "Automático: {0} (clique para alterar)" },

                { "FLOAT_BUTTON_TOOLTIP", "Gerenciador de categorias de ruas" },
                { "LEGACY_TITLE", "Visão geral das ruas" },

                { "CAT_RURAL", "Estradas rurais" },
                { "CAT_COMMUNITY", "Ruas residenciais" },
                { "CAT_SMALL", "Ruas pequenas" },
                { "CAT_MEDIUM", "Ruas médias" },
                { "CAT_ARTERIAL", "Vias arteriais" },
                { "CAT_LARGE", "Ruas grandes" },
                { "CAT_HIGHWAY", "Rodovias" },
                { "CAT_BUS", "Faixas de ônibus" },
                { "CAT_PEDESTRIAN", "Ruas de pedestres" },
                { "CAT_OTHER", "Outras ruas" }
            };
        }

        private static Dictionary<string, string> Russian()
        {
            return new Dictionary<string, string>
            {
                { "MOD_NAME", "Детальные категории дорог" },
                { "MOD_DESCRIPTION", "Делит панель дорог на 10 категорий · Ручной менеджер категорий · Свои значки" },

                { "SET_GROUP_OVERVIEW", "Обзор" },
                { "SET_OVERVIEW_LINE1", "Версия {0}" },
                { "SET_OVERVIEW_LINE2", "В игре плавающая кнопка открывает менеджер категорий." },
                { "SET_OVERVIEW_LINE3", "Можно также использовать кнопку ниже." },
                { "SET_BTN_OPEN_MANAGER", "Открыть менеджер категорий дорог" },
                { "SET_OPEN_MANAGER_IN_GAME_ONLY", "Менеджер доступен только в игре. Сначала загрузите город." },
                { "SET_BTN_OPEN_CONFIG", "Открыть папку конфигурации" },

                { "SET_GROUP_LANGUAGE", "Язык" },
                { "SET_LANGUAGE_LABEL", "Язык" },
                { "SET_LANGUAGE_AUTO", "Автоматически (как в игре)" },
                { "SET_LANGUAGE_CURRENT", "Сейчас используется: {0}" },
                { "SET_LANGUAGE_HINT", "Страница обновится сразу после смены языка." },

                { "SET_GROUP_FLOATING", "Плавающая кнопка" },
                { "SET_FLOATING_ENABLE", "Включить плавающую кнопку" },
                { "SET_FLOATING_IN_GAME_ONLY", "Плавающая кнопка видна только в игре." },
                { "SET_FLOATING_SIZE", "Размер кнопки" },
                { "SET_BTN_RESET_POSITION", "Сбросить позицию" },
                { "SET_FLOATING_POSITION_RESET", "Позиция плавающей кнопки сброшена." },
                { "SET_FLOATING_HINT1", "Перетаскивайте кнопку для перемещения; позиция сохраняется сама." },
                { "SET_FLOATING_HINT2", "Действует только в игре." },

                { "SET_GROUP_INTEGRATION", "Интеграция" },
                { "SET_UUI_ENABLE", "Добавить значок в UnifiedUI" },
                { "SET_UUI_HINT", "Требуется мод UnifiedUI. После включения менеджер открывается с панели UnifiedUI." },
                { "SET_UUI_UNAVAILABLE", "UnifiedUI не обнаружен; параметр недоступен." },
                { "SET_UUI_PENDING", "Ожидание панели UnifiedUI… (обычно несколько секунд)" },
                { "SET_UUI_ENABLED", "Добавлено на панель UnifiedUI." },
                { "SET_UUI_DISABLED", "Удалено из UnifiedUI." },
                { "SET_UUI_IN_GAME_ONLY", "Меняйте этот параметр в игре." },
                { "SET_UUI_FAILED", "Не удалось добавить в UnifiedUI; см. лог." },
                { "SET_UUI_RESTART_HINT", "Кнопка может остаться видимой; перезапустите игру." },

                { "SET_GROUP_MANAGER", "Менеджер" },
                { "SET_MANAGER_LIVE_SEARCH", "Фильтровать при вводе" },
                { "SET_MANAGER_LIVE_SEARCH_HINT", "Если выключено, список фильтруют только Enter или кнопка «Поиск»." },
                { "MANAGER_SEARCH_BUTTON", "Поиск" },

                { "SET_GROUP_HOTKEY", "Горячая клавиша" },
                { "SET_HOTKEY_ENABLE", "Включить Ctrl+Shift+Y" },
                { "SET_HOTKEY_TARGET", "Клавиша открывает" },
                { "SET_HOTKEY_TARGET_OVERVIEW", "Окно обзора дорог" },
                { "SET_HOTKEY_TARGET_MANAGER", "Менеджер категорий дорог" },
                { "SET_HOTKEY_HINT", "Клавиша работает только в игре." },

                { "SET_GROUP_ICONS", "Свои значки" },
                { "SET_ICONS_LINE1", "Поместите PNG 64×64 в папку значков:" },
                { "SET_ICONS_LINE2", "category_10.png … category_90.png (10 категорий)" },
                { "SET_ICONS_LINE3", "или назовите их: rural, community, small, medium," },
                { "SET_ICONS_LINE4", "arterial, large, highway, bus, pedestrian, other" },
                { "SET_ICONS_LINE5", "float_button.png — значок плавающей кнопки." },
                { "SET_ICONS_LINE6", "Для применения перезапустите игру." },
                { "SET_BTN_OPEN_ICONS", "Открыть папку значков" },

                { "SET_GROUP_DATA", "Ручные категории" },
                { "SET_DATA_COUNT", "Сохранено ручных категорий: {0}" },
                { "SET_DATA_CLEAR", "Удалить все ручные категории" },
                { "SET_DATA_CLEAR_CONFIRM", "Нажмите ещё раз для подтверждения ({0})" },
                { "SET_DATA_CLEARED", "Все ручные категории удалены." },
                { "SET_DATA_CLEARED_NOTHING", "Нечего удалять." },
                { "SET_DATA_HINT", "Удалённые дороги снова классифицируются автоматически." },

                { "MANAGER_TITLE", "Менеджер категорий дорог" },
                { "MANAGER_SEARCH", "Поиск..." },
                { "MANAGER_FILTER_ALL", "Все категории" },
                { "MANAGER_SAVE", "Сохранить" },
                { "MANAGER_RESET_ALL", "Сбросить всё" },
                { "MANAGER_REFRESH", "Обновить" },
                { "MANAGER_READY", "Готово" },
                { "MANAGER_STATUS", "Дорог: {0}, показано: {1} | ★ = вручную" },
                { "MANAGER_SAVED", "Сохранено. Панель дорог применит новые категории после перезапуска игры." },
                { "MANAGER_RESET_ALL_DONE", "Все ручные категории сброшены." },
                { "MANAGER_RESET_ONE", "Сброшено: {0}" },
                { "MANAGER_SET", "Задано: {0} → {1} (нажмите «Сохранить»)" },
                { "MANAGER_LANES", "Полос: {0}" },
                { "MANAGER_MANUAL_TAG", "Ручная категория" },
                { "MANAGER_ROW_RESET", "Сброс" },

                { "POPUP_TITLE", "Выберите категорию дороги" },
                { "POPUP_RESTORE_AUTO", "Вернуть автоматическую" },

                { "ROAD_TOOLTIP_MANUAL", "★ Вручную: {0} (нажмите, чтобы изменить)" },
                { "ROAD_TOOLTIP_AUTO", "Автоматически: {0} (нажмите, чтобы изменить)" },

                { "FLOAT_BUTTON_TOOLTIP", "Менеджер категорий дорог" },
                { "LEGACY_TITLE", "Обзор дорог" },

                { "CAT_RURAL", "Сельские дороги" },
                { "CAT_COMMUNITY", "Местные улицы" },
                { "CAT_SMALL", "Малые дороги" },
                { "CAT_MEDIUM", "Средние дороги" },
                { "CAT_ARTERIAL", "Магистрали" },
                { "CAT_LARGE", "Большие дороги" },
                { "CAT_HIGHWAY", "Автомагистрали" },
                { "CAT_BUS", "Автобусные полосы" },
                { "CAT_PEDESTRIAN", "Пешеходные улицы" },
                { "CAT_OTHER", "Прочие дороги" }
            };
        }
    }
}

using System;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 模组设置（保存在 Unity persistentDataPath 下的 RoadCategoryModSettings.xml）。
    ///
    /// 注意：
    /// 1. 使用公共字段，XmlSerializer 序列化；新增字段有默认值，旧配置文件可直接兼容；
    /// 2. 通过 Current 缓存同一个实例，避免频繁读写文件；
    /// 3. 任何修改后都要调用 Save()。
    /// </summary>
    public class ModSettings
    {
        public const float DefaultButtonX = 10f;
        public const float DefaultButtonY = 600f;
        public const float DefaultButtonSize = 40f;
        public const float MinButtonSize = 28f;
        public const float MaxButtonSize = 64f;

        public const string HotkeyTargetOverview = "overview";
        public const string HotkeyTargetManager = "manager";

        /// <summary>是否显示游戏内浮动按钮。</summary>
        public bool EnableIndependentWindow = false;

        /// <summary>是否启用 Ctrl+Shift+Y 热键。</summary>
        public bool EnableHotkey = true;

        /// <summary>热键打开的窗口：overview = 道路总览，manager = 道路分类管理器。</summary>
        public string HotkeyTarget = HotkeyTargetOverview;

        /// <summary>浮动按钮位置。</summary>
        public float ButtonX = DefaultButtonX;
        public float ButtonY = DefaultButtonY;

        /// <summary>浮动按钮大小（像素）。</summary>
        public float ButtonSize = DefaultButtonSize;

        /// <summary>是否把图标注册到 UnifiedUI 工具栏。</summary>
        public bool AddToUnifiedUI = false;

        /// <summary>管理器搜索框：true = 输入时实时过滤，false = 仅回车/搜索按钮触发。</summary>
        public bool SearchLive = true;

        /// <summary>界面语言：auto = 跟随游戏，其它为 zh/en/de/es/fr/ko/pl/pt/ru。</summary>
        public string Language = Localization.AutoCode;

        private static ModSettings _current;

        /// <summary>当前设置实例（懒加载并缓存）。</summary>
        public static ModSettings Current
        {
            get
            {
                if (_current == null)
                    _current = LoadFromDisk();
                return _current;
            }
        }

        /// <summary>丢弃缓存并重新读取配置文件（例如外部修改了 XML）。</summary>
        public static void Reload()
        {
            _current = LoadFromDisk();
        }

        private static string SettingsPath
        {
            get
            {
                return Path.Combine(
                    Application.persistentDataPath,
                    "RoadCategoryModSettings.xml"
                );
            }
        }

        private static ModSettings LoadFromDisk()
        {
            ModSettings settings = null;

            try
            {
                if (File.Exists(SettingsPath))
                {
                    var serializer = new XmlSerializer(typeof(ModSettings));
                    using (var reader = new StreamReader(SettingsPath))
                    {
                        settings = serializer.Deserialize(reader) as ModSettings;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 读取设置失败，将使用默认值: " + e.Message);
                settings = null;
            }

            if (settings == null)
                settings = new ModSettings();

            settings.Normalize();
            return settings;
        }

        /// <summary>修正非法值，保证任何版本写入的配置都能安全使用。</summary>
        public void Normalize()
        {
            if (ButtonSize < MinButtonSize || ButtonSize > MaxButtonSize)
                ButtonSize = DefaultButtonSize;

            if (string.IsNullOrEmpty(HotkeyTarget))
                HotkeyTarget = HotkeyTargetOverview;
            HotkeyTarget = HotkeyTarget.Trim().ToLowerInvariant();
            if (HotkeyTarget != HotkeyTargetOverview && HotkeyTarget != HotkeyTargetManager)
                HotkeyTarget = HotkeyTargetOverview;

            if (string.IsNullOrEmpty(Language))
                Language = Localization.AutoCode;
            Language = Language.Trim().ToLowerInvariant();
            if (Language != Localization.AutoCode && !Localization.IsSupported(Language))
                Language = Localization.AutoCode;

            if (float.IsNaN(ButtonX) || float.IsInfinity(ButtonX))
                ButtonX = DefaultButtonX;
            if (float.IsNaN(ButtonY) || float.IsInfinity(ButtonY))
                ButtonY = DefaultButtonY;
        }

        /// <summary>恢复浮动按钮默认位置。</summary>
        public void ResetButtonPosition()
        {
            ButtonX = DefaultButtonX;
            ButtonY = DefaultButtonY;
            Save();
        }

        public void Save()
        {
            try
            {
                _current = this;
                var serializer = new XmlSerializer(typeof(ModSettings));
                using (var writer = new StreamWriter(SettingsPath))
                {
                    serializer.Serialize(writer, this);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 保存设置失败: " + e.Message);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 手动分类覆盖配置项。
    /// </summary>
    [Serializable]
    public class CategoryOverride
    {
        [XmlAttribute("name")]
        public string RoadName { get; set; }

        [XmlAttribute("category")]
        public int CategoryValue { get; set; }

        [XmlIgnore]
        public RoadCategory Category
        {
            get { return (RoadCategory)CategoryValue; }
            set { CategoryValue = (int)value; }
        }
    }

    /// <summary>
    /// 手动分类覆盖配置文件根节点。
    /// </summary>
    [Serializable]
    [XmlRoot("RoadCategoryConfig")]
    public class CategoryConfigFile
    {
        [XmlArray("Overrides")]
        [XmlArrayItem("Road")]
        public List<CategoryOverride> Overrides { get; set; }

        public CategoryConfigFile()
        {
            Overrides = new List<CategoryOverride>();
        }
    }

    /// <summary>
    /// 道路分类配置管理。
    /// 支持作者预设（内置）和玩家自定义（XML文件）。
    /// 手动覆盖优先于自动分类。
    /// </summary>
    public static class RoadCategoryConfig
    {
        private static CategoryConfigFile _playerConfig;
        private static Dictionary<string, RoadCategory> _overrideCache;
        private static bool _loaded = false;

        /// <summary>
        /// 玩家配置文件路径。
        /// </summary>
        public static string PlayerConfigPath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(Path.Combine(Path.Combine(localAppData, "Colossal Order"), "Cities_Skylines"), "RoadCategoryMod");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "custom_categories.xml");
            }
        }

        /// <summary>
        /// 获取道路的手动分类覆盖。返回 null 表示没有覆盖，应使用自动分类。
        /// </summary>
        public static RoadCategory? GetManualCategory(string roadName)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(roadName))
                return null;

            if (_overrideCache.ContainsKey(roadName))
                return _overrideCache[roadName];

            return null;
        }

        /// <summary>
        /// 设置道路的手动分类覆盖（玩家自定义）。
        /// </summary>
        public static void SetManualCategory(string roadName, RoadCategory category)
        {
            EnsureLoaded();

            // 查找已存在的覆盖
            var existing = _playerConfig.Overrides.Find(o => o.RoadName == roadName);
            if (existing != null)
            {
                existing.Category = category;
            }
            else
            {
                _playerConfig.Overrides.Add(new CategoryOverride
                {
                    RoadName = roadName,
                    Category = category
                });
            }

            // 更新缓存
            _overrideCache[roadName] = category;
        }

        /// <summary>
        /// 移除道路的手动分类覆盖（恢复自动分类）。
        /// </summary>
        public static void RemoveManualCategory(string roadName)
        {
            EnsureLoaded();

            _playerConfig.Overrides.RemoveAll(o => o.RoadName == roadName);
            _overrideCache.Remove(roadName);
        }

        /// <summary>
        /// 检查道路是否有手动覆盖。
        /// </summary>
        public static bool HasManualCategory(string roadName)
        {
            EnsureLoaded();
            return _overrideCache.ContainsKey(roadName);
        }

        /// <summary>
        /// 获取所有手动覆盖。
        /// </summary>
        public static List<CategoryOverride> GetAllOverrides()
        {
            EnsureLoaded();
            return _playerConfig.Overrides;
        }

        /// <summary>
        /// 保存玩家配置到 XML 文件。
        /// </summary>
        public static void Save()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(CategoryConfigFile));
                using (var writer = new StreamWriter(PlayerConfigPath))
                {
                    serializer.Serialize(writer, _playerConfig);
                }
                Debug.Log("[RoadCategoryMod] 配置已保存到 " + PlayerConfigPath);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 保存配置失败: " + e.Message);
            }
        }

        /// <summary>
        /// 重新加载配置（用于设置面板修改后刷新）。
        /// </summary>
        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _playerConfig = new CategoryConfigFile();
            _overrideCache = new Dictionary<string, RoadCategory>();

            // 加载作者预设（内置）
            LoadBuiltinOverrides();

            // 加载玩家自定义（XML文件）
            LoadPlayerConfig();

            _loaded = true;
        }

        /// <summary>
        /// 加载作者预设的分类覆盖。
        /// 模组作者可以在这里添加常见道路的预设分类。
        /// </summary>
        private static void LoadBuiltinOverrides()
        {
            // 示例：可以在这里添加预设分类
            // SetOverrideInternal("Basic Road", RoadCategory.SmallRoads);
            // 目前留空，后续根据实际道路资产补充
        }

        private static void SetOverrideInternal(string roadName, RoadCategory category)
        {
            _overrideCache[roadName] = category;
        }

        /// <summary>
        /// 从 XML 文件加载玩家自定义配置。
        /// </summary>
        private static void LoadPlayerConfig()
        {
            try
            {
                string path = PlayerConfigPath;
                if (!File.Exists(path))
                {
                    Debug.Log("[RoadCategoryMod] 玩家配置文件不存在，将使用默认配置: " + path);
                    return;
                }

                var serializer = new XmlSerializer(typeof(CategoryConfigFile));
                using (var reader = new StreamReader(path))
                {
                    _playerConfig = serializer.Deserialize(reader) as CategoryConfigFile;
                }

                if (_playerConfig != null && _playerConfig.Overrides != null)
                {
                    foreach (var ov in _playerConfig.Overrides)
                    {
                        if (!string.IsNullOrEmpty(ov.RoadName))
                        {
                            _overrideCache[ov.RoadName] = ov.Category;
                        }
                    }
                    Debug.Log("[RoadCategoryMod] 已加载 " + _playerConfig.Overrides.Count + " 条玩家自定义分类");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 加载玩家配置失败: " + e.Message);
                _playerConfig = new CategoryConfigFile();
            }
        }
    }
}

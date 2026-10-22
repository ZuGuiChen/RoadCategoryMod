using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 分类图标配置项。
    /// </summary>
    [Serializable]
    public class IconOverride
    {
        [XmlAttribute("category")]
        public int CategoryValue { get; set; }

        [XmlAttribute("sprite")]
        public string SpriteBase { get; set; }

        [XmlIgnore]
        public RoadCategory Category
        {
            get { return (RoadCategory)CategoryValue; }
            set { CategoryValue = (int)value; }
        }
    }

    /// <summary>
    /// 图标配置文件根节点。
    /// </summary>
    [Serializable]
    [XmlRoot("IconConfig")]
    public class IconConfigFile
    {
        [XmlArray("Icons")]
        [XmlArrayItem("Category")]
        public List<IconOverride> Icons { get; set; }

        public IconConfigFile()
        {
            Icons = new List<IconOverride>();
        }
    }

    /// <summary>
    /// 分类图标配置管理。
    /// 允许玩家自定义每个分类的前景 sprite。
    /// </summary>
    public static class IconConfig
    {
        private static IconConfigFile _config;
        private static Dictionary<RoadCategory, string> _iconCache;
        private static bool _loaded = false;

        /// <summary>
        /// 图标配置文件路径。
        /// </summary>
        public static string ConfigPath
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(Path.Combine(Path.Combine(localAppData, "Colossal Order"), "Cities_Skylines"), "RoadCategoryMod");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return Path.Combine(dir, "custom_icons.xml");
            }
        }

        /// <summary>
        /// 获取分类的自定义图标 sprite 基础名。返回 null 表示使用默认图标。
        /// </summary>
        public static string GetCustomIcon(RoadCategory cat)
        {
            EnsureLoaded();

            if (_iconCache.ContainsKey(cat))
                return _iconCache[cat];

            return null;
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _config = new IconConfigFile();
            _iconCache = new Dictionary<RoadCategory, string>();

            try
            {
                string path = ConfigPath;
                if (File.Exists(path))
                {
                    var serializer = new XmlSerializer(typeof(IconConfigFile));
                    using (var reader = new StreamReader(path))
                    {
                        _config = serializer.Deserialize(reader) as IconConfigFile;
                    }

                    if (_config != null && _config.Icons != null)
                    {
                        foreach (var ic in _config.Icons)
                        {
                            _iconCache[ic.Category] = ic.SpriteBase;
                        }
                        Debug.Log("[RoadCategoryMod] 已加载 " + _config.Icons.Count + " 个自定义图标");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 加载图标配置失败: " + e.Message);
                _config = new IconConfigFile();
            }

            _loaded = true;
        }
    }
}

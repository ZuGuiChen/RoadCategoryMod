using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.UI;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 图标加载器。
    ///
    /// 图标有两类来源：
    /// 1. **玩家自定义**：`%LOCALAPPDATA%\Colossal Order\Cities_Skylines\RoadCategoryMod\Icons\*.png`（优先）
    /// 2. **内置默认图标**：编译进 DLL 的嵌入资源（`Icons\*.png`），创意工坊订阅后也不依赖任何外部路径
    ///
    /// 启动时会：
    /// * 把内置图标“补齐”到玩家目录（只补缺失文件，不覆盖玩家自定义，方便玩家查看/替换）；
    /// * 如果玩家目录不可写或写入失败，**直接从内存里的嵌入资源加载**，保证图标一定能显示。
    ///
    /// 历史问题（v1.0.1 修复）：早期版本依赖 `Assembly.Location` 旁边的 `Icons\` 文件夹，
    /// 订阅创意工坊后模组位于 `steamapps\workshop\content\255710\<id>\`，
    /// 一旦路径判断失败（或玩家目录为空），分类 tab 就只能退回到游戏的默认小图标。
    /// </summary>
    public static class CustomIconLoader
    {
        private const string ResourceMarker = ".icons.";

        private static UITextureAtlas _customAtlas;
        private static Dictionary<RoadCategory, string> _spriteNames;
        private static bool _loaded;
        private static int _attempts;

        /// <summary>
        /// 玩家图标文件夹路径（不存在会自动创建）。
        /// </summary>
        public static string IconsFolder
        {
            get
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string dir = Path.Combine(Path.Combine(Path.Combine(localAppData, "Colossal Order"), "Cities_Skylines"), "RoadCategoryMod");
                dir = Path.Combine(dir, "Icons");
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                return dir;
            }
        }

        /// <summary>
        /// 自定义图标 atlas。没有可用图标时返回 null。
        /// </summary>
        public static UITextureAtlas CustomAtlas
        {
            get
            {
                EnsureLoaded();
                return _customAtlas;
            }
        }

        /// <summary>
        /// 获取分类对应的自定义 sprite 名称。没有时返回 null（调用方应退回游戏默认 sprite）。
        /// </summary>
        public static string GetCustomSprite(RoadCategory cat)
        {
            EnsureLoaded();
            if (_spriteNames != null && _spriteNames.ContainsKey(cat))
                return _spriteNames[cat];
            return null;
        }

        // ==================== 加载流程 ====================

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            _attempts++;
            _spriteNames = new Dictionary<RoadCategory, string>();

            try
            {
                // 1) 尽量把内置图标写到玩家目录（便于玩家替换）
                EnsureDefaultIcons();

                // 2) 收集图标来源：玩家目录 > 内置资源 > 模组目录
                List<IconSource> sources = CollectSources();
                if (sources.Count == 0)
                {
                    Debug.LogWarning("[RoadCategoryMod] 没有任何可用图标（玩家目录/内置资源/模组目录均为空），将使用游戏默认图标");
                    _loaded = true;
                    return;
                }

                // 3) 构建 atlas
                BuildAtlas(sources);
                _loaded = true;
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 加载图标失败（第 " + _attempts + " 次）: " + e.Message + "\n" + e.StackTrace);
                _customAtlas = null;
                if (_spriteNames != null) _spriteNames.Clear();

                // 例如 UIView 尚未初始化：允许稍后重试，但最多 5 次，避免刷屏
                _loaded = _attempts >= 5;
            }
        }

        private class IconSource
        {
            public string FileName;   // 不含扩展名，如 category_10
            public byte[] Bytes;
        }

        /// <summary>按优先级收集所有图标来源，同名只取第一个。</summary>
        private static List<IconSource> CollectSources()
        {
            var list = new List<IconSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1) 玩家自定义图标目录
            try
            {
                string folder = IconsFolder;
                foreach (string file in Directory.GetFiles(folder, "*.png"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!seen.Add(name)) continue;
                    try
                    {
                        list.Add(new IconSource { FileName = name, Bytes = File.ReadAllBytes(file) });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RoadCategoryMod] 读取图标失败 " + file + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 读取玩家图标目录失败: " + e.Message);
            }

            // 2) 内置资源（创意工坊订阅时最可靠）
            int embeddedCount = 0;
            try
            {
                var asm = typeof(CustomIconLoader).Assembly;
                foreach (string resource in asm.GetManifestResourceNames())
                {
                    string name = ResourceToFileName(resource);
                    if (name == null || !seen.Add(name)) continue;
                    try
                    {
                        using (Stream stream = asm.GetManifestResourceStream(resource))
                        {
                            if (stream == null) continue;
                            list.Add(new IconSource { FileName = name, Bytes = ReadAllBytes(stream) });
                            embeddedCount++;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("[RoadCategoryMod] 读取内置图标失败 " + resource + ": " + e.Message);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 枚举内置图标失败: " + e.Message);
            }

            // 3) 模组目录的 Icons\（兼容没有内嵌资源的旧版本）
            int folderCount = 0;
            foreach (string dir in GetCandidateIconFolders())
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (string file in Directory.GetFiles(dir, "*.png"))
                    {
                        string name = Path.GetFileNameWithoutExtension(file);
                        if (!seen.Add(name)) continue;
                        try
                        {
                            list.Add(new IconSource { FileName = name, Bytes = File.ReadAllBytes(file) });
                            folderCount++;
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[RoadCategoryMod] 读取模组图标失败 " + file + ": " + e.Message);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[RoadCategoryMod] 扫描模组图标目录失败 " + dir + ": " + e.Message);
                }
            }

            Debug.Log("[RoadCategoryMod] 图标来源: 共 " + list.Count + " 个（内置资源 " + embeddedCount +
                      "，模组目录 " + folderCount + "，其余来自玩家目录）");
            return list;
        }

        /// <summary>
        /// 把内置图标补齐到玩家图标目录：只补缺失文件，不覆盖玩家自定义图标。
        /// </summary>
        private static void EnsureDefaultIcons()
        {
            try
            {
                string target = IconsFolder;
                int copied = 0;

                // 1) 内置资源（任何安装方式都有效）
                try
                {
                    var asm = typeof(CustomIconLoader).Assembly;
                    foreach (string resource in asm.GetManifestResourceNames())
                    {
                        string name = ResourceToFileName(resource);
                        if (name == null) continue;

                        string dest = Path.Combine(target, name + ".png");
                        if (File.Exists(dest)) continue;

                        try
                        {
                            using (Stream stream = asm.GetManifestResourceStream(resource))
                            {
                                if (stream == null) continue;
                                using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                                {
                                    byte[] buffer = new byte[16 * 1024];
                                    int read;
                                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                                        fs.Write(buffer, 0, read);
                                }
                            }
                            copied++;
                        }
                        catch (Exception)
                        {
                            // 单个文件失败（例如目录只读）忽略：CollectSources 会直接从内存加载
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[RoadCategoryMod] 写入内置图标失败（将直接使用内存中的图标）: " + e.Message);
                }

                // 2) 模组目录 \Icons（旧版本没有内嵌资源时）
                foreach (string dir in GetCandidateIconFolders())
                {
                    try
                    {
                        if (!Directory.Exists(dir)) continue;
                        foreach (string file in Directory.GetFiles(dir, "*.png"))
                        {
                            string dest = Path.Combine(target, Path.GetFileName(file));
                            if (File.Exists(dest)) continue;
                            try
                            {
                                File.Copy(file, dest, false);
                                copied++;
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                if (copied > 0)
                    Debug.Log("[RoadCategoryMod] 已补齐 " + copied + " 个默认图标到 " + target);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 补齐默认图标失败: " + e.Message);
            }
        }

        /// <summary>可能存放 Icons 的目录（模组安装位置 + 本地模组目录）。</summary>
        private static IEnumerable<string> GetCandidateIconFolders()
        {
            var dirs = new List<string>();

            string assemblyDir = null;
            try
            {
                assemblyDir = Path.GetDirectoryName(typeof(CustomIconLoader).Assembly.Location);
            }
            catch (Exception)
            {
            }

            if (!string.IsNullOrEmpty(assemblyDir))
            {
                dirs.Add(Path.Combine(assemblyDir, "Icons"));
                dirs.Add(Path.Combine(Path.Combine(assemblyDir, "RoadCategoryMod"), "Icons"));
                Debug.Log("[RoadCategoryMod] 程序集位置: " + assemblyDir);
            }
            else
            {
                Debug.Log("[RoadCategoryMod] 程序集位置为空（内嵌图标不受影响）");
            }

            try
            {
                string localMods = Path.Combine(
                    Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Colossal Order"), "Cities_Skylines"), "Addons\\Mods");
                dirs.Add(Path.Combine(Path.Combine(localMods, "RoadCategoryMod"), "Icons"));
            }
            catch (Exception)
            {
            }

            return dirs;
        }

        /// <summary>把嵌入资源名（RoadCategoryMod.Icons.category_10.png）转成文件名（category_10）。</summary>
        private static string ResourceToFileName(string resourceName)
        {
            if (string.IsNullOrEmpty(resourceName)) return null;
            if (!resourceName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return null;

            int index = resourceName.ToLowerInvariant().LastIndexOf(ResourceMarker, StringComparison.Ordinal);
            if (index < 0) return null;

            int start = index + ResourceMarker.Length;
            int length = resourceName.Length - start - 4; // 去掉 .png
            if (length <= 0) return null;

            return resourceName.Substring(start, length);
        }

        private static byte[] ReadAllBytes(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                byte[] buffer = new byte[16 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    ms.Write(buffer, 0, read);
                return ms.ToArray();
            }
        }

        /// <summary>用收集到的图标来源构建自定义 atlas。</summary>
        private static void BuildAtlas(List<IconSource> sources)
        {
            var textures = new List<Texture2D>();
            var categories = new List<RoadCategory>();
            var spriteNames = new List<string>();
            var isCategoryIcon = new List<bool>();

            foreach (IconSource source in sources)
            {
                RoadCategory? cat = ParseCategoryFromFileName(source.FileName);

                var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!tex.LoadImage(source.Bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    continue;
                }

                // 分类图标 24x24，浮动按钮 20x20
                int iconSize = source.FileName.ToLowerInvariant() == "float_button" ? 20 : 24;
                tex = ResizeTexture(tex, iconSize, iconSize);

                if (cat.HasValue)
                {
                    tex.name = "RCM_Icon_" + (int)cat.Value;
                    textures.Add(tex);
                    categories.Add(cat.Value);
                    spriteNames.Add("RCM_Category_" + (int)cat.Value);
                    isCategoryIcon.Add(true);
                }
                else
                {
                    string spriteName = "RCM_Extra_" + source.FileName;
                    tex.name = spriteName;
                    textures.Add(tex);
                    categories.Add(RoadCategory.RuralRoads); // 占位
                    spriteNames.Add(spriteName);
                    isCategoryIcon.Add(false);
                }
            }

            if (textures.Count == 0)
            {
                Debug.LogWarning("[RoadCategoryMod] 图标解码全部失败，将使用游戏默认图标");
                return;
            }

            // 参考 Find It 2：PackTextures 打包 → 复用 UV 图集的材质
            Texture2D atlasTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            Rect[] regions = atlasTex.PackTextures(textures.ToArray(), 2, 2048);

            UIView view = UIView.GetAView();
            if (view == null || view.defaultAtlas == null)
                throw new InvalidOperationException("UIView/defaultAtlas 尚未初始化");

            _customAtlas = ScriptableObject.CreateInstance<UITextureAtlas>();
            _customAtlas.name = "RoadCategoryModAtlas";
            Material material = UnityEngine.Object.Instantiate(view.defaultAtlas.material);
            material.mainTexture = atlasTex;
            _customAtlas.material = material;

            for (int i = 0; i < textures.Count; i++)
            {
                _customAtlas.AddSprite(new UITextureAtlas.SpriteInfo
                {
                    name = spriteNames[i],
                    texture = textures[i],
                    region = regions[i]
                });

                if (isCategoryIcon[i])
                    _spriteNames[categories[i]] = spriteNames[i];
            }

            Debug.Log("[RoadCategoryMod] 已加载 " + _spriteNames.Count + " 个分类图标（atlas: " + textures.Count + " 张）");
        }

        /// <summary>调整纹理尺寸。</summary>
        private static Texture2D ResizeTexture(Texture2D source, int width, int height)
        {
            if (source.width == width && source.height == height)
                return source;

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(source, rt);
            RenderTexture prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            Texture2D result = new Texture2D(width, height, TextureFormat.ARGB32, false);
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.Destroy(source);
            return result;
        }

        /// <summary>
        /// 把自定义图标添加到目标 atlas 中（参考 Find It 2 的 AddTexturesInAtlas）。
        /// 使用 PackTextures 重新打包所有纹理，然后 RebuildIndexes。
        /// </summary>
        public static void AddCustomIconsToAtlas(UITextureAtlas targetAtlas)
        {
            EnsureLoaded();
            if (_customAtlas == null || targetAtlas == null) return;

            try
            {
                bool alreadyAdded = true;
                foreach (var kv in _spriteNames)
                {
                    if (targetAtlas[kv.Value] == null)
                    {
                        alreadyAdded = false;
                        break;
                    }
                }
                if (alreadyAdded)
                    return;

                var allTextures = new List<Texture2D>();
                var allNames = new List<string>();

                for (int i = 0; i < targetAtlas.count; i++)
                {
                    var sprite = targetAtlas.sprites[i];
                    var tex = sprite.texture as Texture2D;
                    if (tex != null)
                    {
                        allTextures.Add(tex);
                        allNames.Add(sprite.name);
                    }
                }

                foreach (var sprite in _customAtlas.sprites)
                {
                    if (targetAtlas[sprite.name] != null) continue;

                    var tex = sprite.texture as Texture2D;
                    if (tex != null)
                    {
                        allTextures.Add(tex);
                        allNames.Add(sprite.name);
                    }
                }

                if (allTextures.Count == 0) return;

                Rect[] regions = targetAtlas.texture.PackTextures(allTextures.ToArray(), targetAtlas.padding, 4096, false);

                targetAtlas.sprites.Clear();
                for (int i = 0; i < allTextures.Count; i++)
                {
                    var oldSprite = targetAtlas[allNames[i]];
                    targetAtlas.sprites.Add(new UITextureAtlas.SpriteInfo
                    {
                        texture = allTextures[i],
                        name = allNames[i],
                        border = (oldSprite != null) ? oldSprite.border : new RectOffset(),
                        region = regions[i]
                    });
                }

                targetAtlas.RebuildIndexes();
                Debug.Log("[RoadCategoryMod] 已把 " + _spriteNames.Count + " 个图标注入 atlas: " + targetAtlas.name);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] AddCustomIconsToAtlas failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static RoadCategory? ParseCategoryFromFileName(string fileName)
        {
            // 格式1：category_10
            if (fileName.StartsWith("category_", StringComparison.OrdinalIgnoreCase))
            {
                string numStr = fileName.Substring(9);
                int val;
                if (int.TryParse(numStr, out val) && Enum.IsDefined(typeof(RoadCategory), val))
                    return (RoadCategory)val;
            }

            // 格式2：直接数字 10
            int val2;
            if (int.TryParse(fileName, out val2) && Enum.IsDefined(typeof(RoadCategory), val2))
                return (RoadCategory)val2;

            // 格式3：英文名
            switch (fileName.ToLowerInvariant())
            {
                case "rural": return RoadCategory.RuralRoads;
                case "community": return RoadCategory.CommunityRoads;
                case "small": return RoadCategory.SmallRoads;
                case "medium": return RoadCategory.MediumRoads;
                case "arterial": return RoadCategory.ArterialRoads;
                case "large": return RoadCategory.LargeRoads;
                case "highway": return RoadCategory.Highways;
                case "bus": return RoadCategory.BusRoads;
                case "pedestrian": return RoadCategory.PedestrianRoads;
                case "other": return RoadCategory.OtherRoads;
            }

            return null;
        }
    }
}

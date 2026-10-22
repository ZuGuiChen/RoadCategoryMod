using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.UI;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 自定义图标加载器。
    /// 从配置目录的 Icons 子文件夹加载 PNG 图片，创建自定义 UI atlas。
    /// 图标文件命名：category_{枚举值}.png，如 category_10.png
    /// </summary>
    public static class CustomIconLoader
    {
        private static UITextureAtlas _customAtlas;
        private static Dictionary<RoadCategory, string> _spriteNames;
        private static bool _loaded = false;

        /// <summary>
        /// 图标文件夹路径。
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
        /// 获取自定义图标 atlas。如果没有自定义图标，返回 null。
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
        /// 获取分类的自定义 sprite 名称。如果没有自定义图标，返回 null。
        /// </summary>
        public static string GetCustomSprite(RoadCategory cat)
        {
            EnsureLoaded();
            if (_spriteNames.ContainsKey(cat))
                return _spriteNames[cat];
            return null;
        }

        /// <summary>
        /// 调整纹理尺寸（使用 RenderTexture 方式）。
        /// </summary>
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
        /// 这样不需要替换 button.atlas，背景 sprite 保持不变。
        /// </summary>
        public static void AddCustomIconsToAtlas(UITextureAtlas targetAtlas)
        {
            EnsureLoaded();
            if (_customAtlas == null || targetAtlas == null) return;

            try
            {
                // 检查是否已经添加过
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
                {
                    Debug.Log("[RoadCategoryMod] Custom icons already in atlas: " + targetAtlas.name + ", skip");
                    return;
                }

                // 收集所有纹理：目标 atlas 已有的 + 自定义图标
                var allTextures = new List<Texture2D>();
                var allNames = new List<string>();

                // 目标 atlas 已有的纹理
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

                // 自定义图标纹理（从自定义 atlas 中提取）
                foreach (var sprite in _customAtlas.sprites)
                {
                    // 跳过已存在的
                    if (targetAtlas[sprite.name] != null) continue;

                    var tex = sprite.texture as Texture2D;
                    if (tex != null)
                    {
                        allTextures.Add(tex);
                        allNames.Add(sprite.name);
                    }
                }

                if (allTextures.Count == 0) return;

                // 使用 PackTextures 重新打包
                Rect[] regions = targetAtlas.texture.PackTextures(allTextures.ToArray(), targetAtlas.padding, 4096, false);

                // 清空并重新添加所有 sprite
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
                Debug.Log("[RoadCategoryMod] Added " + _spriteNames.Count + " custom icons to atlas: " + targetAtlas.name);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] AddCustomIconsToAtlas failed: " + e.Message + "\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// 若用户图标目录里没有任何 PNG，则把**模组目录下的 Icons\*.png**（随模组一起发布）复制过去。
        /// 这样新订阅的玩家不用手动找图标，开箱就有默认图标；已有自定义图标的玩家不受影响。
        /// </summary>
        private static void EnsureDefaultIcons()
        {
            try
            {
                string target = IconsFolder;
                if (Directory.Exists(target) && Directory.GetFiles(target, "*.png").Length > 0)
                    return;

                string modDirectory = Path.GetDirectoryName(typeof(CustomIconLoader).Assembly.Location);
                if (string.IsNullOrEmpty(modDirectory))
                    return;

                string bundled = Path.Combine(modDirectory, "Icons");
                if (!Directory.Exists(bundled))
                {
                    Debug.Log("[RoadCategoryMod] 模组目录没有内置图标，跳过默认图标复制: " + bundled);
                    return;
                }

                string[] files = Directory.GetFiles(bundled, "*.png");
                if (files.Length == 0)
                    return;

                int copied = 0;
                foreach (string file in files)
                {
                    try
                    {
                        string dest = Path.Combine(target, Path.GetFileName(file));
                        File.Copy(file, dest, false);
                        copied++;
                    }
                    catch (Exception)
                    {
                        // 单个文件失败（例如已存在）忽略
                    }
                }

                if (copied > 0)
                    Debug.Log("[RoadCategoryMod] 已复制 " + copied + " 个内置默认图标到 " + target);
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 复制内置默认图标失败: " + e.Message);
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            _spriteNames = new Dictionary<RoadCategory, string>();

            try
            {
                // 新玩家第一次运行时，把模组自带的默认图标复制到用户图标目录
                EnsureDefaultIcons();

                string folder = IconsFolder;
                var pngFiles = Directory.GetFiles(folder, "*.png");

                if (pngFiles.Length == 0)
                {
                    Debug.Log("[RoadCategoryMod] 没有找到自定义图标，使用默认图标。图标文件夹: " + folder);
                    _loaded = true;
                    return;
                }

                // 加载所有 PNG
                var textures = new List<Texture2D>();
                var categories = new List<RoadCategory>();
                var spriteNames = new List<string>();
                var isCategoryIcon = new List<bool>(); // 标记是否是分类图标（不是额外图标）

                foreach (var file in pngFiles)
                {
                    string fileName = Path.GetFileNameWithoutExtension(file);
                    RoadCategory? cat = ParseCategoryFromFileName(fileName);

                    byte[] bytes = File.ReadAllBytes(file);
                    var tex = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                    if (!tex.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(tex);
                        continue;
                    }

                    // 调整图标尺寸：分类图标 24x24，浮动按钮 20x20
                    int iconSize = (fileName.ToLower() == "float_button") ? 20 : 24;
                    tex = ResizeTexture(tex, iconSize, iconSize);

                    if (cat.HasValue)
                    {
                        // 分类图标
                        tex.name = "RCM_Icon_" + (int)cat.Value;
                        textures.Add(tex);
                        categories.Add(cat.Value);
                        spriteNames.Add("RCM_Category_" + (int)cat.Value);
                        isCategoryIcon.Add(true);
                    }
                    else
                    {
                        // 额外图标（float_button, category_tag 等）：只放进 atlas，不参与分类映射
                        string spriteName = "RCM_Extra_" + fileName;
                        tex.name = spriteName;
                        textures.Add(tex);
                        categories.Add(RoadCategory.RuralRoads); // 占位，不会被使用
                        spriteNames.Add(spriteName);
                        isCategoryIcon.Add(false);
                    }
                }

                if (textures.Count == 0)
                {
                    _loaded = true;
                    return;
                }

                // 参考 Find It 2 的实现方式创建 atlas
                // 1. 使用 PackTextures 打包纹理
                Texture2D atlasTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                Rect[] regions = atlasTex.PackTextures(textures.ToArray(), 2, 2048);

                // 2. 从默认 atlas 复制材质（关键！不是用 Shader.Find）
                _customAtlas = ScriptableObject.CreateInstance<UITextureAtlas>();
                _customAtlas.name = "RoadCategoryModAtlas";
                Material material = UnityEngine.Object.Instantiate(UIView.GetAView().defaultAtlas.material);
                material.mainTexture = atlasTex;
                _customAtlas.material = material;

                // 3. 添加 sprite（直接调用 AddSprite，不是反射）
                for (int i = 0; i < textures.Count; i++)
                {
                    var sprite = new UITextureAtlas.SpriteInfo
                    {
                        name = spriteNames[i],
                        texture = textures[i],
                        region = regions[i]
                    };
                    _customAtlas.AddSprite(sprite);
                    // 只对分类图标设置 _spriteNames，避免额外图标覆盖分类映射
                    if (isCategoryIcon[i])
                    {
                        _spriteNames[categories[i]] = spriteNames[i];
                    }
                }

                Debug.Log("[RoadCategoryMod] 已加载 " + _spriteNames.Count + " 个自定义图标");

                foreach (var kv in _spriteNames)
                {
                    Debug.Log("[RoadCategoryMod]   " + kv.Key + " -> " + kv.Value);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 加载自定义图标失败: " + e.Message + "\n" + e.StackTrace);
                _customAtlas = null;
                _spriteNames.Clear();
            }

            _loaded = true;
        }

        private static RoadCategory? ParseCategoryFromFileName(string fileName)
        {
            // 格式1：category_10
            if (fileName.StartsWith("category_", StringComparison.OrdinalIgnoreCase))
            {
                string numStr = fileName.Substring(9);
                int val;
                if (int.TryParse(numStr, out val))
                {
                    if (Enum.IsDefined(typeof(RoadCategory), val))
                        return (RoadCategory)val;
                }
            }

            // 格式2：直接数字 10
            int val2;
            if (int.TryParse(fileName, out val2))
            {
                if (Enum.IsDefined(typeof(RoadCategory), val2))
                    return (RoadCategory)val2;
            }

            // 格式3：分类名称 rural, community, small, medium, arterial, large, highway, bus
            string lower = fileName.ToLower();
            switch (lower)
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

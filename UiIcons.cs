using System;
using System.Collections.Generic;
using System.IO;
using ColossalFramework.UI;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 模组自用的 UI 图标/底图生成器。
    ///
    /// 1. 浮动按钮的**圆形底图**：运行时用像素画出来（比找游戏内置圆形 sprite 可靠，
    ///    也不依赖任何贴图资源），生成 3 个状态（常态 / 悬停 / 按下）并打成一个独立 atlas；
    /// 2. 浮动按钮前景图标：优先读 Icons\float_button.png，没有就用文字兜底；
    /// 3. UnifiedUI 按钮图标：圆形底图 + 前景图标（或程序绘制的道路图标）合成一张 64×64 贴图。
    ///
    /// 说明：独立 atlas 不会动游戏默认 atlas（避免像注入自定义图标那样重打包整张图集）。
    /// </summary>
    public static class UiIcons
    {
        public const string CircleNormal = "RCM_CircleNormal";
        public const string CircleHovered = "RCM_CircleHovered";
        public const string CirclePressed = "RCM_CirclePressed";
        public const string FloatIconSprite = "RCM_FloatIcon";

        private const int CircleTextureSize = 64;
        private const string FloatIconFileName = "float_button.png";

        // 圆形底图配色（偏冷灰色，贴近游戏 UI；按下偏蓝）
        private static readonly Color32 NormalFill = new Color32(52, 58, 68, 235);
        private static readonly Color32 NormalRim = new Color32(104, 116, 132, 255);
        private static readonly Color32 HoverFill = new Color32(72, 80, 94, 245);
        private static readonly Color32 HoverRim = new Color32(140, 158, 180, 255);
        private static readonly Color32 PressedFill = new Color32(40, 116, 186, 255);
        private static readonly Color32 PressedRim = new Color32(120, 190, 245, 255);

        private static UITextureAtlas _atlas;
        private static Texture2D _unifiedIconTexture;
        private static Texture2D _floatIconTexture;
        private static bool _floatIconLoaded;
        private static string _floatIconSpriteName;
        private static bool _atlasFailed;

        /// <summary>圆形底图 atlas（需要 UIView 已存在；失败返回 null，调用方回退旧样式）。</summary>
        public static UITextureAtlas Atlas
        {
            get { return _atlas; }
        }

        /// <summary>前景图标 sprite 名；没有 float_button.png 时返回 null。</summary>
        public static string FloatIconSpriteName
        {
            get
            {
                EnsureFloatIcon();
                return _floatIconSpriteName;
            }
        }

        /// <summary>构建圆形底图 atlas（可重复调用）。</summary>
        public static void EnsureAtlas()
        {
            if (_atlas != null || _atlasFailed)
                return;

            UIView view = UIView.GetAView();
            if (view == null || view.defaultAtlas == null)
                return;

            try
            {
                EnsureFloatIcon();

                var textures = new List<Texture2D>();
                var names = new List<string>();

                AddSpriteSource(textures, names, CreateCircle(NormalFill, NormalRim, "RCM_CircleN"), CircleNormal);
                AddSpriteSource(textures, names, CreateCircle(HoverFill, HoverRim, "RCM_CircleH"), CircleHovered);
                AddSpriteSource(textures, names, CreateCircle(PressedFill, PressedRim, "RCM_CircleP"), CirclePressed);

                if (_floatIconTexture != null)
                    AddSpriteSource(textures, names, _floatIconTexture, FloatIconSprite);

                Texture2D atlasTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                Rect[] regions = atlasTex.PackTextures(textures.ToArray(), 2, 512);

                UITextureAtlas atlas = ScriptableObject.CreateInstance<UITextureAtlas>();
                atlas.name = "RoadCategoryModUiAtlas";
                Material material = UnityEngine.Object.Instantiate(view.defaultAtlas.material);
                material.mainTexture = atlasTex;
                atlas.material = material;

                for (int i = 0; i < textures.Count; i++)
                {
                    atlas.AddSprite(new UITextureAtlas.SpriteInfo
                    {
                        name = names[i],
                        texture = textures[i],
                        region = regions[i]
                    });
                }

                _atlas = atlas;
                Debug.Log("[RoadCategoryMod] 圆形按钮底图已生成: " + string.Join(", ", names.ToArray()));
            }
            catch (Exception e)
            {
                _atlasFailed = true;
                _atlas = null;
                Debug.LogError("[RoadCategoryMod] 生成圆形底图失败，将使用默认样式: " + e.Message);
            }
        }

        /// <summary>
        /// 生成 UnifiedUI 按钮使用的 64×64 图标：圆形底图 + 自定义前景图标（无则绘制一个道路图标）。
        /// 只依赖 Texture2D，不需要 UIView，因此可在设置页里直接调用。
        /// </summary>
        public static Texture2D CreateUnifiedUIIcon()
        {
            // 复用同一张贴图：UnifiedUI 注册只会读取它，重复创建会白白占用显存
            if (_unifiedIconTexture != null)
                return _unifiedIconTexture;

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.name = "RCM_UnifiedUIIcon";

            Color32[] pixels = new Color32[size * size];

            // 圆形底图
            FillCircle(pixels, size, size, (size - 1) / 2f, (size - 1) / 2f, size / 2f - 1f, NormalFill, NormalRim, 2f);

            EnsureFloatIcon();
            if (_floatIconTexture != null)
            {
                // 前景图标缩放到 38×38 居中叠加
                DrawScaledTexture(pixels, size, _floatIconTexture, 38);
            }
            else
            {
                DrawRoadGlyph(pixels, size);
            }

            tex.SetPixels32(pixels);
            tex.Apply();

            _unifiedIconTexture = tex;
            return tex;
        }

        /// <summary>读取 Icons\float_button.png（缓存，64×64 等比缩放）。</summary>
        private static void EnsureFloatIcon()
        {
            if (_floatIconLoaded)
                return;
            _floatIconLoaded = true;

            try
            {
                string path = Path.Combine(CustomIconLoader.IconsFolder, FloatIconFileName);
                if (!File.Exists(path))
                {
                    Debug.Log("[RoadCategoryMod] 未找到浮动按钮图标（可用 float_button.png 自定义）: " + path);
                    return;
                }

                byte[] bytes = File.ReadAllBytes(path);
                Texture2D raw = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                if (!raw.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(raw);
                    return;
                }

                Texture2D scaled = ResizeBilinear(raw, CircleTextureSize);
                UnityEngine.Object.Destroy(raw);
                _floatIconTexture = scaled;
                _floatIconSpriteName = FloatIconSprite;
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 读取浮动按钮图标失败: " + e.Message);
            }
        }

        private static void AddSpriteSource(List<Texture2D> textures, List<string> names, Texture2D tex, string name)
        {
            if (tex == null)
                return;
            textures.Add(tex);
            names.Add(name);
        }

        /// <summary>生成一个抗锯齿的实心圆形（带描边）。</summary>
        private static Texture2D CreateCircle(Color32 fill, Color32 rim, string texName)
        {
            int size = CircleTextureSize;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.name = texName;

            Color32[] pixels = new Color32[size * size];
            FillCircle(pixels, size, size, (size - 1) / 2f, (size - 1) / 2f, size / 2f - 1f, fill, rim, 2f);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>在像素数组里画一个带描边的圆（描边宽度 rimWidth）。</summary>
        private static void FillCircle(Color32[] pixels, int width, int height,
                                       float cx, float cy, float radius,
                                       Color32 fill, Color32 rim, float rimWidth)
        {
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - cx;
                    float dy = y - cy;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 边缘 1 像素做抗锯齿
                    float coverage = Mathf.Clamp01(radius + 0.5f - dist);
                    if (coverage <= 0f)
                        continue;

                    bool onRim = dist >= radius - rimWidth;
                    Color32 src = onRim ? rim : fill;
                    float alpha = (src.a / 255f) * coverage;

                    int idx = y * width + x;
                    pixels[idx] = new Color32(src.r, src.g, src.b, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
        }

        /// <summary>把贴图按目标尺寸居中绘制到像素数组里（双线性采样 + Alpha 混合）。</summary>
        private static void DrawScaledTexture(Color32[] target, int targetSize, Texture2D source, int drawSize)
        {
            if (source == null)
                return;

            int offset = (targetSize - drawSize) / 2;
            Color32[] srcPixels;
            try
            {
                srcPixels = source.GetPixels32();
            }
            catch (Exception)
            {
                return;
            }

            int sw = source.width;
            int sh = source.height;

            for (int y = 0; y < drawSize; y++)
            {
                for (int x = 0; x < drawSize; x++)
                {
                    int tx = offset + x;
                    int ty = offset + y;
                    if (tx < 0 || ty < 0 || tx >= targetSize || ty >= targetSize)
                        continue;

                    float sx = (x + 0.5f) * sw / drawSize - 0.5f;
                    float sy = (y + 0.5f) * sh / drawSize - 0.5f;
                    Color32 c = SampleBilinear(srcPixels, sw, sh, sx, sy);

                    if (c.a == 0)
                        continue;

                    int idx = ty * targetSize + tx;
                    target[idx] = AlphaBlend(target[idx], c);
                }
            }
        }

        private static Color32 SampleBilinear(Color32[] pixels, int w, int h, float x, float y)
        {
            int x0 = Mathf.FloorToInt(x);
            int y0 = Mathf.FloorToInt(y);
            float fx = x - x0;
            float fy = y - y0;

            Color32 c00 = GetPixelSafe(pixels, w, h, x0, y0);
            Color32 c10 = GetPixelSafe(pixels, w, h, x0 + 1, y0);
            Color32 c01 = GetPixelSafe(pixels, w, h, x0, y0 + 1);
            Color32 c11 = GetPixelSafe(pixels, w, h, x0 + 1, y0 + 1);

            float r = Mathf.Lerp(Mathf.Lerp(c00.r, c10.r, fx), Mathf.Lerp(c01.r, c11.r, fx), fy);
            float g = Mathf.Lerp(Mathf.Lerp(c00.g, c10.g, fx), Mathf.Lerp(c01.g, c11.g, fx), fy);
            float b = Mathf.Lerp(Mathf.Lerp(c00.b, c10.b, fx), Mathf.Lerp(c01.b, c11.b, fx), fy);
            float a = Mathf.Lerp(Mathf.Lerp(c00.a, c10.a, fx), Mathf.Lerp(c01.a, c11.a, fx), fy);

            return new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255),
                               (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.Clamp(a, 0, 255));
        }

        private static Color32 GetPixelSafe(Color32[] pixels, int w, int h, int x, int y)
        {
            if (x < 0) x = 0;
            if (y < 0) y = 0;
            if (x >= w) x = w - 1;
            if (y >= h) y = h - 1;
            return pixels[y * w + x];
        }

        private static Color32 AlphaBlend(Color32 dst, Color32 src)
        {
            float sa = src.a / 255f;
            float da = dst.a / 255f;
            float outA = sa + da * (1f - sa);
            if (outA <= 0.0001f)
                return new Color32(0, 0, 0, 0);

            float r = (src.r * sa + dst.r * da * (1f - sa)) / outA;
            float g = (src.g * sa + dst.g * da * (1f - sa)) / outA;
            float b = (src.b * sa + dst.b * da * (1f - sa)) / outA;

            return new Color32((byte)Mathf.Clamp(r, 0, 255), (byte)Mathf.Clamp(g, 0, 255),
                               (byte)Mathf.Clamp(b, 0, 255), (byte)Mathf.RoundToInt(outA * 255f));
        }

        private static Texture2D ResizeBilinear(Texture2D source, int size)
        {
            Texture2D dst = new Texture2D(size, size, TextureFormat.ARGB32, false);
            dst.name = "RCM_FloatIcon";

            Color32[] srcPixels = source.GetPixels32();
            Color32[] dstPixels = new Color32[size * size];
            int sw = source.width;
            int sh = source.height;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float sx = (x + 0.5f) * sw / size - 0.5f;
                    float sy = (y + 0.5f) * sh / size - 0.5f;
                    dstPixels[y * size + x] = SampleBilinear(srcPixels, sw, sh, sx, sy);
                }
            }

            dst.SetPixels32(dstPixels);
            dst.Apply();
            return dst;
        }

        /// <summary>没有自定义图标时，画一个简单的“道路”图形（中间虚线 + 两侧实线）。</summary>
        private static void DrawRoadGlyph(Color32[] pixels, int size)
        {
            Color32 road = new Color32(196, 204, 214, 255);
            Color32 line = new Color32(245, 248, 252, 255);

            int cx = size / 2;
            int top = 14;
            int bottom = size - 15;

            // 路面
            for (int y = top; y <= bottom; y++)
            {
                for (int x = cx - 9; x <= cx + 9; x++)
                    BlendPixel(pixels, size, x, y, road, 0.28f);
            }

            // 两侧边线
            for (int y = top; y <= bottom; y++)
            {
                BlendPixel(pixels, size, cx - 8, y, line, 0.9f);
                BlendPixel(pixels, size, cx + 8, y, line, 0.9f);
            }

            // 中间虚线
            for (int y = top + 2; y <= bottom - 2; y += 7)
            {
                for (int k = 0; k < 4 && y + k <= bottom - 2; k++)
                    BlendPixel(pixels, size, cx, y + k, line, 0.95f);
            }
        }

        private static void BlendPixel(Color32[] pixels, int size, int x, int y, Color32 color, float alpha)
        {
            if (x < 0 || y < 0 || x >= size || y >= size)
                return;

            int idx = y * size + x;
            Color32 blended = color;
            blended.a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha) * 255f);
            pixels[idx] = AlphaBlend(pixels[idx], blended);
        }
    }
}

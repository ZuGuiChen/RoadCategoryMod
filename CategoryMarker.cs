using System;

namespace RoadCategoryMod
{
    /// <summary>
    /// 分类标记（`RCM/10` 这种）的解析工具。
    ///
    /// 道路面板的 tab 名字就是 `RCM/10`（游戏 `AddTab(category)` 会这样命名），
    /// 旧版本还把标记写进过 tooltip。这里提供“从任意文本里找出标记数字”的纯函数，
    /// 不依赖 Unity，便于离线单元测试。
    /// </summary>
    public static class CategoryMarker
    {
        /// <summary>标记前缀，与 RoadClassifier.Identifier 保持一致。</summary>
        public const string Identifier = "RCM/";

        /// <summary>
        /// 从任意文本中解析出标记里的数字（例如 "RCM/30" 或 "分类: RCM/30 (点击修改)" → 30）。
        /// </summary>
        /// <param name="text">待解析文本（tab 的 name 或 tooltip）</param>
        /// <param name="value">解析出的数字</param>
        /// <returns>是否找到合法的标记</returns>
        public static bool TryParseValue(string text, out int value)
        {
            value = 0;

            if (string.IsNullOrEmpty(text))
                return false;

            int index = text.IndexOf(Identifier, StringComparison.Ordinal);
            if (index < 0)
                return false;

            int start = index + Identifier.Length;
            int end = start;
            while (end < text.Length && char.IsDigit(text[end]))
                end++;

            if (end <= start)
                return false;

            return int.TryParse(text.Substring(start, end - start), out value);
        }
    }
}

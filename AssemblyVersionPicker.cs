using System;
using System.Collections.Generic;

namespace RoadCategoryMod
{
    /// <summary>
    /// 从多个同名程序集副本里挑出“应该使用的那一个”。
    ///
    /// 背景：游戏里同时存在多份 UnifiedUILib.dll（各模组自带），版本可能不同：
    /// 实测有 2.2.12.32690（UnifiedUI 本体）、2.2.12.31246、2.1.13.30989、2.1.10.35338。
    /// 每个副本都有各自独立的静态类字段，用错副本就会创建出第二块 UnifiedUI 面板。
    ///
    /// 选择规则（与 UnifiedUI 自己的 "using latest UnifiedUILib version" 一致）：
    /// 1. 优先精确匹配 UnifiedUI 本体所引用的版本；
    /// 2. 匹配不到就取版本号最高的；
    /// 3. 没有任何候选返回 -1。
    ///
    /// 本类不依赖 Unity，便于离线单元测试。
    /// </summary>
    public static class AssemblyVersionPicker
    {
        /// <summary>
        /// 在候选版本中挑选索引。
        /// </summary>
        /// <param name="wanted">期望版本（可为 null）</param>
        /// <param name="candidates">候选版本列表（可为 null 项，视为未知版本）</param>
        /// <returns>选中的索引；无候选时返回 -1</returns>
        public static int ChooseIndex(Version wanted, IList<Version> candidates)
        {
            if (candidates == null || candidates.Count == 0)
                return -1;

            if (wanted != null)
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i] != null && candidates[i].Equals(wanted))
                        return i;
                }
            }

            int bestIndex = -1;
            Version bestVersion = null;

            for (int i = 0; i < candidates.Count; i++)
            {
                Version version = candidates[i];

                if (bestIndex < 0)
                {
                    // 第一个候选：即使版本未知也先当作兜底
                    bestIndex = i;
                    bestVersion = version;
                    continue;
                }

                if (version == null)
                    continue;

                if (bestVersion == null || version > bestVersion)
                {
                    bestIndex = i;
                    bestVersion = version;
                }
            }

            return bestIndex;
        }
    }
}

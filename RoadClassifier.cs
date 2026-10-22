using System;
using System.Collections.Generic;
using ColossalFramework;
using UnityEngine;

namespace RoadCategoryMod
{
    /// <summary>
    /// 自定义道路分类枚举。值同时用作排序顺序。
    /// </summary>
    public enum RoadCategory
    {
        RuralRoads = 10,        // 乡村公路（砾石路、非铺装）
        CommunityRoads = 20,    // 社区小路（无中心线的微型道路）
        SmallRoads = 30,        // 小型道路（有中心线的双向2车道）
        MediumRoads = 40,       // 中型道路（双向4车道紧凑型）
        ArterialRoads = 50,     // 主干道（双向4车道带中间分隔带）
        LargeRoads = 60,        // 大型道路（双向6车道及以上）
        Highways = 70,          // 高速公路
        BusRoads = 80,          // 巴士专用道
        PedestrianRoads = 85,   // 步行道路（宽人行道的慢行道路）
        OtherRoads = 90         // 其他道路（非正常道路）
    }

    /// <summary>
    /// 道路分类工具方法。
    /// 自动分类 + 手动覆盖（手动覆盖优先）。
    /// </summary>
    public static class RoadClassifier
    {
        public const string Identifier = "RCM/";

        /// <summary>
        /// 判断一条道路属于哪些分类（一条道路可能属于多个分类，如公交道）。
        /// 优先使用手动覆盖配置，否则使用自动分类逻辑。
        /// </summary>
        public static List<RoadCategory> GetCategories(NetInfo info)
        {
            var cats = new List<RoadCategory>();

            if (info == null || info.m_lanes == null)
                return cats;

            // 优先检查手动覆盖
            RoadCategory? manual = RoadCategoryConfig.GetManualCategory(info.name);
            if (manual.HasValue)
            {
                cats.Add(manual.Value);
                return cats;
            }

            // 自动分类
            int vehicleLanes = GetVehicleLaneCount(info);
            bool hasBusLane = HasBusLanes(info);
            bool hasTram = (info.m_vehicleTypes & VehicleInfo.VehicleType.Tram) != 0;
            bool hasPedestrianOnly = info.m_hasPedestrianLanes && vehicleLanes == 0;
            bool isHighway = IsHighway(info);
            bool hasMedian = HasMedian(info);
            bool hasCenterLine = HasCenterLine(info);
            bool isRural = IsRuralRoad(info);

            // 步行道路：只加入名称包含 "pedestrian" 的道路（创意工坊步行道路资产）
            // 放在最前面，优先于其他所有分类判断
            if (info.name != null && info.name.IndexOf("pedestrian", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                cats.Add(RoadCategory.PedestrianRoads);
                return cats;
            }

            // 步行街不归入任何道路分类（原版会过滤掉）
            if (hasPedestrianOnly)
                return cats;

            // 高速公路优先
            if (isHighway)
            {
                cats.Add(RoadCategory.Highways);
                return cats;
            }

            // 巴士专用道（有公交专用道且普通车道少或没有）
            if (hasBusLane && vehicleLanes <= 2)
            {
                cats.Add(RoadCategory.BusRoads);
                return cats;
            }

            // 乡村公路（砾石路、非铺装）
            if (isRural)
            {
                cats.Add(RoadCategory.RuralRoads);
                return cats;
            }

            // 按车道数和道路特征分类
            if (vehicleLanes <= 0)
            {
                // 没有车行道的非正常道路（如停车场、广场、堤坝等）
                cats.Add(RoadCategory.OtherRoads);
            }
            else if (vehicleLanes <= 1)
            {
                // 单车道归为社区小路
                cats.Add(RoadCategory.CommunityRoads);
            }
            else if (vehicleLanes == 2)
            {
                // 双向2车道：有中心线归为小型道路，无中心线归为社区小路
                if (hasCenterLine)
                    cats.Add(RoadCategory.SmallRoads);
                else
                    cats.Add(RoadCategory.CommunityRoads);
            }
            else if (vehicleLanes == 3)
            {
                // 3车道（潮汐车道等）归为中型道路
                cats.Add(RoadCategory.MediumRoads);
            }
            else if (vehicleLanes == 4)
            {
                // 双向4车道：有中间分隔带归为主干道，否则归为中型道路
                if (hasMedian)
                    cats.Add(RoadCategory.ArterialRoads);
                else
                    cats.Add(RoadCategory.MediumRoads);
            }
            else if (vehicleLanes >= 5 && vehicleLanes <= 6)
            {
                // 5-6车道归为大型道路
                cats.Add(RoadCategory.LargeRoads);
            }
            else if (vehicleLanes >= 7)
            {
                // 7车道及以上归为大型道路
                cats.Add(RoadCategory.LargeRoads);
            }
            else
            {
                // 其他异常情况归为其他道路
                cats.Add(RoadCategory.OtherRoads);
            }

            // 有轨电车道路保持在原分类（不单独归类）
            return cats;
        }

        /// <summary>
        /// 创建 GroupInfo 对象，用于道路面板的分类标签。
        /// </summary>
        public static GeneratedGroupPanel.GroupInfo CreateGroup(RoadCategory cat)
        {
            return new GeneratedGroupPanel.GroupInfo(Identifier + ((int)cat).ToString(), (int)cat);
        }

        /// <summary>
        /// 从 GroupInfo.name 解析出 RoadCategory 枚举。
        /// </summary>
        public static bool TryParseCategory(string name, out RoadCategory cat)
        {
            cat = RoadCategory.RuralRoads;
            if (string.IsNullOrEmpty(name) || !name.StartsWith(Identifier))
                return false;

            string valStr = name.Substring(Identifier.Length);
            int val;
            if (!int.TryParse(valStr, out val))
                return false;

            cat = (RoadCategory)val;
            return true;
        }

        /// <summary>
        /// 从任意文本（tab 的 name，或已经被本地化过的 tooltip）里解析出分类。
        /// 用于兼容两种情况：
        /// - name 形如 "RCM/30"
        /// - tooltip 形如 "分类: RCM/30 (点击修改)"（旧版本写过这种格式）
        /// </summary>
        public static bool TryParseCategoryFromText(string text, out RoadCategory cat)
        {
            cat = RoadCategory.RuralRoads;

            int val;
            if (!CategoryMarker.TryParseValue(text, out val))
                return false;

            if (!Enum.IsDefined(typeof(RoadCategory), val))
                return false;

            cat = (RoadCategory)val;
            return true;
        }

        /// <summary>
        /// 获取分类的显示名称（与当前界面语言一致）。
        /// 实际文案由 Localization 提供，这里保留方法以兼容既有调用方。
        /// </summary>
        public static string GetCategoryName(RoadCategory cat)
        {
            return Localization.GetCategoryName(cat);
        }

        /// <summary>
        /// 获取分类的默认图标 sprite 基础名。
        /// </summary>
        public static string GetDefaultSpriteBase(RoadCategory cat)
        {
            switch (cat)
            {
                case RoadCategory.RuralRoads: return "SubBarRoadsSmall";
                case RoadCategory.CommunityRoads: return "SubBarRoadsSmall";
                case RoadCategory.SmallRoads: return "SubBarRoadsSmall";
                case RoadCategory.MediumRoads: return "SubBarRoadsMedium";
                case RoadCategory.ArterialRoads: return "SubBarRoadsLarge";
                case RoadCategory.LargeRoads: return "SubBarRoadsLarge";
                case RoadCategory.Highways: return "SubBarRoadsHighway";
                case RoadCategory.BusRoads: return "SubBarRoadsIntersection";
                case RoadCategory.PedestrianRoads: return "SubBarRoadsMaintenance";
                case RoadCategory.OtherRoads: return "SubBarRoadsIntersection";
                default: return "SubBarRoadsMedium";
            }
        }

        // ==================== 辅助判断方法 ====================

        /// <summary>
        /// 获取车行道数量。
        /// </summary>
        public static int GetVehicleLaneCount(NetInfo info)
        {
            if (info == null || info.m_lanes == null)
                return 0;

            int count = 0;
            foreach (var lane in info.m_lanes)
            {
                if (IsVehicleLane(lane))
                    count++;
            }
            return count;
        }

        private static bool IsVehicleLane(NetInfo.Lane lane)
        {
            var laneTypes = NetInfo.LaneType.Vehicle | NetInfo.LaneType.TransportVehicle;
            if ((lane.m_laneType & laneTypes) == 0)
                return false;

            // 排除自行车道（只计算机动车道）
            if ((lane.m_vehicleType & VehicleInfo.VehicleType.Bicycle) != 0)
                return false;

            return true;
        }

        /// <summary>
        /// 判断是否是高速公路。
        /// </summary>
        private static bool IsHighway(NetInfo info)
        {
            var ai = info.m_netAI as RoadBaseAI;
            if (ai != null && ai.m_highwayRules && !info.m_hasPedestrianLanes)
                return true;

            if (info.m_lanes != null)
            {
                foreach (var lane in info.m_lanes)
                {
                    if (lane.m_speedLimit > 80f)
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 判断是否有公交专用道。
        /// </summary>
        private static bool HasBusLanes(NetInfo info)
        {
            if ((info.m_laneTypes & NetInfo.LaneType.TransportVehicle) != 0)
                return true;

            if (info.m_lanes == null)
                return false;

            foreach (var lane in info.m_lanes)
            {
                if ((lane.m_laneType & NetInfo.LaneType.Vehicle) != 0 &&
                    (lane.m_vehicleType & VehicleInfo.VehicleType.Car) != 0 &&
                    (lane.m_vehicleCategoryPart1 & VehicleInfo.VehicleCategoryPart1.Bus) != 0 &&
                    (lane.m_vehicleCategoryPart1 & VehicleInfo.VehicleCategoryPart1.PassengerCar) == 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 判断是否有中间分隔带（median）。
        /// 通过 NetInfo.m_median 或车道布局判断。
        /// </summary>
        private static bool HasMedian(NetInfo info)
        {
            // 检查 m_median 字段（如果存在）
            var medianField = typeof(NetInfo).GetField("m_median", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (medianField != null)
            {
                var median = medianField.GetValue(info);
                if (median != null && !string.IsNullOrEmpty(median.ToString()))
                    return true;
            }

            // 通过车道布局判断：双向车道之间有较大间距（>2.0）可能有分隔带
            if (info.m_lanes != null)
            {
                float maxLeft = -100f, minRight = 100f;
                foreach (var lane in info.m_lanes)
                {
                    if (!IsVehicleLane(lane))
                        continue;
                    if (lane.m_position < 0)
                        maxLeft = Mathf.Max(maxLeft, lane.m_position);
                    else
                        minRight = Mathf.Min(minRight, lane.m_position);
                }
                if (maxLeft > -100f && minRight < 100f)
                {
                    float gap = minRight - maxLeft;
                    if (gap > 3.0f)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 判断是否有中心线。
        /// 通过车道布局判断：双向车道紧邻（间距小）通常有中心线。
        /// 或者通过道路名称判断。
        /// </summary>
        private static bool HasCenterLine(NetInfo info)
        {
            // 通过车道布局判断：双向车道间距小（<2.0）通常有中心线
            if (info.m_lanes != null)
            {
                float maxLeft = -100f, minRight = 100f;
                int leftCount = 0, rightCount = 0;
                foreach (var lane in info.m_lanes)
                {
                    if (!IsVehicleLane(lane))
                        continue;
                    if (lane.m_position < 0)
                    {
                        maxLeft = Mathf.Max(maxLeft, lane.m_position);
                        leftCount++;
                    }
                    else
                    {
                        minRight = Mathf.Min(minRight, lane.m_position);
                        rightCount++;
                    }
                }
                // 双向道路且车道间距小，通常有中心线
                if (leftCount > 0 && rightCount > 0 && maxLeft > -100f && minRight < 100f)
                {
                    float gap = minRight - maxLeft;
                    if (gap < 2.5f)
                        return true;
                }
            }

            // 通过道路名称关键词判断
            if (info.name != null)
            {
                string lower = info.name.ToLower();
                if (lower.Contains("twoway") || lower.Contains("two-way") || lower.Contains("avenue") || lower.Contains("road"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 判断是否是乡村公路（砾石路、非铺装）。
        /// 通过道路名称关键词或道路属性判断。
        /// </summary>
        private static bool IsRuralRoad(NetInfo info)
        {
            if (info.name != null)
            {
                string lower = info.name.ToLower();
                // 砾石路、乡村路、土路等关键词
                if (lower.Contains("gravel") || lower.Contains("rural") || lower.Contains("dirt") ||
                    lower.Contains("country") || lower.Contains("farm") || lower.Contains("field"))
                    return true;
            }

            // 乡村公路通常没有人行道、没有路灯
            if (!info.m_hasPedestrianLanes && !info.m_createPavement)
            {
                // 且车道数少
                int lanes = GetVehicleLaneCount(info);
                if (lanes <= 2)
                    return true;
            }

            return false;
        }
    }
}

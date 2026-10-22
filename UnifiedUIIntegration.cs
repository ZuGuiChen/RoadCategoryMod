using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ColossalFramework.UI;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace RoadCategoryMod
{
    /// <summary>
    /// UnifiedUI 集成（可选功能）。
    ///
    /// v1.2.2 重要修复：游戏里常常同时加载**多个版本的 UnifiedUILib 副本**
    /// （各模组把自己的 UnifiedUILib.dll 一起打包，实测同时存在 2.2.12 / 2.2.12.31246 / 2.1.13 / 2.1.10）。
    /// 每个副本都有各自独立的静态字段（MainPanel.instance_、MainAtlas 等），
    /// 如果“随便挑一个 UUIAPI 就调用”，会出现两类严重问题：
    ///   1. 注册进了老副本 → 另建一块面板，盖住真正的 UnifiedUI 面板（表现为“只有我们的图标，别的模组图标全没了”）；
    ///   2. `MainPanel.Instance` 在面板不存在时会**主动创建面板** → 我们的调用抢在 UnifiedUI 本体之前把它建出来，
    ///      之后真正的面板再创建就形成“两个面板”。
    ///
    /// 因此这里的规则是：
    ///   A. 优先使用 **UnifiedUIMod 程序集引用的那个 UnifiedUILib 版本**（也就是 UnifiedUI 本体真正在用的副本）；
    ///      UnifiedUI 未启用（UnifiedUIMod 未加载）时直接视为不可用。
    ///   B. 调用 Register 之前必须确认对方 `MainPanel.Exists == true`（只读判断，不触发创建），
    ///      没准备好就稍后重试，绝不代替 UnifiedUI 创建面板。
    /// </summary>
    public static class UnifiedUIIntegration
    {
        private const string ButtonName = "RoadCategoryMod";
        private const string ApiTypeName = "UnifiedUI.API.UUIAPI";
        private const string ModAssemblyName = "UnifiedUIMod";
        private const string LibAssemblyName = "UnifiedUILib";
        private const string MainPanelTypeName = "UnifiedUI.GUI.MainPanel";

        private static Assembly _libAssembly;
        private static Type _apiType;
        private static MethodInfo _registerMethod;
        private static bool _searched;
        private static float _lastSearchTime = -10f;
        private static object _button;
        private static bool _registered;
        private static bool _syncing;
        private static GameObject _retryHost;
        private static bool _panelEnsureRequested;

        /// <summary>上一次注册尝试是否因为异常而失败（用于设置页给出准确提示）。</summary>
        public static bool LastAttemptFailed { get; private set; }

        /// <summary>本会话是否找到可用的 UnifiedUI（UnifiedUI 本体 + 对应版本的 lib + 匹配的 Register 重载）。</summary>
        public static bool IsAvailable
        {
            get
            {
                EnsureApiFound();
                return _apiType != null && _registerMethod != null;
            }
        }

        /// <summary>当前是否已经注册到 UnifiedUI。</summary>
        public static bool IsRegistered
        {
            get
            {
                if (!_registered)
                    return false;

                UIComponent component = _button as UIComponent;
                if (component == null || component.gameObject == null)
                    return false;

                return true;
            }
        }

        /// <summary>
        /// 尝试注册（面板未就绪时返回 false，调用方可以稍后重试）。
        /// 只应在游戏内调用。
        /// </summary>
        public static bool TryRegister()
        {
            LastAttemptFailed = false;

            if (IsRegistered)
                return true;

            if (!IsAvailable)
                return false;

            if (!RoadCategoryMod.IsInGameScene())
                return false;

            // 关键：绝不通过我们的调用来创建 UnifiedUI 面板
            if (!IsPanelReady())
                return false;

            try
            {
                Texture2D icon = UiIcons.CreateUnifiedUIIcon();
                Action<bool> onToggle = new Action<bool>(OnToggleFromUnifiedUI);
                string tooltip = Localization.Get("MANAGER_TITLE");
                string groupName = Localization.Get("MOD_NAME");

                object result = _registerMethod.Invoke(null, new object[]
                {
                    ButtonName, groupName, tooltip, icon, onToggle, null, null, null
                });

                _button = result;
                _registered = result != null;

                if (_registered)
                {
                    Debug.Log("[RoadCategoryMod] 已加入 UnifiedUI 工具栏（lib " +
                        _libAssembly.GetName().Version + "）。");
                }
                else
                {
                    Debug.LogError("[RoadCategoryMod] UnifiedUI 注册返回空（版本不匹配？）。");
                }

                return _registered;
            }
            catch (Exception e)
            {
                LastAttemptFailed = true;
                Debug.LogError("[RoadCategoryMod] 加入 UnifiedUI 失败: " + DescribeException(e));
                _button = null;
                _registered = false;
                return false;
            }
        }

        /// <summary>
        /// 请求“等 UnifiedUI 面板就绪后自动注册”。
        /// 因为 UnifiedUI 面板由它自己的 LifeCycle 在关卡加载后创建，
        /// 我们可能比它更早执行 OnLevelLoaded，所以需要延迟重试。
        /// </summary>
        public static void RequestRegisterWhenReady()
        {
            if (IsRegistered)
                return;

            if (!RoadCategoryMod.IsInGameScene())
                return;

            if (!ModSettings.Current.AddToUnifiedUI)
                return;

            EnsureRetryHost();
        }

        private static void EnsureRetryHost()
        {
            if (_retryHost != null)
                return;

            try
            {
                _retryHost = new GameObject("RCM_UUIRegister");
                UnityEngine.Object.DontDestroyOnLoad(_retryHost);
                _retryHost.AddComponent<RegisterRetry>();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 无法创建 UnifiedUI 注册重试器: " + e.Message);
                _retryHost = null;
            }
        }

        /// <summary>在后台等待 UnifiedUI 面板创建完成后再注册（最多等 60 秒）。</summary>
        private class RegisterRetry : MonoBehaviour
        {
            private float _elapsed;

            private void Update()
            {
                try
                {
                    if (!ModSettings.Current.AddToUnifiedUI || !RoadCategoryMod.IsInGameScene())
                    {
                        Cleanup();
                        return;
                    }

                    _elapsed += Time.unscaledDeltaTime;

                    // 给 UnifiedUI 自己的 LifeCycle 5 秒时间创建面板；
                    // 超过之后用它的公开 API 请求创建（同一份 lib 副本，不会造出第二个面板）
                    if (_elapsed > 5f)
                        RequestPanelCreation();

                    if (UnifiedUIIntegration.TryRegister())
                    {
                        Cleanup();
                        return;
                    }

                    if (_elapsed > 60f)
                    {
                        Debug.LogWarning("[RoadCategoryMod] 等待 UnifiedUI 面板超时，本次未注册（可在设置页重新勾选）。");
                        Cleanup();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError("[RoadCategoryMod] UnifiedUI 注册重试失败: " + e.Message);
                    Cleanup();
                }
            }

            private void Cleanup()
            {
                UnifiedUIIntegration._retryHost = null;
                UnityEngine.Object.Destroy(gameObject);
            }
        }

        /// <summary>
        /// 通过 UnifiedUI 自己的 `MainPanel.Ensure()` 请求它创建面板。
        /// 只是“请求 UnifiedUI 初始化自己”，不是我们手动 AddUIComponent，
        /// 因此不会产生第二块面板（同一个 lib 副本 → 同一个静态 instance_）。
        /// </summary>
        private static void RequestPanelCreation()
        {
            if (_panelEnsureRequested || _libAssembly == null || IsPanelReady())
                return;

            _panelEnsureRequested = true;

            try
            {
                Type mainPanelType = _libAssembly.GetType(MainPanelTypeName, false);
                MethodInfo ensure = mainPanelType == null
                    ? null
                    : mainPanelType.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static);

                if (ensure == null)
                    return;

                ensure.Invoke(null, null);
                Debug.Log("[RoadCategoryMod] 已请求 UnifiedUI 创建其面板（等待超时兜底）。");
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 请求 UnifiedUI 创建面板失败: " + DescribeException(e));
            }
        }

        /// <summary>
        /// 从 UnifiedUI 移除按钮。返回 true 表示已移除（或本来没注册）。
        /// </summary>
        public static bool Unregister()
        {
            object button = _button;
            _button = null;
            _registered = false;

            if (button == null)
                return true;

            bool ok = true;

            try
            {
                object mainPanel = GetMainPanelInstance();
                if (mainPanel != null)
                {
                    object multiRow = GetMember(mainPanel, "MultiRowPanel");
                    if (multiRow != null)
                        InvokeMethod(multiRow, "RemoveButton", button);

                    object modButtons = GetMember(mainPanel, "ModButtons");
                    IList list = modButtons as IList;
                    if (list != null)
                        list.Remove(button);
                }
            }
            catch (Exception e)
            {
                ok = false;
                Debug.LogError("[RoadCategoryMod] 从 UnifiedUI 移除按钮（列表清理）失败: " + DescribeException(e));
            }

            try
            {
                InvokeMethod(button, "Release", null);
            }
            catch (Exception)
            {
                ok = false;
            }

            try
            {
                RefreshUnifiedUIPanel();
            }
            catch (Exception)
            {
                // 忽略：面板刷新失败不影响按钮已被销毁
            }

            return ok;
        }

        /// <summary>让 UnifiedUI 按钮的高亮状态与管理器窗口可见性保持一致。</summary>
        public static void SyncActive(bool active)
        {
            if (!IsRegistered || _syncing)
                return;

            try
            {
                _syncing = true;
                InvokeMethod(_button, active ? "Activate" : "Deactivate", null);
            }
            catch (Exception)
            {
                // 忽略
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>语言切换后更新按钮 tooltip。</summary>
        public static void RefreshLocalization()
        {
            if (!IsRegistered)
                return;

            try
            {
                UIComponent component = _button as UIComponent;
                if (component != null)
                    component.tooltip = Localization.Get("MANAGER_TITLE");
            }
            catch (Exception)
            {
                // 忽略
            }
        }

        private static void OnToggleFromUnifiedUI(bool active)
        {
            if (_syncing)
                return;

            try
            {
                if (active)
                    RoadCategoryManagerUI.Show();
                else
                    RoadCategoryManagerUI.Hide();
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] UnifiedUI 按钮回调失败: " + e.Message);
            }
        }

        /// <summary>判断 UnifiedUI 面板是否已经由它自己创建好（只读，不会触发创建）。</summary>
        private static bool IsPanelReady()
        {
            if (_libAssembly == null)
                return false;

            try
            {
                Type mainPanelType = _libAssembly.GetType(MainPanelTypeName, false);
                if (mainPanelType == null)
                    return false;

                PropertyInfo existsProperty = mainPanelType.GetProperty("Exists", BindingFlags.Public | BindingFlags.Static);
                if (existsProperty == null)
                    return false;

                object value = existsProperty.GetValue(null, null);
                return value is bool && (bool)value;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void EnsureApiFound()
        {
            // 找到后不再扫描；找不到时允许重试（UnifiedUI 可能比本模组晚加载），但限制扫描频率
            if (_searched)
                return;

            float now = Time.realtimeSinceStartup;
            if (now - _lastSearchTime < 1f)
                return;
            _lastSearchTime = now;

            try
            {
                Assembly modAssembly = null;
                var libCandidates = new List<Assembly>();

                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly == null)
                        continue;

                    string name;
                    try
                    {
                        name = assembly.GetName().Name;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (name == ModAssemblyName)
                        modAssembly = assembly;
                    else if (name == LibAssemblyName)
                        libCandidates.Add(assembly);
                }

                if (libCandidates.Count == 0)
                    return;

                // UnifiedUI 本体没启用时不做集成：避免我们自己“造”一块 UnifiedUI 面板出来
                if (modAssembly == null)
                    return;

                Assembly chosen = ChooseLibrary(modAssembly, libCandidates);
                if (chosen == null)
                    return;

                Type apiType = chosen.GetType(ApiTypeName, false);
                if (apiType == null)
                    return;

                MethodInfo method = FindRegisterMethod(apiType);
                if (method == null)
                    return;

                _libAssembly = chosen;
                _apiType = apiType;
                _registerMethod = method;
                _searched = true;

                Debug.Log("[RoadCategoryMod] UnifiedUI 集成使用 lib 版本: " + chosen.GetName().Version +
                    "（UnifiedUI 本体: " + SafeVersion(modAssembly) + "）");
            }
            catch (Exception e)
            {
                Debug.LogError("[RoadCategoryMod] 搜索 UnifiedUI API 失败: " + e.Message);
            }
        }

        /// <summary>
        /// 选出 UnifiedUI 本体真正在用的 UnifiedUILib 副本：
        /// 先按 UnifiedUIMod 的引用版本精确匹配，再退回“版本号最高的那个”。
        /// （挑选规则在 AssemblyVersionPicker 里，便于离线测试）
        /// </summary>
        private static Assembly ChooseLibrary(Assembly modAssembly, List<Assembly> candidates)
        {
            Version wantedVersion = null;
            try
            {
                foreach (AssemblyName reference in modAssembly.GetReferencedAssemblies())
                {
                    if (reference.Name == LibAssemblyName)
                    {
                        wantedVersion = reference.Version;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                // 忽略：退回按版本号挑选
            }

            var versions = new List<Version>(candidates.Count);
            foreach (Assembly candidate in candidates)
            {
                Version version = null;
                try
                {
                    version = candidate.GetName().Version;
                }
                catch (Exception)
                {
                    // 忽略：版本读不到就当未知
                }
                versions.Add(version);
            }

            int index = AssemblyVersionPicker.ChooseIndex(wantedVersion, versions);
            if (index < 0)
                return null;

            return candidates[index];
        }

        private static string SafeVersion(Assembly assembly)
        {
            try
            {
                return assembly.GetName().Version.ToString();
            }
            catch (Exception)
            {
                return "?";
            }
        }

        /// <summary>找到 (string, string, string, Texture2D, Action&lt;bool&gt;, Action&lt;ToolBase&gt;, SavedInputKey, Dictionary) 这个重载。</summary>
        private static MethodInfo FindRegisterMethod(Type apiType)
        {
            MethodInfo[] methods = apiType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (MethodInfo method in methods)
            {
                if (method.Name != "Register")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 8)
                    continue;
                if (parameters[0].ParameterType != typeof(string) ||
                    parameters[1].ParameterType != typeof(string) ||
                    parameters[2].ParameterType != typeof(string) ||
                    parameters[3].ParameterType != typeof(Texture2D) ||
                    parameters[4].ParameterType != typeof(Action<bool>))
                    continue;

                return method;
            }
            return null;
        }

        private static object GetMainPanelInstance()
        {
            if (_libAssembly == null)
                return null;

            try
            {
                Type mainPanelType = _libAssembly.GetType(MainPanelTypeName, false);
                if (mainPanelType == null)
                    return null;

                // 先看 Exists，避免在 UnifiedUI 尚未初始化时把它创建出来
                PropertyInfo existsProperty = mainPanelType.GetProperty("Exists", BindingFlags.Public | BindingFlags.Static);
                if (existsProperty != null)
                {
                    object exists = existsProperty.GetValue(null, null);
                    if (exists is bool && !(bool)exists)
                        return null;
                }

                PropertyInfo instanceProperty = mainPanelType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                if (instanceProperty == null)
                    return null;

                return instanceProperty.GetValue(null, null);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static void RefreshUnifiedUIPanel()
        {
            object mainPanel = GetMainPanelInstance();
            if (mainPanel != null)
                InvokeMethod(mainPanel, "DoRefreshButtons", null);
        }

        private static object GetMember(object target, string name)
        {
            if (target == null)
                return null;

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
                return property.GetValue(target, null);

            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null)
                return field.GetValue(target);

            return null;
        }

        private static object InvokeMethod(object target, string name, params object[] args)
        {
            if (target == null)
                return null;

            int argCount = args == null ? 0 : args.Length;
            MethodInfo method = null;

            foreach (MethodInfo candidate in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (candidate.Name != name)
                    continue;

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != argCount)
                    continue;

                bool compatible = true;
                for (int i = 0; i < parameters.Length; i++)
                {
                    Type parameterType = parameters[i].ParameterType;
                    if (args[i] == null)
                    {
                        if (parameterType.IsValueType)
                        {
                            compatible = false;
                            break;
                        }
                    }
                    else if (!parameterType.IsInstanceOfType(args[i]))
                    {
                        compatible = false;
                        break;
                    }
                }

                if (compatible)
                {
                    method = candidate;
                    break;
                }
            }

            if (method == null)
                return null;

            return method.Invoke(target, args);
        }

        private static string DescribeException(Exception e)
        {
            Exception inner = e;
            while (inner.InnerException != null)
                inner = inner.InnerException;
            return inner.Message;
        }
    }
}

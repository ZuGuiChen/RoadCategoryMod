using System;

namespace RoadCategoryMod
{
    /// <summary>
    /// 搜索框状态机：决定“什么时候才真的需要重建列表”。
    ///
    /// 背景（v1.2.1 修复）：
    /// UITextField 在“获得焦点清空占位符 / 失去焦点写回占位符”时也会触发 eventTextChanged，
    /// 而且 `submitOnFocusLost` 默认是 true（失焦会触发 eventTextSubmitted）。
    /// 如果直接把这些事件当成“用户改了搜索词”，就会出现：
    /// 点进搜索框刷新一次、鼠标移出又刷新一次。
    ///
    /// 因此这里区分两个词：
    /// - PendingSearch：搜索框里最新的文字（已去掉占位符）
    /// - AppliedSearch：当前列表真正按它过滤的词
    /// 只有两者不同才需要重建列表；占位符来回切换、失焦提交相同内容都不会触发。
    ///
    /// 该类不依赖 Unity，便于离线单元测试。
    /// </summary>
    public class SearchFilterState
    {
        /// <summary>当前列表实际使用的过滤词。</summary>
        public string AppliedSearch { get; private set; }

        /// <summary>搜索框里最新文字（去掉占位符后）。</summary>
        public string PendingSearch { get; private set; }

        /// <summary>是否有等待防抖的改动。</summary>
        public bool HasPending { get; private set; }

        /// <summary>输入防抖时间（秒）。</summary>
        public float DebounceSeconds { get; set; }

        /// <summary>
        /// 是否“输入时实时过滤”（默认 true）。
        /// 设为 false 时，输入不会自动触发刷新，只有回车 / 搜索按钮才会应用搜索词。
        /// </summary>
        public bool LiveSearch
        {
            get { return _liveSearch; }
            set
            {
                if (_liveSearch == value)
                    return;

                _liveSearch = value;

                // 切到手动模式时，丢弃尚未应用的防抖任务
                if (!value)
                {
                    HasPending = false;
                    _timer = 0f;
                }
            }
        }

        private string _placeholder;
        private float _timer;
        private bool _liveSearch = true;

        public SearchFilterState(string placeholder, float debounceSeconds)
        {
            _placeholder = placeholder ?? string.Empty;
            DebounceSeconds = debounceSeconds <= 0f ? 0.5f : debounceSeconds;
            AppliedSearch = string.Empty;
            PendingSearch = string.Empty;
        }

        /// <summary>界面语言变化时更新占位符文案（不会触发刷新）。</summary>
        public void SetPlaceholder(string placeholder)
        {
            _placeholder = placeholder ?? string.Empty;
        }

        /// <summary>把原始文本规范成搜索词（占位符视为空串）。</summary>
        public string Normalize(string rawText)
        {
            if (rawText == null)
                return string.Empty;
            return rawText == _placeholder ? string.Empty : rawText;
        }

        /// <summary>
        /// 搜索框文本变化。返回值：true = 需要“立即”重建（清空搜索时），false = 不需要或只需等待防抖。
        /// </summary>
        public bool OnTextChanged(string rawText)
        {
            string text = Normalize(rawText);

            // 内容没变（占位符来回切换就是这种情况）→ 什么都不做
            if (string.Equals(text, PendingSearch, StringComparison.Ordinal))
                return false;

            PendingSearch = text;
            _timer = 0f;

            if (text.Length == 0)
            {
                // 清空：立即恢复完整列表，不必等防抖（两种搜索模式都这样）
                HasPending = false;
                return true;
            }

            // 手动模式：只记住待应用的词，等回车 / 搜索按钮
            if (!LiveSearch)
            {
                HasPending = false;
                return false;
            }

            HasPending = true;
            return false;
        }

        /// <summary>
        /// 回车 / 失焦提交。返回值：true = 需要立即重建。
        /// </summary>
        public bool OnSubmitted(string rawText)
        {
            string text = Normalize(rawText);

            PendingSearch = text;
            HasPending = false;
            _timer = 0f;

            return !string.Equals(text, AppliedSearch, StringComparison.Ordinal);
        }

        /// <summary>每帧推进防抖。返回值：true = 防抖时间到，需要重建。</summary>
        public bool Tick(float deltaSeconds)
        {
            if (!HasPending)
                return false;

            _timer += deltaSeconds;
            if (_timer < DebounceSeconds)
                return false;

            HasPending = false;
            _timer = 0f;

            return !string.Equals(PendingSearch, AppliedSearch, StringComparison.Ordinal);
        }

        /// <summary>
        /// 把待应用的搜索词应用到列表。返回值：true = 搜索词确实变了，调用方需要重建列表。
        /// </summary>
        public bool Consume()
        {
            HasPending = false;
            _timer = 0f;

            if (string.Equals(PendingSearch, AppliedSearch, StringComparison.Ordinal))
                return false;

            AppliedSearch = PendingSearch ?? string.Empty;
            return true;
        }

        /// <summary>重置状态（例如切换语言后重建列表时不需要清空搜索词，这里保留给测试/复位用）。</summary>
        public void Reset()
        {
            AppliedSearch = string.Empty;
            PendingSearch = string.Empty;
            HasPending = false;
            _timer = 0f;
        }
    }
}

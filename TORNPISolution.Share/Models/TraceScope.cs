namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 日志追踪上下文（scope）：解决"串口日志找不到是哪个测试项哪个点位发的"。
    /// 用 <see cref="AsyncLocal{T}"/> 存当前 scope 字符串，<c>LogService</c> 给每条日志自动加
    /// <c>[scope]</c> 前缀，因此 TX / RX / 解析值 / 判定 这几行天然属于同一组，按前缀检索即可串起来。
    ///
    /// 粒度约定（与测试项子表一致）：
    ///   模块级   —— <c>TestItem.RunModuleAsync</c> 进入模块时 Begin("A05静态压力精度")
    ///   点位/轮次 —— 流程内 using (TraceScope.Push("BP-003@90mmHg")) 或 ("BP-004@120/80/80#R2")
    /// Push 可嵌套（用 · 连接），Dispose 时还原上一层，异常/提前 return 
    /// </summary>
    public static class TraceScope
    {
        private static readonly AsyncLocal<string?> Current = new();

        /// <summary>当前 scope（无上下文时为空串）</summary>
        public static string Text => Current.Value ?? string.Empty;

        /// <summary>是否已有追踪上下文</summary>
        public static bool IsActive => !string.IsNullOrEmpty(Current.Value);

        /// <summary>
        /// 设置 scope 并返回可释放对象
        /// </summary>
        public static IDisposable Begin(string scope)
        {
            var prev = Current.Value;
            Current.Value = scope;
            return new ScopeHandle(prev);
        }

        /// <summary>
        /// 在当前 scope 后追加一段（用 · 连接）
        /// </summary>
        public static IDisposable Push(string suffix)
            => Begin(string.IsNullOrEmpty(Current.Value) ? suffix : $"{Current.Value}·{suffix}");

        private sealed class ScopeHandle : IDisposable
        {
            private readonly string? _previous;
            private bool _disposed;

            public ScopeHandle(string? previous) => _previous = previous;

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                Current.Value = _previous;
            }
        }
    }
}

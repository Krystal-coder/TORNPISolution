using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 测试结果收集器：封装结果字典 + 逐项完成回调。
    /// 模块方法每写入一项结果，自动触发 onItemCompleted 回调，
    /// UI 层收到回调即可立即回填显示，无需等整个模块跑完。
    /// </summary>
    public sealed class TestItemResultCollector
    {
        private readonly Dictionary<string, TestItemResult> _results;
        private readonly HashSet<string> _written = new();
        private readonly Func<string, TestItemResult, Task>? _onItemCompleted;

        public TestItemResultCollector(
            Dictionary<string, TestItemResult> results,
            Func<string, TestItemResult, Task>? onItemCompleted)
        {
            _results = results;
            _onItemCompleted = onItemCompleted;
        }

        /// <summary>
        /// 异步设置测试结果并触发逐项回调。
        /// 模块方法中 await collector["项名"] = result
        /// 区别在于此处会自动 await UI 层回调，确保 UI 更新完成后再继续下一项。
        /// </summary>
        public async Task SetAsync(string key, TestItemResult value)
        {
            _results[key] = value;
            _written.Add(key);
            if (_onItemCompleted != null)
                await _onItemCompleted(key, value);
        }

        /// <summary>本模块实际报过名的项（自检拿它和目录项清单比对，看哪一项漏报）</summary>
        public IReadOnlyCollection<string> WrittenKeys => _written;

        /// <summary>底层结果字典（TestItemxx 最终返回用）</summary>
        public Dictionary<string, TestItemResult> Results => _results;
    }
}

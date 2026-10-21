using Serilog;
using Serilog.Core;
using TORNPISolution.Interface;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service
{
    /// <summary>
    /// 基于 Serilog 的日志实现，各层通过 ILogService 使用、不直接依赖 Serilog。
    /// 文件按天滚动（Logs\log-yyyyMMdd.txt），单文件超过 10MB 自动生成新文件（log-yyyyMMdd_001.txt ...）。
    /// 全局统一输出格式：[时间] [级别] [来源] 消息（时间戳精度见 OutputTemplate）。
    /// </summary>
    public class LogService : ILogService
    {
        private const long MaxFileSizeBytes = 10 * 1024 * 1024;

        private const string OutputTemplate =
            "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}";

        /// <summary>全局唯一的根 Logger（进程内共享，线程安全）</summary>
        private static readonly Lazy<Logger> Root = new(() =>
            new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: Path.Combine(AppContext.BaseDirectory, "Logs", "log-.txt"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: MaxFileSizeBytes,
                    rollOnFileSizeLimit: true,
                    outputTemplate: OutputTemplate,
                    shared: true)
                .CreateLogger());

        private readonly Serilog.ILogger _logger;

        /// <summary>创建带来源标识的日志实例，source 传调用方类名，用于在统一格式中区分日志出自哪一层哪个类</summary>
        public LogService(string source)
        {
            _logger = Root.Value.ForContext("Source", source);
        }

        public void Debug(string message) => _logger.Debug("{Message}", WithScope(message));

        public void Info(string message) => _logger.Information("{Message}", WithScope(message));

        public void Warn(string message) => _logger.Warning("{Message}", WithScope(message));

        public void Error(string message, Exception? ex = null) => _logger.Error(ex, "{Message}", WithScope(message));

        /// <summary>为消息自动附加测试项追踪上下文前缀（见 <see cref="TraceScope"/>）：串口 TX/RX、解析值、判定行会带上 [A05静态压力精度·BP-003@90mmHg]，按前缀可检索一整组</summary>
        private static string WithScope(string message)
            => TraceScope.IsActive ? $"[{TraceScope.Text}] {message}" : message;

        /// <summary>程序退出前调用，确保缓冲日志全部落盘</summary>
        public static void CloseAndFlush()
        {
            if (Root.IsValueCreated)
            {
                Root.Value.Dispose();
            }
        }
    }
}

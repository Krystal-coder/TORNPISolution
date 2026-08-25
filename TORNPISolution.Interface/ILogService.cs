namespace TORNPISolution.Interface
{
    /// <summary>
    /// 统一日志接口：各层只依赖本接口写日志，不直接依赖具体日志框架，
    /// 保证分层结构（UI / Service / 设备层均可注入使用）。
    /// </summary>
    public interface ILogService
    {
        /// <summary>调试日志</summary>
        void Debug(string message);

        /// <summary>普通信息日志</summary>
        void Info(string message);

        /// <summary>警告日志</summary>
        void Warn(string message);

        /// <summary>错误日志</summary>
        void Error(string message, Exception? ex = null);
    }
}

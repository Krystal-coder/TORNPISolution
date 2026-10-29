namespace TORNPISolution.Interface
{
    /// <summary>
    /// 海康 MV-ID 读码器（智能读码相机）设备接口
    /// </summary>
    public interface IBarcodeReader : IDisposable
    {
        /// <summary>位号（日志前缀）</summary>
        string Name { get; }

        /// <summary>触发与结果两条连接是否都已建立</summary>
        bool IsOpen { get; }

        /// <summary>按配置建立触发端口与结果端口的连接</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>关闭两条连接</summary>
        Task CloseAsync();

        /// <summary>软触发一次并等结果：向触发端口发触发文本，从结果端口读一条条码</summary>
        Task<string?> ScanAsync(int? timeoutMs = null, CancellationToken ct = default);

        /// <summary>不触发，只等相机主动推来的下一条结果（自由运行或由 IO 触发时用）</summary>
        Task<string?> ReadAsync(int? timeoutMs = null, CancellationToken ct = default);

        /// <summary>清除缓存结果</summary>
        void DiscardPendingResults();
    }
}

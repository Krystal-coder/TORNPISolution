using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 研为 EW-DTU09 蓝牙网关设备接口（网口 TCP 上的 AT 指令）
    /// </summary>
    public interface IBluetoothGateway : IDisposable
    {
        /// <summary>位号（日志前缀）</summary>
        string Name { get; }

        /// <summary>TCP 连接是否已建立</summary>
        bool IsOpen { get; }

        /// <summary>建立 TCP 连接并启动上报读取泵</summary>
        Task OpenAsync(CancellationToken ct = default);

        /// <summary>停止读取泵并断开连接</summary>
        Task CloseAsync();

        /// <summary>握手测试（AT，返回 OK 即链路通）</summary>
        Task<bool> TestConnectionAsync(CancellationToken ct = default);

        /// <summary>读取网关自身信息（ATI：版本、型号、MAC、扫描与连接参数）</summary>
        Task<string> GetIdentityAsync(CancellationToken ct = default);

        /// <summary>
        /// 读当前工作模式（AT+MODE?）：0=AT 指令模式，1=数据透传模式。
        /// 透传模式下下发的 AT 指令会被当数据转给从机，网关不会答 OK。
        /// </summary>
        Task<int> GetModeAsync(CancellationToken ct = default);

        /// <summary>切工作模式（AT+MODE=n）：true=AT 指令模式，false=数据透传模式</summary>
        Task SetModeAsync(bool atMode, CancellationToken ct = default);

        /// <summary>
        /// 设置/关闭 MAC 过滤（AT+FLT）。
        /// 传空表示关掉过滤，此后上报的是视野内所有广播。
        /// </summary>
        Task SetMacFilterAsync(string? mac, CancellationToken ct = default);

        /// <summary>开始扫描（AT+SCAN=1,...）</summary>
        Task StartScanAsync(CancellationToken ct = default);

        /// <summary>停止扫描（AT+SCAN=0）</summary>
        Task StopScanAsync(CancellationToken ct = default);

        /// <summary>
        /// 扫描一段时间，返回期间收到的广播（按 MAC 去重，同一 MAC 取最后一条）。
        /// </summary>
        /// <param name="durationMs">扫描窗口时长</param>
        /// <param name="mac">只收这个 MAC 的广播，留空则全收</param>
        Task<IReadOnlyList<BluetoothScanInfo>> ScanAsync(int durationMs, string? mac = null, CancellationToken ct = default);

        /// <summary>
        /// 采集指定 MAC 的信号强度样本：开过滤 → 复位一次并接回链路 → 扫描 → 攒够条数或超时 → 关扫描并还原过滤。
        /// 复位（AT+OP=0）是为了把上一轮扫描的残留上报清掉，代价是每台要多等几秒。
        /// </summary>
        /// <param name="sampleCount">要攒的广播条数，留空取配置里的样本条数</param>
        /// <param name="timeoutMs">总超时，留空取配置里的超时；到时按已采到的部分返回</param>
        /// <returns>信号强度样本（dBm），一条都没采到返回空集合</returns>
        /// <exception cref="TimeoutException">复位没人认，或复位后到时间也没重新连上</exception>
        Task<IReadOnlyList<int>> ReadRssiAsync(string mac, int? sampleCount = null, int? timeoutMs = null, CancellationToken ct = default);

        /// <summary>
        /// 网关复位（AT+REST=1）。模块会重启并重新播一遍上电信息
        /// TCP 会话随模块重启作废，复位后必须重新 OpenAsync。
        /// </summary>
        /// <exception cref="TimeoutException">到时间既没看到上电信息也没看到 OK（会话已关）</exception>
        Task ResetAsync(CancellationToken ct = default);
    }
}

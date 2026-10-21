namespace TORNPISolution.Interface
{
    /// <summary>
    /// NFC 读卡器公共接口（THS_HF 高频读写器，如岩柱 YZ806/YZ906）
    /// </summary>
    public interface INfcReader : IDisposable
    {
        /// <summary>串口是否已打开</summary>
        bool IsOpen { get; }

        /// <summary>打开读卡器串口连接。</summary>
        /// <param name="portName">串口号，如 COM3</param>
        /// <param name="baudRate">波特率</param>
        Task OpenAsync(string portName, int baudRate, CancellationToken ct = default);

        /// <summary>关闭连接</summary>
        Task CloseAsync();

        /// <summary>读取读卡器固件版本（验证通讯链路是否正常）。</summary>
        Task<string> GetFirmwareVersionAsync(CancellationToken ct = default);

        /// <summary>读取 NTAG 标签连续页数据（每页 4 字节）。</summary>
        /// <param name="startPage">起始页号（用户区一般从 4 开始）</param>
        /// <returns>读到的原始字节（长度 = pageCount * 4）</returns>
        Task<byte[]> ReadPagesAsync(byte startPage, byte pageCount, CancellationToken ct = default);

        /// <summary>写入 NTAG 标签连续页数据（数据长度须为 4 的整数倍，不足自动补 0）。</summary>
        Task WritePagesAsync(byte startPage, byte[] data, CancellationToken ct = default);

        /// <summary>读取标签 UID（卡号）。</summary>
        Task<byte[]> GetUidAsync(CancellationToken ct = default);
    }
}

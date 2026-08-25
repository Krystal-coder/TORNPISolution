using System.Globalization;
using TORNPISolution.Interface;
using TORNPISolution.Service.Communication;
using TORNPISolution.Share.Models;

namespace TORNPISolution.Service.Devices
{
    /// <summary>
    /// 艾德克斯（ITECH）IT6411 双通道直流电源控制类。
    /// 通讯：网口 LAN，SCPI Raw Socket（默认端口 5025），命令/响应以 \n 结尾。
    /// 通道规格（IT6411S，具体以仪器铭牌为准）：
    ///   CH1：0~30V / 0~3A；CH2：0~5V / 0~1A。
    /// IT6400 系列双通道机型通过 OUTPut:CHANnel 命令选择当前操作通道，
    /// 本类所有带 channel 参数的方法均在设备锁内"先选通道再发命令"，保证原子性。
    /// </summary>
    public class It6411PowerSupply : IDisposable
    {
        private readonly ScpiTcpClient _client;
        private readonly ILogService _log;

        /// <summary>"选择通道 + 下发命令"复合操作的原子性锁</summary>
        private readonly SemaphoreSlim _channelLock = new(1, 1);

        /// <summary>
        /// 按仪器链接配置创建（网口 SCPI）
        /// </summary>
        public It6411PowerSupply(LanLinkSettings link, ILogService? log = null)
            : this(link.IpAddress, link.Port, log)
        {
        }

        public It6411PowerSupply(string ipAddress, int port = 5025, ILogService? log = null)
        {
            _log = log ?? new LogService(nameof(It6411PowerSupply));
            _client = new ScpiTcpClient(ipAddress, port, log: _log);
        }

        public bool IsOpen => _client.IsOpen;

        /// <summary>
        /// 初始化：建立 TCP 连接 → 切换远程模式 → 读取铭牌信息
        /// </summary>
        public async Task OpenAsync(CancellationToken ct = default)
        {
            await _client.ConnectAsync(ct).ConfigureAwait(false);
            await _client.WriteAsync("SYSTem:REMote", ct).ConfigureAwait(false);
            var idn = await _client.QueryAsync("*IDN?", ct).ConfigureAwait(false);
            _log.Info($"IT6411 初始化完成：{idn}");
        }

        /// <summary>
        /// 释放：返回本地模式（失败不阻断）→ 断开连接
        /// </summary>
        public async Task CloseAsync()
        {
            try
            {
                if (_client.IsOpen)
                {
                    await _client.WriteAsync("SYSTem:LOC").ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"IT6411 返回本地模式失败（忽略）：{ex.Message}");
            }
            await _client.CloseAsync().ConfigureAwait(false);
        }

        /// <summary>读取仪器铭牌信息（*IDN?）</summary>
        public Task<string> GetIdentityAsync(CancellationToken ct = default)
            => _client.QueryAsync("*IDN?", ct);

        /// <summary>复位仪器（*RST）</summary>
        public Task ResetAsync(CancellationToken ct = default)
            => _client.WriteAsync("*RST", ct);

        // ==================== 通道输出控制 ====================

        /// <summary>通道输出开/关（OUTPut ON|OFF）</summary>
        public Task SetOutputAsync(int channel, bool on, CancellationToken ct = default)
            => OnChannelAsync(channel, $"OUTPut {(on ? "ON" : "OFF")}", "输出" + (on ? "开启" : "关闭"), ct);

        /// <summary>查询通道输出状态（OUTPut?，返回 1/0）</summary>
        public async Task<bool> GetOutputStateAsync(int channel, CancellationToken ct = default)
        {
            var response = await OnChannelQueryAsync(channel, "OUTPut?", ct).ConfigureAwait(false);
            return response.Trim() == "1";
        }

        /// <summary>设置通道输出电压（VOLTage）</summary>
        public Task SetVoltageAsync(int channel, double volts, CancellationToken ct = default)
            => OnChannelAsync(channel, $"VOLTage {Format(volts)}", $"设置电压 {volts}V", ct);

        /// <summary>查询通道电压设定值（VOLTage?）</summary>
        public async Task<double> GetVoltageSetAsync(int channel, CancellationToken ct = default)
            => Parse(await OnChannelQueryAsync(channel, "VOLTage?", ct).ConfigureAwait(false));

        /// <summary>设置通道限流值（CURRent）</summary>
        public Task SetCurrentAsync(int channel, double amps, CancellationToken ct = default)
            => OnChannelAsync(channel, $"CURRent {Format(amps)}", $"设置限流 {amps}A", ct);

        /// <summary>查询通道限流设定值（CURRent?）</summary>
        public async Task<double> GetCurrentSetAsync(int channel, CancellationToken ct = default)
            => Parse(await OnChannelQueryAsync(channel, "CURRent?", ct).ConfigureAwait(false));

        /// <summary>一次性设置通道电压与限流并打开输出（APPLy + OUTPut ON）</summary>
        public async Task ApplyOutputAsync(int channel, double volts, double amps, CancellationToken ct = default)
        {
            await _channelLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SelectChannelCoreAsync(channel, ct).ConfigureAwait(false);
                await _client.WriteAsync($"APPLy {Format(volts)},{Format(amps)}", ct).ConfigureAwait(false);
                await _client.WriteAsync("OUTPut ON", ct).ConfigureAwait(false);
                _log.Info($"IT6411 CH{channel} 输出 {volts}V / 限流 {amps}A 已开启");
            }
            finally
            {
                _channelLock.Release();
            }
        }

        // ==================== 通道回读测量 ====================

        /// <summary>测量通道实际输出电压（MEASure:VOLTage?）</summary>
        public async Task<double> MeasureVoltageAsync(int channel, CancellationToken ct = default)
            => Parse(await OnChannelQueryAsync(channel, "MEASure:VOLTage?", ct).ConfigureAwait(false));

        /// <summary>测量通道实际输出电流（MEASure:CURRent?）</summary>
        public async Task<double> MeasureCurrentAsync(int channel, CancellationToken ct = default)
            => Parse(await OnChannelQueryAsync(channel, "MEASure:CURRent?", ct).ConfigureAwait(false));

        // ==================== 系统状态 ====================

        /// <summary>读取错误队列（SYSTem:ERRor?，返回如 0,"No error"）</summary>
        public Task<string> ReadErrorAsync(CancellationToken ct = default)
            => _client.QueryAsync("SYSTem:ERRor?", ct);

        // ==================== 内部实现 ====================

        /// <summary>选择通道后下发无响应命令（原子操作）</summary>
        private async Task OnChannelAsync(int channel, string command, string what, CancellationToken ct)
        {
            await _channelLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SelectChannelCoreAsync(channel, ct).ConfigureAwait(false);
                await _client.WriteAsync(command, ct).ConfigureAwait(false);
                _log.Info($"IT6411 CH{channel} {what}");
            }
            finally
            {
                _channelLock.Release();
            }
        }

        /// <summary>选择通道后下发查询命令（原子操作）</summary>
        private async Task<string> OnChannelQueryAsync(int channel, string command, CancellationToken ct)
        {
            await _channelLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await SelectChannelCoreAsync(channel, ct).ConfigureAwait(false);
                return await _client.QueryAsync(command, ct).ConfigureAwait(false);
            }
            finally
            {
                _channelLock.Release();
            }
        }

        /// <summary>选择当前操作通道（OUTPut:CHANnel 1|2），必须在锁内调用</summary>
        private Task SelectChannelCoreAsync(int channel, CancellationToken ct)
        {
            if (channel is not (1 or 2))
            {
                throw new ArgumentOutOfRangeException(nameof(channel), $"IT6411 只有两个通道（1/2），收到：{channel}");
            }
            return _client.WriteAsync($"OUTPut:CHANnel {channel}", ct);
        }

        /// <summary>数值格式化（固定小数点分隔符，避免中文区域逗号问题）</summary>
        private static string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>解析仪器返回的数值（如 "12.034"）</summary>
        private static double Parse(string response)
            => double.Parse(response.Trim(), CultureInfo.InvariantCulture);

        public void Dispose()
        {
            _client.Dispose();
            _channelLock.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

namespace TORNPISolution.Share.Models
{
    /// <summary>单条协议命令配置项（JSON 序列化用）</summary>
    public class ProtocolCommandEntry
    {
        /// <summary>命令码（十进制存储，界面以十六进制显示）</summary>
        public byte Code { get; set; }

        /// <summary>功能码：0x02 产品信息查询 / 0x05 测量控制 / 0x06 主动推送 / 0x07 硬件控制与产测（缺省值）。
        /// 同一命令码在不同功能码下是不同命令（如 0x80/0x05 读过压状态、0x80/0x07 启动 ECG+PPG），必须与 Code 成对配置。</summary>
        public byte FuncCode { get; set; } = 0x07;

        /// <summary>命令描述（中文说明）</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// 命令默认参数字节，HEX 文本、空格/短横/逗号分隔（如 "05 00"）
        /// </summary>
        public string? Data { get; set; }
    }
}

namespace TORNPISolution.Share.Models
{
    /// <summary>单条协议命令配置项（JSON 序列化用）</summary>
    public class ProtocolCommandEntry
    {
        /// <summary>命令码（十进制存储，界面以十六进制显示）</summary>
        public byte Code { get; set; }

        /// <summary>功能码（默认 0x07 = 读命令；实测所有读命令均为 0x07，含充电状态 0x6D）</summary>
        public byte FuncCode { get; set; } = 0x07;

        /// <summary>命令描述（中文说明）</summary>
        public string Description { get; set; } = string.Empty;
    }
}

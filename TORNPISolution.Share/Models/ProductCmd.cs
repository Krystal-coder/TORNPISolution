namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品命令标识：功能码 + 控制码
    /// </summary>
    /// <param name="Func">功能码（0x02 产品信息 / 0x05 测量控制 / 0x06 主动推送 / 0x07 硬件控制与产测）</param>
    /// <param name="Code">控制码</param>
    public readonly record struct ProductCmd(byte Func, byte Code)
    {
        public override string ToString() => $"Func=0x{Func:X2} Code=0x{Code:X2}";
    }
}

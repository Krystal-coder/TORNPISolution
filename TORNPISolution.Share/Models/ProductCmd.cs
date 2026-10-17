namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 产品命令标识：功能码 + 控制码
    /// 同一控制码在不同功能码下是不同命令（如 0x80/Func=0x05 读过压状态 vs 0x80/Func=0x07 启动ECG+PPG测量），
    /// 因此命令身份必须由 (Func, Code) 共同确定，不能只靠 Code 反查 Func。
    /// 放在 Share 层以便 Interface（IProductController）与 Service（ProductProtocol）共用。
    /// </summary>
    /// <param name="Func">功能码（0x02 产品信息 / 0x05 测量控制 / 0x06 主动推送 / 0x07 硬件控制与产测）</param>
    /// <param name="Code">控制码</param>
    public readonly record struct ProductCmd(byte Func, byte Code)
    {
        public override string ToString() => $"Func=0x{Func:X2} Code=0x{Code:X2}";
    }
}

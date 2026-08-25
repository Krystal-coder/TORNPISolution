namespace TORNPISolution.GoogolEtherCAT.GEN
{
    public class ConvertHelper
    {
        public static bool[] Convert4BytesTo32BoolArray(byte[] di4Bytes,int diSize)
        {
            // 初始化32位bool数组，默认全false（通道断开）
            bool[] diStatusArr = new bool[diSize];

            try
            {
                // 入参空值/长度校验
                if (di4Bytes == null)
                {
                    Console.WriteLine("[DI转换] 错误：原始数据为null");
                    return diStatusArr;
                }
                //if (di4Bytes.Length != 4)
                //{
                //    Console.WriteLine($"[DI转换] 错误：原始数据长度={di4Bytes.Length}，必须为4字节");
                //    return diStatusArr;
                //}

                // 核心位运算转换
                for (int channel = 0; channel < diSize; channel++)
                {
                    int bytePos = channel / 8;   // 字节下标（0-3）
                    int bitPos = channel % 8;    // 位下标（0-7）
                                                 // 位运算：判断当前位是否为1 → true=接通，false=断开
                    diStatusArr[channel] = (di4Bytes[bytePos] & 1 << bitPos) != 0;
                }

            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DI转换] 异常：{ex.Message}，{ex.StackTrace}");
            }

            return diStatusArr;
        }
    }

}
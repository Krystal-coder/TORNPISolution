using TORNPISolution.GoogolEtherCAT.GEN;
using TORNPISolution.Share.Config.Cylinder;
using TORNPISolution.Share.Enum;

namespace TORNPISolution.GoogolEtherCAT.Cylinder
{
    public class CylinderEtherCAT
    {
        private GENControl _gENControl;
        private CylinderModel _cylinderModel;
        public static Dictionary<int, CylinderEtherCAT> CylinderModelDic = new Dictionary<int, CylinderEtherCAT>();
        private CylinderEtherCAT(GENControl gENControl, int cylinderId, CylinderModel CylinderModel)
        {
            _gENControl = gENControl;
            _cylinderModel = CylinderModel;
            CylinderModelDic.Add(cylinderId, this);
        }

        public static CylinderEtherCAT CreateInstance(GENControl gENControl, CylinderModel CylinderModel)
        {
            if (CylinderModelDic.ContainsKey(CylinderModel.id))
            {
                return CylinderModelDic[CylinderModel.id];
            }
            return new CylinderEtherCAT(gENControl, CylinderModel.id, CylinderModel);
        }
        public async Task<(bool, string, LevelEnum)> SetCylinder(short Value, CancellationToken token = default)
        {
            //var Count = 0;

            if (Value == 1)
            {
                if (_cylinderModel.InterlockingPointPosition != null)
                {
                    bool[]? blDIInput = _gENControl.ReadDIInputValue();
                    if (blDIInput == null)
                        return (false, $"【{_cylinderModel.Describe}】【读取数字量模块的输入值失败", LevelEnum.Info);
                    foreach (InterlockingPointPositionModel item in _cylinderModel.InterlockingPointPosition)
                    {
                        if (blDIInput[item.Chennel])
                        {
                            //Debug.WriteLine($"读取值通道{item.Chennel}扩展{item.IsExtend},值{Temp}");
                            return (false, $"【{_cylinderModel.Describe}】【{item.IsExtend}】【{item.Chennel}】通道未完成动作", LevelEnum.Info);
                        }
                    }
                }
            }

            if (_cylinderModel.IsCylinderEnabled)
            {
                _gENControl.WriteDOBitOutputValue((ushort)_cylinderModel.InputChannel, (byte)Value);//
                DateTime Dt = DateTime.Now;
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                linkedCts.CancelAfter(_cylinderModel.TimeOut);
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var Sensor = _cylinderModel?.Sensor?.Where(o => o.id == Value).First();
                    if (Sensor == null)
                    {
                        return (false, $"【{_cylinderModel?.Describe}】【{Sensor?.Describe}】通道未配置", LevelEnum.Info);
                    }
                    var sensor = _gENControl.ReadDIInputValue();
                    if (sensor != null)
                    {
                        if (sensor[Sensor.Channel] == true)
                        {
                            return (true, $"【{_cylinderModel?.Describe}】【{Sensor.Describe}】成功", LevelEnum.Info);
                        }
                        if ((DateTime.Now - Dt).TotalMilliseconds > _cylinderModel?.TimeOut)
                        {
                            return (false, $"【{_cylinderModel.Describe}】【{Sensor.Describe}】超时，请检查", LevelEnum.Error);
                        }
                    }
                    await Task.Delay(100);
                }
                return (false, $"【{_cylinderModel?.Describe}】终止！！！", LevelEnum.Error);
            }
            else
            {
                return (false, $"【{_cylinderModel.Describe}】未打开，请打开后测试", LevelEnum.Info);
            }
        }
        public (bool, string, LevelEnum) SetCylinder(short Value, bool IsExtend)
        {
            if (_gENControl.WriteDOBitOutputValue((ushort)_cylinderModel.InputChannel, (byte)Value))//_cylinderModel.IsCylinderEnabled
            {
                return (true, $"【{_cylinderModel.Describe}】成功", LevelEnum.Info);
            }
            else
            {
                return (false, $"【{_cylinderModel.Describe}】未打开，请打开后测试", LevelEnum.Info);
            }
        }

        public (double, string, LevelEnum) GetADInputValue(int type, int channel)
        {
            if (true)
            {
                double val = _gENControl.ReadADInputValue(type, channel);
                if (val > 0)
                {
                    return (val, $"【{_cylinderModel.Describe}】成功", LevelEnum.Info);
                }
                return (val, $"【{_cylinderModel.Describe}】读取异常，", LevelEnum.Error);
            }


        }
    }
}

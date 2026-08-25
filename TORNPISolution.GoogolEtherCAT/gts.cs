using System;
using System.Runtime.InteropServices;

namespace TORNPISolution.GoogolEtherCAT
{
    public class mc
    {
        public const short CHANNEL_HOST = 0;
        public const short CHANNEL_UART = 1;
        public const short CHANNEL_SIM = 2;
        public const short CHANNEL_ETHER = 3;
        public const short CHANNEL_RS232 = 4;
        public const short CHANNEL_PCIE = 5;

        /*-----------------------------------------------------------*/
        /* �������                                                  */
        /*-----------------------------------------------------------*/

        public const short CMD_SUCCESS = 0;

        public const short CMD_ERROR_READ_LEN = -2;   /* ��ȡ���ݳ��ȴ��� */
        public const short CMD_ERROR_READ_CHECKSUM = -3;   /* ��ȡ����У��ʹ��� */

        public const short CMD_ERROR_WRITE_BLOCK = -4;   /* д�����ݿ���� */
        public const short CMD_ERROR_READ_BLOCK = -5;   /* ��ȡ���ݿ���� */

        public const short CMD_ERROR_OPEN = -6;   /* ���豸���� */
        public const short CMD_ERROR_CLOSE = -6;   /* �ر��豸���� */
        public const short CMD_ERROR_DSP_BUSY = -7;    /* DSPæ */

        public const short CMD_LOCK_ERROR = -8;  /*���߳���Դæ*/

        public const short CMD_ERROR_EXECUTE = 1;
        public const short CMD_ERROR_VERSION_NOT_MATCH = 3;
        public const short CMD_ERROR_PARAMETER = 7;
        public const short CMD_ERROR_UNKNOWN = 8;   /* ��֧�ֵ�ָ�� */


        public const short MC_NONE = -1;

        public const short MC_LIMIT_POSITIVE = 0;
        public const short MC_LIMIT_NEGATIVE = 1;
        public const short MC_ALARM = 2;
        public const short MC_HOME = 3;
        public const short MC_GPI = 4;
        public const short MC_ARRIVE = 5;
        //      public const short MC_EGPI0     =(6);
        //      public const short MC_EGPI1     = (7);
        //      public const short MC_EGPI2     =(8);
        public const short MC_MPG = 9;

        public const short MC_ENABLE = 10;
        public const short MC_CLEAR = 11;
        public const short MC_GPO = 12;
        //      public const short MC_EGPO0      =                  (13);
        //      public const short MC_EGPO1      =                  (14);

        public const short MC_HSO = 18;

        public const short MC_DAC = 20;
        public const short MC_STEP = 21;
        public const short MC_PULSE = 22;
        public const short MC_ENCODER = 23;
        public const short MC_ADC = 24;
        public const short MC_AU_ADC = 17;
        public const short MC_AU_ENCODER = 26;

        public const short MC_ABS_ENCODER = 29;

        public const short MC_AXIS = 30;
        public const short MC_PROFILE = 31;
        public const short MC_CONTROL = 32;

        public const short MC_TRIGGER = 40;

        public const short MC_TERMINAL = 50;

        public const short MC_EXT_MODULE = 60;
        public const short MC_EXT_DI = 61;
        public const short MC_EXT_DO = 62;
        public const short MC_EXT_AI = 63;
        public const short MC_EXT_AO = 64;

        public const short MC_SCAN_CRD = 70;

        public const short MC_POS_COMPARE = 80;

        public const short MC_WATCH_VAR = 200;
        public const short MC_WATCH_EVENT = 201;

        public struct TVersion
        {
            public short year;
            public short month;
            public short day;
            public short version;
            public short user;
            public short reserve1;
            public short reserve2;
            public short chip;
        }
        public const short CORE_MODE_TIMER = 0;
        public const short CORE_MODE_SYNCH = 1;
        public const short CORE_MODE_EXTERNAL = 2;

        public const short CORE_TASK_DEFAULT = 0;
        public const short CORE_TASK_DLM = 1;

        public const short SKIP_MODULE_SCAN = 0x001;
        public const short SKIP_MODULE_POS_COMPARE = 0x002;
        public const short SKIP_MODULE_CRD = 0x004;

        public const short SKIP_MODULE_PLC = 0x010;
        public const short SKIP_MODULE_DLM = 0x020;

        public const short SKIP_MODULE_AXIS_CALCULATE = 0x100;

        public const short SKIP_MODULE_WATCH = 0x800;

        public enum ETimeElapse
        {
            TIME_ELAPSE_PROFILE = 1000,

            TIME_ELAPSE_HOST_COMMAND_EXECUTE = 1220,
            TIME_ELAPSE_ETHER_COMMAND_EXECUTE,

            TIME_ELAPSE_PROFILE_CALCULATE = 6000,

            TIME_ELAPSE_SCAN = 18000,

            TIME_ELAPSE_AXIS_CHECK = 20000,
            TIME_ELAPSE_AXIS_CALCULATE,

            TIME_ELAPSE_ENCODER = 30000,

            TIME_ELAPSE_DI = 31000,

            TIME_ELAPSE_DO = 32000,

            TIME_ELAPSE_AI = 33000,

            TIME_ELAPSE_AO = 34000,

            TIME_ELAPSE_TRIGGER = 38000,

            TIME_ELAPSE_CONTROL = 40000,

            TIME_ELAPSE_WATCH = 52000,

            TIME_ELAPSE_TERMINAL = 53000,
            TIME_ELAPSE_TERMINALDET = 54000,

        }

        public struct TTerminalStatus
        {
            public ushort type;
            public short id;
            public int status;
            public uint synchCount;
            public uint ringNetType;//��������
            public uint coreStatus;//ģ��˿�״̬
            public uint scoreDropCount;//ģ��SPORT����ʧ����
            public uint reserve1;
            public uint reserve2;
            public uint reserve3;
            public uint reserve4;
            public uint reserve5;
            public uint reserve6;
            public uint reserve7;
        }

        public struct TPid
        {
            public double kp;
            public double ki;
            public double kd;
            public double kvff;
            public double kaff;
            public int IntegralLimit;
            public int derivativeLimit;
            public short limit;
        }
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtModuleCount(short core, short station, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAuDac(short core, short dac, ref short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuDac(short core, short dac, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuAdcValue(short core, short adc, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuAdc(short core, short adc, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_SetGPIOConfig(short core, short station, int effectiveLevel, int direction);
        [DllImport("gts.dll")]
        public static extern short GT_SetCardNo(short index);
        [DllImport("gts.dll")]
        public static extern short GT_GetCardNo(out short index);
        [DllImport("gts.dll")]
        public static extern short GT_Open(short channel, short param);
        [DllImport("gts.dll")]
        public static extern short GT_OpenAllDevice(short channel, short param, ref string pFile, ref short pDeviceCount);
        [DllImport("gts.dll")]
        public static extern short GT_Close();
        [DllImport("gts.dll")]
        public static extern short GT_SetCore(short core);
        [DllImport("gts.dll")]
        public static extern short GT_GetCore(out short core);
        [DllImport("gts.dll")]
        public static extern short GT_GetChannel(out short pChannel);
        [DllImport("gts.dll")]
        public static extern short GT_GetVersion(out string pVersion);
        [DllImport("gts.dll")]
        public static extern short GT_GetVersionEx(short type, out TVersion pVersion);
        [DllImport("gts.dll")]
        public static extern short GT_SetVersion(short type, ref TVersion pVersion);
        [DllImport("gts.dll")]
        public static extern short GT_Reset();
        [DllImport("gts.dll")]
        public static extern short GT_GetClock(out uint pClock, out uint pLoop);
        [DllImport("gts.dll")]
        public static extern short GT_GetClockHighPrecision(out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_ClearTime(ETimeElapse item);
        [DllImport("gts.dll")]
        public static extern short GT_GetTime(ETimeElapse item, out uint pTime, out uint pTimeMax, out uint pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetResMax(short core, short type, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_SetResCount(short type, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetResCount(short core, short type, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetSts(short axis, out int pSts, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_ClrSts(short axis, short count);
        [DllImport("gts.dll")]
        public static extern short GT_AxisOn(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_AxisOff(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_MultiAxisOn(uint mask);
        [DllImport("gts.dll")]
        public static extern short GT_MultiAxisOff(uint mask);
        [DllImport("gts.dll")]
        public static extern short GT_SetAxisOnDelayTime(ushort ms);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisOnDelayTime(out ushort pMs);
        [DllImport("gts.dll")]
        public static extern short GT_Stop(int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GT_SetPrfPos(short profile, int prfPos);
        [DllImport("gts.dll")]
        public static extern short GT_SynchAxisPos(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_ZeroPos(short axis, short count);
        [DllImport("gts.dll")]
        public static extern short GT_SynchPrfPosWithEncPos(short profile, short encoder);
        [DllImport("gts.dll")]
        public static extern short GT_GetLimitStatus(short axis, out short pLimitPositive, out short pLimitNegative);
        [DllImport("gts.dll")]
        public static extern short GT_SetSoftLimitMode(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetSoftLimitMode(short axis, out short pMode);
        [DllImport("gts.dll")]
        public static extern short GT_SetSoftLimit(short axis, int positive, int negative);
        [DllImport("gts.dll")]
        public static extern short GT_GetSoftLimit(short axis, out int pPositive, out int pNegative);

        [DllImport("gts.dll")]
        public static extern short GT_SetAxisBand(short axis, int band, int time);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisBand(short axis, out int pBand, out int pTime);
        [DllImport("gts.dll")]
        public static extern short GT_GetPrfPos(short profile, out double Value, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetPrfVel(short profile, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetPrfAcc(short profile, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetPrfMode(short profile, out uint pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisPrfPos(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisPrfPosCompensate(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisPrfVel(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisPrfAcc(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisEncPos(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisEncVel(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisEncAcc(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisError(short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_SetControlFilter(short control, short index);
        [DllImport("gts.dll")]
        public static extern short GT_GetControlFilter(short control, out short pIndex);
        [DllImport("gts.dll")]
        public static extern short GT_SetControlSuperimposed(short control, short superimposedType, short superimposedIndex);
        [DllImport("gts.dll")]
        public static extern short GT_GetControlSuperimposed(short control, out short pSuperimposedType, out short pSuperimposedIndex);
        [DllImport("gts.dll")]
        public static extern short GT_SetPid(short control, short index, ref TPid pPid);
        [DllImport("gts.dll")]
        public static extern short GT_GetPid(short control, short index, out TPid pPid);
        [DllImport("gts.dll")]
        public static extern short GT_SetKvffFilter(short control, short index, short kvffFilterExp, double accMax);
        [DllImport("gts.dll")]
        public static extern short GT_GetKvffFilter(short control, short index, out short pKvffFilterExp, out double pAccMax);
        [DllImport("gts.dll")]
        public static extern short GT_Delay(ushort ms);
        [DllImport("gts.dll")]
        public static extern short GT_DelayHighPrecision(ushort profile);
        [DllImport("gts.dll")]
        public static extern short GTN_Open(short channel, short param);
        [DllImport("gts.dll")]
        public static extern short GTN_OpenRingNet(short channel, short param, ref string pFile, short index, int count);
        [DllImport("gts.dll")]
        public static extern short GTN_Close();
        [DllImport("gts.dll")]
        public static extern short GTN_GetChannel(out short pChannel);
        [DllImport("gts.dll")]
        public static extern short GTN_GetVersion(short core, out string pVersion);
        [DllImport("gts.dll")]
        public static extern short GTN_GetVersionEx(short core, short type, out TVersion pVersion);
        [DllImport("gts.dll")]
        public static extern short GTN_SetVersion(short core, short type, ref TVersion pVersion);
        [DllImport("gts.dll")]
        public static extern short GTN_Reset(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_GetClock(short core, out uint pClock, out uint pLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetClockHighPrecision(short core, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearTime(short core, ETimeElapse item);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTime(short core, ETimeElapse item, out uint pTime, out uint pTimeMax, out uint pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCoreMode(short core, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCoreMode(short core, out short pMode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCoreShare(short core, short type, short index, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCoreShare(short core, short type, out short pIndex, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCoreTask(short core, short task);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCoreTask(short core, out short pTask);
        [DllImport("gts.dll")]
        public static extern short GTN_GetResMax(short core, short type, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_SetResCount(short core, short type, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetResCount(short core, short type, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetSts(short core, short axis, out int pSts, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_ClrSts(short core, short axis, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_AxisOn(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_AxisOff(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_MultiAxisOn(short core, uint mask);
        [DllImport("gts.dll")]
        public static extern short GTN_MultiAxisOff(short core, uint mask);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAxisOnDelayTime(short core, ushort delayTime);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisOnDelayTime(short core, out ushort pDelayTime);
        [DllImport("gts.dll")]
        public static extern short GTN_Stop(short core, int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPrfPos(short core, short profile, int prfPos);
        [DllImport("gts.dll")]
        public static extern short GTN_SynchAxisPos(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_ZeroPos(short core, short axis, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SynchPrfPosWithEncPos(short core, short profile, short encoder);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLimitStatus(short core, short axis, out short pLimitPositive, out short pLimitNegative);
        [DllImport("gts.dll")]
        public static extern short GTN_SetSoftLimitMode(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetSoftLimitMode(short core, short axis, out short pMode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetSoftLimit(short core, short axis, int positive, int negative);
        [DllImport("gts.dll")]
        public static extern short GTN_GetSoftLimit(short core, short axis, out int pPositive, out int pNegative);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAxisBand(short core, short axis, int band, int time);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisBand(short core, short axis, out int pBand, out int pTime);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPrfPos(short core, short profile, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPrfVel(short core, short profile, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPrfAcc(short core, short profile, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPrfMode(short core, short profile, out int pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisPrfPos(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisPrfPosCompensate(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisPrfVel(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisPrfAcc(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisEncPos(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisEncVel(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisEncAcc(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisError(short core, short axis, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_SetControlFilter(short core, short control, short index);
        [DllImport("gts.dll")]
        public static extern short GTN_GetControlFilter(short core, short control, out short pIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_SetControlSuperimposed(short core, short control, short superimposedType, short superimposedIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_GetControlSuperimposed(short core, short control, out short pSuperimposedType, out short pSuperimposedIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPid(short core, short control, short index, ref TPid pPid);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPid(short core, short control, short index, out TPid pPid);
        [DllImport("gts.dll")]
        public static extern short GTN_SetKvffFilter(short core, short control, short index, short kvffFilterExp, double accMax);
        [DllImport("gts.dll")]
        public static extern short GTN_GetKvffFilter(short core, short control, short index, out short pKvffFilterExp, out double pAccMax);
        [DllImport("gts.dll")]
        public static extern short GTN_Delay(short core, ushort ms);
        [DllImport("gts.dll")]
        public static extern short GTN_DelayHighPrecision(short core, ushort profile);

        public const short STEP_DIR = 0;
        public const short STEP_PULSE = 1;
        public const short STEP_ORTHOGONAL = 2;

        [DllImport("gts.dll")]
        public static extern short GT_LoadConfig(ref string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_SaveConfig(string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_LoadTerminalConfig(short core, ref string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_SaveTerminalConfig(short core, out string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_LoadExtModuleConfig(short core, ref string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_SaveExtModuleConfig(short core, out string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_AlarmOff(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_AlarmOn(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_LmtsOn(short axis, short limitType);
        [DllImport("gts.dll")]
        public static extern short GT_LmtsOff(short axis, short limitType);
        [DllImport("gts.dll")]
        public static extern short GT_StepDir(short step);
        [DllImport("gts.dll")]
        public static extern short GT_StepPulse(short step);
        [DllImport("gts.dll")]
        public static extern short GT_StepOrthogonal(short step);
        [DllImport("gts.dll")]
        public static extern short GT_SetMtrBias(short dac, short bias);
        [DllImport("gts.dll")]
        public static extern short GT_GetMtrBias(short dac, out short pBias);
        [DllImport("gts.dll")]
        public static extern short GT_SetMtrLmt(short dac, short limit);
        [DllImport("gts.dll")]
        public static extern short GT_GetMtrLmt(short dac, out short pLimit);
        [DllImport("gts.dll")]
        public static extern short GT_SetSense(short dataType, short dataIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GT_GetSense(short dataType, short dataIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GT_EncOn(short encoder);
        [DllImport("gts.dll")]
        public static extern short GT_EncOff(short encoder);
        [DllImport("gts.dll")]
        public static extern short GT_SetPosErr(short control, int error);
        [DllImport("gts.dll")]
        public static extern short GT_GetPosErr(short control, out int pError);
        [DllImport("gts.dll")]
        public static extern short GT_SetStopDec(short profile, double decSmoothStop, double decAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GT_GetStopDec(short profile, out double pDecSmoothStop, out double pDecAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GT_CtrlMode(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_SetStopIo(short axis, short stopType, short inputType, short inputIndex);
        [DllImport("gts.dll")]
        public static extern short GT_SetAdcFilterPrm(short adc, double k);
        [DllImport("gts.dll")]
        public static extern short GT_GetAdcFilterPrm(short adc, out double pk);
        [DllImport("gts.dll")]
        public static extern short GT_SetAxisPrfVelFilter(short axis, short filterNumExp);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisPrfVelFilter(short axis, out short pFilterNumExp);
        [DllImport("gts.dll")]
        public static extern short GT_SetAxisEncVelFilter(short axis, short filterNumExp);
        [DllImport("gts.dll")]
        public static extern short GT_GetAxisEncVelFilter(short axis, out short pFilterNumExp);
        [DllImport("gts.dll")]
        public static extern short GT_SetProfileScale(short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GT_GetProfileScale(short i, out int pAlhpa, out int pBeta);
        [DllImport("gts.dll")]
        public static extern short GT_SetEncoderScale(short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GT_GetEncoderScale(short i, out int pAlhpa, out int pBeta);
        [DllImport("gts.dll")]
        public static extern short GT_SetAuEncoderScale(short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GT_GetAuEncoderScale(short i, out int pAlhpa, out int pBeta);
        [DllImport("gts.dll")]
        public static extern short GTN_LoadConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_SaveConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_LoadTerminalConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_SaveTerminalConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_LoadExtModuleConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_SaveExtModuleConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_AlarmOn(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_AlarmOff(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_LmtsOn(short core, short axis, short limitType);
        [DllImport("gts.dll")]
        public static extern short GTN_LmtsOff(short core, short axis, short limitType);
        [DllImport("gts.dll")]
        public static extern short GTN_StepDir(short core, short step);
        [DllImport("gts.dll")]
        public static extern short GTN_StepPulse(short core, short step);
        [DllImport("gts.dll")]
        public static extern short GTN_StepOrthogonal(short core, short step);
        [DllImport("gts.dll")]
        public static extern short GTN_SetMtrBias(short core, short dac, short bias);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMtrBias(short core, short dac, out short pBias);
        [DllImport("gts.dll")]
        public static extern short GTN_SetMtrLmt(short core, short dac, short limit);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMtrLmt(short core, short dac, out short pLimit);
        [DllImport("gts.dll")]
        public static extern short GTN_SetSense(short core, short dataType, short dataIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GTN_GetSense(short core, short dataType, short dataIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EncOn(short core, short encoder);
        [DllImport("gts.dll")]
        public static extern short GTN_EncOff(short core, short encoder);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPosErr(short core, short control, int error);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPosErr(short core, short control, out int pError);
        [DllImport("gts.dll")]
        public static extern short GTN_SetStopDec(short core, short profile, double decSmoothStop, double decAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetStopDec(short core, short profile, out double pDecSmoothStop, out double pDecAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GTN_CtrlMode(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetStopIo(short core, short axis, short stopType, short inputType, short inputIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAdcFilterPrm(short core, short adc, double k);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAdcFilterPrm(short core, short adc, out double pk);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAxisPrfVelFilter(short core, short axis, short filterNumExp);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisPrfVelFilter(short core, short axis, out short pFilterNumExp);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAxisEncVelFilter(short core, short axis, short filterNumExp);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAxisEncVelFilter(short core, short axis, out short pFilterNumExp);
        [DllImport("gts.dll")]
        public static extern short GTN_SetProfileScale(short core, short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GTN_GetProfileScale(short core, short i, out int pAlhpa, out int pBeta);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEncoderScale(short core, short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEncoderScale(short core, short i, out int pAlhpa, out int pBeta);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAuEncoderScale(short core, short i, int alpha, int beta);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuEncoderScale(short core, short i, out int pAlhpa, out int pBeta);

        public const short CAPTURE_HOME = 1;
        public const short CAPTURE_INDEX = 2;
        public const short CAPTURE_PROBE = 3;
        public const short CAPTURE_HSIO0 = 6;
        public const short CAPTURE_HSIO1 = 7;

        public struct TTrigger
        {
            public short encoder;
            public short probeType;
            public short probeIndex;
            public short sense;
            public int offset;
            public uint loop;
            public short windowOnly;
            public int firstPosition;
            public int lastPosition;
        }

        public struct TTriggerAlign
        {
            public short encoder;
            public short probeType;
            public short probeIndex;
            public short sense;
            public int offset;
            public uint loop;
            public short windowOnly;
            public short pad2;
            public int firstPosition;
            public int lastPosition;
        }

        public struct TTriggerStatus
        {
            public short execute;
            public short done;
            public int position;
        }

        public struct TTriggerStatusEx
        {
            public short execute;
            public short done;
            public int position;
            public uint clock;
            public uint loopCount;
        }
        [DllImport("gts.dll")]
        public static extern short GT_SetTrigger(short i, ref TTrigger pTrigger);
        [DllImport("gts.dll")]
        public static extern short GT_GetTrigger(short i, out TTrigger pTrigger);
        [DllImport("gts.dll")]
        public static extern short GT_GetTriggerStatus(short i, out TTriggerStatus pTriggerStatus, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetTriggerStatusEx(short i, out TTriggerStatusEx pTriggerStatusEx, short count);
        [DllImport("gts.dll")]
        public static extern short GT_ClearTriggerStatus(short i);
        [DllImport("gts.dll")]
        public static extern short GT_SetCaptureMode(short encoder, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetCaptureMode(short encoder, out short pMode, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetCaptureStatus(short encoder, out short pStatus, out int pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_SetCaptureSense(short encoder, short mode, short sense);
        [DllImport("gts.dll")]
        public static extern short GT_ClearCaptureStatus(short encoder);
        [DllImport("gts.dll")]
        public static extern short GT_SetCaptureRepeat(short encoder, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetCaptureRepeatStatus(short encoder, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetCaptureRepeatPos(short encoder, out int pValue, short startNum, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTrigger(short core, short i, ref TTrigger pTrigger);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTrigger(short core, short i, out TTrigger pTrigger);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTriggerStatus(short core, short i, out TTriggerStatus pTriggerStatus, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTriggerStatusEx(short core, short i, out TTriggerStatusEx pTriggerStatusEx, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearTriggerStatus(short core, short i);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCaptureMode(short core, short encoder, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCaptureMode(short core, short encoder, out short pMode, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCaptureStatus(short core, short encoder, out short pStatus, out int pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCaptureSense(short core, short encoder, short mode, short sense);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearCaptureStatus(short core, short encoder);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCaptureRepeat(short core, short encoder, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCaptureRepeatStatus(short core, short encoder, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCaptureRepeatPos(short core, short encoder, out int pValue, short startNum, short count);

        public const short TRIGGER_DELTA_MODE_DEFAULT = 0;
        public const short TRIGGER_DELTA_CHECKPOINT_MODE_AUTO = 0;
        public const short TRIGGER_DELTA_CHECKPOINT_MODE_MANUAL = 1;
        public struct TCheckpoint
        {
            public short mode;
            public int offset;
            public short fifoIndex;
            public uint crossCount;
            public short fifoDataCount;
            public short dataReady;
            public int data;
            public short dataIndex;
        }
        public struct TTriggerDeltaPrm
        {
            public short mode;
            public short dir;
            public short triggerIndex0;
            public short triggerIndex1;
            public short triggerIndex2;
        }

        public struct TTriggerDeltaInfo
        {
            public short enable;
            public short checkpointCount;
            public short fifoDataCount;
            public short lostCount;
        }
        [DllImport("gts.dll")]
        public static extern short GT_ClearTriggerDelta(short index, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_AddTriggerDeltaCheckpoint(short index, short mode, int offset, short fifo, ref short pIndex);
        [DllImport("gts.dll")]
        public static extern short GT_ReadTriggerDeltaCheckpointData(short index, short checkpointIndex, out int pBuf, short count, out short pReadCount);
        [DllImport("gts.dll")]
        public static extern short GT_WriteTriggerDeltaCheckpointData(short index, short checkpointIndex, ref int pBuf, short count, ref short pWriteCount);
        [DllImport("gts.dll")]
        public static extern short GT_SetTriggerDeltaPrm(short index, ref TTriggerDeltaPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetTriggerDeltaPrm(short index, out TTriggerDeltaPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetTriggerDeltaCheckpoint(short index, short checkpointIndex, out TCheckpoint pCheckpoint);
        [DllImport("gts.dll")]
        public static extern short GT_GetTriggerDeltaInfo(short index, out TTriggerDeltaInfo pTriggerDelta);
        [DllImport("gts.dll")]
        public static extern short GT_TriggerDeltaOn(short index);
        [DllImport("gts.dll")]
        public static extern short GT_TriggerDeltaOff(short index);
        [DllImport("gts.dll")]

        public static extern short GTN_ClearTriggerDelta(short core, short index, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_AddTriggerDeltaCheckpoint(short core, short index, short mode, int offset, short fifo, ref short pIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_ReadTriggerDeltaCheckpointData(short core, short index, short checkpointIndex, out int pBuf, short count, out short pReadCount);
        [DllImport("gts.dll")]
        public static extern short GTN_WriteTriggerDeltaCheckpointData(short core, short index, short checkpointIndex, ref int pBuf, short count, ref short pWriteCount);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTriggerDeltaPrm(short core, short index, ref TTriggerDeltaPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTriggerDeltaPrm(short core, short index, out TTriggerDeltaPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTriggerDeltaCheckpoint(short core, short index, short checkpointIndex, out TCheckpoint pCheckpoint);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTriggerDeltaInfo(short core, short index, out TTriggerDeltaInfo pTriggerDelta);
        [DllImport("gts.dll")]
        public static extern short GTN_TriggerDeltaOn(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GTN_TriggerDeltaOff(short core, short index);


        public struct TTrapPrm
        {
            public double acc;
            public double dec;
            public double velStart;
            public short smoothTime;
        }
        [DllImport("gts.dll")]
        public static extern short GT_Update(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_SetPos(short profile, int pos);
        [DllImport("gts.dll")]
        public static extern short GT_GetPos(short profile, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GT_SetVel(short profile, double vel);
        [DllImport("gts.dll")]
        public static extern short GT_GetVel(short profile, out double pVel);
        [DllImport("gts.dll")]

        public static extern short GT_PrfTrap(short profile);
        [DllImport("gts.dll")]
        public static extern short GT_SetTrapPrm(short profile, ref TTrapPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetTrapPrm(short profile, out TTrapPrm pPrm);
        [DllImport("gts.dll")]

        public static extern short GTN_Update(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPos(short core, short profile, int pos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPos(short core, short profile, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GTN_SetVel(short core, short profile, double vel);
        [DllImport("gts.dll")]
        public static extern short GTN_GetVel(short core, short profile, out double pVel);
        [DllImport("gts.dll")]

        public static extern short GTN_PrfTrap(short core, short profile);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTrapPrm(short core, short profile, ref TTrapPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTrapPrm(short core, short profile, out TTrapPrm pPrm);


        public struct TJogPrm
        {
            public double acc;
            public double dec;
            public double smooth;
        }
        [DllImport("gts.dll")]
        public static extern short GT_PrfJog(short profile);
        [DllImport("gts.dll")]
        public static extern short GT_SetJogPrm(short profile, ref TJogPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetJogPrm(short profile, out TJogPrm pPrm);
        [DllImport("gts.dll")]

        public static extern short GTN_PrfJog(short core, short profile);
        [DllImport("gts.dll")]
        public static extern short GTN_SetJogPrm(short core, short profile, ref TJogPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetJogPrm(short core, short profile, out TJogPrm pPrm);

        public const short PT_MODE_STATIC = 0;
        public const short PT_MODE_DYNAMIC = 1;

        public const short PT_SEGMENT_NORMAL = 0;
        public const short PT_SEGMENT_EVEN = 1;
        public const short PT_SEGMENT_STOP = 2;

        public struct TPtInfo
        {
            public double prfPos;
            public int loop;
            public short mode;
            public short fifoUse;
            public short fifoPlace;
            public short segmentNumber;
            public uint segmentReceive1;
            public uint segmentReceive2;
            public uint segmentExecute1;
            public uint segmentExecute2;
            public uint bufferReceive1;
            public uint bufferReceive2;
            public uint bufferExecute1;
            public uint bufferExecute2;
        }
        [DllImport("gts.dll")]
        public static extern short GT_PrfPt(short profile, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_SetPtLoop(short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GT_GetPtLoop(short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GT_PtSpace(short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_PtSpaceEx(short profile, out short pSpace, out short pListSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_PtData(short profile, double pos, int time, short type);
        [DllImport("gts.dll")]
        public static extern short GT_PtClear(short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_PtStart(int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GT_SetPtMemory(short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GT_GetPtMemory(short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GT_SetPtPrecisionMode(short profile, short precisionMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetPtPrecisionMode(short profile, out short pPrecisionMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetPtInfo(short profile, out TPtInfo pPtInfo);
        [DllImport("gts.dll")]
        public static extern short GT_SetPtLink(short profile, short fifo, short list);
        [DllImport("gts.dll")]
        public static extern short GT_GetPtLink(short profile, short fifo, out short pList);
        [DllImport("gts.dll")]
        public static extern short GT_PtDoBit(short profile, short doType, short index, short value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_PtAo(short profile, short aoType, short index, double value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PrfPt(short core, short profile, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPtLoop(short core, short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPtLoop(short core, short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_PtSpace(short core, short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PtSpaceEx(short core, short profile, out short pSpace, out short pListSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PtData(short core, short profile, double pos, int time, short type, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PtClear(short core, short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PtStart(short core, int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPtMemory(short core, short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPtMemory(short core, short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPtPrecisionMode(short core, short profile, short precisionMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPtPrecisionMode(short core, short profile, out short pPrecisionMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPtInfo(short core, short profile, out TPtInfo pPtInfo);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPtLink(short core, short profile, short fifo, short list);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPtLink(short core, short profile, short fifo, out short pList);
        [DllImport("gts.dll")]
        public static extern short GTN_PtDoBit(short core, short profile, short doType, short index, short value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PtAo(short core, short profile, short aoType, short index, double value, short fifo);



        public struct TPvtTableMovePrm
        {
            public short tableId;
            public int distance;
            public double vm;
            public double am;
            public double jm;
            public double time;
        }
        [DllImport("gts.dll")]
        public static extern short GT_PrfPvt(short profile);
        [DllImport("gts.dll")]
        public static extern short GT_SetPvtLoop(short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GT_GetPvtLoop(short profile, out int pLoopCount, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GT_PvtStatus(short profile, out short pTableId, out double pTime, short count);
        [DllImport("gts.dll")]
        public static extern short GT_PvtStart(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableSelect(short profile, short tableId);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTable(short tableId, int count, ref double pTime, ref double pPos, ref double pVel);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableEx(short tableId, int count, ref double pTime, ref double pPos, ref double pVelBegin, ref double pVelEnd);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableComplete(short tableId, int count, ref double pTime, ref double pPos, ref double pA, ref double pB, ref double pC, double velBegin, double velEnd);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTablePercent(short tableId, int count, ref double pTime, ref double pPos, ref double pPercent, double velBegin);
        [DllImport("gts.dll")]
        public static extern short GT_PvtPercentCalculate(int n, ref double pTime, ref double pPos, ref double pPercent, double velBegin, ref double pVel);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableContinuous(short tableId, int count, ref double pPos, ref double pVel, ref double pPercent, ref double pVelMax, ref double pAcc, ref double pDec, double timeBegin);
        [DllImport("gts.dll")]
        public static extern short GT_PvtContinuousCalculate(int n, ref double pPos, ref double pVel, ref double pPercent, ref double pVelMax, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableMovePercent(short tableId, int distance, double vm, double acc, double pa1, double pa2, double dec, double pd1, double pd2, ref double pVel, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableMove(short tableId, int distance, double vm, double am, double jm, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GT_PvtTableMovePercentEx(short tableId, int distance, double vm,
        double acc, double pa1, double pa2, double ma,
        double dec, double pd1, double pd2, double md,
        ref double pVel, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]

        public static extern short GT_PvtTableMoveTogether(short tableCount, ref TPvtTableMovePrm pPvtTableMovePrm);
        [DllImport("gts.dll")]

        public static extern short GTN_PrfPvt(short core, short profile);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPvtLoop(short core, short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPvtLoop(short core, short profile, out int pLoopCount, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtStatus(short core, short profile, out short pTableId, out double pTime, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtStart(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableSelect(short core, short profile, short tableId);
        [DllImport("gts.dll")]

        public static extern short GTN_PvtTable(short core, short tableId, int count, ref double pTime, ref double pPos, ref double pVel);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableEx(short core, short tableId, int count, ref double pTime, ref double pPos, ref double pVelBegin, ref double pVelEnd);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableComplete(short core, short tableId, int count, ref double pTime, ref double pPos, ref double pA, ref double pB, ref double pC, double velBegin, double velEnd);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTablePercent(short core, short tableId, int count, ref double pTime, ref double pPos, ref double pPercent, double velBegin);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtPercentCalculate(short core, int n, ref double pTime, ref double pPos, ref double pPercent, double velBegin, ref double pVel);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableContinuous(short core, short tableId, int count, ref double pPos, ref double pVel, ref double pPercent, ref double pVelMax, ref double pAcc, ref double pDec, double timeBegin);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtContinuousCalculate(short core, int n, ref double pPos, ref double pVel, ref double pPercent, ref double pVelMax, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableMove(short core, short tableId, int distance, double vm, double am, double jm, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableMovePercent(short core, short tableId, int distance, double vm,
        double acc, double pa1, double pa2,
        double dec, double pd1, double pd2,
        ref double pVel, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableMovePercentEx(short core, short tableId, int distance, double vm,
        double acc, double pa1, double pa2, double ma,
        double dec, double pd1, double pd2, double md,
        ref double pVel, ref double pAcc, ref double pDec, ref double pTime);
        [DllImport("gts.dll")]
        public static extern short GTN_PvtTableMoveTogether(short core, short tableCount, ref TPvtTableMovePrm pPvtTableMovePrm);
        public const short GEAR_MASTER_ENCODER = 1;
        public const short GEAR_MASTER_PROFILE = 2;
        public const short GEAR_MASTER_AXIS = 3;
        public const short GEAR_MASTER_AU_ENCODER = 4;

        public const short GEAR_MASTER_ENCODER_OTHER = 101;
        public const short GEAR_MASTER_AXIS_OTHER = 103;

        public const short GEAR_EVENT_START = 1;
        public const short GEAR_EVENT_PASS = 2;
        public const short GEAR_EVENT_AREA = 5;
        [DllImport("gts.dll")]
        public static extern short GT_PrfGear(short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GT_SetGearMaster(short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GT_GetGearMaster(short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GT_SetGearRatio(short profile, int masterEven, int slaveEven, int masterSlope);
        [DllImport("gts.dll")]
        public static extern short GT_GetGearRatio(short profile, out int pMasterEven, out int pSlaveEven, out int pMasterSlope);
        [DllImport("gts.dll")]
        public static extern short GT_GearStart(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_SetGearEvent(short profile, short gearevent, int startPara0, int startPara1);
        [DllImport("gts.dll")]
        public static extern short GT_GetGearEvent(short profile, out short pEvent, out int pStartPara0, out int pStartPara1);
        [DllImport("gts.dll")]

        public static extern short GTN_PrfGear(short core, short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GTN_SetGearMaster(short core, short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_GetGearMaster(short core, short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_SetGearRatio(short core, short profile, int masterEven, int slaveEven, int masterSlope);
        [DllImport("gts.dll")]
        public static extern short GTN_GetGearRatio(short core, short profile, out int pMasterEven, out int pSlaveEven, out int pMasterSlope);
        [DllImport("gts.dll")]
        public static extern short GTN_GearStart(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_SetGearEvent(short core, short profile, short gearevent, int startPara0, int startPara1);
        [DllImport("gts.dll")]
        public static extern short GTN_GetGearEvent(short core, short profile, out short pEvent, out int pStartPara0, out int pStartPara1);
        public const short FOLLOW_SWITCH_SEGMENT = 1;
        public const short FOLLOW_SWITCH_TABLE = 2;

        public const short FOLLOW_MASTER_ENCODER = 1;
        public const short FOLLOW_MASTER_PROFILE = 2;
        public const short FOLLOW_MASTER_AXIS = 3;
        public const short FOLLOW_MASTER_AU_ENCODER = 4;

        public const short FOLLOW_MASTER_ENCODER_OTHER = 101;
        public const short FOLLOW_MASTER_AXIS_OTHER = 103;

        public const short FOLLOW_EVENT_START = 1;
        public const short FOLLOW_EVENT_PASS = 2;

        public const short FOLLOW_SEGMENT_NORMAL = 0;
        public const short FOLLOW_SEGMENT_EVEN = 1;
        public const short FOLLOW_SEGMENT_STOP = 2;
        public const short FOLLOW_SEGMENT_CONTINUE = 3;
        [DllImport("gts.dll")]
        public static extern short GT_PrfFollow(short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowMaster(short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowMaster(short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowLoop(short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowLoop(short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowEvent(short profile, short followevent, short masterDir, int pos);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowEvent(short profile, out short pEvent, out short pMasterDir, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GT_FollowSpace(short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowData(short profile, int masterSegment, double slaveSegment, short type, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowClear(short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowStart(int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GT_FollowSwitch(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowMemory(short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowMemory(short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowStatus(short profile, out short pFifoNum, out short pSwitchStatus);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowPhasing(short profile, short profilePhasing);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowPhasing(short profile, out short pProfilePhasing);
        [DllImport("gts.dll")]

        public static extern short GT_PrfFollowEx(short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowMasterEx(short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowMasterEx(short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowLoopEx(short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowLoopEx(short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowEventEx(short profile, short followevent, short masterDir, int pos);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowEventEx(short profile, out short pEvent, out short pMasterDir, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GT_FollowSpaceEx(short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowDataPercentEx(short profile, double masterSegment, double slaveSegment, short type, short percent, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowClearEx(short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowStartEx(int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GT_FollowSwitchEx(int mask);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowMemoryEx(short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowMemoryEx(short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowStatusEx(short profile, out short pFifoNum, out short pSwitchStatus);
        [DllImport("gts.dll")]
        public static extern short GT_SetFollowPhasingEx(short profile, short profilePhasing);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowPhasingEx(short profile, out short pProfilePhasing);
        [DllImport("gts.dll")]
        public static extern short GT_FollowSwitchNowEx(short profile, short method, short buffer, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowDataPercent2Ex(short profile, double masterSegment, double slaveSegment, double velBeginRatio, double velEndRatio, short percent, out short pPercent1, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetFollowDataPercent2Ex(double masterPos, double v1, double v2, double p, double p1, out double pSlavePos);
        [DllImport("gts.dll")]
        public static extern short GT_FollowDoBitEx(short profile, short doType, short index, short value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowDelayEx(short profile, uint delayTime, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_FollowDiBitEx(short profile, short diType, short index, short value, uint time, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_PrfFollow(short core, short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowMaster(short core, short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowMaster(short core, short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowLoop(short core, short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowLoop(short core, short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowEvent(short core, short profile, short followevent, short masterDir, int pos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowEvent(short core, short profile, out short pEvent, out short pMasterDir, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowSpace(short core, short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowData(short core, short profile, int masterSegment, double slaveSegment, short type, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowClear(short core, short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowStart(short core, int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowSwitch(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowMemory(short core, short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowMemory(short core, short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowStatus(short core, short profile, out short pFifoNum, out short pSwitchStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowPhasing(short core, short profile, short profilePhasing);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowPhasing(short core, short profile, out short pProfilePhasing);
        [DllImport("gts.dll")]

        public static extern short GTN_PrfFollowEx(short core, short profile, short dir);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowMasterEx(short core, short profile, short masterIndex, short masterType, short masterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowMasterEx(short core, short profile, out short pMasterIndex, out short pMasterType, out short pMasterItem);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowLoopEx(short core, short profile, int loop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowLoopEx(short core, short profile, out int pLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowEventEx(short core, short profile, short followevent, short masterDir, int pos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowEventEx(short core, short profile, out short pEvent, out short pMasterDir, out int pPos);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowSpaceEx(short core, short profile, out short pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowDataPercentEx(short core, short profile, double masterSegment, double slaveSegment, short type, short percent, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowClearEx(short core, short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowStartEx(short core, int mask, int option);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowSwitchEx(short core, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowMemoryEx(short core, short profile, short memory);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowMemoryEx(short core, short profile, out short pMemory);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowStatusEx(short core, short profile, out short pFifoNum, out short pSwitchStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_SetFollowPhasingEx(short core, short profile, short profilePhasing);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowPhasingEx(short core, short profile, out short pProfilePhasing);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowSwitchNowEx(short core, short profile, short method, short buffer, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowDataPercent2Ex(short core, short profile, double masterSegment, double slaveSegment, double velBeginRatio, double velEndRatio, short percent, out short pPercent1, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFollowDataPercent2Ex(short core, double masterPos, double v1, double v2, double p, double p1, out double pSlavePos);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowDoBitEx(short core, short profile, short doType, short index, short value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowDelayEx(short core, short profile, uint delayTime, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_FollowDiBitEx(short core, short profile, short diType, short index, short value, uint time, short fifo);


        public struct TMoveAbsolutePrm
        {
            public int pos;
            public double vel;
            public double acc;
            public double dec;
            public short percent;
        }

        public struct TMoveAbsolutePrmEx
        {
            public int pos; /*Ŀ��λ��*/
            public double vel; /*����ٶ�*/
            public double acc; /*���ٶȣ���λpulse/ms2*/
            public double dec; /*���ٶȣ���λpulse/ms2*/
            public short percent; /*S���߰ٷֱ�*/
            public double velStart; /*����ٶȣ���λpulse/ms*/
            public double velEnd; /*�յ��ٶȣ���λpulse/ms*/
            public double accStartPercent; /*���ٶ���ʼ���ٶȰٷֱ�*/
            public double decEndPercent; /*���ٶ��յ���ٶȰٷֱ�*/
        }

        public struct TMoveVelocityPrm
        {
            public double vel; /*Ŀ���ٶ�*/
            public double acc; /*���ٶȣ���λpulse/ms2*/
            public double dec; /*���ٶȣ���λpulse/ms2*/
            public double jerkBegin; /*��ʼjerk����λpulse/ms3*/
            public double jerkEnd; /*����Ŀ���ٶ�ʱ��jerk����λpulse/ms3*/
            public short direction; /*�˶�����*/
        }

        [DllImport("gts.dll")]
        public static extern short GT_MoveAbsolute(short profile, ref TMoveAbsolutePrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetMoveAbsolute(short profile, out TMoveAbsolutePrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_MoveAbsoluteEx(short profile, ref TMoveAbsolutePrmEx pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetMoveAbsoluteEx(short profile, out TMoveAbsolutePrmEx pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_MoveVelocity(short profile, ref TMoveVelocityPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetMoveVelocity(short profile, out TMoveVelocityPrm pPrm);

        [DllImport("gts.dll")]
        public static extern short GTN_MoveAbsolute(short core, short profile, ref TMoveAbsolutePrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMoveAbsolute(short core, short profile, out TMoveAbsolutePrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_MoveAbsoluteEx(short core, short profile, ref TMoveAbsolutePrmEx pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMoveAbsoluteEx(short core, short profile, out TMoveAbsolutePrmEx pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_MoveVelocity(short core, short profile, ref TMoveVelocityPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMoveVelocity(short core, short profile, out TMoveVelocityPrm pPrm);


        public struct TTransformOrthogonal
        {
            public short source;
            public short enable;
            public short x;
            public short y;
            public double theta;		// degree
        }
        [DllImport("gts.dll")]
        public static extern short GT_SetTransformOrthogonal(short index, ref TTransformOrthogonal pOrthogonal);
        [DllImport("gts.dll")]
        public static extern short GT_GetTransformOrthogonal(short index, out TTransformOrthogonal pOrthogonal);
        [DllImport("gts.dll")]
        public static extern short GT_GetTransformOrthogonalPosition(short index, out double pPositionX, out double pPositionY);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTransformOrthogonal(short core, short index, ref TTransformOrthogonal pOrthogonal);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTransformOrthogonal(short core, short index, out TTransformOrthogonal pOrthogonal);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTransformOrthogonalPosition(short core, short index, out double pPositionX, out double pPositionY);


        public const short HOME_STAGE_IDLE = 0;
        public const short HOME_STAGE_START = 1;

        public const short HOME_STAGE_SEARCH_LIMIT = 10;
        public const short HOME_STAGE_SEARCH_LIMIT_STOP = 11;

        public const short HOME_STAGE_SEARCH_LIMIT_ESCAPE = 13;

        public const short HOME_STAGE_SEARCH_LIMIT_RETURN = 15;
        public const short HOME_STAGE_SEARCH_LIMIT_RETURN_STOP = 16;

        public const short HOME_STAGE_SEARCH_HOME = 20;

        public const short HOME_STAGE_SEARCH_HOME_RETURN = 25;

        public const short HOME_STAGE_SEARCH_INDEX = 30;

        public const short HOME_STAGE_SEARCH_GPI = 40;

        public const short HOME_STAGE_SEARCH_GPI_RETURN = 45;

        public const short HOME_STAGE_GO_HOME = 80;

        public const short HOME_STAGE_END = 100;

        public const short HOME_ERROR_NONE = 0;
        public const short HOME_ERROR_NOT_TRAP_MODE = 1;
        public const short HOME_ERROR_DISABLE = 2;
        public const short HOME_ERROR_ALARM = 3;
        public const short HOME_ERROR_STOP = 4;
        public const short HOME_ERROR_STAGE = 5;
        public const short HOME_ERROR_HOME_MODE = 6;
        public const short HOME_ERROR_SET_CAPTURE_HOME = 7;
        public const short HOME_ERROR_NO_HOME = 8;
        public const short HOME_ERROR_SET_CAPTURE_INDEX = 9;
        public const short HOME_ERROR_NO_INDEX = 10;

        public const short HOME_MODE_LIMIT = 10;
        public const short HOME_MODE_LIMIT_HOME = 11;
        public const short HOME_MODE_LIMIT_INDEX = 12;
        public const short HOME_MODE_LIMIT_HOME_INDEX = 13;

        public const short HOME_MODE_HOME = 20;

        public const short HOME_MODE_HOME_INDEX = 22;

        public const short HOME_MODE_INDEX = 30;

        public struct THomePrm
        {
            public short mode;                      // ����ģʽ
            public short moveDir;                   // ������������ʱ���˶�����
            public short indexDir;                  // ����Index��������
            public short edge;                      // ���ò�����
            public short triggerIndex;              // �������ô�����
            public short pad10;					// ����1
            public short pad11;					// ����1
            public short pad12;                 // ����1
            public double velHigh;                  // Home�����ٶ�
            public double velLow;                   // Index�����ٶ�
            public double acc;
            public double dec;
            public short smoothTime;
            public short pad20;					// ����2
            public short pad21;					// ����2
            public short pad22;                 // ����2
            public int homeOffset;                // ԭ��ƫ��
            public int searchHomeDistance;        // Home����������룬Ϊ0��ʾ������
            public int searchIndexDistance;       // Index����������룬Ϊ0��ʾ������
            public int escapeStep;                // ���벽��
            public int pad31;					// ����3
            public int pad32;					// ����3
            public int pad33;					// ����3
        }

        public struct THomeStatus
        {
            public short run;
            public short stage;
            public short error;
            public short pad1;
            public int capturePos;
            public int targetPos;
        }
        [DllImport("gts.dll")]
        public static extern short GT_GoHome(short axis, ref THomePrm pHomePrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetHomePrm(short profile, out THomePrm pHomePrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetHomeStatus(short profile, out THomeStatus pHomeStatus);
        [DllImport("gts.dll")]

        public static extern short GTN_GoHome(short core, short axis, ref THomePrm pHomePrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetHomePrm(short core, short axis, out THomePrm pHomePrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetHomeStatus(short core, short axis, out THomeStatus pHomeStatus);


        public const short PLC_THREAD_MAX = 32;
        public const short PLC_PAGE_MAX = 32;
        public const short PLC_LOCAL_VAR_MAX = 1024;
        public const short PLC_ACCESS_VAR_COUNT_MAX = 8;

        public const short PLC_TIMER_TT = 0;
        public const short PLC_TIMER_TF = 1;
        public const short PLC_TIMER_TTF = 2;

        public const short PLC_COUNTER_EQ = 0;
        public const short PLC_COUNTER_LE = 1;
        public const short PLC_COUNTER_GE = 2;

        public const short PLC_COUNTER_EDGE_UP = 0;
        public const short PLC_COUNTER_EDGE_DOWN = 1;
        public const short PLC_COUNTER_EDGE_UP_DOWN = 2;

        public const short PLC_FLANK_UP = 0;
        public const short PLC_FLANK_DOWN = 1;
        public const short PLC_FLANK_UP_DOWN = 2;

        public enum EPlcBind
        {
            PLC_BIND_NONE,
            PLC_BIND_DI,
            PLC_BIND_DO,
            PLC_BIND_TIMER,
            PLC_BIND_COUNTER,
            PLC_BIND_FLANK,
            PLC_BIND_SRFF,
        }

        public struct TVarInfo
        {
            public short id;
            public short dataType;
            public string name;
        }

        public struct TBindDi
        {
            public short diType;
            public short index;
            public short reverse;
        }

        public struct TBindDo
        {
            public short doType;
            public short index;
            public short reverse;
        }

        public struct TBindTimer
        {
            public short timerType;
            public int delay;
            public short inputVarId;
        }

        public struct TBindCounter
        {
            public short counterType;
            public short edge;
            public int init;
            public int target;
            public int begin;
            public int end;
            public short dir;
            public int unit;
            public short inputVarId;
            public short resetVarId;
        }

        public struct TBindFlank
        {
            public short flankType;
            public short inputVarId;
        }

        public struct TBindSrff
        {
            public short setVarId;
            public short resetVarId;
        }

        public struct TCompileInfo
        {
            public string pFileName;
            public short pLineNo;
            public string pMessage;
        }

        public struct TThreadSts
        {
            public short run;
            public short error;
            public double result;
            public short line;
        }
        [DllImport("gts.dll")]
        public static extern short GT_Compile(string pFileName, out TCompileInfo pWrongInfo);
        [DllImport("gts.dll")]
        public static extern short GT_Download(string pFileName);
        [DllImport("gts.dll")]
        public static extern short GT_GetFunId(string pFunName, out short pFunId);
        [DllImport("gts.dll")]
        public static extern short GT_GetVarId(string pFunName, string pVarName, out TVarInfo pVarInfo);
        [DllImport("gts.dll")]
        public static extern short GT_Bind(short thread, short funId, short page);
        [DllImport("gts.dll")]
        public static extern short GT_RunThread(short thread);
        [DllImport("gts.dll")]
        public static extern short GT_RunThreadPeriod(short thread, short ms, short priority);
        [DllImport("gts.dll")]
        public static extern short GT_StepThread(short thread);
        [DllImport("gts.dll")]
        public static extern short GT_StopThread(short thread);
        [DllImport("gts.dll")]
        public static extern short GT_PauseThread(short thread);
        [DllImport("gts.dll")]
        public static extern short GT_GetThreadSts(short thread, out TThreadSts pThreadSts);
        [DllImport("gts.dll")]
        public static extern short GT_GetThreadTime(short thread, out short pPeriod, out double pExecuteTime, out double pExecuteTimeMax);
        [DllImport("gts.dll")]
        public static extern short GT_SetVarValue(short page, ref TVarInfo pVarInfo, out double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetVarValue(short page, out TVarInfo pVarInfo, out double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_UnbindVar(short thread);
        [DllImport("gts.dll")]
        public static extern short GT_BindDi(short thread, ref TVarInfo pVarInfo, ref TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GT_BindDo(short thread, ref TVarInfo pVarInfo, ref TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GT_BindTimer(short thread, ref TVarInfo pVarInfo, ref TBindTimer pBindTimer);
        [DllImport("gts.dll")]
        public static extern short GT_BindCounter(short thread, ref TVarInfo pVarInfo, ref TBindCounter pBindCounter);
        [DllImport("gts.dll")]
        public static extern short GT_BindFlank(short thread, ref TVarInfo pVarInfo, ref TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GT_BindSrff(short thread, ref TVarInfo pVarInfo, ref TBindSrff pBindSrff);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindDi(out TVarInfo pVarInfo, out TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindDo(out TVarInfo pVarInfo, out TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindTimer(out TVarInfo pVarInfo, out TBindTimer pBindTimer, out int pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindCounter(out TVarInfo pVarInfo, out TBindCounter pBindCounter, out int pUnitCount, out int pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindFlank(out TVarInfo pVarInfo, out TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindSrff(out TVarInfo pVarInfo, out TBindSrff pBindSrff);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindDiCount(short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindDoCount(short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindTimerCount(short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindCounterCount(short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindFlankCount(short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindSrffCount(short thread, out short pCount);
        [DllImport("gts.dll")]

        public static extern short GT_GetBindDiInfo(short thread, short index, out short pVar, out TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindDoInfo(short thread, short index, out short pVar, out TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindTimerInfo(short thread, short index, out short pVar, out TBindTimer pBindTimer);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindCounterInfo(short thread, short index, out short pVar, out TBindCounter pBindCounter);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindFlankInfo(short thread, short index, out short pVar, out TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GT_GetBindSrffInfo(short thread, short index, out short pVar, out TBindSrff pBindSrff);
        [DllImport("gts.dll")]

        public static extern short GTN_Compile(string pFileName, out TCompileInfo pWrongInfo);
        [DllImport("gts.dll")]
        public static extern short GTN_Download(short core, string pFileName);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFunId(string pFunName, out short pFunId);
        [DllImport("gts.dll")]
        public static extern short GTN_GetVarId(string pFunName, string pVarName, out TVarInfo pVarInfo);
        [DllImport("gts.dll")]
        public static extern short GTN_Bind(short core, short thread, short funId, short page);
        [DllImport("gts.dll")]
        public static extern short GTN_RunThread(short core, short thread);
        [DllImport("gts.dll")]
        public static extern short GTN_RunThreadPeriod(short core, short thread, short ms, short priority);
        [DllImport("gts.dll")]
        public static extern short GTN_StepThread(short core, short thread);
        [DllImport("gts.dll")]
        public static extern short GTN_StopThread(short core, short thread);
        [DllImport("gts.dll")]
        public static extern short GTN_PauseThread(short core, short thread);
        [DllImport("gts.dll")]
        public static extern short GTN_GetThreadSts(short core, short thread, out TThreadSts pThreadSts);
        [DllImport("gts.dll")]
        public static extern short GTN_GetThreadTime(short core, short thread, out short pPeriod, out double pExecuteTime, out double pExecuteTimeMax);
        [DllImport("gts.dll")]
        public static extern short GTN_SetVarValue(short core, short page, ref TVarInfo pVarInfo, ref double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetVarValue(short core, short page, out TVarInfo pVarInfo, out double pValue, short count);
        [DllImport("gts.dll")]

        public static extern short GTN_UnbindVar(short core, short thread);
        [DllImport("gts.dll")]
        public static extern short GTN_BindDi(short core, short thread, ref TVarInfo pVarInfo, ref TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GTN_BindDo(short core, short thread, ref TVarInfo pVarInfo, ref TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GTN_BindTimer(short core, short thread, ref TVarInfo pVarInfo, ref TBindTimer pBindTimer);
        [DllImport("gts.dll")]
        public static extern short GTN_BindCounter(short core, short thread, ref TVarInfo pVarInfo, ref TBindCounter pBindCounter);
        [DllImport("gts.dll")]
        public static extern short GTN_BindFlank(short core, short thread, ref TVarInfo pVarInfo, ref TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GTN_BindSrff(short core, short thread, ref TVarInfo pVarInfo, ref TBindSrff pBindSrff);
        [DllImport("gts.dll")]

        public static extern short GTN_GetBindDi(short core, out TVarInfo pVarInfo, out TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindDo(short core, out TVarInfo pVarInfo, out TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindTimer(short core, out TVarInfo pVarInfo, out TBindTimer pBindTimer, out int pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindCounter(short core, out TVarInfo pVarInfo, out TBindCounter pBindCounter, out int pUnitCount, out int pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindFlank(short core, out TVarInfo pVarInfo, out TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindSrff(short core, out TVarInfo pVarInfo, out TBindSrff pBindSrff);
        [DllImport("gts.dll")]

        public static extern short GTN_GetBindDiCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindDoCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindTimerCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindCounterCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindFlankCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindSrffCount(short core, short thread, out short pCount);
        [DllImport("gts.dll")]

        public static extern short GTN_GetBindDiInfo(short core, short thread, short index, out short pVar, out TBindDi pBindDi);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindDoInfo(short core, short thread, short index, out short pVar, out TBindDo pBindDo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindTimerInfo(short core, short thread, short index, out short pVar, out TBindTimer pBindTimer);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindCounterInfo(short core, short thread, short index, out short pVar, out TBindCounter pBindCounter);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindFlankInfo(short core, short thread, short index, out short pVar, out TBindFlank pBindFlank);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBindSrffInfo(short core, short thread, short index, out short pVar, out TBindSrff pBindSrff);



        public const short INTERPOLATION_AXIS_MAX = 8;

        public const short CRD_OPERATION_DATA_EXT_MAX = 2;


        public const short CRD_BUFFER_MODE_DYNAMIC_DEFAULT = 0;
        public const short CRD_BUFFER_MODE_DYNAMIC_KEEP = 1;
        public const short CRD_BUFFER_MODE_STATIC_INPUT = 11;
        public const short CRD_BUFFER_MODE_STATIC_READY = 12;
        public const short CRD_BUFFER_MODE_STATIC_START = 13;

        public const short INTERPOLATION_CIRCLE_PLAT_XY = 0;
        public const short INTERPOLATION_CIRCLE_PLAT_YZ = 1;
        public const short INTERPOLATION_CIRCLE_PLAT_ZX = 2;

        public const short INTERPOLATION_HELIX_CIRCLE_XY_LINE_Z = 0;
        public const short INTERPOLATION_HELIX_CIRCLE_YZ_LINE_X = 1;
        public const short INTERPOLATION_HELIX_CIRCLE_ZX_LINE_Y = 2;
        public const short INTERPOLATION_CIRCLE_DIR_CW = 0;
        public const short INTERPOLATION_CIRCLE_DIR_CCW = 1;

        public struct TCrdPrm
        {
            public short dimension;                              // ����ϵά��
            public short profile1;                             // ����profile��������
            public short profile2;                             // ����profile��������
            public short profile3;                             // ����profile��������
            public short profile4;                             // ����profile��������
            public short profile5;                             // ����profile��������
            public short profile6;                             // ����profile��������
            public short profile7;                             // ����profile��������
            public short profile8;                             // ����profile��������
            public double synVelMax;                             // ���ϳ��ٶ�
            public double synAccMax;                             // ���ϳɼ��ٶ�
            public short evenTime;                               // ��С����ʱ��
            public short setOriginFlag;                          // ����ԭ������ֵ��־,0:Ĭ�ϵ�ǰ�滮λ��Ϊԭ��λ��;1:�û�ָ��ԭ��λ��
            public int originPos1;                            // �û�ָ����ԭ��λ��
            public int originPos2;                            // �û�ָ����ԭ��λ��
            public int originPos3;                            // �û�ָ����ԭ��λ��
            public int originPos4;                            // �û�ָ����ԭ��λ��
            public int originPos5;                            // �û�ָ����ԭ��λ��
            public int originPos6;                            // �û�ָ����ԭ��λ��
            public int originPos7;                            // �û�ָ����ԭ��λ��
            public int originPos8;                            // �û�ָ����ԭ��λ��
        }

        public struct TCrdBufOperation
        {
            public short flag;                                   // ��־�ò岹���Ƿ���IO����ʱ
            public ushort delay;                         // ��ʱʱ��
            public short doType;                                 // ������IO������,0:�����IO
            public ushort doMask;                        // ������IO�������������
            public ushort doValue;                       // ������IO�����ֵ
            public ushort dataExt1;     // ����������չ����
            public ushort dataExt2;     // ����������չ����
        }

        public struct TCrdData
        {
            public short motionType;                             // �˶�����,0:ֱ�߲岹,1:Բ���岹
            public short circlePlat;                             // Բ���岹��ƽ��
            public int pos1;             // ��ǰ�θ����յ�λ��
            public int pos2;             // ��ǰ�θ����յ�λ��
            public int pos3;             // ��ǰ�θ����յ�λ��
            public int pos4;             // ��ǰ�θ����յ�λ��
            public int pos5;             // ��ǰ�θ����յ�λ��
            public int pos6;             // ��ǰ�θ����յ�λ��
            public int pos7;             // ��ǰ�θ����յ�λ��
            public int pos8;             // ��ǰ�θ����յ�λ��
            public double radius;                                  // Բ���岹�İ뾶
            public short circleDir;                              // Բ����ת����,0:˳ʱ��;1:��ʱ��
            public double center1;                               // Բ���岹��Բ������
            public double center2;                               // Բ���岹��Բ������
            public double vel;                                   // ��ǰ�κϳ�Ŀ���ٶ�
            public double acc;                                   // ��ǰ�κϳɼ��ٶ�
            public short velEndZero;                             // ��־��ǰ�ε��յ��ٶ��Ƿ�ǿ��Ϊ0,0:��ǿ��Ϊ0;1:ǿ��Ϊ0
            public TCrdBufOperation operation;                   // ��������ʱ��IO�ṹ��

            public double cos1;           // ��ǰ�θ����Ӧ������ֵ
            public double cos2;           // ��ǰ�θ����Ӧ������ֵ
            public double cos3;           // ��ǰ�θ����Ӧ������ֵ
            public double cos4;           // ��ǰ�θ����Ӧ������ֵ
            public double cos5;           // ��ǰ�θ����Ӧ������ֵ
            public double cos6;           // ��ǰ�θ����Ӧ������ֵ
            public double cos7;           // ��ǰ�θ����Ӧ������ֵ
            public double cos8;           // ��ǰ�θ����Ӧ������ֵ
            public double velEnd;                                // ��ǰ�κϳ��յ��ٶ�
            public double velEndAdjust;                          // �����յ��ٶ�ʱ�õ��ı���(ǰհģ��)
            public double r;                                     // ��ǰ�κϳ�λ����
        }

        public struct TCrdTime
        {
            public double time;
            public int segmentUsed;
            public int segmentHead;
            public int segmentTail;
        }

        public struct TBufFollowMaster
        {
            public short crdAxis;
            public short masterIndex;
            public short masterType;
        }

        public struct TBufFollowEventCross
        {
            public int masterPos;
            public int pad;
        }

        public struct TBufFollowEventTrigger
        {
            public short triggerIndex;
            public int triggerOffset;
            public int pad;
        }

        public struct TCrdFollowPrm
        {
            public double velRatioMax;
            public double accRatioMax;
            public int masterLead;
            public int masterEven;
            public int slaveEven;
            public short dir;
            public short smoothPercent;
            public short synchAlign;
        }

        public struct TCrdFollowStatus
        {
            public short stage;
            public double slavePos;
            public double slaveVel;
            public int masterFrameWidth;
            public int masterFrameIndex;
            public uint loopCount;
        }

        [DllImport("gts.dll")]
        public static extern short GT_SetCrdPrm(short crd, ref TCrdPrm pCrdPrm);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdPrm(short crd, out TCrdPrm pCrdPrm);
        [DllImport("gts.dll")]
        public static extern short GT_CrdSpace(short crd, out int pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_CrdData(short crd, nint pCrdData, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXY(short crd, int x, int y, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYOverride2(short crd, int x, int y, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYWN(short crd, int x, int y, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYOverride2WN(short crd, int x, int y, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXYG0(short crd, int x, int y, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYG0Override2(short crd, int x, int y, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYG0WN(short crd, int x, int y, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYG0Override2WN(short crd, int x, int y, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]


        public static extern short GT_LnXYZ(short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZOverride2(short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZWN(short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZOverride2WN(short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXYZG0(short crd, int x, int y, int z, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZG0Override2(short crd, int x, int y, int z, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZG0WN(short crd, int x, int y, int z, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZG0Override2WN(short crd, int x, int y, int z, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXYZA(short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAOverride2(short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAWN(short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAOverride2WN(short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXYZAG0(short crd, int x, int y, int z, int a, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAG0Override2(short crd, int x, int y, int z, int a, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAG0WN(short crd, int x, int y, int z, int a, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZAG0Override2WN(short crd, int x, int y, int z, int a, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_LnXYZACUVW(short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZACUVWWN(short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZACUVWOverride2(short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_LnXYZACUVWOverride2WN(short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcXYR(short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYROverride2(short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYRWN(short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYROverride2WN(short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcXYC(short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYCOverride2(short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYCWN(short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcXYCOverride2WN(short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcYZR(short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZROverride2(short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZRWN(short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZROverride2WN(short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcYZC(short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZCOverride2(short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZCWN(short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcYZCOverride2WN(short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcZXR(short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXROverride2(short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXRWN(short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXROverride2WN(short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_ArcZXC(short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXCOverride2(short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXCWN(short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_ArcZXCOverride2WN(short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixXYRZ(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYRZOverride2(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYRZWN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYRZOverride2WN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixXYCZ(short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYCZOverride2(short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYCZWN(short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixXYCZOverride2WN(short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixYZRX(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZRXOverride2(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZRXWN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZRXOverride2WN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixYZCX(short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZCXOverride2(short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZCXWN(short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixYZCXOverride2WN(short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixZXRY(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXRYOverride2(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXRYWN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXRYOverride2WN(short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_HelixZXCY(short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXCYOverride2(short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXCYWN(short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_HelixZXCYOverride2WN(short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GT_BufIO(short crd, uint doType, uint doMask, uint doValue, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufDelay(short crd, uint delayTime, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufDA(short crd, short chn, short daValue, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufLmtsOn(short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufLmtsOff(short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufSetStopIo(short crd, short axis, short stopType, short inputType, short inputIndex, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufMove(short crd, short moveAxis, int pos, double vel, double acc, short modal, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufGear(short crd, short gearAxis, int pos, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufGearPercent(short crd, short gearAxis, int pos, short accPercent, short decPercent, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufStopMotion(short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufSetVarValue(short crd, short pageId, ref TVarInfo pVarInfo, double value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufJumpNextSeg(short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufSynchPrfPos(short crd, short encoder, short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_BufVirtualToActual(short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_CrdStart(short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GT_CrdStartStep(short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GT_CrdStepMode(short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GT_SetOverride(short crd, double synVelRatio);
        [DllImport("gts.dll")]
        public static extern short GT_SetOverride2(short crd, double synVelRatio);
        [DllImport("gts.dll")]
        public static extern short GT_InitLookAhead(short crd, short fifo, double T, double accMax, short n, ref TCrdData pLookAheadBuf);
        [DllImport("gts.dll")]
        public static extern short GT_GetLookAheadSpace(short crd, out int pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetLookAheadSegCount(short crd, out int pSegCount, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_CrdClear(short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_CrdStatus(short crd, out short pRun, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_SetUserSegNum(short crd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetUserSegNum(short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetUserSegNumWN(short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetRemainderSegNum(short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_SetCrdStopDec(short crd, double decSmoothStop, double decAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdStopDec(short crd, out double pDecSmoothStop, out double pDecAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GT_SetCrdLmtStopMode(short crd, short lmtStopMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdLmtStopMode(short crd, out short pLmtStopMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetUserTargetVel(short crd, out double pTargetVel);
        [DllImport("gts.dll")]
        public static extern short GT_GetSegTargetPos(short crd, out int pTargetPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdPos(short crd, out double pPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdVel(short crd, out double pSynVel);
        [DllImport("gts.dll")]
        public static extern short GT_BufLaserOn(short crd, short fifo, short channel);
        [DllImport("gts.dll")]
        public static extern short GT_BufLaserOff(short crd, short fifo, short channel);
        [DllImport("gts.dll")]
        public static extern short GT_BufLaserPrfCmd(short crd, double laserPower, short fifo, short channel);
        [DllImport("gts.dll")]

        public static extern short GT_SetG0Mode(short crd, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetG0Mode(short crd, out short pMode);
        [DllImport("gts.dll")]

        public static extern short GT_SetCrdMapBase(short crd, short Mapbase);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdMapBase(short crd, out short pBase);
        [DllImport("gts.dll")]
        public static extern short GT_SetCrdBufferMode(short crd, short bufferMode, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdBufferMode(short crd, out short pBufferMode, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdSegmentTime(short crd, int segmentIndex, out double pSegmentTime, out int pSegmentNumber, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetCrdTime(short crd, out TCrdTime pTime, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_SetCrdPrm(short core, short crd, ref TCrdPrm pCrdPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdPrm(short core, short crd, out TCrdPrm pCrdPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdSpace(short core, short crd, out int pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdData(short core, short crd, nint pCrdData, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_CrdHsOn(short core, short crd, short fifo, short link, ushort threshold, short lookaheadInMc);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdHsOff(short core, short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdHsPrm(short core, short crd, short fifo, out short pEnable, out short pLink, out ushort pThreshold, out short pLookAheadInMc);
        [DllImport("gts.dll")]
        public static extern short GTN_GetFlashPassword(short core, out ushort pPassword);
        [DllImport("gts.dll")]
        public static extern short GT_GetFlashPassword(out ushort pPassword);

        [DllImport("gts.dll")]
        public static extern short GTN_LnXY(short core, short crd, int x, int y, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYOverride2(short core, short crd, int x, int y, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYWN(short core, short crd, int x, int y, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYOverride2WN(short core, short crd, int x, int y, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYG0(short core, short crd, int x, int y, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYG0Override2(short core, short crd, int x, int y, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYG0WN(short core, short crd, int x, int y, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYG0Override2WN(short core, short crd, int x, int y, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYZ(short core, short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZOverride2(short core, short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZWN(short core, short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZOverride2WN(short core, short crd, int x, int y, int z, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYZG0(short core, short crd, int x, int y, int z, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZG0Override2(short core, short crd, int x, int y, int z, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZG0WN(short core, short crd, int x, int y, int z, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZG0Override2WN(short core, short crd, int x, int y, int z, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYZA(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAOverride2(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAWN(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAOverride2WN(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYZAG0(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAG0Override2(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAG0WN(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZAG0Override2WN(short core, short crd, int x, int y, int z, int a, double synVel, double synAcc, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_LnXYZACUVW(short core, short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZACUVWOverride2(short core, short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZACUVWWN(short core, short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_LnXYZACUVWOverride2WN(short core, short crd, ref int pPos, short posMask, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcXYR(short core, short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYROverride2(short core, short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYRWN(short core, short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYROverride2WN(short core, short crd, int x, int y, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcXYC(short core, short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYCOverride2(short core, short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYCWN(short core, short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcXYCOverride2WN(short core, short crd, int x, int y, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcYZR(short core, short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZROverride2(short core, short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZRWN(short core, short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZROverride2WN(short core, short crd, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcYZC(short core, short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZCOverride2(short core, short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZCWN(short core, short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcYZCOverride2WN(short core, short crd, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcZXR(short core, short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXROverride2(short core, short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXRWN(short core, short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXROverride2WN(short core, short crd, int z, int x, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_ArcZXC(short core, short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXCOverride2(short core, short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXCWN(short core, short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_ArcZXCOverride2WN(short core, short crd, int z, int x, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixXYRZ(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYRZOverride2(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYRZWN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYRZOverride2WN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixXYCZ(short core, short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYCZOverride2(short core, short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYCZWN(short core, short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixXYCZOverride2WN(short core, short crd, int x, int y, int z, double xCenter, double yCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixYZRX(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZRXOverride2(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZRXWN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZRXOverride2WN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixYZCX(short core, short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZCXOverride2(short core, short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZCXWN(short core, short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixYZCXOverride2WN(short core, short crd, int x, int y, int z, double yCenter, double zCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixZXRY(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXRYOverride2(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXRYWN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXRYOverride2WN(short core, short crd, int x, int y, int z, double radius, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_HelixZXCY(short core, short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXCYOverride2(short core, short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXCYWN(short core, short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_HelixZXCYOverride2WN(short core, short crd, int x, int y, int z, double zCenter, double xCenter, short circleDir, double synVel, double synAcc, double velEnd, int segNum, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_BufIO(short core, short crd, short doType, ushort doMask, ushort doValue, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufDelay(short core, short crd, ushort delayTime, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufDA(short core, short crd, short chn, short daValue, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufLmtsOn(short core, short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufLmtsOff(short core, short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufSetStopIo(short core, short crd, short axis, short stopType, short inputType, short inputIndex, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufMove(short core, short crd, short moveAxis, int pos, double vel, double acc, short modal, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufGear(short core, short crd, short gearAxis, int pos, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufGearPercent(short core, short crd, short gearAxis, int pos, short accPercent, short decPercent, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufStopMotion(short core, short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufSetVarValue(short core, short crd, short pageId, ref TVarInfo pVarInfo, double value, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufJumpNextSeg(short core, short crd, short axis, short limitType, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufSynchPrfPos(short core, short crd, short encoder, short profile, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_BufVirtualToActual(short core, short crd, short fifo);
        [DllImport("gts.dll")]

        public static extern short GTN_CrdStart(short core, short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdStartStep(short core, short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdStepMode(short core, short mask, short option);
        [DllImport("gts.dll")]
        public static extern short GTN_SetOverride(short core, short crd, double synVelRatio);
        [DllImport("gts.dll")]
        public static extern short GTN_SetOverride2(short core, short crd, double synVelRatio);
        [DllImport("gts.dll")]
        public static extern short GTN_InitLookAhead(short core, short crd, short fifo, double T, double accMax, short n, ref TCrdData pLookAheadBuf);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLookAheadSpace(short core, short crd, out int pSpace, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLookAheadSegCount(short core, short crd, out int pSegCount, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdClear(short core, short crd, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_CrdStatus(short core, short crd, out short pRun, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_SetUserSegNum(short core, short crd, int segNum, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetUserSegNum(short core, short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetUserSegNumWN(short core, short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetRemainderSegNum(short core, short crd, out int pSegment, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCrdStopDec(short core, short crd, double decSmoothStop, double decAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdStopDec(short core, short crd, out double pDecSmoothStop, out double pDecAbruptStop);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCrdLmtStopMode(short core, short crd, short lmtStopMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdLmtStopMode(short core, short crd, out short pLmtStopMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetUserTargetVel(short core, short crd, out double pTargetVel);
        [DllImport("gts.dll")]
        public static extern short GTN_GetSegTargetPos(short core, short crd, out int pTargetPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdPos(short core, short crd, out double pPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdVel(short core, short crd, out double pSynVel);
        [DllImport("gts.dll")]
        public static extern short GTN_BufLaserOn(short core, short crd, short fifo, short channel);
        [DllImport("gts.dll")]
        public static extern short GTN_BufLaserOff(short core, short crd, short fifo, short channel);
        [DllImport("gts.dll")]
        public static extern short GTN_BufLaserPrfCmd(short core, short crd, double laserPower, short fifo, short channel);
        [DllImport("gts.dll")]

        public static extern short GTN_SetG0Mode(short core, short crd, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetG0Mode(short core, short crd, out short pMode);
        [DllImport("gts.dll")]

        public static extern short GTN_SetCrdMapBase(short core, short crd, short Mapbase);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdMapBase(short core, short crd, out short pBase);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCrdBufferMode(short core, short crd, short bufferMode, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdBufferMode(short core, short crd, out short pBufferMode, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdSegmentTime(short core, short crd, int segmentIndex, out double pSegmentTime, out int pSegmentNumber, short fifo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCrdTime(short core, short crd, out TCrdTime pTime, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_GetCmdCount(short crd, out short pResult, short fifo);
        [DllImport("gts.dll")]
        public static extern short GT_SetBacklash(short axis, int value, double changeValue, int dir);
        [DllImport("gts.dll")]
        public static extern short GT_GetBacklash(short axis, out int pValue, out double pChangeValue, out int pDir);
        [DllImport("gts.dll")]
        public static extern short GT_SetLeadScrewComp(short axis, short n, int startPos, int lenPos, ref int pPositive, ref int pNegative);
        [DllImport("gts.dll")]
        public static extern short GT_EnableLeadScrewComp(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_SetLeadScrewCrossComp(short axis, short n, int startPos, int lenPos, ref int pPositive, ref int pNegative, short link);
        [DllImport("gts.dll")]
        public static extern short GT_EnableLeadScrewCrossComp(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetCompensate(short axis, out double pPitchError, out double pCrossError, out double pBacklashError, out double pEncPos, out double pPrfPos);
        [DllImport("gts.dll")]
        public static extern short GTN_SetBacklash(short core, short axis, int value, double changeValue, int dir);
        [DllImport("gts.dll")]
        public static extern short GTN_GetBacklash(short core, short axis, out int pValue, out double pChangeValue, out int pDir);
        [DllImport("gts.dll")]
        public static extern short GTN_SetLeadScrewComp(short core, short axis, short n, int startPos, int lenPos, ref int pPositive, ref int pNegative);
        [DllImport("gts.dll")]
        public static extern short GTN_EnableLeadScrewComp(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetLeadScrewCrossComp(short core, short axis, short n, int startPos, int lenPos, ref int pPositive, out int pNegative, short link);
        [DllImport("gts.dll")]
        public static extern short GTN_EnableLeadScrewCrossComp(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCompensate(short core, short axis, out double pPitchError, out double pCrossError, out double pBacklashError, ref double pEncPos, ref double pPrfPos);
        public struct TLeadScrewPrm
        {
            public short n;
            public int startPos;
            public int lenPos;
            public int pCompPos;
            public int pCompNeg;
        }
        [DllImport("gts.dll")]
        public static extern short GT_SetLeadScrewTable(short axis, ref TLeadScrewPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GT_EnableLeadScrewTable(short axis, int error);
        [DllImport("gts.dll")]
        public static extern short GT_DisableLeadScrewTable(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_GetLeadScrewTablePrfPosCount(int encPos, out TLeadScrewPrm pPrm, out short pCountPositive, out short pCountNegative);
        [DllImport("gts.dll")]
        public static extern short GT_GetLeadScrewTablePrfPosPositive(int encPos, out TLeadScrewPrm pPrm, short index, out int pPrfPosPositive);
        [DllImport("gts.dll")]
        public static extern short GT_GetLeadScrewTablePrfPosNegative(int encPos, out TLeadScrewPrm pPrm, short index, out int pPrfPosNegative);
        [DllImport("gts.dll")]

        public static extern short GTN_SetLeadScrewTable(short core, short axis, ref TLeadScrewPrm pPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_EnableLeadScrewTable(short core, short axis, int error);
        [DllImport("gts.dll")]
        public static extern short GTN_DisableLeadScrewTable(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLeadScrewTablePrfPosCount(short core, int encPos, out TLeadScrewPrm pPrm, out short pCountPositive, out short pCountNegative);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLeadScrewTablePrfPosPositive(short core, int encPos, out TLeadScrewPrm pPrm, short index, out int pPrfPosPositive);
        [DllImport("gts.dll")]
        public static extern short GTN_GetLeadScrewTablePrfPosNegative(short core, int encPos, out TLeadScrewPrm pPrm, short index, out int pPrfPosNegative);


        public struct TCompensate2DTable
        {
            public short count1;                               // ��������������СֵΪ2
            public short count2;                               // ��������������СֵΪ2
            public int posBegin1;                             // ���λ��
            public int posBegin2;                             // ���λ��
            public int step1;                                 // ����
            public int step2;                                 // ����
        }

        public struct TCompensate2D
        {
            public short enable;                                  // 2D����ʹ�ܱ�־
            public short tableIndex;                              // ��ʹ�õ�2D������
            public short axisType1;                             // ���������
            public short axisType2;                             // ���������
            public short axisIndex1;                            // ���������
            public short axisIndex2;                            // ���������
        }
        [DllImport("gts.dll")]
        public static extern short GT_SetCompensate2DTable(short tableIndex, ref TCompensate2DTable pTable, ref int pData, short extend);
        [DllImport("gts.dll")]
        public static extern short GT_GetCompensate2DTable(short tableIndex, out TCompensate2DTable pTable, out short pExtend);
        [DllImport("gts.dll")]
        public static extern short GT_SetCompensate2D(short axis, ref TCompensate2D pComp2d);
        [DllImport("gts.dll")]
        public static extern short GT_GetCompensate2D(short axis, out TCompensate2D pComp2d);
        [DllImport("gts.dll")]
        public static extern short GT_GetCompensate2DValue(short axis, out double pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCompensate2DTable(short core, short tableIndex, ref TCompensate2DTable pTable, ref int pData, short extend);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCompensate2DTable(short core, short tableIndex, out TCompensate2DTable pTable, out short pExtend);
        [DllImport("gts.dll")]
        public static extern short GTN_SetCompensate2D(short core, short axis, ref TCompensate2D pComp2d);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCompensate2D(short core, short axis, out TCompensate2D pComp2d);
        [DllImport("gts.dll")]
        public static extern short GTN_GetCompensate2DValue(short core, short axis, out double pValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetDo(short doType, int value);
        [DllImport("gts.dll")]
        public static extern short GT_SetDoBit(short doType, short doIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GT_GetDo(short doType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetDoBitReverse(short doType, short doIndex, short value, short reverseTime);
        [DllImport("gts.dll")]
        public static extern short GT_GetDi(short diType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetDiReverseCount(short diType, short diIndex, out uint pReverseCount, short count);
        [DllImport("gts.dll")]
        public static extern short GT_SetDiReverseCount(short diType, short diIndex, ref uint pReverseCount, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetDiRaw(short diType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetDiEx(short diType, out int pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_SetDac(short dac, ref short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetDac(short dac, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAdc(short adc, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAdcValue(short adc, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_SetEncPos(short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetEncPos(short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetEncPosPre(short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetEncVel(short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_SetPlsPos(short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetPlsPos(short pulse, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetPlsVel(short pulse, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_SetAuEncPos(short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetAuEncPos(short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAuEncVel(short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GT_GetAbsEncPos(short core, short encoder, out int pValue, short mode, short param);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDo(short core, short doType, int value);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDoEx(short core, short doType, ref int pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDoBit(short core, short doType, short doIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDo(short core, short doType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDoBitReverse(short core, short doType, short doIndex, short value, short reverseTime);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDi(short core, short diType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDiReverseCount(short core, short diType, short diIndex, out uint pReverseCount, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDiReverseCount(short core, short diType, short diIndex, ref uint pReverseCount, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDiRaw(short core, short diType, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDiEx(short core, short diType, out int pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDac(short core, short dac, ref short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDac(short core, short dac, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAdc(short core, short adc, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAdcValue(short core, short adc, out short pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEncPos(short core, short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEncPos(short core, short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEncPosPre(short core, short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEncVel(short core, short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]

        public static extern short GTN_SetPlsPos(short core, short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPlsPos(short core, short pulse, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPlsVel(short core, short pulse, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]

        public static extern short GTN_SetAuEncPos(short core, short encoder, int encPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuEncPos(short core, short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuEncVel(short core, short encoder, out double pValue, short count, out uint pClock);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAbsEncPos(short core, short encoder, out int pValue, short mode, short param);
        [DllImport("gts.dll")]

        public static extern short GT_ExtModuleInit(short core, short method);
        [DllImport("gts.dll")]
        public static extern short GT_SetExtDoBit(short core, short doIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtDoBit(short core, short doIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetExtDo(short core, short doIndex, int value, int mask);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtDo(short core, short doIndex, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtDiBit(short core, short diIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtDi(short core, short diIndex, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetExtAoValue(short core, short index, short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtAiValue(short core, short index, out short pValue, short count);

        [DllImport("gts.dll")]
        public static extern short GT_SetExtAo(short core, short index, ref double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtAo(short core, short index, out double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GT_GetExtAi(short core, short index, out double pValue, short count);


        [DllImport("gts.dll")]
        public static extern short GT_SetILinkDo(short station, short module, ushort data, ushort mask);
        [DllImport("gts.dll")]
        public static extern short GT_GetILinkDi(short station, short module, out ushort data);
        [DllImport("gts.dll")]
        public static extern short GT_SetILinkAo(short station, short module, short channel, double data);
        [DllImport("gts.dll")]
        public static extern short GT_GetILinkAi(short station, short module, short channel, out double data);


        [DllImport("gts.dll")]
        public static extern short GTN_ExtModuleInit(short core, short method);
        [DllImport("gts.dll")]
        public static extern short GTN_SetExtDoBit(short core, short doIndex, short value);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtDoBit(short core, short doIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetExtDo(short core, short doIndex, int value, int mask);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtDo(short core, short doIndex, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtDiBit(short core, short diIndex, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtDi(short core, short diIndex, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetExtAoValue(short core, short index, out short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtAiValue(short core, short index, out short pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_SetExtAo(short core, short index, ref double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtAo(short core, short index, out double pValue, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_GetExtAi(short core, short index, out double pValue, short count);

        [DllImport("gts.dll")]
        public static extern short GTN_SetILinkDo(short core, short station, short module, ushort data, ushort mask);
        [DllImport("gts.dll")]
        public static extern short GTN_GetILinkDi(short core, short station, short module, ushort data);
        [DllImport("gts.dll")]
        public static extern short GTN_SetILinkAo(short core, short station, short module, short channel, short data);
        [DllImport("gts.dll")]
        public static extern short GTN_GetILinkAi(short core, short station, short module, short channel, out short data);


        public const short POS_COMPARE_MODE_FIFO = 0;
        public const short POS_COMPARE_MODE_LINEAR = 1;

        public const short POS_COMPARE_OUTPUT_PULSE = 0;
        public const short POS_COMPARE_OUTPUT_LEVEL = 1;

        public const short POS_COMPARE_SOURCE_ENCODER = 0;
        public const short POS_COMPARE_SOURCE_PULSE = 1;


        public struct TPosCompareMode
        {
            public short mode;                       // FIFOģʽ��Linearģʽ
            public short dimension;                  // 1D,2D
            public short sourceMode;                 // �����������������
            public short sourceX;                    // X��Ƚ�Դ
            public short sourceY;                    // Y��Ƚ�Դ
            public short outputMode;                 // ���ģʽ�����塢��ƽ
            public short outputCounter;              // ����������Դ
            public ushort outputPulseWidth;  // ���������ȣ���ƽģʽ��Ч
            public ushort errorBand;         // ��άλ�ñȽ��������
        }

        public struct TPosCompareLinear
        {
            public uint count;
            public ushort hso;
            public ushort gpo;

            public int startPos;
            public int interval;
        }

        public struct TPosCompareLinear2D
        {
            public uint count;
            public ushort hso;
            public ushort gpo;

            public int startPosX;
            public int startPosY;
            public int intervalX;
            public int intervalY;
        }


        public struct TPosCompareData
        {
            public int pos;
            public ushort hso;
            public ushort gpo;
            public uint segmentNumber;
        }

        public struct TPosCompareData2D
        {
            public int posX;
            public int posY;
            public ushort hso;
            public ushort gpo;
            public uint segmentNumber;
        }

        public struct TPosCompareStatus
        {
            public ushort mode;      // 0:FIFOģʽ��1��Linearģʽ
            public ushort run;
            public ushort space;
            public uint pulseCount;
            public ushort hso;
            public ushort gpo;
            public uint segmentNumber;
        }

        public struct TPosCompareInfo
        {
            public ushort config;
            public ushort fifoEmpty;
            public ushort head;
            public ushort tail;
            public uint commandReceive;
            public uint commandSend;
            public int posX;
            public int posY;
        }

        [DllImport("gts.dll")]
        public static extern short GT_PosCompareStart(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareStop(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareClear(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareStatus(short core, short index, out TPosCompareStatus pStatus);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareData(short core, short index, ref TPosCompareData pData);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareData2D(short core, short index, ref TPosCompareData2D pData);
        [DllImport("gts.dll")]
        public static extern short GT_SetPosCompareMode(short core, short index, ref TPosCompareMode pMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetPosCompareMode(short core, short index, out TPosCompareMode pMode);
        [DllImport("gts.dll")]
        public static extern short GT_SetPosCompareLinear(short core, short index, ref TPosCompareLinear pLinear);
        [DllImport("gts.dll")]
        public static extern short GT_GetPosCompareLinear(short core, short index, out TPosCompareLinear pLinear);
        [DllImport("gts.dll")]
        public static extern short GT_SetPosCompareLinear2D(short core, short index, ref TPosCompareLinear2D pLinear);
        [DllImport("gts.dll")]
        public static extern short GT_GetPosCompareLinear2D(short core, short index, out TPosCompareLinear2D pLinear);
        [DllImport("gts.dll")]
        public static extern short GT_PosCompareInfo(short core, short index, out TPosCompareInfo pInfo);
        [DllImport("gts.dll")]

        public static extern short GTN_PosCompareStart(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareStop(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareClear(short core, short index);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareStatus(short core, short index, out TPosCompareStatus pStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareData(short core, short index, ref TPosCompareData pData);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareData2D(short core, short index, ref TPosCompareData2D pData);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPosCompareMode(short core, short index, ref TPosCompareMode pMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPosCompareMode(short core, short index, out TPosCompareMode pMode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPosCompareLinear(short core, short index, ref TPosCompareLinear pLinear);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPosCompareLinear(short core, short index, out TPosCompareLinear pLinear);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPosCompareLinear2D(short core, short index, ref TPosCompareLinear2D pLinear);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPosCompareLinear2D(short core, short index, out TPosCompareLinear2D pLinear);
        [DllImport("gts.dll")]
        public static extern short GTN_PosCompareInfo(short core, short index, out TPosCompareInfo pInfo);



        public const short FIFO_MODE_STATIC = 0;
        public const short FIFO_MODE_DYNAMIC = 1;

        public const short SCAN_STATUS_WAIT = 0;
        public const short SCAN_STATUS_RUN = 1;
        public const short SCAN_STATUS_DONE = 2;

        public struct TScanInit
        {
            public int lookAheadNum;             //ǰհ����
            public double time;                  //ʱ�䳣��
            public double radiusRatio;           //�������Ƶ��ڲ���
        }

        public struct TScanInfo
        {
            public uint segmentNumber;
            public ushort commandNumber;
            public ushort prfVel;
            public ushort fifoEmpty;
            public ushort head;
            public ushort tail;
            public uint commandReceive;
            public uint commandSend;
            public uint reserve1;
            public uint reserve2;
            public uint reserve3;
            public uint reserve4;
            public uint reserve5;
            public uint reserve6;
        }

        public struct TLaserInfo
        {
            public ushort hso;
            public ushort powerMode;
            public ushort power;
            public ushort powerMax;
            public ushort powerMin;
            public ushort frequency;
            public ushort pulseWidth;
        }
        public struct TLaserPowerPrm
        {
            public short n;
            public double startVel;
            public double power;
        }

        public struct TLaserPowerTable
        {
            public short n;
            public double startVel;
            public double velStep;
            public double power;
        }

        [DllImport("gts.dll")]
        public static extern short GT_ScanInit(ref TScanInit pScanInit, double jumpAcc, double markAcc, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanCrdDataEnd(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_SetScanMode(short core, short mode, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_GetScanMode(short core, out short pMode, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ClearScanStatus(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanGetCrdPos(out short pPos, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanJump(short x, short y, double vel, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanJumpPoint(short x, short y, double vel, int motionDelayTime, int laserDelayTime, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanTimeJump(short x, short y, ushort time, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanTimeJumpPoint(short x, short y, ushort time, int motionDelayTime, int laserDelayTime, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanMark(short x, short y, double vel, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanTimeMark(short x, short y, ushort time, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_SetScanDaType(short type, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufLaserPrfCmd(double laserPower, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufIO(ushort doType, ushort doMask, ushort doValue, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufDelay(int time, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufDA(short chn, short value, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufLaserDelay(short laserOnDelay, short laserOffDelay, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufLaserOutFrq(double outFrq, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufSetPulseWidth(ushort width, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufLaserOn(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufLaserOff(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanBufStop(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanLaserIntervalOnList(int time, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_SetScanDelayTime(ushort maxJumpDelay, ushort markDelay, ushort multiMarkDelayConst, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_SetScanDelayMode(short multiMarkDelayMode, ushort multiMarkLaserOffDelay, ushort minJumpDelay, ushort jumpDelayLengthLimit, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanStop(short stopType, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanCrdSpace(out short pSpace, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanCrdStart(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanCrdClear(short crd);
        [DllImport("gts.dll")]
        public static extern short GT_ScanCrdStatus(out short pRun, out short pStatus, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_SetHSIOOpt(ushort value, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_GetHSIOOpt(out short pValue, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_SetLaserMode(ushort laserMode, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_LaserPowerMode(short laserPowerMode, double maxValue, double minValue, uint laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_LaserPrfCmd(double power, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_LaserOutFrq(double outFrq, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_SetPulseWidth(ushort width, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_SetLevelDelay(ushort highLevelDelay, ushort lowLevelDelay, ushort laserChannel);
        [DllImport("gts.dll")]
        public static extern short GT_ScanInfo(ref TScanInfo pScanInfo, short crd);
        [DllImport("gts.dll")]
        public static extern short GT_LaserInfo(ref TLaserInfo pLaserInfo, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanInit(short core, ref TScanInit pScanInit, double jumpAcc, double markAcc, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanCrdDataEnd(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetScanMode(short core, short mode, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_GetScanMode(short core, out short pMode, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearScanStatus(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanGetCrdPos(short core, out short pPos, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanJump(short core, short x, short y, double vel, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanJumpPoint(short core, short x, short y, double vel, int motionDelayTime, int laserDelayTime, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanTimeJump(short core, short x, short y, ushort time, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanTimeJumpPoint(short core, short x, short y, ushort time, int motionDelayTime, int laserDelayTime, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanMark(short core, short x, short y, double vel, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanTimeMark(short core, short x, short y, ushort time, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetScanDaType(short core, short type, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufLaserPrfCmd(short core, double laserPower, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufIO(short core, ushort doType, ushort doMask, ushort doValue, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufDelay(short core, int time, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufDA(short core, ushort chn, short value, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufLaserDelay(short core, short laserOnDelay, short laserOffDelay, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufLaserOutFrq(short core, double outFrq, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufSetPulseWidth(short core, ushort width, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufLaserOn(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufLaserOff(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanBufStop(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanLaserIntervalOnList(short core, int time, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetScanDelayTime(short core, ushort maxJumpDelay, ushort markDelay, ushort multiMarkDelayConst, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetScanDelayMode(short core, short multiMarkDelayMode, ushort multiMarkLaserOffDelay, ushort minJumpDelay, ushort jumpDelayLengthLimit, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanStop(short core, short stopType, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanCrdSpace(short core, out short pSpace, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanCrdStart(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanCrdClear(short core, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanCrdStatus(short core, out short pRun, out short pCmdId, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetHSIOOpt(short core, ushort value, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_GetHSIOOpt(short core, out short pValue, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetLaserMode(short core, ushort laserMode, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_LaserPowerMode(short core, short laserPowerMode, double maxValue, double minValue, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_LaserPrfCmd(short core, double power, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_LaserOutFrq(short core, double outFrq, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPulseWidth(short core, ushort width, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_SetLevelDelay(short core, ushort highLevelDelay, ushort lowLevelDelay, ushort crd);
        [DllImport("gts.dll")]
        public static extern short GTN_ScanInfo(short core, ref TScanInfo pScanInfo, short crd);
        [DllImport("gts.dll")]
        public static extern short GTN_LaserInfo(short core, ref TLaserInfo pLaserInfo, short crd);


        public const short DLM_FUNCTION_EVENT = 0;
        public const short DLM_FUNCTION_TIMER = 1;
        public const short DLM_FUNCTION_BACKGROUND = 2;
        public const short DLM_FUNCTION_COMMAND = 3;

        public const short DLM_FUNCTION_PROCEDURE = 7;

        public const short DLM_FUNCTION_PROFILE_EVENT = 8;
        public const short DLM_FUNCTION_PROFILE = 9;
        public const short DLM_FUNCTION_PROFILE_SUPERIMPOSED = 10;
        public const short DLM_FUNCTION_PROFILE_FILTER = 11;

        public const short DLM_FUNCTION_SERVO_EVENT = 16;
        public const short DLM_FUNCTION_SERVO = 17;
        public const short DLM_FUNCTION_SERVO_SUPERIMPOSED = 18;
        public const short DLM_FUNCTION_SERVO_FILTER = 19;

        public const short DLM_LOAD_MODE_NONE = 0;
        public const short DLM_LOAD_MODE_COMMAND = 1;
        public const short DLM_LOAD_MODE_BOOT = 2;
        public const short DLM_LOAD_MODE_RUN = 3;

        public struct TDlmStatus
        {
            public int version;
            public int date;
            public short enable;
            public int function;
        }

        public struct TDlmFunction
        {
            public short function;
            public short enable;
            public int value;
        }
        [DllImport("gts.dll")]
        public static extern short GT_LoadDlm(int vender, int module, string fileName, out short pId);
        [DllImport("gts.dll")]
        public static extern short GT_ProgramDlm(short id, short loadMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetDlmLoadMode(short id, out short pLoadMode);
        [DllImport("gts.dll")]
        public static extern short GT_RunDlm(short id);
        [DllImport("gts.dll")]
        public static extern short GT_StopDlm(short id);
        [DllImport("gts.dll")]
        public static extern short GT_GetDlmStatus(short id, out TDlmStatus pStatus);
        [DllImport("gts.dll")]
        public static extern short GT_SetDlmFunction(short id, ref TDlmFunction pFunction);
        [DllImport("gts.dll")]
        public static extern short GT_GetDlmFunction(short id, out TDlmFunction pFunction);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandInit(short code, int index);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandAdd16(short value);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandAdd32(int value);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandAddFloat(float value);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandAddDouble(double value);
        [DllImport("gts.dll")]
        public static extern short GT_SendDlmCommand(short id, out short pReturnValue);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandGet16(out short pValue);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandGet32(out int pValue);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandGetFloat(out float pValue);
        [DllImport("gts.dll")]
        public static extern short GT_DlmCommandGetDouble(out double pValue);
        [DllImport("gts.dll")]

        public static extern short GTN_LoadDlm(short core, int vender, int module, string fileName, out short pId);
        [DllImport("gts.dll")]
        public static extern short GTN_ProgramDlm(short core, short id, short loadMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDlmLoadMode(short core, short id, out short pLoadMode);
        [DllImport("gts.dll")]
        public static extern short GTN_RunDlm(short core, short id);
        [DllImport("gts.dll")]
        public static extern short GTN_StopDlm(short core, short id);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDlmStatus(short core, short id, out TDlmStatus pStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_SetDlmFunction(short core, short id, ref TDlmFunction pFunction);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDlmFunction(short core, short id, out TDlmFunction pFunction);
        [DllImport("gts.dll")]

        public static extern short GTN_DlmCommandInit(short core, short code, int index);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandAdd16(short core, short value);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandAdd32(short core, int value);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandAddFloat(short core, float value);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandAddDouble(short core, double value);
        [DllImport("gts.dll")]
        public static extern short GTN_SendDlmCommand(short core, short id, out short pReturnValue);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandGet16(short core, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandGet32(short core, out int pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandGetFloat(short core, ref float pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_DlmCommandGetDouble(short core, out double pValue);


        public const short WATCH_GROUP_TIMER = 0;
        public const short WATCH_GROUP_BACKGROUND = 1;

        public const short WATCH_LOAD_MODE_NONE = 0;
        public const short WATCH_LOAD_MODE_BOOT = 2;
        public const short WATCH_LOAD_MODE_RUN = 3;

        public const short WATCH_MODE_STATIC = 0;
        public const short WATCH_MODE_LOOP = 1;
        public const short WATCH_MODE_DYNAMIC = 2;

        public const short WATCH_EVENT_RUN = 1;
        public const short WATCH_EVENT_START = 10;
        public const short WATCH_EVENT_STOP = 20;
        public const short WATCH_EVENT_OFF = 30;

        public const short WATCH_CONDITION_EQ = 1;
        public const short WATCH_CONDITION_NE = 2;
        public const short WATCH_CONDITION_GE = 3;
        public const short WATCH_CONDITION_LE = 4;

        public const short WATCH_CONDITION_CHANGE_TO = 11;
        public const short WATCH_CONDITION_CHANGE = 12;
        public const short WATCH_CONDITION_UP = 13;
        public const short WATCH_CONDITION_DOWN = 14;

        public const short WATCH_CONDITION_REMAIN_AT = 21;
        public const short WATCH_CONDITION_REMAIN = 22;
        public const short WATCH_CONDITION_CROSS_POSITIVE = 23;
        public const short WATCH_CONDITION_CROSS_NEGATIVE = 24;

        public const short WATCH_CONDITION_NEAREST = 31;
        public const short WATCH_CONDITION_DELTA = 32;

        public struct TWatchVar
        {
            public ushort type;
            public ushort index;
            public ushort id;
        }

        public struct TWatchEvent
        {
            public ushort type;
            public ushort loop;
            public ushort watchCount;
            public TWatchVar var;
            public ushort condition;
            public double value;
        }

        public struct TWatchVarInfo
        {
            public uint pAddress;
            public ushort length;
            public short fraction;
            public ushort format;
            public ushort hex;
            public ushort sign;
            public ushort bit;
        }
        [DllImport("gts.dll")]
        public static extern short GT_SetWatchGroup(short group);
        [DllImport("gts.dll")]
        public static extern short GT_GetWatchGroup(out short pGroup);
        [DllImport("gts.dll")]
        public static extern short GT_LoadWatchConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_SaveWatchConfig(short core, short group, string pFile);
        [DllImport("gts.dll")]
        public static extern short GT_PrintWatch(string pFileName, int start, uint printCount);
        [DllImport("gts.dll")]
        public static extern short GT_ReadWatch(short varIndex, ref double pBuffer, uint bufferSize, ref uint pReadCount);
        [DllImport("gts.dll")]
        public static extern short GT_ProgramWatch(short loadMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetWatchLoadMode(short group, out short pLoadMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetMcVar(out TWatchVar pVar, out double pValue);
        [DllImport("gts.dll")]

        public static extern short GT_ClearWatch(short mode);
        [DllImport("gts.dll")]
        public static extern short GT_AddWatchVar(ref TWatchVar pVar);
        [DllImport("gts.dll")]
        public static extern short GT_AddWatchEvent(ref TWatchEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GT_WatchOn(short interval, short mode, ushort count);
        [DllImport("gts.dll")]
        public static extern short GT_WatchOff();
        [DllImport("gts.dll")]

        public static extern short GT_GetWatchEvent(short index, out TWatchEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GT_GetWatchVar(short index, out TWatchVar pVar, out TWatchVarInfo pVarInfo);
        [DllImport("gts.dll")]
        public static extern short GT_SetWatchModeEx(short group, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_SetWatchIntervalEx(short group, short interval);
        [DllImport("gts.dll")]

        public static extern short GTN_SetWatchGroup(short core, short group);
        [DllImport("gts.dll")]
        public static extern short GTN_GetWatchGroup(short core, out short pGroup);
        [DllImport("gts.dll")]
        public static extern short GTN_LoadWatchConfig(short core, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_SaveWatchConfig(short core, short group, string pFile);
        [DllImport("gts.dll")]
        public static extern short GTN_PrintWatch(short core, string pFileName, int start, uint printCount);
        [DllImport("gts.dll")]
        public static extern short GTN_ReadWatch(short core, short varIndex, ref double pBuffer, uint bufferSize, ref uint pReadCount);
        [DllImport("gts.dll")]
        public static extern short GTN_ProgramWatch(short core, short loadMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetWatchLoadMode(short core, short group, out short pLoadMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetMcVar(short core, out TWatchVar pVar, out double pValue);
        [DllImport("gts.dll")]

        public static extern short GTN_ClearWatch(short core, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_AddWatchVar(short core, ref TWatchVar pVar);
        [DllImport("gts.dll")]
        public static extern short GTN_AddWatchEvent(short core, ref TWatchEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GTN_WatchOn(short core, short interval, short mode, uint count);
        [DllImport("gts.dll")]
        public static extern short GTN_WatchOff(short core);
        [DllImport("gts.dll")]

        public static extern short GTN_GetWatchEvent(short core, short index, out TWatchEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GTN_GetWatchVar(short core, short index, out TWatchVar pVar, out TWatchVarInfo pVarInfo);
        [DllImport("gts.dll")]
        public static extern short GTN_SetWatchModeEx(short core, short group, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetWatchIntervalEx(short core, short group, short interval);


        public const short TASK_SET_DO_BIT = 0x1101;
        public const short TASK_SET_DAC = 0x1120;

        public const short TASK_STOP = 0x1303;

        public const short TASK_UPDATE_POS = 0x2002;
        public const short TASK_UPDATE_VEL = 0x2004;

        public const short TASK_PT_START = 0x2306;
        public const short TASK_PVT_START = 0x2346;
        public const short TASK_MOVE_ABSOLUTE = 0x2500;

        public const short TASK_GEAR_START = 0x3005;

        public const short TASK_FOLLOW_START = 0x310A;
        public const short TASK_FOLLOW_SWITCH = 0x310B;

        public const short TASK_CRD_START = 0x4004;
        public const short TASK_SCAN_START = 0x4102;

        public const short TASK_SET_DO_BIT_MODE_NONE = 0;
        public const short TASK_SET_DO_BIT_MODE_TIME = 10;
        public const short TASK_SET_DO_BIT_MODE_DISTANCE = 20;


        public struct TTaskSetDoBit
        {
            public short doType;
            public short doIndex;
            public short doValue;
            public short mode;
            public int parameter1;
            public int parameter2;
            public int parameter3;
            public int parameter4;
            public int parameter5;
            public int parameter6;
            public int parameter7;
            public int parameter8;
        }

        public struct TTaskSetDac
        {
            public short dac;
            public short value;
        }

        public struct TTaskStop
        {
            public int mask;
            public int option;
        }

        public struct TTaskFifoOperation
        {
            public short type;
            public short index;
            public short operation;
            public short data1;
            public short data2;
            public short data3;
            public short data4;
            public short data5;
            public short data6;
            public short data7;
            public short data8;
            public short data9;
            public short data10;
            public short data11;
            public short data12;
            public short data13;
            public short data14;
            public short data15;
            public short data16;
            public short data17;
            public short data18;
            public short data19;
            public short data20;
        }

        public struct TTaskUpdatePos
        {
            public short profile;
            public int pos;
        }
        public struct TTaskUpdateDistance
        {
            public short profile;
            public short triggerIndex;
            public int distance;
        }
        public struct TTaskUpdateVel
        {
            public short profile;
            public double vel;
        }

        public struct TTaskPtStart
        {
            public int mask;
            public int option;
        }

        public struct TTaskPvtStart
        {
            public int mask;
        }

        public struct TTaskGearStart
        {
            public int mask;
        }

        public struct TTaskFollowStart
        {
            public int mask;
            public int option;
        }

        public struct TTaskFollowSwitch
        {
            public int mask;
        }

        public struct TTaskMoveAbsolute
        {
            public short profile;
            public int pos;
            public double vel;
            public double acc;
            public double dec;
            public short percent;
        }

        public struct TTaskCrdStart
        {
            public short mask;
            public short option;
        }

        public struct TTaskScanStart
        {
            public short core;
            public short index;
            public short count;
        }

        public struct TEvent
        {
            public uint loop;
            public TWatchVar var;
            public ushort condition;
            public double value;
        }
        [DllImport("gts.dll")]
        public static extern short GT_ClearEvent();
        [DllImport("gts.dll")]
        public static extern short GT_ClearTask();
        [DllImport("gts.dll")]
        public static extern short GT_ClearEventTaskLink();
        [DllImport("gts.dll")]
        public static extern short GT_AddTask(short taskType, nint pTaskData, ref short pTaskIndex);
        [DllImport("gts.dll")]
        public static extern short GT_AddEvent(ref TEvent pEvent, ref short pEventIndex);
        [DllImport("gts.dll")]
        public static extern short GT_AddEventTaskLink(short eventIndex, short taskIndex, ref short pLinkIndex);
        [DllImport("gts.dll")]
        public static extern short GT_GetEventCount(out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetEvent(short eventIndex, out TEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GT_GetEventLoop(short eventIndex, out uint pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetTaskCount(out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetEventTaskLinkCount(out short pCount);
        [DllImport("gts.dll")]
        public static extern short GT_GetEventTaskLink(short linkIndex, out short pEventIndex, out short pTaskIndex);
        [DllImport("gts.dll")]
        public static extern short GT_EventOn(short eventIndex, short count);
        [DllImport("gts.dll")]
        public static extern short GT_EventOff(short eventIndex, short count);
        [DllImport("gts.dll")]

        public static extern short GTN_ClearEvent(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearTask(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_ClearEventTaskLink(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_AddTask(short core, short taskType, nint pTaskData, ref short pTaskIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_AddEvent(short core, ref TEvent pEvent, ref short pEventIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_AddEventTaskLink(short core, short eventIndex, short taskIndex, ref short pLinkIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEventCount(short core, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEvent(short core, short eventIndex, out TEvent pEvent);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEventLoop(short core, short eventIndex, out uint pEventLoop);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTaskCount(short core, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEventTaskLinkCount(short core, out short pCount);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEventTaskLink(short core, short linkIndex, out short pEventIndex, out short pTaskIndex);
        [DllImport("gts.dll")]
        public static extern short GTN_EventOn(short core, short eventIndex, short count);
        [DllImport("gts.dll")]
        public static extern short GTN_EventOff(short core, short eventIndex, short count);


        public const short TERMINAL_LOAD_MODE_NONE = 0;
        public const short TERMINAL_LOAD_MODE_BOOT = 2;
        [DllImport("gts.dll")]
        public static extern short GT_TerminalInit(short detect);
        [DllImport("gts.dll")]
        public static extern short GT_GetTerminalVersion(short core, short station, out TVersion pTerminalVersion);
        [DllImport("gts.dll")]
        public static extern short GT_SetTerminalPermit(short core, short index, short dataType, ushort permit);
        [DllImport("gts.dll")]
        public static extern short GT_GetTerminalPermit(short core, short index, short dataType, out short pPermit);
        [DllImport("gts.dll")]
        public static extern short GT_ProgramTerminalConfig(short loadMode);
        [DllImport("gts.dll")]
        public static extern short GT_GetTerminalConfigLoadMode(out short pLoadMode);
        [DllImport("gts.dll")]

        public static extern short GTN_TerminalInit(short core, short detect);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTerminalVersion(short core, short index, out TVersion pTerminalVersion);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTerminalPermit(short core, short index, short dataType, ushort permit);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTerminalPermit(short core, short index, short dataType, out short pPermit);
        [DllImport("gts.dll")]
        public static extern short GTN_ProgramTerminalConfig(short core, short loadMode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTerminalConfigLoadMode(short core, out short pLoadMode);
        [DllImport("gts.dll")]

        public static extern short GTN_ReadPhysicalMap();
        [DllImport("gts.dll")]
        public static extern short ConvertPhysical(short core, short dataType, short terminal, short index);
        [DllImport("gts.dll")]

        public static extern short GTN_SetRetainValue(short core, uint address, short count, ref short pData);
        [DllImport("gts.dll")]
        public static extern short GTN_GetRetainValue(short core, uint address, short count, out short pData);


        //EtherCAT
        public const short ECAT_STATE_OP = 2;
        public const short ECAT_AXIS_MAX = 12;

        public const short ECAT_MODE_HOMING = 6;
        public const short ECAT_MODE_CSP = 8;
        public const short ECAT_MODE_CSV = 9;
        public const short ECAT_MODE_CST = 10;
        public const short ECAT_MODE_PV = 3;
        public const short ECAT_MODE_PT = 4;

        public const short ECAT_PROBE_TRIGGER_TYPE_FIRST_EVENT = 0;
        public const short ECAT_PROBE_TRIGGER_TYPE_CONTINUES = 1;
        public const short ECAT_PROBE_TRIGGER_LEVEL_POS = 1;
        public const short ECAT_PROBE_TRIGGER_LEVEL_NEG = -1;
        public const short ECAT_PROBE_TRIGGER_LEVEL_DUL = 0;

        public const short ECAT_ACT_ENC_POS = 0;
        public const short ECAT_ACT_ENC_VEL = 1;
        public const short ECAT_ACT_CURRENT = 2;
        public const short ECAT_ACT_TORQUE = 3;
        public const short ECAT_ACT_CT = 4;

        public struct TSlaveInfo
        {
            public int slave_cnt;
            public int slave_type;
            public int motion_cnt;
            public int io_nmap;
            public int io_length;
            public uint Vid;
            public uint Pid;
        }

        [DllImport("gts.dll")]
        public static extern short GT_SetEcatHomingPrm(short axis, short method, double speed1, double speed2, double acc, int offset, ushort probeFunction);
        [DllImport("gts.dll")]
        public static extern short GT_StartEcatHoming(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_SetHomingMode(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatHomingStatus(short axis, out ushort homingStatus);
        [DllImport("gts.dll")]
        public static extern short GT_StopEcatHoming(short axis);
        [DllImport("gts.dll")]
        public static extern short GT_SetTouchProbeFunction(short axis, short ProbePrm);
        [DllImport("gts.dll")]
        public static extern short GT_SetTouchProbeFunctionEx(short axis, short Probe1Enable, short Probe1TriggerType, short Probe1TriggerLevel, short Probe2Enable, short Probe2TriggerType, short Probe2TriggerLevel);
        [DllImport("gts.dll")]
        public static extern short GT_GetTouchProbeStatus(short axis, out ushort probeStatus, out int probe1PosValue, out int probe1NegValue, out int probe2PosValue, out int probe2NegValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatGpioConfig(short effectiveLevel, short direction);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisOnThreshold(short axis, ushort threshold);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisOnThreshold(short axis, out ushort threshold);
        [DllImport("gts.dll")]
        public static extern short GT_InitEcatComm();
        [DllImport("gts.dll")]
        public static extern short GT_StartEcatComm();
        [DllImport("gts.dll")]
        public static extern short GT_IsEcatReady(out short pStatus);
        [DllImport("gts.dll")]
        public static extern short GT_TerminateEcatComm();
        [DllImport("gts.dll")]
        public static extern short GT_TerminateEcatCommEx();
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatErrorCode(short axis, out ushort pErrorCode);
        [DllImport("gts.dll")]
        public static extern short GT_GetDcError(out short pError);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisMode(short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisMode(short axis, out ushort drvMode);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisPV(short axis, int velocity);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisPT(short axis, short torque);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatEncPos(short axis, out int pEncPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAuxEncPos(short axis, out int pAuxEncPos);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatEncVel(short axis, out int pVelocity);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisAtlCurrent(short axis, out short pCur);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisAtlTorque(short axis, out short pTorque);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisAV(short axis, out int pVel);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisACT(short axis, out short pCur, out short pTorque);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatMCType(out short pType);
        [DllImport("gts.dll")]
        public static extern short GT_SetPosScale(short axis, ushort posScale);
        [DllImport("gts.dll")]
        public static extern short GT_GetPosScale(short axis, out ushort posScale);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisAI(short axis, short channel, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisDI(short axis, out uint pDi);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisDO(short axis, uint DoValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisDO(short axis, out uint pDo);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatSlaves(out short pSlaveMotionCnt, out short pSlaveIOCnt);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatSlaveInfo(short slave, out TSlaveInfo pSlaveinfo);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisPE(short axis, out int pPosErr);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatRawData(ushort offset, ushort nByteSize, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatRawData(ushort offset, ushort nByteSize, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GT_SetEcatAxisTorqueOffset(short axis, short torqueOffset);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisTorqueOffset(short axis, out short pTorqueOffset);
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatPdoLength(out short pPdoLen);
        [DllImport("gts.dll")]
        public static extern short GT_SetAuxEncoderCapture(short encoder, short CapPrm);
        [DllImport("gts.dll")]
        public static extern short GT_SetAuxEncoderCaptureEx(short encoder, short CapUpEnable, short CapUpIO, short CapDnEnable, short CapDnIO);
        [DllImport("gts.dll")]
        public static extern short GT_GetAuxEncoderCaptureStatus(short encoder, out ushort CapSts, out int PosValue, out int NegValue);
        [DllImport("gts.dll")]
        public static extern short GT_EcatSDODownload(ushort slave, ushort index, byte subindex, ref byte data, uint data_size, out uint abort_code);
        [DllImport("gts.dll")]
        public static extern short GT_EcatSDOUpload(ushort slave, ushort index, byte subindex, out byte target, uint target_size, out uint result_size, out uint abort_code);
        [DllImport("gts.dll")]
        public static extern short GT_EcatIOReadInput(ushort slave, ushort offset, ushort nSize, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GT_EcatIOWriteOutput(ushort slave, ushort offset, ushort nSize, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GT_EcatIOBitWriteOutput(ushort slaveno, ushort offset, short Index, byte value);
        [DllImport("gts.dll")]
        public static extern short GT_EcatIOSynch();
        [DllImport("gts.dll")]
        public static extern short GT_GetEcatAxisPdoData(short axis, ushort objectWord, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatHomingPrm(short core, short axis, short method, double speed1, double speed2, double acc, int offset, ushort probeFunction);
        [DllImport("gts.dll")]
        public static extern short GTN_StartEcatHoming(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_SetHomingMode(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatHomingStatus(short core, short axis, out ushort homingStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_StopEcatHoming(short core, short axis);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTouchProbeFunction(short core, short axis, short ProbePrm);
        [DllImport("gts.dll")]
        public static extern short GTN_SetTouchProbeFunctionEx(short core, short axis, short Probe1Enable, short Probe1TriggerType, short Probe1TriggerLevel, short Probe2Enable, short Probe2TriggerType, short Probe2TriggerLevel);
        [DllImport("gts.dll")]
        public static extern short GTN_GetTouchProbeStatus(short core, short axis, out ushort probeStatus, out int probe1PosValue, out int probe1NegValue, out int probe2PosValue, out int probe2NegValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatGpioConfig(short core, short effectiveLevel, short direction);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisOnThreshold(short core, short axis, ushort threshold);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisOnThreshold(short core, short axis, out ushort threshold);
        [DllImport("gts.dll")]
        public static extern short GTN_InitEcatComm(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_StartEcatComm(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_IsEcatReady(short core, out short pStatus);
        [DllImport("gts.dll")]
        public static extern short GTN_TerminateEcatComm(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_TerminateEcatCommEx(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatErrorCode(short core, short axis, out ushort pErrorCode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetDcError(short core, out short pError);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisMode(short core, short axis, short mode);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisMode(short core, short axis, out ushort drvMode);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisPV(short core, short axis, int velocity);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisPT(short core, short axis, short torque);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatEncPos(short core, short axis, out int pEncPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAuxEncPos(short core, short axis, out int pAuxEncPos);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatEncVel(short core, short axis, out int pVelocity);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisAtlCurrent(short core, short axis, out short pCur);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisAtlTorque(short core, short axis, out short pTorque);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisAV(short core, short axis, out int pVel);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisACT(short core, short axis, out short pCur, out short pTorque);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatMCType(short core, out short pType);
        [DllImport("gts.dll")]
        public static extern short GTN_SetPosScale(short core, short axis, ushort posScale);
        [DllImport("gts.dll")]
        public static extern short GTN_GetPosScale(short core, short axis, out ushort posScale);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisAI(short core, short axis, short channel, out short pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisDI(short core, short axis, out uint pDi);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisDO(short core, short axis, uint DoValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisDO(short core, short axis, out uint pDo);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisDOBit(short core, short axis, short bitOffset, byte DoBitValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisDOBit(short core, short axis, short bitOffset, out byte pDoBitValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatSlaves(short core, out short pSlaveMotionCnt, out short pSlaveIOCnt);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatSlaveInfo(short core, short slave, out TSlaveInfo pSlaveinfo);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisPE(short core, short axis, out int pPosErr);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatRawData(short core, ushort offset, ushort nByteSize, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatRawData(short core, ushort offset, ushort nByteSize, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_SetEcatAxisTorqueOffset(short core, short axis, short torqueOffset);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisTorqueOffset(short core, short axis, out short pTorqueOffset);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatPdoLength(short core, out short pPdoLen);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAuxEncoderCapture(short core, short encoder, short CapPrm);
        [DllImport("gts.dll")]
        public static extern short GTN_SetAuxEncoderCaptureEx(short core, short encoder, short CapUpEnable, short CapUpIO, short CapDnEnable, short CapDnIO);
        [DllImport("gts.dll")]
        public static extern short GTN_GetAuxEncoderCaptureStatus(short core, short encoder, out ushort CapSts, out int PosValue, out int NegValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatSDODownload(short core, ushort slave, ushort index, byte subindex, ref byte data, uint data_size, out uint abort_code);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatSDOUpload(short core, ushort slave, ushort index, byte subindex, out byte target, uint target_size, out uint result_size, out uint abort_code);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOReadInput(short core, ushort slave, ushort offset, ushort nSize, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOReadOutput(short core, ushort slaveno, ushort offset, ushort nSize, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOWriteOutput(short core, ushort slave, ushort offset, ushort nSize, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOBitReadInput(short core, ushort slaveno, ushort offset, ushort Index, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOBitReadOutput(short core, ushort slaveno, ushort offset, ushort Index, ref byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOBitWriteOutput(short core, ushort slaveno, ushort offset, short Index, byte value);
        [DllImport("gts.dll")]
        public static extern short GTN_EcatIOSynch(short core);
        [DllImport("gts.dll")]
        public static extern short GTN_GetEcatAxisPdoData(short core, short axis, ushort objectWord, out byte pValue);
        [DllImport("gts.dll")]
        public static extern short GTN_RelateEcatSlaveToMcGpoBit(short core, short gpoIndex, ushort slave, short slaveType, short bitOffset, short pdoOffset);

        [DllImport("gts.dll")]
        public static extern short GTN_RelateEcatSlaveToMcGpiBit(short core, short gpoIndex, ushort slave, short slaveType, short bitOffset, short pdoOffset);
    }
}

using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 仪器链接配置服务：负责配置的读取与保存，
    /// 界面层只依赖本接口，不关心存储介质与格式。
    /// </summary>
    public interface IInstrumentConfigService
    {
        /// <summary>读取配置；文件不存在或损坏时返回默认配置</summary>
        InstrumentConnectionConfig Load();

        /// <summary>保存配置到持久化存储</summary>
        void Save(InstrumentConnectionConfig config);
    }
}

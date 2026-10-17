using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 烧录工位配置服务：负责 flashing.json 的读取与保存。
    /// 与仪器链接配置分开两套：烧录页改得频繁，且它跟测试工位的仪器清单没有任何关系。
    /// </summary>
    public interface IFlashingConfigService
    {
        /// <summary>读取配置；文件不存在或损坏时回退默认配置（主文件不可用会先试上一份 .bak）</summary>
        FlashingConfig Load();

        /// <summary>保存配置（临时文件 + 改名替换，覆盖前把旧档留成 .bak）</summary>
        void Save(FlashingConfig config);
    }
}

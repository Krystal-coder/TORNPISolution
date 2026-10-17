using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>
    /// 单板工位运行配置服务：勾了哪几项测试、哪几路参与，落在 Config\board-test.json。
    /// 单独一份文件是因为它既不属于仪器链接配置（那些是设备参数），也不属于测试方案
    /// （单板这一页不组方案），跟着方案走会让人以为勾选项是按方案各存一份的。
    /// </summary>
    public interface IBoardTestConfigService
    {
        /// <summary>读回来；文件不存在或坏了就返回默认配置（勾选项与通道清单都不为空）</summary>
        BoardTestConfig Load();

        /// <summary>整档覆盖保存。写失败抛出去让界面提示，别让人以为勾已经存住了</summary>
        void Save(BoardTestConfig config);
    }
}

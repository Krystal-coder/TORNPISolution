using TORNPISolution.Share.Models;

namespace TORNPISolution.Interface
{
    /// <summary>产品协议命令码配置服务：负责 16 条 Ctrl* 命令码的 JSON 读取与保存</summary>
    public interface IProtocolCommandConfigService
    {
        /// <summary>加载命令码配置；文件不存在时自动生成默认 JSON 并返回默认值</summary>
        Dictionary<string, ProtocolCommandEntry> Load();

        /// <summary>保存命令码配置到 JSON 文件</summary>
        void Save(Dictionary<string, ProtocolCommandEntry> commands);
    }
}

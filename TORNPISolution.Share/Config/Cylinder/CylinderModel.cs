using System.Text.Json.Serialization;
namespace TORNPISolution.Share.Config.Cylinder
{
    /// <summary>
    /// 气缸配置
    /// </summary>
    public class CylinderModel
    {
        public int id { get; set; }
        public string? Describe { get; set; }
        public bool IsCylinderEnabled { get; set; }
        public bool IsExtend { get; set; }
        public short InputChannel { get; set; }
        public List<InterlockingPointPositionModel>? InterlockingPointPosition { get; set; }
        public int TimeOut { get; set; }
        public List<DIModel>? Sensor { get; set; }
        [JsonIgnore]
        public int CylinderPosition {  get; set; }
    }
}

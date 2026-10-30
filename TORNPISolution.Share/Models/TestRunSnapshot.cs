namespace TORNPISolution.Share.Models
{
    /// <summary>
    /// 一轮测试的入库快照：UI 从方案模块树采集后交给 <see cref="Interface.ITestDataService"/> 落库。
    /// 与界面解耦——保存服务只认快照，不认识 ViewModel；未来单板/烧录工位采自己的快照走同一条保存链路。
    /// </summary>
    public class TestRunSnapshot
    {
        public string Station { get; set; } = "整机";

        /// <summary>方案名（未保存/未加载方案时传"未命名方案"）</summary>
        public string PlanName { get; set; } = string.Empty;

        /// <summary>本轮模块 ID 列表（如 A07/A17/A04），存为 ModulesJson 快照</summary>
        public List<string> ModuleIds { get; set; } = new();

        /// <summary>各模块（大项）本轮用时，按执行序号对齐测试项的 Sequence</summary>
        public List<TestModuleTiming> ModuleTimings { get; set; } = new();

        /// <summary>本轮血压量测时间（仪器链接配置里的稳压保持秒数快照，null = 未采集）</summary>
        public int? MeasureSeconds { get; set; }

        public DateTime StartedAt { get; set; }

        /// <summary>方案收口时间（报告编号按这一天的序号排）</summary>
        public DateTime FinishedAt { get; set; }

        /// <summary>产品 MAC（UI 传空时由保存服务从 A16「产品信息获取(MAC地址)」结果自动带入）</summary>
        public string? ProductMAC { get; set; }

        /// <summary>产品 SN 与测试员工号：列预留，当前传 null</summary>
        public string? ProductSN { get; set; }

        public string? Operator { get; set; }

        /// <summary>本轮全部测试项（含未测项——报表里能分清"跑了没测到"和"没排这项"）</summary>
        public List<TestItemSnapshot> Items { get; set; } = new();
    }

    /// <summary>一个测试项的入库快照（一行一项，动态项名原样带）</summary>
    public class TestItemSnapshot
    {
        public string ModuleId { get; set; } = string.Empty;

        public string ModuleName { get; set; } = string.Empty;

        /// <summary>模块在方案内的执行序号（从 1 开始）</summary>
        public int Sequence { get; set; }

        public string ItemName { get; set; } = string.Empty;

        public string Unit { get; set; } = string.Empty;

        /// <summary>值来源口径：Instrument / Product / Both / Error / Points</summary>
        public string ValueSource { get; set; } = "Instrument";

        public double? Value { get; set; }

        public double? InstrumentValue { get; set; }

        public double? ProductValue { get; set; }

        public string? InstrumentText { get; set; }

        public string? ProductText { get; set; }

        /// <summary>本次运行规格快照（血压类按仪器读回现算）</summary>
        public double? SpecMin { get; set; }

        public double? SpecMax { get; set; }

        public string SpecText { get; set; } = string.Empty;

        public TestResultStatus Result { get; set; } = TestResultStatus.NotTested;

        /// <summary>只展示不判定（如 BP-005 平均值项）：不计入模块判定</summary>
        public bool DisplayOnly { get; set; }

        /// <summary>不判定（预留项/未配置判定区间）：保持未测试</summary>
        public bool NotJudged { get; set; }

        /// <summary>本项耗时（秒）：上一项上报（模块首项取派发时刻）到本项上报的墙钟；null = 本轮没跑到这项</summary>
        public double? DurationSeconds { get; set; }

        /// <summary>逐点子表（RepeatStats 行只入库/出报表，UI 子表不显示）</summary>
        public List<TestPointSnapshot> Points { get; set; } = new();
    }

    /// <summary>一个模块（大项）的本轮用时：模块从派发到收尾的实际墙钟秒数</summary>
    public class TestModuleTiming
    {
        /// <summary>模块在方案内的执行序号（与 TestItemSnapshot.Sequence 对齐）</summary>
        public int Sequence { get; set; }

        public double Seconds { get; set; }
    }

    /// <summary>一个逐点子表行的入库快照</summary>
    public class TestPointSnapshot
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>列口径：Point / Channel / Group / Repeat / RepeatMatrix / Event / Slice / RepeatStats</summary>
        public string Schema { get; set; } = "Point";

        public string Unit { get; set; } = string.Empty;

        public double TargetPressure { get; set; }

        public double? InstrumentValue { get; set; }

        public double? ProductValue { get; set; }

        public double? Error { get; set; }

        public bool Pass { get; set; }

        public string Remark { get; set; } = string.Empty;

        /// <summary>多轮统计量（重复性图表的数据来源；非统计行为 null）</summary>
        public double? Average { get; set; }

        public double? Max { get; set; }

        public double? Min { get; set; }

        public double? Range { get; set; }

        /// <summary>自定义单元格文本（Event/Slice 口径）</summary>
        public List<string>? Cells { get; set; }

        /// <summary>末列是否显示判定（统计中间行为 false）</summary>
        public bool ShowVerdict { get; set; } = true;
    }

    /// <summary>保存结果：报告编号 + 各项计数（UI toast 报数用）</summary>
    public class TestRunSaveResult
    {
        /// <summary>入库后的轮次 ID（历史页查明细用）</summary>
        public int RunId { get; init; }

        /// <summary>报告编号：日期+当天序号（如 20261022-0001），toast/列表/PDF 文件名同一个号</summary>
        public string ReportNo { get; init; } = string.Empty;

        /// <summary>参与判定的测试项计数（只展示/不判定项不计入）</summary>
        public int PassCount { get; init; }

        public int FailCount { get; init; }

        public int NotTestedCount { get; init; }

        /// <summary>逐点子表行数</summary>
        public int PointCount { get; init; }
    }
}

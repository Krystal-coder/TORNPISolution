namespace TORNPISolution.ViewModels
{
    /// <summary>
    /// 行详情子表的列定义（动态列头）
    /// </summary>
    public sealed class SubTableColumn
    {
        /// <summary>列头文本</summary>
        public string Header { get; init; } = string.Empty;

        /// <summary>列宽（像素，表头与该列单元格必须同值，否则错位）</summary>
        public double Width { get; init; } = 80;

        /// <summary>是否左对齐（文本列左对齐，数值列居中）</summary>
        public bool LeftAlign { get; init; }
    }

    /// <summary>子表单元格（文本 + 与所属列一致的宽度 + 配色用途）</summary>
    public sealed class SubTableCell
    {
        public string Text { get; init; } = string.Empty;
        public double Width { get; init; } = 80;
        public bool LeftAlign { get; init; }

        /// <summary>配色用途：main 正文 / sub 次要 / bold 强调 / stat 统计行 / pass 合格 / fail 不合格 / head 列头</summary>
        public string Tone { get; init; } = "main";
    }

    /// <summary>子表数据行（单元格顺序与 SubTableColumn 一一对应，末列为判定）</summary>
    public sealed class SubTableRow
    {
        public IReadOnlyList<SubTableCell> Cells { get; init; } = Array.Empty<SubTableCell>();
    }

    /// <summary>单元格配色用途键（界面样式按此触发器上色，避开把 WPF Brush 塞进 ViewModel）</summary>
    public static class SubTableTone
    {
        public const string Main = "main";
        public const string Sub = "sub";
        public const string Bold = "bold";
        public const string Stat = "stat";
        public const string Pass = "pass";
        public const string Fail = "fail";
        public const string Head = "head";
    }
}

namespace TORNPISolution.Data
{
    /// <summary>
    /// 测试数据文件路径
    /// </summary>
    public static class TestDataPaths
    {
        public static string RootDirectory { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"TORNPISolution", "TestData");

        public static string DatabaseFile { get; } = Path.Combine(RootDirectory, "tornpi_test.db");

        /// <summary>PDF 报告目录：输出目录下的 TestData\Reports，文件名 = 报告编号.pdf</summary>
        public static string ReportsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "TestData", "Reports");

        public static string ConnectionString { get; } = $"Data Source={DatabaseFile}";

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(ReportsDirectory);
        }
    }
}

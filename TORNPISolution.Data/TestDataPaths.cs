namespace TORNPISolution.Data
{
    /// <summary>
    /// 测试数据文件路径：库文件与 PDF 报告存于 %APPDATA%\TORNPISolution\TestData（与程序目录的 Config 不同路）。
    /// 不放程序目录：Rebuild、清理输出、换构建配置都会连测试历史一起删掉，而历史数据与已签发的报告不可再生。
    /// </summary>
    public static class TestDataPaths
    {
        public static string RootDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "TORNPISolution", "TestData");

        public static string DatabaseFile { get; } = Path.Combine(RootDirectory, "tornpi_test.db");

        /// <summary>PDF 报告目录：文件名 = 报告编号.pdf</summary>
        public static string ReportsDirectory { get; } = Path.Combine(RootDirectory, "Reports");

        public static string ConnectionString { get; } = $"Data Source={DatabaseFile}";

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(RootDirectory);
            Directory.CreateDirectory(ReportsDirectory);
        }
    }
}

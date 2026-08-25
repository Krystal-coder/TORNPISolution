using Microsoft.EntityFrameworkCore;

namespace TORNPISolution.Data
{
    /// <summary>
    /// 数据库上下文（预留）。
    /// 后续接入数据库时：
    /// 1. 按所选数据库引入对应 Provider 包（如 Microsoft.EntityFrameworkCore.Sqlite / SqlServer）；
    /// 2. 在 OnConfiguring 或外部 DbContextOptions 中配置连接字符串；
    /// 3. 在此声明各实体的 DbSet 并在 OnModelCreating 中配置映射。
    /// </summary>
    public class TornpiDbContext : DbContext
    {
        public TornpiDbContext()
        {
        }

        public TornpiDbContext(DbContextOptions<TornpiDbContext> options)
            : base(options)
        {
        }

        // 示例（启用时取消注释并建好实体类）：
        // public DbSet<TestRecordEntity> TestRecords => Set<TestRecordEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}

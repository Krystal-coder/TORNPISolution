using Microsoft.EntityFrameworkCore;

namespace TORNPISolution.Data
{
    /// <summary>
    /// 数据库上下文（EF Core，预留，未接入主流程）。
    /// 接入时：按所选数据库引入 Provider 包（如 Microsoft.EntityFrameworkCore.Sqlite / SqlServer）、配置连接字符串、在此声明各实体 DbSet 并在 OnModelCreating 中配置映射。
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

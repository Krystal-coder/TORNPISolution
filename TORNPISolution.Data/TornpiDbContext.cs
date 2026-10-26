using Microsoft.EntityFrameworkCore;

namespace TORNPISolution.Data
{
    /// <summary>
    /// 数据库上下文（EF Core + SQLite）：轮次(TestRun) → 测试项(TestItemResult) → 逐点(TestPointResult) 三张表。
    /// 表结构按数据形态建、不按模块/工位建：未来单板/烧录复用同一套表，用 TestRun.Station 区分来源。
    /// 绿场用 EnsureCreated 建库（库文件位置见 <see cref="TestDataPaths"/>），以后加列改迁移或提升 PRAGMA user_version。
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

        public DbSet<TestRunEntity> TestRuns => Set<TestRunEntity>();

        public DbSet<TestItemResultEntity> TestItemResults => Set<TestItemResultEntity>();

        public DbSet<TestPointResultEntity> TestPointResults => Set<TestPointResultEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // DI 没配连接串时兜底默认库
            if (!optionsBuilder.IsConfigured)
            {
                TestDataPaths.EnsureDirectories();
                optionsBuilder.UseSqlite(TestDataPaths.ConnectionString);
            }

            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<TestRunEntity>(e =>
            {
                e.ToTable("TestRun");
                e.HasKey(x => x.Id);
                // 撞号（同日重复保存）靠这个索引抛异常，由 UI 提示重试
                e.HasIndex(x => x.ReportNo).IsUnique();
                e.HasIndex(x => x.FinishedAt);
                e.Property(x => x.ReportNo).IsRequired().HasMaxLength(32);
                e.Property(x => x.Station).IsRequired().HasMaxLength(16);
                e.Property(x => x.PlanName).IsRequired().HasMaxLength(128);
                e.Property(x => x.ModulesJson).IsRequired();
                e.Property(x => x.OverallResult).IsRequired().HasMaxLength(16);
                // 删轮次连带删测试项与逐点，不留孤儿行
                e.HasMany(x => x.Items)
                    .WithOne(x => x.Run!)
                    .HasForeignKey(x => x.RunId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TestItemResultEntity>(e =>
            {
                e.ToTable("TestItemResult");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.RunId);
                e.Property(x => x.ModuleId).IsRequired().HasMaxLength(16);
                e.Property(x => x.ModuleName).IsRequired().HasMaxLength(64);
                e.Property(x => x.ItemName).IsRequired().HasMaxLength(128);
                e.Property(x => x.Unit).HasMaxLength(16);
                e.Property(x => x.ValueSource).HasMaxLength(16);
                e.Property(x => x.SpecText).HasMaxLength(128);
                e.Property(x => x.Result).IsRequired().HasMaxLength(16);
                e.HasMany(x => x.Points)
                    .WithOne(x => x.ItemResult!)
                    .HasForeignKey(x => x.ItemResultId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TestPointResultEntity>(e =>
            {
                e.ToTable("TestPointResult");
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.ItemResultId);
                e.Property(x => x.Name).IsRequired().HasMaxLength(64);
                e.Property(x => x.Schema).IsRequired().HasMaxLength(16);
                e.Property(x => x.Unit).HasMaxLength(16);
                e.Property(x => x.Remark).HasMaxLength(256);
            });
        }
    }
}

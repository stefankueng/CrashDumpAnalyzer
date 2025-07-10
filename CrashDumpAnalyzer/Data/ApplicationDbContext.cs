using CrashDumpAnalyzer.Models;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CrashDumpAnalyzer.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<DumpFileInfo>? DumpFileInfos { get; set; }
        public DbSet<DumpCallstack>? DumpCallstacks { get; set; }
        public DbSet<LogFileData>? LogFileDatas { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<DumpFileInfo>().ToTable("DumpFileInfo");
            modelBuilder.Entity<DumpCallstack>().ToTable("DumpCallstack");
            modelBuilder.Entity<LogFileData>().ToTable("LogFileData");

            // Configure cascade delete for all foreign key relationships

            // DumpFileInfo -> DumpCallstack (already configured in migration, but explicit here)
            modelBuilder.Entity<DumpFileInfo>()
                .HasOne(d => d.DumpCallstack)
                .WithMany(dc => dc.DumpInfos)
                .HasForeignKey(d => d.DumpCallstackId)
                .OnDelete(DeleteBehavior.Cascade);

            // LogFileData -> DumpCallstack (needs cascade delete)
            modelBuilder.Entity<LogFileData>()
                .HasOne<DumpCallstack>()
                .WithMany(dc => dc.LogFileDatas)
                .HasForeignKey("DumpCallstackId")
                .OnDelete(DeleteBehavior.Cascade);

            // LogFileData -> DumpFileInfo (needs cascade delete)
            modelBuilder.Entity<LogFileData>()
                .HasOne(l => l.DumpFileInfo)
                .WithMany()
                .HasForeignKey("DumpFileInfoId")
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}

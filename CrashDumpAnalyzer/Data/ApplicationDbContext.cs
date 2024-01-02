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

		protected override void OnModelCreating(ModelBuilder modelBuilder)
		{
			base.OnModelCreating(modelBuilder);
			modelBuilder.Entity<DumpFileInfo>().ToTable("DumpFileInfo");
			modelBuilder.Entity<DumpCallstack>().ToTable("DumpCallstack");
		}
	}
}

namespace ConsoleApp1
{
	using System;
	using System.Data.Entity;
	using System.ComponentModel.DataAnnotations.Schema;
	using System.Linq;

	public partial class CSPAContext : DbContext
	{
		public CSPAContext()
			: base("name=CSPAContext")
		{
		}

		public virtual DbSet<maintable> maintables { get; set; }

		protected override void OnModelCreating(DbModelBuilder modelBuilder)
		{
			modelBuilder.Entity<maintable>()
				.Property(e => e.timestamp)
				.HasPrecision(6);

			modelBuilder.Entity<maintable>()
				.Property(e => e.username)
				.IsUnicode(false);

			modelBuilder.Entity<maintable>()
				.Property(e => e.role)
				.IsUnicode(false);

			modelBuilder.Entity<maintable>()
				.Property(e => e.point)
				.IsUnicode(false);

			modelBuilder.Entity<maintable>()
				.Property(e => e.message)
				.IsUnicode(false);

			modelBuilder.Entity<maintable>()
				.Property(e => e.source)
				.IsUnicode(false);
		}
	}
}

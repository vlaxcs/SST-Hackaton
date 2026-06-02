using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SST_Hackaton.Models;

namespace SST_Hackaton.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
	public DbSet<Note> Notes => Set<Note>();
	public DbSet<NoteAttachment> NoteAttachments => Set<NoteAttachment>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);

		builder.Entity<NoteAttachment>()
			.HasOne(a => a.Note)
			.WithMany(n => n.Attachments)
			.HasForeignKey(a => a.NoteId)
			.OnDelete(DeleteBehavior.Cascade);
	}

	public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		var utcNow = DateTime.UtcNow;
		foreach (var entry in ChangeTracker.Entries<Note>())
		{
			if (entry.State == EntityState.Added)
			{
				entry.Entity.CreatedAtUtc = utcNow;
				entry.Entity.ModifiedAtUtc = utcNow;
			}

			if (entry.State == EntityState.Modified)
			{
				entry.Entity.ModifiedAtUtc = utcNow;
			}
		}

		return base.SaveChangesAsync(cancellationToken);
	}
}

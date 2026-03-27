using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SST_Hackaton.Models;

namespace SST_Hackaton.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
	public DbSet<Note> Notes => Set<Note>();
	public DbSet<NoteType> NoteTypes => Set<NoteType>();
	public DbSet<NoteAttachment> NoteAttachments => Set<NoteAttachment>();

	protected override void OnModelCreating(ModelBuilder builder)
	{
		base.OnModelCreating(builder);

		builder.Entity<NoteAttachment>()
			.HasOne(a => a.Note)
			.WithMany(n => n.Attachments)
			.HasForeignKey(a => a.NoteId)
			.OnDelete(DeleteBehavior.Cascade);

		builder.Entity<NoteType>().HasData(
			new NoteType { Id = 1, Name = "Text" },
			new NoteType { Id = 2, Name = "Checkbox" },
			new NoteType { Id = 3, Name = "Audio" },
			new NoteType { Id = 4, Name = "Video" },
			new NoteType { Id = 5, Name = "Photo" },
			new NoteType { Id = 6, Name = "Drawing" });
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

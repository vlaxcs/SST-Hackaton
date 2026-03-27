using System.ComponentModel.DataAnnotations;

namespace SST_Hackaton.Models;

public class NoteAttachment
{
    public int Id { get; set; }

    [Required]
    public int NoteId { get; set; }
    public Note? Note { get; set; }

    [Required]
    [StringLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(255)]
    public string StoredFileName { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

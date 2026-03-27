using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace SST_Hackaton.Models;

public class Note
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Titlul este obligatoriu.")]
    [StringLength(200)]
    [Display(Name = "Titlu")]
    public string Title { get; set; } = string.Empty;

    [StringLength(5000)]
    [Display(Name = "Conținut")]
    public string Content { get; set; } = string.Empty;

    [StringLength(120)]
    [Display(Name = "Folder")]
    public string? FolderName { get; set; }

    [Display(Name = "Tip notiță")]
    public int NoteTypeId { get; set; }
    public NoteType? NoteType { get; set; }

    [Display(Name = "Creat la")]
    public DateTime CreatedAtUtc { get; set; }

    [Display(Name = "Modificat la")]
    public DateTime ModifiedAtUtc { get; set; }

    public string UserId { get; set; } = string.Empty;
    public IdentityUser? User { get; set; }

    public ICollection<NoteAttachment> Attachments { get; set; } = new List<NoteAttachment>();
}

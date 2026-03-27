using System.ComponentModel.DataAnnotations;

namespace SST_Hackaton.Models;

public class NoteType
{
    public int Id { get; set; }

    [Required]
    [StringLength(64)]
    public string Name { get; set; } = string.Empty;

    public ICollection<Note> Notes { get; set; } = new List<Note>();
}

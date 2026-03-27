using Microsoft.AspNetCore.Mvc.Rendering;

namespace SST_Hackaton.Models;

public class NoteIndexViewModel
{
    public List<Note> Notes { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? FolderName { get; set; }
    public int? NoteTypeId { get; set; }
    public string SortOrder { get; set; } = "modified_desc";
    public List<SelectListItem> NoteTypeOptions { get; set; } = new();
    public List<string> FolderOptions { get; set; } = new();
}

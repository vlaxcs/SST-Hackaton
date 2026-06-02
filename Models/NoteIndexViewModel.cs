namespace SST_Hackaton.Models;

public class NoteIndexViewModel
{
    public List<Note> Notes { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? FolderName { get; set; }
    public string SortOrder { get; set; } = "modified_desc";
    public List<string> FolderOptions { get; set; } = new();
}

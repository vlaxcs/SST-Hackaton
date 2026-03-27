using System.Text.Json;

namespace SST_Hackaton.Models;

public static class NoteContentHelper
{
    private static readonly JsonSerializerOptions ChecklistJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static List<ChecklistItem> ParseChecklistContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new List<ChecklistItem>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<ChecklistItem>>(content, ChecklistJsonOptions);
            if (parsed != null)
            {
                return parsed
                    .Where(i => !string.IsNullOrWhiteSpace(i.Text))
                    .Select(i => new ChecklistItem { Text = i.Text.Trim(), IsChecked = i.IsChecked })
                    .ToList();
            }
        }
        catch (JsonException)
        {
            // Fallback below for old plain text format.
        }

        return content
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => new ChecklistItem { Text = line, IsChecked = false })
            .ToList();
    }

    public static string SerializeChecklistContent(IEnumerable<ChecklistItem> items)
    {
        var normalized = items
            .Where(i => !string.IsNullOrWhiteSpace(i.Text))
            .Select(i => new ChecklistItem { Text = i.Text.Trim(), IsChecked = i.IsChecked })
            .ToList();

        return JsonSerializer.Serialize(normalized, ChecklistJsonOptions);
    }

    public static string NormalizeChecklistContent(string? content)
    {
        return SerializeChecklistContent(ParseChecklistContent(content));
    }

    public static bool IsCheckboxTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Checkbox", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTextTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Text", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAudioTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Audio", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVideoTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Video", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPhotoTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Photo", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDrawingTypeName(string? noteTypeName)
    {
        return string.Equals(noteTypeName, "Drawing", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAttachmentNoteTypeName(string? noteTypeName)
    {
        return IsAudioTypeName(noteTypeName) ||
               IsVideoTypeName(noteTypeName) ||
               IsPhotoTypeName(noteTypeName) ||
               IsDrawingTypeName(noteTypeName);
    }

    public static string NoteTypeNameRo(string? noteTypeName)
    {
        if (string.Equals(noteTypeName, "Text", StringComparison.OrdinalIgnoreCase))
        {
            return "Text";
        }

        if (string.Equals(noteTypeName, "Checkbox", StringComparison.OrdinalIgnoreCase))
        {
            return "Listă de bifat";
        }

        if (string.Equals(noteTypeName, "Audio", StringComparison.OrdinalIgnoreCase))
        {
            return "Înregistrare audio";
        }

        if (string.Equals(noteTypeName, "Video", StringComparison.OrdinalIgnoreCase))
        {
            return "Înregistrare video";
        }

        if (string.Equals(noteTypeName, "Photo", StringComparison.OrdinalIgnoreCase))
        {
            return "Fotografie";
        }

        if (string.Equals(noteTypeName, "Drawing", StringComparison.OrdinalIgnoreCase))
        {
            return "Desen";
        }

        return noteTypeName ?? string.Empty;
    }
}

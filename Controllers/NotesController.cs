using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Text.Json;
using System.Globalization;
using SST_Hackaton.Data;
using SST_Hackaton.Models;

namespace SST_Hackaton.Controllers;

[Authorize]
public class NotesController : Controller
{
    private const string NoteManifestFileName = "note.json";

    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IWebHostEnvironment _environment;
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public NotesController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment environment)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
    }

    public async Task<IActionResult> Index(string? searchTerm, string? folderName, string sortOrder = "modified_desc")
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var query = _context.Notes
            .AsNoTracking()
            .Include(n => n.Attachments)
            .Where(n => n.UserId == userId);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim();
            var queryByText = query.Where(n => n.Title.Contains(term) || n.Content.Contains(term));

            if (DateTime.TryParse(term, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsedDate))
            {
                var localDate = DateTime.SpecifyKind(parsedDate.Date, DateTimeKind.Local);
                var startUtc = localDate.ToUniversalTime();
                var endUtc = localDate.AddDays(1).ToUniversalTime();

                query = query.Where(n =>
                    n.Title.Contains(term) ||
                    n.Content.Contains(term) ||
                    (n.CreatedAtUtc >= startUtc && n.CreatedAtUtc < endUtc) ||
                    (n.ModifiedAtUtc >= startUtc && n.ModifiedAtUtc < endUtc));
            }
            else
            {
                query = queryByText;
            }
        }

        if (!string.IsNullOrWhiteSpace(folderName))
        {
            var normalizedFolder = folderName.Trim();
            query = query.Where(n => n.FolderName == normalizedFolder);
        }

        query = sortOrder switch
        {
            "title_asc" => query.OrderBy(n => n.Title),
            "title_desc" => query.OrderByDescending(n => n.Title),
            "created_asc" => query.OrderBy(n => n.CreatedAtUtc),
            "created_desc" => query.OrderByDescending(n => n.CreatedAtUtc),
            "modified_asc" => query.OrderBy(n => n.ModifiedAtUtc),
            _ => query.OrderByDescending(n => n.ModifiedAtUtc)
        };

        var folderOptions = await _context.Notes
            .AsNoTracking()
            .Where(n => n.UserId == userId && n.FolderName != null && n.FolderName != "")
            .Select(n => n.FolderName!)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync();

        var model = new NoteIndexViewModel
        {
            Notes = await query.ToListAsync(),
            SearchTerm = searchTerm,
            FolderName = folderName,
            SortOrder = sortOrder,
            FolderOptions = folderOptions
        };

        return View(model);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var note = await _context.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

        if (note == null)
        {
            return NotFound();
        }

        return View(note);
    }

    public IActionResult Create()
    {
        return View(new Note());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Content")] Note note, List<IFormFile>? uploadedFiles)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        note.Title = note.Title?.Trim() ?? string.Empty;
        note.UserId = userId;
        note.Content ??= string.Empty;
        ValidateAttachmentBatch(uploadedFiles);

        if (ModelState.IsValid)
        {
            _context.Add(note);
            await _context.SaveChangesAsync();

            await SaveUploadedFilesAsync(note, uploadedFiles);
            await _context.SaveChangesAsync();

            await KeepNewestNoteForTitleAsync(userId, note.Title);

            return RedirectToAction(nameof(Index));
        }

        return View(note);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Content")] Note note, List<IFormFile>? uploadedFiles)
    {
        if (id != note.Id)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var existingNote = await _context.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (existingNote == null)
        {
            return NotFound();
        }

        ValidateAttachmentBatch(uploadedFiles);

        if (ModelState.IsValid)
        {
            existingNote.Title = note.Title;
            existingNote.Content = note.Content ?? string.Empty;

            await SaveUploadedFilesAsync(existingNote, uploadedFiles);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!NoteExists(note.Id, userId))
                {
                    return NotFound();
                }

                throw;
            }

            return RedirectToAction(nameof(Details), new { id = existingNote.Id });
        }

        note.Attachments = existingNote.Attachments;
        return View("Details", note);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var note = await _context.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
        if (note == null)
        {
            return NotFound();
        }

        return View(note);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var note = await _context.Notes
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
        if (note != null)
        {
            foreach (var attachment in note.Attachments)
            {
                DeleteStoredFile(note.UserId, attachment);
            }

            _context.Notes.Remove(note);
            await _context.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete(string? selectedNoteIds)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var ids = ParseSelectedIds(selectedNoteIds);
        if (ids.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        var notes = await _context.Notes
            .Include(n => n.Attachments)
            .Where(n => n.UserId == userId && ids.Contains(n.Id))
            .ToListAsync();

        foreach (var note in notes)
        {
            foreach (var attachment in note.Attachments)
            {
                DeleteStoredFile(note.UserId, attachment);
            }
        }

        _context.Notes.RemoveRange(notes);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkMoveToFolder(string? selectedNoteIds, string? folderName)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var ids = ParseSelectedIds(selectedNoteIds);
        var normalizedFolderName = (folderName ?? string.Empty).Trim();
        if (ids.Count == 0 || string.IsNullOrWhiteSpace(normalizedFolderName))
        {
            return RedirectToAction(nameof(Index));
        }

        var notes = await _context.Notes
            .Where(n => n.UserId == userId && ids.Contains(n.Id))
            .ToListAsync();

        foreach (var note in notes)
        {
            note.FolderName = normalizedFolderName;
        }

        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadAllAsZip()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var notes = await _context.Notes
            .AsNoTracking()
            .Include(n => n.Attachments)
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.ModifiedAtUtc)
            .ToListAsync();

        if (notes.Count == 0)
        {
            TempData["ImportExportMessage"] = "Nu există notițe pentru export.";
            return RedirectToAction(nameof(Index));
        }

        return await BuildNotesZipResultAsync(userId, notes, "toate-notitele");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DownloadSelectedAsZip(string? selectedNoteIds)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var ids = ParseSelectedIds(selectedNoteIds);
        if (ids.Count == 0)
        {
            TempData["ImportExportMessage"] = "Selectează cel puțin o notiță pentru export.";
            return RedirectToAction(nameof(Index));
        }

        var notes = await _context.Notes
            .AsNoTracking()
            .Include(n => n.Attachments)
            .Where(n => n.UserId == userId && ids.Contains(n.Id))
            .OrderByDescending(n => n.ModifiedAtUtc)
            .ToListAsync();

        if (notes.Count == 0)
        {
            TempData["ImportExportMessage"] = "Nu s-au găsit notițele selectate pentru export.";
            return RedirectToAction(nameof(Index));
        }

        return await BuildNotesZipResultAsync(userId, notes, "notite-selectate");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportZip(IFormFile? importZip)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        if (importZip == null || importZip.Length == 0)
        {
            TempData["ImportExportMessage"] = "Selectează un fișier ZIP valid pentru import.";
            return RedirectToAction(nameof(Index));
        }

        if (Path.GetExtension(importZip.FileName).ToLowerInvariant() != ".zip")
        {
            TempData["ImportExportMessage"] = "Fișierul de import trebuie să fie de tip ZIP.";
            return RedirectToAction(nameof(Index));
        }

        var provider = new FileExtensionContentTypeProvider();
        var importedCount = 0;
        var highlightedImportedNoteIds = new HashSet<int>();

        await using var buffer = new MemoryStream();
        await importZip.CopyToAsync(buffer);
        buffer.Position = 0;

        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntries = archive.Entries
            .Where(e => e.FullName.EndsWith($"/{NoteManifestFileName}", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (manifestEntries.Count == 0)
        {
            TempData["ImportExportMessage"] = "ZIP-ul nu conține structura așteptată pentru import.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var manifestEntry in manifestEntries)
        {
            ExportedNoteDto? manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<ExportedNoteDto>(manifestStream, ExportJsonOptions);
            }

            if (manifest == null)
            {
                continue;
            }

            manifest.Attachments ??= new List<ExportedAttachmentDto>();

            var importedNote = new Note
            {
                UserId = userId,
                Title = string.IsNullOrWhiteSpace(manifest.Title) ? "Notiță importată" : manifest.Title.Trim(),
                Content = manifest.Content ?? string.Empty,
                FolderName = string.IsNullOrWhiteSpace(manifest.FolderName) ? null : manifest.FolderName.Trim()
            };

            _context.Notes.Add(importedNote);
            await _context.SaveChangesAsync();

            var noteBasePath = manifestEntry.FullName[..^NoteManifestFileName.Length];

            foreach (var attachment in manifest.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.RelativePath))
                {
                    continue;
                }

                var relativePath = attachment.RelativePath.Replace('\\', '/').TrimStart('/');
                var entryPath = $"{noteBasePath}{relativePath}";
                var zipFileEntry = archive.GetEntry(entryPath);
                if (zipFileEntry == null || string.IsNullOrEmpty(zipFileEntry.Name))
                {
                    continue;
                }

                var originalFileName = string.IsNullOrWhiteSpace(attachment.OriginalFileName)
                    ? zipFileEntry.Name
                    : Path.GetFileName(attachment.OriginalFileName);

                var safeOriginalName = string.IsNullOrWhiteSpace(originalFileName) ? "fisier" : originalFileName;
                var extension = Path.GetExtension(safeOriginalName);
                var storedFileName = $"{Guid.NewGuid():N}{extension}";

                var uploadDir = Path.Combine(GetStorageRoot(), userId, importedNote.Id.ToString(CultureInfo.InvariantCulture));
                Directory.CreateDirectory(uploadDir);

                var targetPath = Path.Combine(uploadDir, storedFileName);
                await using (var source = zipFileEntry.Open())
                await using (var target = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(target);
                }

                var contentType = attachment.ContentType;
                if (string.IsNullOrWhiteSpace(contentType))
                {
                    contentType = provider.TryGetContentType(safeOriginalName, out var mappedType)
                        ? mappedType
                        : "application/octet-stream";
                }

                _context.NoteAttachments.Add(new NoteAttachment
                {
                    NoteId = importedNote.Id,
                    OriginalFileName = safeOriginalName,
                    StoredFileName = storedFileName,
                    ContentType = contentType,
                    SizeBytes = zipFileEntry.Length,
                    UploadedAtUtc = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();
            var keptNoteId = await KeepNewestNoteForTitleAsync(userId, importedNote.Title);
            if (keptNoteId.HasValue)
            {
                highlightedImportedNoteIds.Add(keptNoteId.Value);
            }

            importedCount++;
        }

        TempData["ImportExportMessage"] = importedCount == 0
            ? "Importul nu a adăugat notițe. Verifică formatul ZIP-ului."
            : $"Import finalizat: {importedCount} notițe adăugate.";

        if (highlightedImportedNoteIds.Count > 0)
        {
            TempData["ImportedNoteHighlightIds"] = string.Join(',', highlightedImportedNoteIds);
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> DownloadAttachment(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var attachment = await _context.NoteAttachments
            .AsNoTracking()
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id && a.Note != null && a.Note.UserId == userId);

        if (attachment == null || attachment.Note == null)
        {
            return NotFound();
        }

        var fullPath = GetAttachmentPath(attachment.Note.UserId, attachment);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, attachment.ContentType, attachment.OriginalFileName);
    }

    [HttpGet]
    public async Task<IActionResult> PreviewAttachment(int id)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var attachment = await _context.NoteAttachments
            .AsNoTracking()
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id && a.Note != null && a.Note.UserId == userId);

        if (attachment == null || attachment.Note == null)
        {
            return NotFound();
        }

        var fullPath = GetAttachmentPath(attachment.Note.UserId, attachment);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        return PhysicalFile(fullPath, attachment.ContentType, enableRangeProcessing: true);
    }

    [HttpGet("Notes/DeleteAttachment/{id:int}")]
    public async Task<IActionResult> DeleteAttachment(int id, int? noteId)
    {
        return await DeleteAttachmentCore(id, noteId);
    }

    [HttpPost("Notes/DeleteAttachment/{id:int}")]
    [ValidateAntiForgeryToken]
    [ActionName("DeleteAttachment")]
    public async Task<IActionResult> DeleteAttachmentPost(int id, int? noteId)
    {
        return await DeleteAttachmentCore(id, noteId);
    }

    private async Task<IActionResult> DeleteAttachmentCore(int id, int? noteId)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var attachment = await _context.NoteAttachments
            .Include(a => a.Note)
            .FirstOrDefaultAsync(a => a.Id == id && a.Note != null && a.Note.UserId == userId);

        if (attachment == null)
        {
            return NotFound();
        }

        if (noteId.HasValue && noteId.Value != attachment.NoteId)
        {
            return RedirectToAction(nameof(Details), new { id = attachment.NoteId });
        }

        DeleteStoredFile(userId, attachment);
        _context.NoteAttachments.Remove(attachment);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Details), new { id = attachment.NoteId });
    }

    private static HashSet<int> ParseSelectedIds(string? selectedNoteIds)
    {
        if (string.IsNullOrWhiteSpace(selectedNoteIds))
        {
            return new HashSet<int>();
        }

        return selectedNoteIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => int.TryParse(v, out var id) ? id : 0)
            .Where(id => id > 0)
            .ToHashSet();
    }

    private async Task<FileContentResult> BuildNotesZipResultAsync(string userId, IReadOnlyCollection<Note> notes, string exportKind)
    {
        await using var zipBuffer = new MemoryStream();
        using (var archive = new ZipArchive(zipBuffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var note in notes)
            {
                var folderName = BuildNoteFolderName(note);
                var manifest = new ExportedNoteDto
                {
                    Title = note.Title,
                    Content = note.Content,
                    FolderName = note.FolderName,
                    NoteTypeId = null,
                    NoteTypeName = "Mixed",
                    CreatedAtUtc = note.CreatedAtUtc,
                    ModifiedAtUtc = note.ModifiedAtUtc,
                    Attachments = new List<ExportedAttachmentDto>()
                };

                var usedAttachmentNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var attachment in note.Attachments.OrderBy(a => a.Id))
                {
                    var sourcePath = GetAttachmentPath(userId, attachment);
                    if (!System.IO.File.Exists(sourcePath))
                    {
                        continue;
                    }

                    var safeFileName = BuildUniqueFileName(
                        Path.GetFileName(attachment.OriginalFileName),
                        usedAttachmentNames);
                    var relativePath = $"attachments/{safeFileName}";
                    var zipPath = $"{folderName}/{relativePath}";

                    var entry = archive.CreateEntry(zipPath, CompressionLevel.Optimal);
                    await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await using (var destination = entry.Open())
                    {
                        await source.CopyToAsync(destination);
                    }

                    manifest.Attachments.Add(new ExportedAttachmentDto
                    {
                        OriginalFileName = attachment.OriginalFileName,
                        RelativePath = relativePath,
                        ContentType = attachment.ContentType,
                        SizeBytes = attachment.SizeBytes
                    });
                }

                var manifestEntry = archive.CreateEntry($"{folderName}/{NoteManifestFileName}", CompressionLevel.Optimal);
                await using var manifestStream = manifestEntry.Open();
                await JsonSerializer.SerializeAsync(manifestStream, manifest, ExportJsonOptions);
            }
        }

        zipBuffer.Position = 0;
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"{exportKind}-{timestamp}.zip";
        return File(zipBuffer.ToArray(), "application/zip", fileName);
    }

    private static string BuildNoteFolderName(Note note)
    {
        var titlePart = string.IsNullOrWhiteSpace(note.Title) ? "notita" : note.Title;
        var safeTitle = SanitizeForFileName(titlePart);
        return $"{safeTitle}-{note.Id}";
    }

    private static string BuildUniqueFileName(string? requestedName, ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName) ? "fisier" : requestedName;
        var sanitized = SanitizeForFileName(baseName);
        var extension = Path.GetExtension(sanitized);
        var stem = Path.GetFileNameWithoutExtension(sanitized);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "fisier";
        }

        var candidate = string.IsNullOrWhiteSpace(extension) ? stem : $"{stem}{extension}";
        var index = 1;
        while (!usedNames.Add(candidate))
        {
            candidate = string.IsNullOrWhiteSpace(extension)
                ? $"{stem}-{index}"
                : $"{stem}-{index}{extension}";
            index++;
        }

        return candidate;
    }

    private static string SanitizeForFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value
            .Trim()
            .Select(ch => invalid.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(cleaned) ? "notita" : cleaned;
    }

    private async Task<int?> KeepNewestNoteForTitleAsync(string userId, string? title)
    {
        var normalizedTitle = NormalizeTitleForDuplicate(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            return null;
        }

        var notesWithSameTitle = await _context.Notes
            .Include(n => n.Attachments)
            .Where(n => n.UserId == userId && n.Title.Trim().ToLower() == normalizedTitle)
            .OrderByDescending(n => n.ModifiedAtUtc)
            .ThenByDescending(n => n.Id)
            .ToListAsync();

        if (notesWithSameTitle.Count == 0)
        {
            return null;
        }

        var newest = notesWithSameTitle[0];
        if (notesWithSameTitle.Count == 1)
        {
            return newest.Id;
        }

        var olderNotes = notesWithSameTitle.Skip(1).ToList();
        foreach (var oldNote in olderNotes)
        {
            foreach (var attachment in oldNote.Attachments)
            {
                DeleteStoredFile(oldNote.UserId, attachment);
            }
        }

        _context.Notes.RemoveRange(olderNotes);
        await _context.SaveChangesAsync();

        return newest.Id;
    }

    private static string NormalizeTitleForDuplicate(string? title)
    {
        return (title ?? string.Empty).Trim().ToLowerInvariant();
    }

    private void ValidateAttachmentBatch(List<IFormFile>? uploadedFiles)
    {
        var files = uploadedFiles?
            .Where(f => f != null && f.Length > 0)
            .ToList() ?? new List<IFormFile>();

        if (files.Count == 0)
        {
            return;
        }

        foreach (var file in files)
        {
            if (!IsFileAllowed(file))
            {
                ModelState.AddModelError("uploadedFiles", "Tipul fișierului încărcat nu este acceptat.");
                return;
            }

            if (file.Length > 100 * 1024 * 1024)
            {
                ModelState.AddModelError("uploadedFiles", "Un fișier este prea mare. Limita este 100 MB per fișier.");
                return;
            }
        }
    }

    private static bool IsFileAllowed(IFormFile file)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();

        if (contentType.StartsWith("audio/") ||
            contentType.StartsWith("video/") ||
            contentType.StartsWith("image/") ||
            contentType.StartsWith("text/") ||
            contentType == "application/pdf" ||
            contentType == "application/json")
        {
            return true;
        }

        return extension is ".mp3" or ".wav" or ".ogg" or ".aac" or ".m4a" or ".webm" or
               ".mp4" or ".mov" or ".mkv" or
               ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp" or
               ".txt" or ".md" or ".json" or ".pdf";
    }

    private async Task SaveUploadedFilesAsync(Note note, List<IFormFile>? uploadedFiles)
    {
        var files = uploadedFiles?
            .Where(f => f != null && f.Length > 0)
            .ToList() ?? new List<IFormFile>();

        if (files.Count == 0)
        {
            return;
        }

        var uploadDir = Path.Combine(GetStorageRoot(), note.UserId, note.Id.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(uploadDir);

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName);
            var storedFileName = $"{Guid.NewGuid():N}{extension}";
            var targetPath = Path.Combine(uploadDir, storedFileName);

            await using var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await file.CopyToAsync(stream);

            _context.NoteAttachments.Add(new NoteAttachment
            {
                NoteId = note.Id,
                OriginalFileName = Path.GetFileName(file.FileName),
                StoredFileName = storedFileName,
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = file.Length,
                UploadedAtUtc = DateTime.UtcNow
            });
        }
    }

    private string GetStorageRoot()
    {
        return Path.Combine(_environment.ContentRootPath, "App_Data", "uploads");
    }

    private string GetAttachmentPath(string userId, NoteAttachment attachment)
    {
        return Path.Combine(GetStorageRoot(), userId, attachment.NoteId.ToString(CultureInfo.InvariantCulture), attachment.StoredFileName);
    }

    private void DeleteStoredFile(string userId, NoteAttachment attachment)
    {
        var fullPath = GetAttachmentPath(userId, attachment);
        if (System.IO.File.Exists(fullPath))
        {
            System.IO.File.Delete(fullPath);
        }
    }

    private bool NoteExists(int id, string userId)
    {
        return _context.Notes.Any(e => e.Id == id && e.UserId == userId);
    }

    private sealed class ExportedNoteDto
    {
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? FolderName { get; set; }
        public int? NoteTypeId { get; set; }
        public string? NoteTypeName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ModifiedAtUtc { get; set; }
        public List<ExportedAttachmentDto> Attachments { get; set; } = new();
    }

    private sealed class ExportedAttachmentDto
    {
        public string OriginalFileName { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public string? ContentType { get; set; }
        public long SizeBytes { get; set; }
    }
}
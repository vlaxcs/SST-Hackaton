using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using SST_Hackaton.Data;
using SST_Hackaton.Models;

namespace SST_Hackaton.Controllers;

[Authorize]
public class NotesController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<IdentityUser> _userManager;
    private readonly IWebHostEnvironment _environment;

    public NotesController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IWebHostEnvironment environment)
    {
        _context = context;
        _userManager = userManager;
        _environment = environment;
    }

    public async Task<IActionResult> Index(string? searchTerm, int? noteTypeId, string? folderName, string sortOrder = "modified_desc")
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        var query = _context.Notes
            .AsNoTracking()
            .Include(n => n.NoteType)
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

        if (noteTypeId.HasValue)
        {
            query = query.Where(n => n.NoteTypeId == noteTypeId.Value);
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

        var noteTypeOptions = await _context.NoteTypes
            .AsNoTracking()
            .OrderBy(nt => nt.Id)
            .Select(nt => new SelectListItem { Value = nt.Id.ToString(), Text = NoteContentHelper.NoteTypeNameRo(nt.Name) })
            .ToListAsync();

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
            NoteTypeId = noteTypeId,
            SortOrder = sortOrder,
            NoteTypeOptions = noteTypeOptions,
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
            .Include(n => n.NoteType)
            .Include(n => n.Attachments)
            .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);

        if (note == null)
        {
            return NotFound();
        }

        await PopulateNoteTypesAsync(note.NoteTypeId);
        return View(note);
    }

    public async Task<IActionResult> Create(int? noteTypeId)
    {
        var selectedTypeId = noteTypeId ?? 1;
        await PopulateNoteTypesAsync(selectedTypeId);
        return View(new Note { NoteTypeId = selectedTypeId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Title,Content,NoteTypeId")] Note note, List<IFormFile>? uploadedFiles)
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        note.UserId = userId;
        note.Content ??= string.Empty;

        var noteTypeName = await GetNoteTypeNameAsync(note.NoteTypeId);
        if (noteTypeName == null)
        {
            ModelState.AddModelError(nameof(note.NoteTypeId), "Tipul notiței nu este valid.");
        }

        if (NoteContentHelper.IsCheckboxTypeName(noteTypeName))
        {
            note.Content = NoteContentHelper.NormalizeChecklistContent(note.Content);
        }

        if (NoteContentHelper.IsAudioTypeName(noteTypeName) && string.IsNullOrWhiteSpace(note.Content))
        {
            ModelState.AddModelError(nameof(note.Content), "Descrierea este obligatorie pentru notițele audio.");
        }

        ValidateAttachmentBatch(uploadedFiles, noteTypeName);

        if (ModelState.IsValid)
        {
            _context.Add(note);
            await _context.SaveChangesAsync();

            if (NoteContentHelper.IsAttachmentNoteTypeName(noteTypeName))
            {
                await SaveUploadedFilesAsync(note, uploadedFiles);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

        await PopulateNoteTypesAsync(note.NoteTypeId);
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
    public async Task<IActionResult> Edit(int id, [Bind("Id,Title,Content,NoteTypeId")] Note note, List<IFormFile>? uploadedFiles)
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

        if (existingNote.NoteTypeId != note.NoteTypeId)
        {
            ModelState.AddModelError(nameof(note.NoteTypeId), "Tipul notiței nu poate fi schimbat. Creează o notiță nouă.");
            await PopulateNoteTypesAsync(existingNote.NoteTypeId);
            note.NoteTypeId = existingNote.NoteTypeId;
            return View("Details", note);
        }

        var noteTypeName = await GetNoteTypeNameAsync(existingNote.NoteTypeId);
        if (NoteContentHelper.IsAudioTypeName(noteTypeName) && string.IsNullOrWhiteSpace(note.Content))
        {
            ModelState.AddModelError(nameof(note.Content), "Descrierea este obligatorie pentru notițele audio.");
        }

        ValidateAttachmentBatch(uploadedFiles, noteTypeName);

        if (ModelState.IsValid)
        {
            existingNote.Title = note.Title;
            existingNote.Content = NoteContentHelper.IsCheckboxTypeName(noteTypeName)
                ? NoteContentHelper.NormalizeChecklistContent(note.Content)
                : note.Content;
            existingNote.NoteTypeId = note.NoteTypeId;

            if (NoteContentHelper.IsAttachmentNoteTypeName(noteTypeName))
            {
                await SaveUploadedFilesAsync(existingNote, uploadedFiles);
            }

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

        await PopulateNoteTypesAsync(note.NoteTypeId);
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
            .Include(n => n.NoteType)
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

    private async Task PopulateNoteTypesAsync(int? selectedTypeId = null)
    {
        var noteTypes = await _context.NoteTypes
            .AsNoTracking()
            .OrderBy(nt => nt.Id)
            .Select(nt => new
            {
                nt.Id,
                Name = NoteContentHelper.NoteTypeNameRo(nt.Name)
            })
            .ToListAsync();

        ViewData["NoteTypeId"] = new SelectList(noteTypes, "Id", "Name", selectedTypeId);
    }

    private async Task<string?> GetNoteTypeNameAsync(int noteTypeId)
    {
        return await _context.NoteTypes
            .AsNoTracking()
            .Where(nt => nt.Id == noteTypeId)
            .Select(nt => nt.Name)
            .FirstOrDefaultAsync();
    }

    private static string GetNoteTypeErrorText(string? noteTypeName)
    {
        if (NoteContentHelper.IsAudioTypeName(noteTypeName))
        {
            return "Pentru tipul audio poți încărca doar fișiere audio.";
        }

        if (NoteContentHelper.IsVideoTypeName(noteTypeName))
        {
            return "Pentru tipul video poți încărca doar fișiere video.";
        }

        if (NoteContentHelper.IsPhotoTypeName(noteTypeName) || NoteContentHelper.IsDrawingTypeName(noteTypeName))
        {
            return "Pentru tipul ales poți încărca doar imagini.";
        }

        return "Tipul notiței nu permite atașamente.";
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

    private void ValidateAttachmentBatch(List<IFormFile>? uploadedFiles, string? noteTypeName)
    {
        var files = uploadedFiles?
            .Where(f => f != null && f.Length > 0)
            .ToList() ?? new List<IFormFile>();

        if (files.Count == 0)
        {
            return;
        }

        if (!NoteContentHelper.IsAttachmentNoteTypeName(noteTypeName))
        {
            ModelState.AddModelError("uploadedFiles", "Tipul notiței nu acceptă atașamente.");
            return;
        }

        foreach (var file in files)
        {
            if (!IsFileAllowedForType(file, noteTypeName))
            {
                ModelState.AddModelError("uploadedFiles", GetNoteTypeErrorText(noteTypeName));
                return;
            }

            if (file.Length > 100 * 1024 * 1024)
            {
                ModelState.AddModelError("uploadedFiles", "Un fișier este prea mare. Limita este 100 MB per fișier.");
                return;
            }
        }
    }

    private static bool IsFileAllowedForType(IFormFile file, string? noteTypeName)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var contentType = (file.ContentType ?? string.Empty).ToLowerInvariant();

        if (NoteContentHelper.IsAudioTypeName(noteTypeName))
        {
            return contentType.StartsWith("audio/") ||
                   extension is ".mp3" or ".wav" or ".ogg" or ".aac" or ".m4a" or ".webm";
        }

        if (NoteContentHelper.IsVideoTypeName(noteTypeName))
        {
            return contentType.StartsWith("video/") ||
                   extension is ".mp4" or ".mov" or ".webm" or ".mkv";
        }

        if (NoteContentHelper.IsPhotoTypeName(noteTypeName) || NoteContentHelper.IsDrawingTypeName(noteTypeName))
        {
            return contentType.StartsWith("image/") ||
                   extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" or ".bmp";
        }

        return false;
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
}
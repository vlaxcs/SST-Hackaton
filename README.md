# SST Hackaton - README

## 1. Ce este proiectul
Aplicatie web ASP.NET Core MVC pentru notite, cu autentificare pe utilizator (Identity), baza de date SQLite si suport pentru tipuri diferite de notite (text, checklist, audio, video, foto, desen).

Fiecare utilizator vede si modifica doar notitele lui.

## 2. Tehnologii folosite si unde
- ASP.NET Core MVC (.NET 10)
  - Routing, controller-e, view-uri Razor.
  - Fisiere principale: `Program.cs`, `Controllers/NotesController.cs`, `Views/Notes/*`.
- ASP.NET Core Identity
  - Login, register, pagini account.
  - Fisiere: `Areas/Identity/Pages/Account/*`, configurare in `Program.cs`.
- Entity Framework Core + SQLite
  - Persistenta notitelor, tipurilor si atasamentelor.
  - Fisiere: `Data/ApplicationDbContext.cs`, `Data/Migrations/*`, `app.db`.
- Frontend: Razor + JavaScript + CSS
  - UI, validari in browser, tool-uri media (camera/mic/canvas), selectii bulk.
  - Fisiere: `Views/Notes/Create.cshtml`, `Views/Notes/Details.cshtml`, `Views/Notes/Index.cshtml`, `wwwroot/css/site.css`.

## 3. Ce este implementat
### 3.1 Notite si tipuri
- CRUD pentru notite.
- Tipuri de notite:
  - Text
  - Checkbox (checklist)
  - Audio
  - Video
  - Photo
  - Drawing
- Tipul notitei nu poate fi schimbat dupa creare.

Fisiere:
- `Models/Note.cs`
- `Models/NoteType.cs`
- `Models/ChecklistItem.cs`
- `Models/NoteContentHelper.cs`
- `Controllers/NotesController.cs`

### 3.2 Checklist
- Continut stocat ca JSON.
- Parsare compatibila (inclusiv variante de naming).
- Preview in lista de notite, cu limita si text de tip "si alte X checkboxuri".

Fisiere:
- `Models/NoteContentHelper.cs`
- `Views/Notes/Create.cshtml`
- `Views/Notes/Details.cshtml`
- `Views/Notes/Index.cshtml`

### 3.3 Atasamente media
- Suport upload pentru tipurile media.
- Validare pe tipuri de fisier (audio/video/imagine) + limita dimensiune.
- Salvare fisier in sistem local + metadata in DB.
- Preview in pagina notei:
  - audio player
  - video player
  - image preview
- Download si delete pentru atasamente.

Fisiere:
- `Models/NoteAttachment.cs`
- `Controllers/NotesController.cs`
- `Views/Notes/Create.cshtml`
- `Views/Notes/Details.cshtml`
- `Data/Migrations/20260327141138_AddMediaNoteAttachments.cs`

### 3.4 Tool-uri de capturare
- Inregistrare audio din microfon.
- Inregistrare video din camera.
- Captura foto din camera.
- Canvas drawing + salvare desen ca imagine.
- Paleta de culori cu highlight pe culoarea activa.

Fisiere:
- `Views/Notes/Create.cshtml`
- `Views/Notes/Details.cshtml`
- `wwwroot/css/site.css`

### 3.5 Cautare, filtrare, sortare
- Cautare dupa text si data.
- Filtrare dupa tip de notita.
- Sortare dupa titlu/creare/modificare.
- Debounce pe filtre in UI.

Fisiere:
- `Controllers/NotesController.cs`
- `Views/Notes/Index.cshtml`

### 3.6 Selectie multipla si actiuni bulk
- Multi-select pe carduri cu long-press (telefon/desktop) + checkbox.
- Bulk delete pentru notitele selectate.
- Bulk move in folder pentru notitele selectate.
- Afisare folder pe card.

Fisiere:
- `Models/Note.cs` (camp `FolderName`)
- `Controllers/NotesController.cs` (`BulkDelete`, `BulkMoveToFolder`)
- `Views/Notes/Index.cshtml`
- `wwwroot/css/site.css`
- `Data/Migrations/20260327144820_AddNoteFoldersAndBulkSelection.cs`

### 3.7 Localizare si UI
- Localizare configurata pe `ro-RO`.
- Tema UI custom in CSS.
- Ajustari pe formulare, checkbox-uri, popup-uri, stari butoane.

Fisiere:
- `Program.cs`
- `wwwroot/css/site.css`
- `Views/Notes/*`
- `Areas/Identity/Pages/Account/*`

### 3.8 Validari UX in browser
- Butonul "Salveaza" in create/edit este dezactivat daca lipsesc titlul sau descrierea.
- Pentru notitele audio, descrierea este obligatorie.

Fisiere:
- `Views/Notes/Create.cshtml`
- `Views/Notes/Details.cshtml`
- `Controllers/NotesController.cs`

## 4. Migrate / schema
Migrari existente:
- `00000000000000_CreateIdentitySchema`
- `20260327132126_AddNotesAndNoteTypes`
- `20260327141138_AddMediaNoteAttachments`
- `20260327144820_AddNoteFoldersAndBulkSelection`

## 5. Cum rulezi proiectul
1. Restore + build:
   - `dotnet restore`
   - `dotnet build`
2. Aplica migrari (daca e nevoie):
   - `dotnet ef database update`
3. Ruleaza:
   - `dotnet run`

Aplicatia porneste pe localhost (portul il vezi in output-ul `dotnet run`).

## 6. Observatii practice
- Daca ai aplicatia deja pornita, un nou `dotnet build` poate da eroare de fisier blocat (`SST Hackaton.dll`).
  - Solutie: opresti procesul curent, apoi rulezi build din nou.
- Atasamentele sunt stocate local in `App_Data/uploads/...` si sunt legate de user + nota. În viitor, se poate trece ușor la stocare în Cloud.

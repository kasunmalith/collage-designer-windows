# Collage Designer

### A desktop workspace for photo collages and printed albums

Collage Designer brings photo arrangement, crop editing, print-size setup, layered export, and album design into one offline Windows application. It is built for photographers who want precise control over every frame while keeping original JPG files available for final-quality rendering.

**Windows 10/11 · x64 · .NET 8 WPF · SkiaSharp · Source version 2.0.0**

> This repository contains the Windows application source and automated checks. It does not contain a compiled app, customer photographs, saved projects, or the local training database.

## At a glance

| Workspace | What it does |
| --- | --- |
| **Create Collage** | Design a 1–12 photo collage with normal or open-space layouts, frame editing, crop controls, and JPG, PNG, or layered PSD export. |
| **Create Album** | Arrange selected JPGs across 10–35 full spreads, edit each sheet, track photo usage, save the project, and export numbered print-ready JPGs. |
| **Album Style Trainer** | Review previous finished albums and build a local library of validated layout examples for future album generation. |

The home screen keeps these workflows separate, so a quick collage does not require setting up an album or training library.

## Create Collage

### Canvas and design

- Import **1–12 JPG/JPEG photos** with the file picker or drag them into the window.
- Set a custom canvas in pixels or inches, choose DPI, or start from **4 × 6, 8 × 12, 9 × 16, 4 × 5, or 5 × 5 inches**. Portrait and Landscape swap the dimensions. Press **Enter** after editing a size field to apply it.
- Choose **Normal layouts** for structured grids or **Random space layouts** for intentional white space, staggered arrangements, feature photos, and cinematic bands. Use the Design option selector or cycle layouts from the toolbar.
- Set the photo gap from **0–500 px**, the outer margin from **0–1000 px**, a background color, and JPEG quality. The last valid canvas size is restored at the next launch.

### Edit directly on the collage

- **Drag a frame** to move the photo and its frame together. **Ctrl + drag** moves only the photo crop inside the fixed frame.
- Drag any of a frame's **four sides** to resize it. Shared borders adjust neighboring frames; outer borders can reveal background space.
- Drag a photo onto another photo to **insert it at that position and shift the photos between them**. Drag onto open canvas to position the frame there.
- **Shift + click** to select multiple frames. Move or resize selected frames together. Hold **Alt** during an edge resize to resize symmetrically from both sides.
- Use the zoom slider, mouse wheel, or zoom buttons to refine the crop. Reset Photo Position restores its original crop.
- Click **Center Photo in Collage** to center a selected frame when space allows. After a manual frame change, **Equal Photo Gaps** can bring neighboring gaps back toward the configured value.
- Use **Ctrl + A** in the photo list to select imported photos. Click the empty canvas or outside it to clear the selection outline. Undo and Redo are available while editing.

Changes appear in the preview while you drag. The same frame and crop geometry is used for export.

### Save and export

Save a `.collageproject` file to continue later. Export to **JPG**, **PNG**, or a **layered PSD**. In the PSD, each visible photo frame has its own named raster layer above a Background layer; the current crop and zoom are rendered into that layer. The original full JPG is not embedded as a smart object.

Export uses the original image files at the selected canvas resolution. **Quick save** reuses the previous output location and format, increments the filename, and skips names that already exist. For example, `1.jpg` becomes `2.jpg`, while `album.jpg` becomes `album_2.jpg`. Export files are committed without replacing an existing file.

The optional **After export, move originals to ‘used photos’ and clear them here** checkbox is off by default. When enabled after a successful export, the app creates a `used photos` folder beside each source folder, moves successfully used JPGs there with collision-safe names, and removes only those moved photos from the editor. A missing or changed source is left in place and reported.

## Create Album

Choose a full-spread size, **10–35 sheets**, and **72–600 DPI** (300 default). Supported sizes are **8 × 16, 8 × 20, 8 × 24, 10 × 20, 10 × 24, 12 × 24, 12 × 30, and 12 × 36 inches**.

1. Import the JPGs already selected for the album. The app sorts filenames naturally and keeps their story order.
2. Generate the album. Every imported photo is assigned once across the sheets, with varying photo counts per sheet.
3. Review each spread. Change layouts, zoom or pan crops, move or resize frames, reorder photos, move photos between sheets, or keep them in the visible Unassigned Photos list until you place them again.
4. Use the fold guide and safe-zone control to check the center fold. Approve finished sheets and save the `.albumproject` file.
5. Export all sheets as full-resolution `001.jpg`, `002.jpg`, and so on. If those filenames already exist, the app creates a new numbered export folder instead of overwriting them.

The editor shows used, unused, and duplicate counts. Export stops if photos remain unassigned or original files are missing. A saved album project references the original JPG paths; keep those files accessible when reopening it.

## Album Style Trainer

The trainer learns from **validated examples of your finished album layouts**. It works locally with the original JPGs and flattened final sheet JPGs; no PSD or online service is required.

```text
All Albums/
  Couple 001/
    Used Photos/
      photo001.jpg
      photo002.jpg
    All Sheets/
      001.jpg
      002.jpg
  Couple 002/
    Used Photos/
    All Sheets/
```

Open **Tools → Album Style Trainer** from the home screen, select the `All Albums` folder, and analyze it. New or changed albums are indexed in a local SQLite catalog; unchanged albums can be skipped. The analyzer proposes photo regions and source-photo matches. Review uncertain sheets, correct regions or matches, and validate only the accurate results. **Train / Update Style** groups validated layouts by spread ratio and photo count and makes them available to new albums.

You can also approve a newly edited album's sheets and use **Save Final Album To My Style** to add those design choices to the training library. See [ALBUM_GUIDE.md](ALBUM_GUIDE.md) for the complete review workflow, database tables, and troubleshooting notes.

The trainer uses geometric clustering and ranking of validated examples. **It does not use a neural or ONNX model**, does not detect faces, and does not guarantee subject placement away from the fold. Complex full-bleed, overlapping, or decorated sheets may need manual review.

## Install and build

### Run a packaged Windows build

If you have a separately supplied Windows release, extract it and run `CollageDesigner.exe` from its `Release` folder. Keep the release files together. The self-contained build does not require a separate .NET or Python installation.

### Build this source

Use **Windows 10/11 x64**, **Visual Studio 2022** with the .NET desktop development workload, and the **.NET 8 SDK**. Open `CollageDesigner.sln`, restore packages, and build Release; or use PowerShell:

```powershell
dotnet restore CollageDesigner.sln
dotnet build CollageDesigner.sln -c Release
dotnet publish CollageDesigner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o Release
```

Run the automated source checks with:

```powershell
dotnet run --project tests/CollageDesigner.Tests.csproj -c Release
```

The interface uses WPF, so this Windows project does not build as a native Mac application. The Mac preview port is a separate project.

## Project layout

| Files | Responsibility |
| --- | --- |
| `MainWindow.xaml*`, `HomeWindow.xaml*` | Collage editor and home-screen interface. |
| `AlbumWindow.xaml*`, `AlbumEngine.cs`, `AlbumRenderService.cs` | Album editing, sheet planning, and full-resolution export. |
| `TrainerWindow.xaml*`, `TrainingReviewWindow.xaml*`, `TrainingCatalog.cs`, `TrainingSheetAnalyzer.cs` | Training import, review, SQLite catalog, and sheet analysis. |
| `LayoutEngine.cs`, `RandomSpaceLayoutEngine.cs`, `LayoutResizeEngine.cs`, `FramePlacementEngine.cs` | Layout generation and frame geometry. |
| `Rendering.cs`, `PsdExporter.cs`, `ExportFileWriter.cs` | Image rendering and safe output writing. |
| `Models.cs`, `EditorViewModel.cs`, `*SettingsStore.cs` | Project state, undo/redo, and remembered settings. |
| `tests/` | Automated checks for layouts, rendering, exports, persistence, and album/trainer logic. |

## Data and production notes

- Collage projects and album projects refer to source JPGs by **absolute path**. Moving the originals can break a previously saved project until its paths are updated.
- The app works offline. The training catalog is stored under `%LOCALAPPDATA%\Energetic Edge\Collage Designer\training.sqlite` for the current Windows user. Full-resolution photos are not embedded in that database.
- The editor does not edit an original JPG. The optional post-export **move** setting is the only workflow that relocates source files.
- A collage canvas is limited to **30,000 px per side and 220 megapixels**. Classic PSD export has a tighter **80-megapixel** limit and may need substantial RAM.
- Automated builds and logic checks have passed on Windows x64. Real mouse/keyboard workflows, large 200-album training collections, and final print color/quality still need acceptance checks with representative customer data.

---

Software Development by [Energetic Edge (Pvt) Ltd](https://www.energeticedge.lk/)

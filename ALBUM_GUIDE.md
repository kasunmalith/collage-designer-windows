# Collage Designer: Album workflow

## Create an album

1. Open `CollageDesigner.exe` and choose **Create Album**. The existing **Create Collage** editor remains available from the same home screen.
2. Choose a full-spread print size (8×16, 8×20, 8×24, 10×20, 10×24, 12×24, 12×30, or 12×36 inches), 10–35 sheets, and DPI (72–600; 300 default). The preview shows ratio, average photo density, and final pixel size.
3. Click **Import Selected JPG Photos** and select the photographer's final photos. Files use natural filename order. The application does not cull or reject photos.
4. Click **Generate Album**. Every imported photo is placed once, in contiguous story order, with varied sheet densities. The left navigator and status show sheet counts; the usage panel shows used, unused, and duplicates.
5. Edit each full spread. Click a photo in the preview or list, move **Zoom**, drag inside the photo to pan the crop, or click **Reset Crop**. Ctrl+drag a photo in the spread to move its frame, and use the frame width/height buttons for 30-pixel size steps. Use **Previous Layout** and **Next Layout**, or Up/Down arrow keys, to cycle arrangements without changing the sheet's photos. Drag a photo name onto another name to change order; hold Shift during that drop to swap. Drag a photo name onto a different sheet in the left navigator, or use the move buttons. A photo removed with **Remove to Unassigned** stays visible in **Unassigned Photos** and can be added to the current sheet. Use **Undo/Redo** as needed.
6. Use **Show Fold Guide** and the fold safe-zone field to inspect the central fold. Photos can cross the fold. The guide is for editing and is not printed.
7. Click **Save Album** to write an `.albumproject` file that references the original JPGs. Keep the original files available when reopening the project.
8. Click **Export All Sheets**, select an output folder, and wait for the status to finish. The app renders original JPGs into `001.jpg`, `002.jpg`, and so on at the chosen physical dimensions and DPI. If those sheet names already exist, it makes a new `Album Export 2`, `Album Export 3`, etc. subfolder. It never replaces an existing JPG.

For example, a 12×30 inch album at 300 DPI exports 9000×3600 pixel full spreads. A 20-sheet album with 96 photos shows 96/96 used, 0 unused, and 0 duplicates immediately after generation. Export is stopped if any photos remain unassigned or originals are missing.

## Teach the album style

Prepare one root folder with this structure. Folder names are matched case-insensitively, and unrelated files are ignored.

```text
All Albums/
  Couple Name 001/
    Used Photos/
      WAR_001.jpg
      WAR_002.jpg
    All Sheets/
      001.jpg
      002.jpg
  Couple Name 002/
    Used Photos/
    All Sheets/
```

1. Open **Tools → Album Style Trainer** from the home screen.
2. Click **Select All Albums Folder** and select the root `All Albums` folder. The scan discovers all couple folders, assigns stable `TRN-000001` style IDs, counts photos and sheets, naturally sorts sheets, and determines ratio families from the flattened JPG pixel dimensions. Known families are 2.0, 2.4, 2.5, and 3.0, with a ±0.03 tolerance. Unknown or inconsistent ratios appear as **Needs Attention**.
3. Click **Analyze All Albums**. Work runs in the background. Pause, resume, or cancel from the toolbar. New or changed albums are analyzed; unchanged albums are skipped. The application tries to find photograph regions on flattened sheets and match them to original JPG crops. Uncertain results stay in **Needs Review**.
4. Select an album and click **Review Selected Album**. Inspect the flattened sheet, choose a region, and assign the correct original JPG if necessary. Use **Add Missing Photo Area** to draw a rectangle, or remove an incorrect region and draw it again. Click **Validate Sheet** only when every region is assigned to a distinct original photo.
5. Click **Train / Update Style**. Validated sheet layouts and approved album feedback are grouped by ratio and photo count, clustered by normalized geometry, and stored as a reusable local layout library. New album generation ranks matching layouts using sheet position, photo orientation fit, and example frequency; it falls back to built-in layouts when no suitable learned layout exists.
6. To add your finished manual design decisions, approve every sheet in the Album editor, click **Save Final Album To My Style**, then return to **Tools → Album Style Trainer → Train / Update Style**.

Training uses final flattened JPG sheets and original JPGs; PSDs and an internet service are not required. Physical sizes sharing the same spread ratio use the same layout family. Original photos are never replaced by synthetic images.

## Local database

The catalog is stored at `%LOCALAPPDATA%\Energetic Edge\Collage Designer\training.sqlite`.

| Table | Purpose |
| --- | --- |
| `TrainingAlbum` | Stable numeric ID displayed as `TRN-000001`, source folder, ratio, counts, validation state, change fingerprint |
| `TrainingPhoto` | Source JPG path, story order, dimensions, aspect ratio, orientation |
| `TrainingSheet` | Ordered flattened sheet path, ratio, normalized sheet position, status |
| `TrainingSlot` | Normalized frame rectangle, matched source path, fold crossing, match confidence and crop fields |
| `AlbumFeedback` | Approved final layouts from Album projects, keyed to avoid duplicate imports |
| `LearnedLayout` | Clustered ratio-based layout templates and usage frequency |

No full-resolution JPG is embedded in the database. The database is local to the Windows user account.

## Current limits

The flattened-sheet analyzer is conservative. Its region detector works best when photo rectangles are separated by a fairly uniform background. Full-bleed images, overlapping pictures, decoration, text, and complex backgrounds can require manual region review. Crop matching is an approximate local-image comparison; a low-confidence result is never silently treated as validated. The program does not detect faces or guarantee that faces avoid the fold. There is no neural or ONNX model; recommendations use validated layout examples with geometric clustering and local ranking. A large 200-album collection has not yet been performance-tested on customer data. Inspect exported JPGs at print size before production printing.

## Verification

The automated suite checks 96-photo/20-sheet generation, contiguous story order, natural sorting, ratio classification, bulk folder scanning, SQLite indexing, review-to-layout training, original-resolution sheet rendering, no-overwrite repeated export, and the existing Collage engine. A separate Windows smoke test opens the Home, Collage, Album, Trainer, and Review windows. Real mouse-driven review and a full 200-album training run still need acceptance testing with customer files.

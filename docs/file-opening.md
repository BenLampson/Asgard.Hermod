# Opening and previewing files

Settings → Open with follows the Pencil settings design. Add one extension such
as `txt` or `.txt`, then select an installed `.exe` using its full path. Matching
is case insensitive. A duplicate extension must be edited rather than silently
overwritten. Changing an extension and its program is an atomic operation.

Rules are saved immediately in the local SQLite `app_setting` table under
`file.openwith:<extension>`. They apply immediately within Hermod and survive
restarting. They are local to the machine, do not change Windows associations,
and are not included in workspace synchronization. Removing a rule leaves files
and installed programs intact. Unconfigured extensions use Windows default
programs; if no association exists, Hermod reports a safe error directing the
user to the settings page.

## Object storage

Double-click a file or press Enter on its row to download and open it. Directories
continue to navigate. The row menu and details menu include **Open file** and
**Preview file**. Opening captures the original bucket and object key, downloads
the entire file, and only then starts the program. Transfers keep progress,
cancellation and retry. Failed or canceled downloads never start a program.

**Preview file** displays a read-only Hermod window for common text and image
formats. PNG, JPEG, BMP, GIF, TIFF and ICO are supported by the image viewer;
multi-frame images display their first frame. Images support zoom and fit to
window. Text supports selection and copying, including JSON, Markdown, CSV,
configuration and source files. HTML, SVG and scripts are displayed as plain
text. UTF-8 and BOM-marked UTF-16/32 are supported. Other encodings or damaged
files can be downloaded or opened with a configured external program.

Text preview is limited to 2 MiB; image preview is limited to 20 MiB and supported
dimensions. Downloads enforce the limit against actual bytes, including streams
without a trustworthy length. Large images are decoded at a maximum dimension
of 4096 pixels. The existing details panel retains its 256 KiB text preview.
Selecting a supported raster image previews it directly in the details panel,
without a type selector or confirmation dialog. Recognition uses its file name or
already loaded Content-Type and makes no additional content request for inference.
The preview area displays a traffic-charge notice during and after loading.
Unknown types retain **Force preview**: the user must select Image and confirm the
object size and provider traffic charges before downloading content.

Images appear in the details panel and can be enlarged without another
download. Force preview ignores the extension but retains the 20 MiB actual-byte
limit and existing decoded-image dimension limits. Cancellation, switching
objects, refreshing, navigation, closing details, and closing the page cancel the
read and discard obsolete results. Temporary files are removed on success,
failure, and cancellation. Recognized images retry directly; unknown types require
another type and traffic-charge confirmation. Traffic already used may still be
charged. Objects and Content-Type
are never modified.

Object details can be temporarily hidden with the close button and reopened with
the panel button next to Refresh. Hiding details preserves the selected row,
cancels previews and skips detail/content reads while browsing other objects.
Reopening loads the currently selected object. The vertical splitter changes the
details width by dragging horizontally or using Left/Right when focused. The
panel restores its last width on reopening and clamps it when the window becomes
narrower. Visibility and width are local to the current page and are not saved.

Other files, including documents and archives, use an installed program through
the opening rules or Windows associations.

Remote executable files and scripts are not run through Windows associations.
They can be previewed as text where supported, opened by an explicitly configured
editor, or downloaded for local handling. Custom programs receive the file as one
argument through `ProcessStartInfo.ArgumentList`, without shell interpolation.

## Built-in file type icons

Object rows and details share a bundled Lucide icon catalog. It covers video,
audio, images, vector/design files, PDF, documents, spreadsheets, presentations,
text/Markdown, archives, source code, web/styles, configuration/data, databases,
fonts, applications/installers, scripts, certificates and disk images. Unknown
files use a generic file icon; directories retain their folder icon. MP4 and MOV
use the purple video icon, including while selected. Theme-specific colors also
remain readable in dark mode. Wide lists show a localized file type column;
narrow lists hide it before reducing the file name and action space.

Extension matching ignores case and handles compound archive suffixes such as
`.tar.gz` before their final suffix. `.env` and common extensionless project
files are recognized. Extensionless objects can use Content-Type from details
already requested by the user; rendering a row never downloads contents or
requests metadata. Known extensions take priority over conflicting Content-Type.
The ambiguous `.key` extension defaults to Keynote presentations. Icon recognition
does not grant preview support or change file-opening security policies.

## Cache lifetime

Each open operation uses a separate directory beneath
`%LOCALAPPDATA%\Asgard\Hermod\file-cache`. Remote keys cannot supply local paths
or overwrite existing files. Failed and canceled operations remove their cache;
preview files are removed when the window closes. Files opened externally remain
available to the external application and are protected for the current Hermod
session, even when the external program does not keep a file handle open. Startup
cleans unused caches older than seven days. Editing a cached file does not upload
it back to object storage. See [storage settings](storage-settings.md) for manual
cleanup and actual usage statistics.

Contracts and validation live in Abstractions, rule persistence in
Persistence.Sqlite, temporary file lifecycle in Core, Windows program launching
and preview windows in UI, and object download orchestration in ObjectStorage.UI.
Provider protocols remain independent of WPF.

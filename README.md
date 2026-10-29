# Image Converter

Windows 11 batch image converter (WinUI 3, .NET 9). Sibling of Image Renamer, built the same way.

Drop images or folders on the window, pick a format, press **Convert all** (Ctrl+Enter).

To convert only some images, select them in the list (click, Shift/Ctrl+click, Ctrl+A) or type in the
selection box at the bottom — the same syntax as Image Renamer: `5915-5920` (numbers in file names),
`#1-#10` (list positions), `IMG_*` or `*.dds` (wildcards), separated by commas — then press
**Convert selected** (Ctrl+Shift+Enter). The **Select** menu also has select all/none, invert, and
"select images that failed" for a retry.

## Formats

| | |
| --- | --- |
| **Reads** | JPG, PNG, GIF, BMP, TIFF, WebP, HEIC/HEIF, AVIF, JXL, ICO, DDS, TGA, PSD, camera RAW |
| **Writes** | PNG, JPEG, WebP (lossy or lossless), AVIF, TIFF, BMP, GIF, ICO, DDS |

* **DDS**: BC1/DXT1, BC3/DXT5 or uncompressed; *Auto* picks DXT5 when the image has alpha.
  Mipmaps are generated for power-of-two sizes. BC7/BC5 can't be written (ImageMagick has no encoder).
* **Uncompressed DDS input** is read by `DdsReader` using the channel masks / DXGI format in the
  header. ImageMagick assumes BGRA and ignores the masks, so RGBA-ordered files (typical of textures
  dumped from D3D11/12 games) would otherwise come out with red and blue swapped. Compressed DDS
  (DXT/BC) still goes through ImageMagick.
* **ICO**: one icon with 16, 32, 48 and 256 px sizes; non-square images are padded.
* **HEIC** can be read but not written: the bundled ImageMagick has no HEVC encoder. The format
  list only offers what the installed Magick.NET can actually write (`FormatInfo.Writable`).
* Animated GIF/WebP: the first frame is converted.
* 8 bits per channel (Magick.NET Q8): 16-bit PNG/TIFF sources are reduced to 8-bit.

## Create GIF

Select two or more images and press **Create GIF** (command bar or right-click). Frames follow the
list order. Options: frame delay, loop count (0 = forever), play forward then backward, longest side,
keep transparency or fill with the background colour, dithering, and where to save. Images of other
sizes are fitted into the first image's frame and centred. GIF has 256 colours and on/off
transparency only, so photos lose some quality and soft edges become hard.

## Options

Quality, resize (fit within W×H or percent, no enlarging unless asked), keep or remove metadata
(the colour profile is always kept), background colour for formats without transparency, output to
the same folder / a subfolder / another folder (optionally keeping the folder tree), name suffix,
and what to do when the file exists (number, overwrite, skip).

Photos are always rotated upright according to their EXIF orientation. Originals are never changed:
a target that would equal its source is numbered instead. Files are written under a temporary name
and moved into place, so a failed or cancelled conversion leaves nothing half-written.

## Build

```
dotnet test
dotnet run --project src\ImageConverter.App -p:Platform=x64
dotnet publish src\ImageConverter.App -c Release -r win-x64 -p:Platform=x64 -p:SelfContained=true -o publish\ImageConverter
```

The published folder is portable: copy it anywhere and run `ImageConverter.exe`. Settings live in
`%LOCALAPPDATA%\ImageConverter`. Paths passed on the command line are loaded at startup.

## Layout

| Path | What |
| --- | --- |
| `src\ImageConverter.Core` | Scanning, output planning, conversion (Magick.NET), batch runner. No UI. |
| `src\ImageConverter.App` | WinUI 3 window, view model, thumbnails. |
| `tests\ImageConverter.Core.Tests` | xUnit tests; they generate their own images. |

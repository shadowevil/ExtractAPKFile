# Helbreath `.apk` Sprite Extraction and Repacking

This project provides a toolset to **extract**, **analyze**, and optionally **repack** Helbreath `.apk` sprite files into `.pak` archives.  
Unlike the standard `.pak` format used by the game client, the `.apk` variant uses non-standard alignment and partial obfuscation in its internal data layout.

---

## Overview

Helbreath `.apk` files store sprite data, frame definitions, and bitmap graphics.  
While similar to `.pak`, `.apk` was used by certain versions of the client to bundle graphics with different internal offsets and file layouts.

This extractor:
- Decodes the total sprite count.
- Locates and extracts each sprite’s bitmap data.
- Reads per-frame metadata (`stBrush` structs) describing frame position, size, and pivot offsets.
- Exports data as `.bmp` and `.json` files.
- Optionally converts the results into a clean `.pak` format using **PAKLib**.

---

## File Encoding & Structure

| Section | Description | Notes |
|----------|--------------|-------|
| 0x00–0x13 | Header data | Unused / alignment region |
| 0x14 (20) | Encoded sprite count (`int`) | Must be decoded to get real total |
| 0x18+ | Sprite index table | Each entry = **77 bytes** |
| `iASDstart` (per entry) | Sprite header block | Contains frame count, brush table, and image data |

---

### Sprite Count Encoding

At file offset `0x14`, the total number of sprites is stored **encoded**.  
To get the actual total, the client applies this transformation:

```cpp
decodedTotal = (((encodedTotal - 51) / 3) - 17) / 44;
```

This formula must be used to calculate how many sprites exist in the file.

---

### Sprite Index Table

Each entry in the index table (starting at offset `0x18`) is **77 bytes** long.  
Only the first 4 bytes of each entry are actively used — they store the file offset (`iASDstart`) where that sprite’s data begins.

---

### Sprite Data Layout

At each `iASDstart`:

| Offset | Field | Type | Description |
|---------|--------|------|-------------|
| +100 | `m_iTotalFrame` | `int` | Number of animation frames |
| +108 | `stBrush[]` | array | Frame metadata (12 bytes × frame count) |
| +108 + (12 × frames) | Bitmap data | Raw BMP bytes for that sprite |

---

### The `stBrush` Frame Structure

Each frame entry is exactly **12 bytes** long and defined as:

```cpp
typedef struct stBrushtag {
    short sx;   // X
    short sy;   // Y
    short szx;  // Width
    short szy;  // Height
    short pvx;  // Pivot X
    short pvy;  // Pivot Y
} stBrush;
```

Converted to C#:
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FrameData
{
    public short X { get; set; }
    public short Y { get; set; }
    public short Width { get; set; }
    public short Height { get; set; }
    public short PivotX { get; set; }
    public short PivotY { get; set; }
}
```

**Valid ranges:**
- `X`, `Y`, `Width`, `Height` → always positive  
- `PivotX`, `PivotY` → may be negative (for sprite alignment)

---

## Encoding Variations

Helbreath `.apk` files are not formally standardized.  
Different tools or client builds produced slightly inconsistent layouts.  
The most common variation is where the **brush table offset** (after frame count) is inconsistent:
- Usually starts at `iASDstart + 108`
- Occasionally at `iASDstart + 104`

This is likely due to:
- Different compilers or struct alignment rules.
- Incorrect use of padding or non-packed structs when building the original `.apk`.

For compatibility, the extractor includes an **auto-detection pass** that checks offsets 100, 104, and 108 and chooses whichever produces valid frame data.

---

## Extraction Flow

1. Read encoded sprite count at offset 20.  
2. Decode with formula above.  
3. Read `iASDstart` values from 77-byte entries.  
4. For each sprite:
   - Seek to `iASDstart + 100`, read `m_iTotalFrame`.  
   - Locate brush table at `iASDstart + 108` (auto-adjusted if necessary).  
   - Read `12 × frames` bytes into `FrameData[]`.  
   - Read bitmap bytes until next `iASDstart` or EOF.  
   - Export `.bmp` and `.json`.

---

## Output Format

Each extracted sprite produces:
```
Extracted\<filename>\<filename>_0000.bmp
Extracted\<filename>\<filename>_0000.json
```

Example JSON:
```json
{
  "SpriteIndex": 0,
  "FrameCount": 4,
  "Frames": [
    { "X": 0, "Y": 0, "Width": 64, "Height": 64, "PivotX": -32, "PivotY": -48 },
    { "X": 64, "Y": 0, "Width": 64, "Height": 64, "PivotX": -32, "PivotY": -48 }
  ]
}
```

---

## Repacking (`ConvertToPAK = true`)

When `ConvertToPAK` is enabled, the tool:
- Reads `.bmp` and `.json` pairs from `Extracted\...`
- Reconstructs sprite definitions
- Uses **PAKLib** to build `.pak` archives under:
  ```
  RePacked\<filename>.pak
  ```

---

## Notes

- Negative `PivotX`/`PivotY` values are intentional and define the sprite’s anchor point.
- Not all `.apk` files follow identical struct alignment; this extractor tolerates the known variations.
- The `.bmp` data inside is typically **valid DIB headers**, not compressed or encrypted.
- The `.apk` name here refers to “art pack,” **not Android APKs**.

---

## Example Command

```bash
ExtractAPKFile.exe "C:\Games\Helbreath\sprites\hbarg.apk"
```

Output:
```
C:\Games\Helbreath\sprites\Extracted\hbarg\hbarg_0000.bmp
C:\Games\Helbreath\sprites\Extracted\hbarg\hbarg_0000.json
C:\Games\Helbreath\sprites\RePacked\hbarg.pak
```

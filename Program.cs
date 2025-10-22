using PAKLib;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExtractAPKFile
{
    internal class Program
    {
        const bool ConvertToPAK = true;

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

        public struct SpriteData
        {
            public int SpriteIndex { get; set; }
            public int FrameCount { get; set; }
            public FrameData[] Frames { get; set; }
        }

        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: ExtractAPKFile <path_to_apk_file(s)>");
                return;
            }

            foreach (string filePath in args)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    continue;
                }

                string sourceDir = Path.GetDirectoryName(filePath)!;

                // Extracted/<filename_without_ext>/
                string extractRoot = Path.Combine(sourceDir, "Extracted");
                string outputDir = Path.Combine(extractRoot, Path.GetFileNameWithoutExtension(filePath));

                Directory.CreateDirectory(outputDir);
                ExtractApk(filePath, outputDir);
            }

            if (!ConvertToPAK)
                return;

            string[] folders = Directory.GetDirectories(Path.Combine(Directory.GetCurrentDirectory(), "Extracted"));
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "RePacked"));

            foreach (var folder in folders)
            {
                string filename = Path.Combine(Directory.GetCurrentDirectory(), "RePacked", Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) + ".pak");

                PAKLib.PAKData pak = new();
                var sprites = Directory.GetFiles(folder, "*.bmp")
                    .ToDictionary(bmp => bmp, bmp =>
                        File.Exists(Path.ChangeExtension(bmp, ".json"))
                            ? Path.ChangeExtension(bmp, ".json")
                            : null);

                foreach(var (bmp, json) in sprites)
                {
                    byte[] imageData = File.ReadAllBytes(bmp);
                    SpriteData? spriteData = null;
                    if (json != null)
                    {
                        string jsonContent = File.ReadAllText(json);
                        spriteData = JsonSerializer.Deserialize<SpriteData>(jsonContent);
                    }
                    Sprite spr = new Sprite();
                    spr.data = File.ReadAllBytes(bmp);
                    spr.Rectangles = spriteData != null ? spriteData.Value.Frames.Select(f => new SpriteRectangle
                    {
                        x = f.X,
                        y = f.Y,
                        width = f.Width,
                        height = f.Height,
                        pivotX = f.PivotX,
                        pivotY = f.PivotY
                    }).ToList() : new List<SpriteRectangle>();
                    pak.Sprites.Add(spr);
                }

                pak.Write(filename);
            }
        }

        static void ExtractApk(string filePath, string outputDir)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);

            fs.Seek(20, SeekOrigin.Begin);
            int encodedTotal = br.ReadInt32();
            int totalImages = (((encodedTotal - 51) / 3 - 17) / 44);

            Console.WriteLine($"[INFO] File: {Path.GetFileName(filePath)}");
            Console.WriteLine($"[INFO] Decoded Total Sprites: {totalImages}");

            const int EntrySize = 77;
            int[] spriteOffsets = new int[totalImages];

            // Pass 1: read all sprite offsets
            for (int i = 0; i < totalImages; i++)
            {
                fs.Seek(24 + i * EntrySize, SeekOrigin.Begin);
                spriteOffsets[i] = br.ReadInt32();
            }

            // Pass 2: extract image data
            for (int i = 0; i < totalImages; i++)
            {
                int iASDstart = spriteOffsets[i];
                int nextOffset = (i + 1 < totalImages) ? spriteOffsets[i + 1] : (int)fs.Length;

                fs.Seek(iASDstart + 100, SeekOrigin.Begin);
                int totalFrames = br.ReadInt32();

                // --- Auto-detect brush table offset ---
                int brushTableOffset = 0;
                int[] possibleOffsets = { 100, 104, 108 };
                bool foundOffset = false;

                foreach (int offsetTest in possibleOffsets)
                {
                    fs.Seek(iASDstart + offsetTest, SeekOrigin.Begin);
                    byte[] testData = br.ReadBytes(12);
                    if (testData.Length < 12)
                        continue;

                    short testX = BitConverter.ToInt16(testData, 0);
                    short testY = BitConverter.ToInt16(testData, 2);
                    short testW = BitConverter.ToInt16(testData, 4);
                    short testH = BitConverter.ToInt16(testData, 6);
                    short testPivotX = BitConverter.ToInt16(testData, 8);
                    short testPivotY = BitConverter.ToInt16(testData, 10);

                    // Must look like valid frame data
                    if (testX >= 0 && testY >= 0 && testW > 0 && testH > 0 &&
                        Math.Abs(testPivotX) < 2048 && Math.Abs(testPivotY) < 2048)
                    {
                        brushTableOffset = iASDstart + offsetTest;
                        foundOffset = true;
                        break;
                    }
                }

                if (!foundOffset)
                {
                    brushTableOffset = iASDstart + 108; // fallback
                }
                // --------------------------------------

                int brushTableSize = 12 * totalFrames;

                fs.Seek(brushTableOffset, SeekOrigin.Begin);
                byte[] brushData = br.ReadBytes(brushTableSize);

                FrameData[] frames = new FrameData[totalFrames];
                for (int f = 0; f < totalFrames; f++)
                {
                    int offset = f * 12;
                    frames[f].X = BitConverter.ToInt16(brushData, offset + 0);
                    frames[f].Y = BitConverter.ToInt16(brushData, offset + 2);
                    frames[f].Width = BitConverter.ToInt16(brushData, offset + 4);
                    frames[f].Height = BitConverter.ToInt16(brushData, offset + 6);
                    frames[f].PivotX = BitConverter.ToInt16(brushData, offset + 8);
                    frames[f].PivotY = BitConverter.ToInt16(brushData, offset + 10);
                }

                int imageOffset = iASDstart + 108 + (12 * totalFrames);
                int imageSize = nextOffset - imageOffset;
                if (imageSize <= 0 || imageOffset >= fs.Length)
                {
                    Console.WriteLine($"[WARN] Sprite {i} has invalid size or offset.");
                    continue;
                }

                fs.Seek(imageOffset, SeekOrigin.Begin);
                byte[] imageData = br.ReadBytes(imageSize);

                string basePath = Path.Combine(outputDir, $"{Path.GetFileNameWithoutExtension(filePath)}_{i:D4}");
                File.WriteAllBytes(basePath + ".bmp", imageData);

                // Write JSON frame data
                var jsonOptions = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
                };

                var json = new
                {
                    SpriteIndex = i,
                    FrameCount = totalFrames,
                    Frames = frames
                };

                string jsonPath = basePath + ".json";
                File.WriteAllText(jsonPath, JsonSerializer.Serialize(json, jsonOptions));

                Console.WriteLine($"[EXTRACTED] Sprite {i + 1}/{totalImages} -> {Path.GetFileName(basePath)}.bmp + .json");
            }

            Console.WriteLine($"[DONE] Extraction complete: {outputDir}");
        }
    }
}
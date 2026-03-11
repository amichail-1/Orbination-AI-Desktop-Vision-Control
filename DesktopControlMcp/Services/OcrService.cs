using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace DesktopControlMcp.Services;

/// <summary>
/// Shared OCR service with dark theme enhancement.
/// Used by ScreenTools and VisionTools.
/// </summary>
public static class OcrService
{
    /// <summary>
    /// Represents a line of text found by OCR with its bounding box in screen coordinates.
    /// </summary>
    public sealed class OcrTextLine
    {
        public string Text { get; set; } = "";
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int CenterX => X + Width / 2;
        public int CenterY => Y + Height / 2;
    }

    /// <summary>
    /// Run OCR on a bitmap and return structured text lines with screen coordinates.
    /// Checks darkness FIRST — if dark, enhances before OCR (single pass, not two).
    /// </summary>
    public static List<OcrTextLine> RecognizeText(Bitmap bmp, string language, int offsetX, int offsetY)
    {
        OcrResult? result;

        // Check darkness first — if dark, enhance BEFORE OCR (avoids running OCR twice)
        if (IsDarkImage(bmp))
        {
            using var enhanced = EnhanceForOcr(bmp);
            result = RunOcrEngine(enhanced, language);
            // If enhanced gave nothing, try original as fallback
            if (result == null || result.Lines.Count == 0)
                result = RunOcrEngine(bmp, language);
        }
        else
        {
            result = RunOcrEngine(bmp, language);
        }

        if (result == null) return [];

        var lines = new List<OcrTextLine>();
        foreach (var line in result.Lines)
        {
            double lx = line.Words.Min(w => w.BoundingRect.X);
            double ly = line.Words.Min(w => w.BoundingRect.Y);
            double lr = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            double lb = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);

            lines.Add(new OcrTextLine
            {
                Text = line.Text,
                X = offsetX + (int)lx,
                Y = offsetY + (int)ly,
                Width = (int)(lr - lx),
                Height = (int)(lb - ly),
            });
        }
        return lines;
    }

    /// <summary>
    /// Find specific text via OCR. Returns matching lines with screen coordinates.
    /// </summary>
    public static List<OcrTextLine> FindText(Bitmap bmp, string searchText, string language, int offsetX, int offsetY)
    {
        var allLines = RecognizeText(bmp, language, offsetX, offsetY);
        return allLines.Where(l => l.Text.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    // ─── Engine ──────────────────────────────────────────────────────────────────

    public static OcrResult? RunOcrEngine(Bitmap bmp, string language)
    {
        OcrEngine? engine;
        try
        {
            var lang = new Windows.Globalization.Language(language);
            engine = OcrEngine.TryCreateFromLanguage(lang);
        }
        catch
        {
            engine = OcrEngine.TryCreateFromUserProfileLanguages();
        }

        if (engine == null) return null;

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;

        var decoder = BitmapDecoder.CreateAsync(
            ms.AsRandomAccessStream()).AsTask().GetAwaiter().GetResult();

        var softwareBitmap = decoder.GetSoftwareBitmapAsync().AsTask().GetAwaiter().GetResult();

        if (softwareBitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
            softwareBitmap = SoftwareBitmap.Convert(softwareBitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        return engine.RecognizeAsync(softwareBitmap).AsTask().GetAwaiter().GetResult();
    }

    // ─── Dark Theme Enhancement ──────────────────────────────────────────────────

    public static bool IsDarkImage(Bitmap bmp, double threshold = 100.0)
    {
        int sampleStep = Math.Max(1, Math.Min(bmp.Width, bmp.Height) / 40);
        long totalLum = 0;
        int count = 0;

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                byte* ptr = (byte*)data.Scan0;
                for (int y = 0; y < bmp.Height; y += sampleStep)
                {
                    for (int x = 0; x < bmp.Width; x += sampleStep)
                    {
                        int offset = y * data.Stride + x * 4;
                        byte b = ptr[offset];
                        byte g = ptr[offset + 1];
                        byte r = ptr[offset + 2];
                        totalLum += (int)(0.2126 * r + 0.7152 * g + 0.0722 * b);
                        count++;
                    }
                }
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        double avgLum = count > 0 ? (double)totalLum / count : 128;
        return avgLum < threshold;
    }

    public static Bitmap EnhanceForOcr(Bitmap source)
    {
        bool isDark = IsDarkImage(source);
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);

        var srcData = source.LockBits(new Rectangle(0, 0, source.Width, source.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var dstData = result.LockBits(new Rectangle(0, 0, result.Width, result.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

        try
        {
            unsafe
            {
                byte* src = (byte*)srcData.Scan0;
                byte* dst = (byte*)dstData.Scan0;
                int stride = srcData.Stride;
                int w = source.Width;
                int h = source.Height;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int offset = y * stride + x * 4;
                        byte sb = src[offset];
                        byte sg = src[offset + 1];
                        byte sr = src[offset + 2];
                        byte sa = src[offset + 3];

                        if (isDark)
                        {
                            int ir = 255 - sr;
                            int ig = 255 - sg;
                            int ib = 255 - sb;
                            ir = Math.Clamp((int)(((ir - 128) * 1.4) + 128), 0, 255);
                            ig = Math.Clamp((int)(((ig - 128) * 1.4) + 128), 0, 255);
                            ib = Math.Clamp((int)(((ib - 128) * 1.4) + 128), 0, 255);
                            dst[offset] = (byte)ib;
                            dst[offset + 1] = (byte)ig;
                            dst[offset + 2] = (byte)ir;
                            dst[offset + 3] = sa;
                        }
                        else
                        {
                            int cr = Math.Clamp((int)(((sr - 128) * 1.2) + 128), 0, 255);
                            int cg = Math.Clamp((int)(((sg - 128) * 1.2) + 128), 0, 255);
                            int cb = Math.Clamp((int)(((sb - 128) * 1.2) + 128), 0, 255);
                            dst[offset] = (byte)cb;
                            dst[offset + 1] = (byte)cg;
                            dst[offset + 2] = (byte)cr;
                            dst[offset + 3] = sa;
                        }
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(srcData);
            result.UnlockBits(dstData);
        }

        return result;
    }
}

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CardPlayer.Models;
using QRCoder;
using SkiaSharp;

namespace CardPlayer.ViewModels;

/// <summary>
/// Generates in-memory front and back card preview bitmaps for the selected MediaEntry.
/// Card ratio is always 2.5 × 3.5 inches at 100 px/inch = 250 × 350 px.
/// Rendering is debounced (300ms) and runs off the UI thread.
/// </summary>
public partial class CardPreviewViewModel : ViewModelBase
{
    // Preview dimensions at 100 px/inch — 2.5 × 3.5 in
    private const int PW = 250;
    private const int PH = 350;

    // Real card dimensions (from PrintGeneratorProfile defaults)
    private const int RealW = 750;
    private const int RealH = 1050;

    // QR size and position constants (matching PrintGeneratorService)
    private const int RealQrSize = 100;  // data area only (QrSize in service)
    private const int RealQrQuietPx = 16;   // 4 modules × 4px per module
    private const int RealQrAboveBottom = 38;

    // Square art area: 0.25in margins on left/right/top → 2×2in square at 100px/in
    private const float SquareMargin = 25f;   // 0.25in × 100px/in
    private const float SquareSize = 200f;  // 2.0in × 100px/in

    private const int DebounceMs = 300;

    [ObservableProperty] private Bitmap? _frontBitmap;
    [ObservableProperty] private Bitmap? _backBitmap;

    private CancellationTokenSource? _cts;

    /// <summary>
    /// Schedules a preview render after a 300ms debounce.
    /// Cancels any in-flight render for the previous call.
    /// Rendering runs on a background thread; bitmaps are set on the UI thread.
    /// </summary>
    public void Update(MediaEntry? entry)
    {
        // Cancel previous pending render
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // Take a snapshot of the entry fields we need — entry may change on the UI thread
        var snapshot = entry == null ? null : new EntrySnapshot(entry);

        _ = Task.Run(async () =>
        {
            try
            {
                // Debounce: wait before doing any work
                await Task.Delay(DebounceMs, token);

                // Render on background thread
                var front = RenderFront(snapshot, token);
                var back = RenderBack(snapshot, token);

                if (token.IsCancellationRequested)
                {
                    front?.Dispose();
                    back?.Dispose();
                    return;
                }

                // Switch to UI thread to set properties
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested)
                    {
                        front?.Dispose();
                        back?.Dispose();
                        return;
                    }
                    FrontBitmap?.Dispose();
                    BackBitmap?.Dispose();
                    FrontBitmap = front;
                    BackBitmap = back;
                });
            }
            catch (OperationCanceledException) { }
            catch { /* ignore render errors */ }
        }, token);
    }

    // ── Snapshot to avoid capturing the live entry across threads ──────────
    private sealed class EntrySnapshot
    {
        public string? ArtPath { get; }
        public string? ArtBackPath { get; }
        public string ArtFit { get; }
        public string DisplayText { get; }
        public string TextSide { get; }
        public string TextFont { get; }
        public string TextStyle { get; }
        public int TextSize { get; }
        public string TextColor { get; }
        public string FrontBgColor { get; }
        public string BackBgColor { get; }
        public string? QrCode { get; }

        public EntrySnapshot(MediaEntry e)
        {
            ArtPath = e.ArtPath;
            ArtBackPath = e.ArtBackPath;
            ArtFit = e.ArtFit ?? "fill";
            DisplayText = e.DisplayText ?? "";
            TextSide = string.IsNullOrWhiteSpace(e.TextSide) ? "front" : e.TextSide;
            TextFont = e.TextFont ?? "";
            TextStyle = e.TextStyle ?? "Normal";
            TextSize = e.TextSize > 0 ? e.TextSize : 36;
            TextColor = e.TextColor ?? "#000000";
            FrontBgColor = e.FrontBgColor ?? "#FFFFFF";
            BackBgColor = e.BackBgColor ?? "#FFFFFF";
            QrCode = e.QrCode;
        }
    }

    // ── Front ─────────────────────────────────────────────────────────────
    private static Bitmap? RenderFront(EntrySnapshot? entry, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;
        using var surface = SKSurface.Create(new SKImageInfo(PW, PH));
        var canvas = surface.Canvas;

        // Background color — fill the full square bitmap; AXAML Border clips corners
        var bgColor = ParsePreviewColor(entry?.FrontBgColor ?? "#FFFFFF");
        canvas.Clear(bgColor);

        // Art image
        string? artPath = entry?.ArtPath;
        string? artFit = entry?.ArtFit;

        if (!string.IsNullOrWhiteSpace(artPath) && File.Exists(artPath))
        {
            try
            {
                using var artBmp = DecodeWithOrientation(artPath, PW, PH);
                if (artBmp != null)
                {
                    var destRect = new SKRect(0, 0, PW, PH);
                    DrawArtFit(canvas, artBmp, destRect, artFit ?? "fill");
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(artPath))
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(0xFFCCCCCC),
                TextSize = 11,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true,
            };
            canvas.DrawText("Front", PW / 2f, PH / 2f, paint);
            canvas.DrawText("(no art path)", PW / 2f, PH / 2f + 14, paint);
        }

        // Display text on front
        if (!string.IsNullOrWhiteSpace(entry?.DisplayText) && entry.TextSide == "front" &&
            !string.IsNullOrWhiteSpace(entry.DisplayText))
        {
            DrawPreviewText(canvas, entry.DisplayText,
                entry.TextFont, entry.TextStyle, entry.TextSize, entry.TextColor,
                onFront: true);
        }

        if (ct.IsCancellationRequested) return null;
        return SkSurfaceToAvalonia(surface);
    }

    // ── Back ──────────────────────────────────────────────────────────────
    private static Bitmap? RenderBack(EntrySnapshot? entry, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return null;
        using var surface = SKSurface.Create(new SKImageInfo(PW, PH));
        var canvas = surface.Canvas;

        // Background color — fill the full square bitmap; AXAML Border clips corners
        string backBg = entry?.BackBgColor ?? "#FFFFFF";
        var bgColor = ParsePreviewColor(backBg);
        canvas.Clear(bgColor);

        // Back art — over background, under QR code. Always "fit" mode.
        string? artBackPath = entry?.ArtBackPath;
        if (!string.IsNullOrWhiteSpace(artBackPath) && File.Exists(artBackPath))
        {
            try
            {
                using var artBmp = DecodeWithOrientation(artBackPath, PW, PH);
                if (artBmp != null)
                {
                    var destRect = new SKRect(0, 0, PW, PH);
                    DrawArtFit(canvas, artBmp, destRect, "fit");
                }
            }
            catch { }
        }

        string? qr = entry?.QrCode;
        if (!string.IsNullOrWhiteSpace(qr))
        {
            try
            {
                using var qrGenerator = new QRCodeGenerator();
                var qrData = qrGenerator.CreateQrCode(qr,
                    QRCodeGenerator.ECCLevel.L, forceUtf8: false, utf8BOM: false,
                    eciMode: QRCodeGenerator.EciMode.Default, requestedVersion: 2);
                using var qrCode = new PngByteQRCode(qrData);
                byte[] qrPng = qrCode.GetGraphic(4, true);
                using var qrBmp = SKBitmap.Decode(qrPng);

                if (qrBmp != null)
                {
                    float scaleX = (float)PW / RealW;
                    float scaleY = (float)PH / RealH;

                    // Bitmap includes quiet zone — scale it fully
                    float qrW = qrBmp.Width * scaleX;
                    float qrH = qrBmp.Height * scaleY;

                    // Data bottom = PH - RealQrAboveBottom*scaleY
                    // Bitmap top = data top - scaled quiet zone
                    float quietScaled = RealQrQuietPx * scaleX; // scaleX ≈ scaleY
                    float dataTopY = PH - (RealQrAboveBottom * scaleY) - (RealQrSize * scaleY);
                    float qrT = dataTopY - quietScaled;
                    float qrL = (PW - qrW) / 2f;

                    // White box behind QR if background isn't white
                    if (!IsPreviewWhite(backBg))
                    {
                        using var whitePaint = new SKPaint { Color = SKColors.White };
                        canvas.DrawRect(new SKRect(qrL, qrT, qrL + qrW, qrT + qrH), whitePaint);
                    }

                    var dest = new SKRect(qrL, qrT, qrL + qrW, qrT + qrH);
                    using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(qrBmp, dest, paint);
                }
            }
            catch { }
        }
        else
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(0xFFCCCCCC),
                TextSize = 11,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true,
            };
            canvas.DrawText("Back", PW / 2f, PH / 2f, paint);
            canvas.DrawText("(no QR code)", PW / 2f, PH / 2f + 14, paint);
        }

        // Display text on back (mirrors service: text goes at the top, above QR)
        if (!string.IsNullOrWhiteSpace(entry?.DisplayText) && entry.TextSide == "back" &&
            !string.IsNullOrWhiteSpace(entry.DisplayText))
        {
            DrawPreviewText(canvas, entry.DisplayText,
                entry.TextFont, entry.TextStyle, entry.TextSize, entry.TextColor,
                onFront: false);
        }

        if (ct.IsCancellationRequested) return null;
        return SkSurfaceToAvalonia(surface);
    }

    // ── Preview text rendering ─────────────────────────────────────────────
    // Mirrors PrintGeneratorService positioning, scaled to preview dimensions.
    private static void DrawPreviewText(SKCanvas canvas, string text,
        string fontName, string fontStyle, int fontSize, string textColor,
        bool onFront)
    {
        SKFontStyle skStyle = (fontStyle ?? "").ToLowerInvariant() switch
        {
            "bold" => SKFontStyle.Bold,
            "italic" => SKFontStyle.Italic,
            "bold italic" => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal
        };
        using var tf = (string.IsNullOrWhiteSpace(fontName)
            ? null
            : SKTypeface.FromFamilyName(fontName, skStyle)) ?? SKTypeface.Default;

        SKColor color = SKColors.Black;
        if (!string.IsNullOrWhiteSpace(textColor)) SKColor.TryParse(textColor, out color);

        // Scale real font size to preview (preview is PW/RealW = 250/750 = 1/3 scale)
        float scale = (float)PW / RealW;
        float previewFontSize = Math.Max(6f, (fontSize > 0 ? fontSize : 36) * scale);

        using var paint = new SKPaint
        {
            Typeface = tf,
            TextSize = previewFontSize,
            IsAntialias = true,
            Color = color,
            TextAlign = SKTextAlign.Center,
        };

        float maxWidth = (float)PW / RealW * Math.Min(500f, RealW - 40f);
        var lines = WrapText(text, paint, maxWidth);
        float lineH = paint.TextSize * 1.3f;

        float startY;
        if (onFront)
        {
            // Mirror service: text centred in the strip below the 600px art square
            // Service: artBottomOut = sidePx + 600 + vm; midY = (artBottomOut + outH) / 2
            // At preview scale (no vm):
            int sidePx = (RealW - 600) / 2;          // = 75px real
            float artBottomReal = sidePx + 600f;      // = 675px real
            float artBottomPreview = artBottomReal * ((float)PH / RealH);
            float textAreaMidY = (artBottomPreview + PH) / 2f;
            startY = textAreaMidY - (lines.Count * lineH) / 2f + paint.TextSize;
        }
        else
        {
            // Mirror DrawBackText: topMarginPx = (cardWidth - 600) / 2, scaled
            float topMarginReal = (RealW - 600f) / 2f;   // = 75px real
            float topMarginPreview = topMarginReal * scale;
            startY = topMarginPreview + paint.TextSize;
        }

        foreach (var line in lines)
        {
            canvas.DrawText(line, PW / 2f, startY, paint);
            startY += lineH;
        }
    }

    private static System.Collections.Generic.List<string> WrapText(
        string text, SKPaint paint, float maxWidth)
    {
        var lines = new System.Collections.Generic.List<string>();
        // Split on explicit newlines first, then word-wrap each paragraph
        var paragraphs = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var current = new System.Text.StringBuilder();
        foreach (var para in paragraphs)
        {
            if (current.Length > 0) { lines.Add(current.ToString()); current.Clear(); }
            var words = para.Split(' ');
            foreach (var word in words)
            {
                string test = current.Length == 0 ? word : current + " " + word;
                if (paint.MeasureText(test) > maxWidth && current.Length > 0)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                    current.Append(word);
                }
                else
                {
                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
    private static void DrawArtFit(SKCanvas canvas, SKBitmap art, SKRect dest, string fit)
    {
        float dw = dest.Width, dh = dest.Height;
        float sw = art.Width, sh = art.Height;

        switch (fit.ToLowerInvariant())
        {
            case "fill":
            default:
                {
                    // Scale to fill entire card, centre-crop
                    float scale = Math.Max(dw / sw, dh / sh);
                    float fw = sw * scale, fh = sh * scale;
                    float srcX = (fw - dw) / 2f / scale;
                    float srcY = (fh - dh) / 2f / scale;
                    var src = new SKRect(srcX, srcY, srcX + dw / scale, srcY + dh / scale);
                    using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(art, src, dest, p);
                    break;
                }

            case "fit":
                {
                    // Scale to fit inside entire card, letterbox
                    float scale = Math.Min(dw / sw, dh / sh);
                    float rw = sw * scale, rh = sh * scale;
                    var fitDest = new SKRect(
                        dest.Left + (dw - rw) / 2f, dest.Top + (dh - rh) / 2f,
                        dest.Left + (dw - rw) / 2f + rw, dest.Top + (dh - rh) / 2f + rh);
                    using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(art, fitDest, p);
                    break;
                }

            case "square fill":
                {
                    // Art fills a 2×2in square: 0.25in margin left/right/top
                    var squareDest = new SKRect(
                        SquareMargin, SquareMargin,
                        SquareMargin + SquareSize, SquareMargin + SquareSize);
                    // Centre-crop source to square
                    float sqSrc = Math.Min(sw, sh);
                    float sqSrcX = (sw - sqSrc) / 2f, sqSrcY = (sh - sqSrc) / 2f;
                    var src = new SKRect(sqSrcX, sqSrcY, sqSrcX + sqSrc, sqSrcY + sqSrc);
                    using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(art, src, squareDest, p);
                    break;
                }

            case "square fit":
                {
                    // Scale full image to fit inside 2×2in square, letterboxing if needed
                    var squareDest = new SKRect(
                        SquareMargin, SquareMargin,
                        SquareMargin + SquareSize, SquareMargin + SquareSize);
                    float fitScale = Math.Min(SquareSize / sw, SquareSize / sh);
                    float rw = sw * fitScale, rh = sh * fitScale;
                    var fitDest = new SKRect(
                        squareDest.Left + (SquareSize - rw) / 2f,
                        squareDest.Top + (SquareSize - rh) / 2f,
                        squareDest.Left + (SquareSize - rw) / 2f + rw,
                        squareDest.Top + (SquareSize - rh) / 2f + rh);
                    using var p = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(art, fitDest, p);
                    break;
                }
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes an image and applies EXIF orientation so the bitmap is always
    /// correctly rotated regardless of how the camera stored it.
    /// </summary>
    private static SKBitmap? DecodeWithOrientation(string path, int maxW = 0, int maxH = 0)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;

        SKBitmap bmp;
        if (maxW > 0 && maxH > 0)
        {
            // Ask codec for the smallest native downscale that still covers the target size.
            // Codecs support power-of-two downscales (1/2, 1/4, 1/8 …) — this avoids
            // loading the full multi-megapixel image just to display a 250×350 preview.
            var full = codec.Info;
            float scale = Math.Min((float)maxW / full.Width, (float)maxH / full.Height);
            // Clamp to [0.125, 1.0] — don't upscale, and codec min is usually 1/8
            scale = Math.Max(0.125f, Math.Min(1f, scale));
            var scaled = codec.GetScaledDimensions(scale);
            var info = new SKImageInfo(scaled.Width, scaled.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul);
            bmp = new SKBitmap();
            if (bmp.TryAllocPixels(info))
            {
                var result = codec.GetPixels(info, bmp.GetPixels());
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    bmp.Dispose();
                    bmp = SKBitmap.Decode(codec) ?? new SKBitmap();
                }
            }
            else
            {
                bmp.Dispose();
                bmp = SKBitmap.Decode(codec) ?? new SKBitmap();
            }
        }
        else
        {
            bmp = SKBitmap.Decode(codec) ?? new SKBitmap();
        }

        if (bmp.Width == 0) { bmp.Dispose(); return null; }
        return ApplyExifOrientation(bmp, codec.EncodedOrigin);
    }

    private static SKBitmap ApplyExifOrientation(SKBitmap bmp, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft || origin == SKEncodedOrigin.Default)
            return bmp;

        int w = bmp.Width, h = bmp.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                             or SKEncodedOrigin.LeftBottom or SKEncodedOrigin.RightBottom;

        var rotated = new SKBitmap(swap ? h : w, swap ? w : h);
        using var canvas = new SKCanvas(rotated);
        canvas.Clear(SKColors.Transparent);

        var m = origin switch
        {
            SKEncodedOrigin.TopRight => SKMatrix.CreateScale(-1, 1, w / 2f, h / 2f),
            SKEncodedOrigin.BottomRight => SKMatrix.CreateRotationDegrees(180, w / 2f, h / 2f),
            SKEncodedOrigin.BottomLeft => SKMatrix.CreateScale(1, -1, w / 2f, h / 2f),
            SKEncodedOrigin.LeftTop => SKMatrix.Concat(SKMatrix.CreateRotationDegrees(90), SKMatrix.CreateScale(-1, 1)),
            SKEncodedOrigin.RightTop => SKMatrix.CreateRotationDegrees(90, h / 2f, h / 2f),
            SKEncodedOrigin.RightBottom => SKMatrix.Concat(SKMatrix.CreateRotationDegrees(270), SKMatrix.CreateScale(-1, 1)),
            SKEncodedOrigin.LeftBottom => SKMatrix.CreateRotationDegrees(270, w / 2f, w / 2f),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(m);
        canvas.DrawBitmap(bmp, 0, 0);
        bmp.Dispose();
        return rotated;
    }

    private static SKColor ParsePreviewColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return SKColors.White;
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return new SKColor(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
        }
        catch { }
        return SKColors.White;
    }

    private static bool IsPreviewWhite(string? hex)
    {
        var c = ParsePreviewColor(hex);
        return c.Red == 255 && c.Green == 255 && c.Blue == 255;
    }

    private static Bitmap? SkSurfaceToAvalonia(SKSurface surface)
    {
        try
        {
            using var img = surface.Snapshot();
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = new MemoryStream(data.ToArray());
            return new Bitmap(ms);
        }
        catch { return null; }
    }
}
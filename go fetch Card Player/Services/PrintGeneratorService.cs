using System;
using System.IO;
using QRCoder;
using SkiaSharp;

namespace CardPlayer.Services;

public static class PrintGeneratorService
{
    private const int QrSize = 100;
    // 4 modules × 4 px/module = 16 px quiet zone per side (when GetGraphic quiet=true)
    private const int QrQuietPx = 16;

    public enum CardMode { SingleCard }

    public static string? GenerateSingleCard(string qrValue, string displayText,
        string outputFolder,
        int cardWidth = 750,
        int cardHeight = 1050,
        int qrAboveBottom = 87,
        bool drawDisplayText = false, string? fontName = null,
        string? fontStyle = null, int fontSize = 36, string? textColor = null,
        string backBgColor = "#FFFFFF",
        int printMarginH = 0, int printMarginV = 0,
        int artBleed = 0,
        string? artBackPath = null,
        CardMode mode = CardMode.SingleCard)
    {
        try
        {
            // Print margins expand (positive) or shrink (negative) the output canvas only.
            // Card content is placed within the card area [offX,offY → offX+cardW,offY+cardH].
            int outW = Math.Max(1, cardWidth + printMarginH * 2);
            int outH = Math.Max(1, cardHeight + printMarginV * 2);
            int offX = printMarginH;
            int offY = printMarginV;

            // QR centered on card; data bottom at qrAboveBottom from card bottom
            int qrDataLeft = cardWidth / 2 - QrSize / 2;
            int qrDataTop = (cardHeight - qrAboveBottom) - QrSize;
            int qrLeft = qrDataLeft - QrQuietPx + offX;
            int qrTop = qrDataTop - QrQuietPx + offY;

            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(qrValue,
                QRCodeGenerator.ECCLevel.L, forceUtf8: false, utf8BOM: false,
                eciMode: QRCodeGenerator.EciMode.Default, requestedVersion: 2);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrPng = qrCode.GetGraphic(4, true);
            using var qrBitmap = SKBitmap.Decode(qrPng);

            using var outBitmap = new SKBitmap(outW, outH);
            using (var canvas = new SKCanvas(outBitmap))
            {
                canvas.Clear(SKColors.White);

                // Background fills the card area expanded/shrunk by bleed, but clipped to output bounds
                float bgB = -artBleed;
                var bgRect = new SKRect(offX + bgB, offY + bgB, offX + cardWidth - bgB, offY + cardHeight - bgB);
                if (!IsWhite(backBgColor))
                {
                    using var bgPaint = new SKPaint { Color = ParseColor(backBgColor) };
                    canvas.DrawRect(bgRect, bgPaint);
                }

                // Back art — over background, under QR, always fit
                if (!string.IsNullOrWhiteSpace(artBackPath) && System.IO.File.Exists(artBackPath))
                {
                    using var artBmp = DecodeWithOrientation(artBackPath);
                    if (artBmp != null)
                    {
                        var artRect = FitRect(artBmp.Width, artBmp.Height,
                            new SKRect(offX, offY, offX + cardWidth, offY + cardHeight));
                        using var ap = new SKPaint { FilterQuality = SKFilterQuality.High };
                        canvas.DrawBitmap(artBmp, artRect, ap);
                    }
                }

                if (!IsWhite(backBgColor))
                    DrawQrWhiteBox(canvas, qrLeft, qrTop, qrBitmap.Width, qrBitmap.Height);
                canvas.DrawBitmap(qrBitmap, new SKPoint(qrLeft, qrTop));
                if (drawDisplayText && !string.IsNullOrWhiteSpace(displayText))
                    DrawBackText(canvas, displayText, outW, cardWidth, fontName, fontStyle, fontSize, textColor, offX, offY);
            }

            using var image = SKImage.FromBitmap(outBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            string safeName = SafeFileName(qrValue);
            string outPath = Path.Combine(outputFolder, $"{safeName}.png");
            using var stream = File.OpenWrite(outPath);
            data.SaveTo(stream);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    public static string? GenerateCardFront(string qrValue, string artPath,
        string artFit, string outputFolder,
        int cardWidth = 750, int cardHeight = 1050,
        int artBleed = 0,
        string bgColor = "#FFFFFF",
        bool drawDisplayText = false, string? fontName = null,
        string? fontStyle = null, int fontSize = 36, string? textColor = null,
        string? displayText = null,
        int printMarginH = 0, int printMarginV = 0)
    {
        try
        {
            int outW = Math.Max(1, cardWidth + printMarginH * 2);
            int outH = Math.Max(1, cardHeight + printMarginV * 2);
            int offX = printMarginH;
            int offY = printMarginV;

            using var outBitmap = new SKBitmap(outW, outH);
            using (var canvas = new SKCanvas(outBitmap))
            {
                canvas.Clear(SKColors.White);

                // Background expands/shrinks with bleed — no card-area clip, bleed fills into margins
                float bgB = -artBleed;
                var bgRect = new SKRect(offX + bgB, offY + bgB, offX + cardWidth - bgB, offY + cardHeight - bgB);
                if (!IsWhite(bgColor))
                {
                    using var bgPaint = new SKPaint { Color = ParseColor(bgColor) };
                    canvas.DrawRect(bgRect, bgPaint);
                }

                if (!string.IsNullOrWhiteSpace(artPath))
                {
                    if (!System.IO.File.Exists(artPath))
                        return $"Art file not found: {artPath}";
                    using var artBitmap = DecodeWithOrientation(artPath);
                    if (artBitmap == null)
                        return $"Could not decode image: {artPath}";
                    SKRect artRect = ComputeArtRect(artFit, artBitmap.Width, artBitmap.Height,
                        cardWidth, cardHeight, artBleed);
                    artRect = new SKRect(artRect.Left + offX, artRect.Top + offY,
                                         artRect.Right + offX, artRect.Bottom + offY);
                    // Clip to the square for square modes, or the bleed rect for fill/fit
                    string fitLower = (artFit ?? "fill").Trim().ToLowerInvariant();
                    SKRect clipRect = bgRect;
                    if (fitLower == "square fill" || fitLower == "square fit")
                    {
                        const int SqSize = 600;
                        int sidePx = (cardWidth - SqSize) / 2;
                        clipRect = new SKRect(sidePx + offX, sidePx + offY,
                                             sidePx + SqSize + offX, sidePx + SqSize + offY);
                    }
                    canvas.Save();
                    canvas.ClipRect(clipRect);
                    using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(artBitmap, artRect, paint);
                    canvas.Restore();
                }

                if (drawDisplayText && !string.IsNullOrWhiteSpace(displayText))
                {
                    int sidePx = (cardWidth - 600) / 2;
                    float artBottom = sidePx + 600f;
                    float textAreaMidY = (artBottom + cardHeight) / 2f + offY;
                    float textAreaWidth = Math.Min(500f, cardWidth - 40f);
                    DrawFrontText(canvas, displayText, outW, textAreaMidY, textAreaWidth,
                        fontName, fontStyle, fontSize, textColor);
                }
            }

            using var image = SKImage.FromBitmap(outBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            string safeName = SafeFileName(qrValue);
            string outPath = System.IO.Path.Combine(outputFolder, $"{safeName}_front.png");
            using var stream = System.IO.File.OpenWrite(outPath);
            data.SaveTo(stream);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    /// <summary>
    /// Computes art destination rect. Card is always cardWidth × cardHeight.
    /// bleed: positive = expands art beyond card edges; negative = shrinks art inward (border effect).
    /// Square modes ignore bleed and always place art in the fixed 2×2 in square.
    /// </summary>
    private static SKRect ComputeArtRect(string artFit, int artW, int artH,
        int cardWidth, int cardHeight, int bleed = 0)
    {
        string fit = (artFit ?? "fill").Trim().ToLowerInvariant();

        if (fit == "square fill" || fit == "square fit")
        {
            const int SqSize = 600;
            int sidePx = (cardWidth - SqSize) / 2;
            SKRect squareRect = new(sidePx, sidePx, sidePx + SqSize, sidePx + SqSize);
            return fit == "square fill" ? FillRect(artW, artH, squareRect)
                                        : FitRect(artW, artH, squareRect);
        }

        // Fill/Fit: positive bleed expands art rect beyond card edges; negative shrinks inward
        float b = -bleed;
        SKRect cardRect = new(b, b, cardWidth - b, cardHeight - b);
        if (fit == "fit") return FitRect(artW, artH, cardRect);
        return FillRect(artW, artH, cardRect);
    }

    /// <summary>Scale to fill rect, cropping overflow (centre-crop).</summary>
    private static SKRect FillRect(int artW, int artH, SKRect dest)
    {
        float destAr = dest.Width / dest.Height;
        float artAr = (float)artW / artH;
        float scale = destAr > artAr
            ? dest.Width / artW
            : dest.Height / artH;
        float w = artW * scale;
        float h = artH * scale;
        float x = dest.Left + (dest.Width - w) / 2f;
        float y = dest.Top + (dest.Height - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    /// <summary>Scale to fit entirely within rect, letterboxing (centre).</summary>
    private static SKRect FitRect(int artW, int artH, SKRect dest)
    {
        float destAr = dest.Width / dest.Height;
        float artAr = (float)artW / artH;
        float scale = destAr < artAr
            ? dest.Width / artW
            : dest.Height / artH;
        float w = artW * scale;
        float h = artH * scale;
        float x = dest.Left + (dest.Width - w) / 2f;
        float y = dest.Top + (dest.Height - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }

    /// <summary>Returns a 100x100 QR bitmap (caller must dispose).</summary>
    public static SKBitmap? GenerateQrBitmap(string qrValue)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(qrValue,
                QRCodeGenerator.ECCLevel.L, forceUtf8: false, utf8BOM: false,
                eciMode: QRCodeGenerator.EciMode.Default, requestedVersion: 2);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrPng = qrCode.GetGraphic(4, true);
            return SKBitmap.Decode(qrPng);
        }
        catch { return null; }
    }

    /// <summary>Generates a standalone QR code PNG (100x100px at 300dpi).</summary>
    public static string? GenerateQrOnly(string qrValue, string outputFolder)
    {
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(qrValue,
                QRCodeGenerator.ECCLevel.L, forceUtf8: false, utf8BOM: false,
                eciMode: QRCodeGenerator.EciMode.Default, requestedVersion: 2);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrPng = qrCode.GetGraphic(4, true);

            string safeName = SafeFileName(qrValue);
            string outPath = System.IO.Path.Combine(outputFolder, $"QR_{safeName}.png");

            System.IO.File.WriteAllBytes(outPath, qrPng);
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    private static SKBitmap? DecodeWithOrientation(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return null;
        var bmp = SKBitmap.Decode(codec);
        if (bmp == null) return null;
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

    private static bool IsWhite(string hex)
    {
        var c = ParseColor(hex);
        return c.Red == 255 && c.Green == 255 && c.Blue == 255;
    }

    /// <summary>
    /// Draws a white rectangle behind the QR code area to ensure required quiet zone
    /// is visible regardless of card background color.
    /// The QR bitmap includes 4-module (16px) quiet zone on each side.
    /// We draw white behind the full bitmap area so the quiet zone stays white on colored backgrounds.
    /// </summary>
    private static void DrawQrWhiteBox(SKCanvas canvas, int qrLeft, int qrTop, int qrW, int qrH)
    {
        using var paint = new SKPaint { Color = SKColors.White };
        canvas.DrawRect(new SKRect(qrLeft, qrTop, qrLeft + qrW, qrTop + qrH), paint);
    }

    private static SKColor ParseColor(string hex)
    {
        try
        {
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex[0..2], 16);
                byte g = Convert.ToByte(hex[2..4], 16);
                byte b = Convert.ToByte(hex[4..6], 16);
                return new SKColor(r, g, b);
            }
        }
        catch { }
        return SKColors.White;
    }

    private static System.Collections.Generic.List<string> WrapText(
        string text, SKPaint paint, float maxWidth)
    {
        var lines = new System.Collections.Generic.List<string>();
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

    /// <summary>Returns a back card bitmap (caller must dispose).</summary>
    public static SKBitmap? GenerateSingleCardBitmap(string qrValue,
        int cardWidth, int cardHeight, int qrAboveBottom,
        bool drawDisplayText = false, string? displayText = null,
        string? fontName = null, string? fontStyle = null,
        int fontSize = 36, string? textColor = null,
        string backBgColor = "#FFFFFF",
        int artBleed = 0,
        string? artBackPath = null)
    {
        try
        {
            int qrDataLeft = cardWidth / 2 - QrSize / 2;
            int qrDataTop = (cardHeight - qrAboveBottom) - QrSize;
            int qrLeft = qrDataLeft - QrQuietPx;
            int qrTop = qrDataTop - QrQuietPx;

            using var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(qrValue,
                QRCodeGenerator.ECCLevel.L, forceUtf8: false, utf8BOM: false,
                eciMode: QRCodeGenerator.EciMode.Default, requestedVersion: 2);
            using var qrCode = new PngByteQRCode(qrData);
            byte[] qrPng = qrCode.GetGraphic(4, true);
            using var qrBitmap = SKBitmap.Decode(qrPng);

            var bmp = new SKBitmap(cardWidth, cardHeight);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.White);

            // Background expands/shrinks with bleed; canvas edge clips any overflow naturally
            float bgB = -artBleed;
            var bgRect = new SKRect(bgB, bgB, cardWidth - bgB, cardHeight - bgB);
            if (!IsWhite(backBgColor))
            {
                using var bgPaint = new SKPaint { Color = ParseColor(backBgColor) };
                canvas.DrawRect(bgRect, bgPaint);
            }

            // Back art — over background, under QR, always fit
            if (!string.IsNullOrWhiteSpace(artBackPath) && System.IO.File.Exists(artBackPath))
            {
                using var artBmp = DecodeWithOrientation(artBackPath);
                if (artBmp != null)
                {
                    var artRect = FitRect(artBmp.Width, artBmp.Height,
                        new SKRect(0, 0, cardWidth, cardHeight));
                    using var ap = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(artBmp, artRect, ap);
                }
            }

            if (!IsWhite(backBgColor))
                DrawQrWhiteBox(canvas, qrLeft, qrTop, qrBitmap.Width, qrBitmap.Height);
            canvas.DrawBitmap(qrBitmap, new SKPoint(qrLeft, qrTop));

            if (drawDisplayText && !string.IsNullOrWhiteSpace(displayText))
                DrawBackText(canvas, displayText, cardWidth, cardWidth, fontName, fontStyle, fontSize, textColor);

            return bmp;
        }
        catch { return null; }
    }

    /// <summary>Returns a front card bitmap (caller must dispose).</summary>
    public static SKBitmap? GenerateCardFrontBitmap(string artPath, string artFit,
        int cardWidth, int cardHeight,
        int artBleed = 0,
        string bgColor = "#FFFFFF",
        bool drawDisplayText = false, string? fontName = null,
        string? fontStyle = null, int fontSize = 36, string? textColor = null,
        string? displayText = null)
    {
        try
        {
            var bmp = new SKBitmap(cardWidth, cardHeight);
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(SKColors.White);

            // Background expands/shrinks with bleed; canvas boundary clips any overflow
            float bgB = -artBleed;
            var bgRect = new SKRect(bgB, bgB, cardWidth - bgB, cardHeight - bgB);
            if (!IsWhite(bgColor))
            {
                using var bgPaint = new SKPaint { Color = ParseColor(bgColor) };
                canvas.DrawRect(bgRect, bgPaint);
            }

            if (!string.IsNullOrWhiteSpace(artPath) && System.IO.File.Exists(artPath))
            {
                using var artBitmap = DecodeWithOrientation(artPath);
                if (artBitmap != null)
                {
                    var destRect = ComputeArtRect(artFit, artBitmap.Width, artBitmap.Height,
                        cardWidth, cardHeight, artBleed);
                    string fitLower2 = (artFit ?? "fill").Trim().ToLowerInvariant();
                    SKRect clipRect2 = bgRect;
                    if (fitLower2 == "square fill" || fitLower2 == "square fit")
                    {
                        const int SqSize2 = 600;
                        int sidePx2 = (cardWidth - SqSize2) / 2;
                        clipRect2 = new SKRect(sidePx2, sidePx2, sidePx2 + SqSize2, sidePx2 + SqSize2);
                    }
                    canvas.Save();
                    canvas.ClipRect(clipRect2);
                    using var paint = new SKPaint { FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(artBitmap, destRect, paint);
                    canvas.Restore();
                }
            }

            if (drawDisplayText && !string.IsNullOrWhiteSpace(displayText))
            {
                int sidePx = (cardWidth - 600) / 2;
                float artBottom = sidePx + 600f;
                float textAreaMidY = (artBottom + cardHeight) / 2f;
                float textAreaWidth = Math.Min(500f, cardWidth - 40f);
                DrawFrontText(canvas, displayText, cardWidth, textAreaMidY, textAreaWidth,
                    fontName, fontStyle, fontSize, textColor);
            }

            return bmp;
        }
        catch { return null; }
    }

    private static string SafeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return string.IsNullOrWhiteSpace(name) ? "card" : name.Trim();
    }

    /// <summary>
    /// Draws display text at the top of the back of the card.
    /// Top margin = (cardWidth - 600px) / 2  (≈ 2 inches at 300 DPI on a 750px wide card).
    /// offX/offY are print-margin offsets applied when the output canvas is larger than the card.
    /// </summary>
    private static void DrawBackText(SKCanvas canvas, string displayText,
        int outW, int cardWidth,
        string? fontName, string? fontStyle, int fontSize, string? textColor,
        int offX = 0, int offY = 0)
    {
        if (string.IsNullOrWhiteSpace(displayText)) return;

        SKFontStyle skStyle = (fontStyle ?? "Normal").ToLowerInvariant() switch
        {
            "bold" => SKFontStyle.Bold,
            "italic" => SKFontStyle.Italic,
            "bold italic" => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal
        };
        using var tf = SKTypeface.FromFamilyName(fontName ?? "Arial", skStyle) ?? SKTypeface.Default;

        SKColor color = SKColors.Black;
        if (!string.IsNullOrWhiteSpace(textColor)) SKColor.TryParse(textColor, out color);

        using var textPaint = new SKPaint
        {
            Typeface = tf,
            TextSize = fontSize > 0 ? fontSize : 36,
            IsAntialias = true,
            Color = color,
            TextAlign = SKTextAlign.Center,
        };

        float textAreaWidth = Math.Min(500f, outW - 40f);
        var lines = WrapText(displayText, textPaint, textAreaWidth);
        float lineH = textPaint.TextSize * 1.3f;

        // Top margin = (cardWidth - 600) / 2, matching the 2-inch inset used elsewhere
        float topMarginPx = (cardWidth - 600) / 2f;
        float startY = topMarginPx + textPaint.TextSize + offY;

        foreach (var line in lines)
        {
            canvas.DrawText(line, outW / 2f, startY, textPaint);
            startY += lineH;
        }
    }

    private static void DrawFrontText(SKCanvas canvas, string displayText,
        int cardWidth, float textAreaMidY, float textAreaWidth,
        string? fontName, string? fontStyle, int fontSize, string? textColor)
    {
        if (string.IsNullOrWhiteSpace(displayText)) return;

        SKFontStyle skStyle = (fontStyle ?? "Normal").ToLowerInvariant() switch
        {
            "bold" => SKFontStyle.Bold,
            "italic" => SKFontStyle.Italic,
            "bold italic" => SKFontStyle.BoldItalic,
            _ => SKFontStyle.Normal
        };
        using var tf = SKTypeface.FromFamilyName(fontName ?? "Arial", skStyle) ?? SKTypeface.Default;

        SKColor color = SKColors.Black;
        if (!string.IsNullOrWhiteSpace(textColor)) SKColor.TryParse(textColor, out color);

        using var textPaint = new SKPaint
        {
            Typeface = tf,
            TextSize = fontSize > 0 ? fontSize : 36,
            IsAntialias = true,
            Color = color,
            TextAlign = SKTextAlign.Center,
        };

        var lines = WrapText(displayText, textPaint, textAreaWidth);
        float lineH = textPaint.TextSize * 1.3f;
        float startY = textAreaMidY - (lines.Count * lineH) / 2f + textPaint.TextSize;
        foreach (var line in lines)
        {
            canvas.DrawText(line, cardWidth / 2f, startY, textPaint);
            startY += lineH;
        }
    }
}
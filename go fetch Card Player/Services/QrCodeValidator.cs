using System.Linq;
using System.Text;

namespace CardPlayer.Services;

public static class QrCodeValidator
{
    // QR alphanumeric charset
    private const string AlphanumericChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 $%*+-./:";

    private const int MaxAlphanumeric = 47; // version 2, ECC L
    private const int MaxBytes        = 32; // version 2, ECC L, byte mode

    public static bool IsAlphanumeric(string value) =>
        value.All(c => AlphanumericChars.Contains(c));

    /// <summary>
    /// Returns null if the value fits, or an error message if it doesn't.
    /// </summary>
    public static string? Validate(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        if (IsAlphanumeric(value))
        {
            if (value.Length > MaxAlphanumeric)
                return $"Too long for QR v2 alphanumeric ({value.Length}/{MaxAlphanumeric} chars)";
        }
        else
        {
            int byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount > MaxBytes)
                return $"Too long for QR v2 byte mode ({byteCount}/{MaxBytes} bytes)";
        }
        return null;
    }

    /// <summary>
    /// Trims a string to the max length valid for QR v2 ECC L.
    /// Respects whether content is alphanumeric or byte mode.
    /// </summary>
    public static string TrimToQrLimit(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Determine mode based on full string
        // If trimmed version stays alphanumeric, use alphanumeric limit
        if (IsAlphanumeric(value))
            return value.Length <= MaxAlphanumeric ? value : value[..MaxAlphanumeric];

        // Byte mode — trim to MaxBytes UTF-8 bytes without splitting a multibyte char
        var bytes = Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= MaxBytes) return value;

        // Trim bytes then decode, stepping back if we split a multibyte sequence
        int limit = MaxBytes;
        while (limit > 0)
        {
            try
            {
                // Check if this is a valid UTF-8 boundary
                Encoding.UTF8.GetString(bytes, 0, limit);
                return Encoding.UTF8.GetString(bytes, 0, limit);
            }
            catch { limit--; }
        }
        return "";
    }
}

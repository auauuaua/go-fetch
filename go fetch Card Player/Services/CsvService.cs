using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CardPlayer.Models;

namespace CardPlayer.Services;

public static class CsvService
{
    // ── Generic quoted-field CSV parser ────────────────────────────────────

    public static IEnumerable<string[]> ReadRows(string path)
    {
        if (!File.Exists(path)) yield break;

        // Read entire file so quoted fields containing newlines are handled correctly.
        // RFC 4180: a field enclosed in double-quotes may contain newlines.
        string all = File.ReadAllText(path, Encoding.UTF8);
        int pos = 0;

        while (pos < all.Length)
        {
            // Skip blank lines (but not newlines inside quoted fields)
            while (pos < all.Length && (all[pos] == '\r' || all[pos] == '\n')) pos++;
            if (pos >= all.Length) break;

            var row = ParseRow(all, ref pos);
            if (row.Length > 0) yield return row;
        }
    }

    private static string[] ParseRow(string text, ref int pos)
    {
        var fields = new List<string>();
        while (pos <= text.Length)
        {
            if (pos == text.Length || text[pos] == '\r' || text[pos] == '\n')
            {
                // End of row — consume the line ending
                if (pos < text.Length && text[pos] == '\r') pos++;
                if (pos < text.Length && text[pos] == '\n') pos++;
                break;
            }

            if (text[pos] == '"')
            {
                // Quoted field — may span multiple lines
                pos++; // skip opening quote
                var sb = new StringBuilder();
                while (pos < text.Length)
                {
                    if (text[pos] == '"')
                    {
                        pos++;
                        if (pos < text.Length && text[pos] == '"') { sb.Append('"'); pos++; } // escaped quote
                        else break; // closing quote
                    }
                    else sb.Append(text[pos++]);
                }
                fields.Add(sb.ToString());
                if (pos < text.Length && text[pos] == ',') pos++; // skip comma
            }
            else
            {
                // Unquoted field — read until comma or end of line
                int start = pos;
                while (pos < text.Length && text[pos] != ',' && text[pos] != '\r' && text[pos] != '\n')
                    pos++;
                fields.Add(text[start..pos]);
                if (pos < text.Length && text[pos] == ',') pos++; // skip comma
            }
        }
        return fields.ToArray();
    }

    // SplitCsvLine kept for any callers that use it directly
    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        int i = 0;
        while (i <= line.Length)
        {
            if (i == line.Length) { result.Add(""); break; }
            if (line[i] == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < line.Length)
                {
                    if (line[i] == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i += 2; }
                    else if (line[i] == '"') { i++; break; }
                    else sb.Append(line[i++]);
                }
                result.Add(sb.ToString());
                if (i < line.Length && line[i] == ',') i++;
            }
            else
            {
                int comma = line.IndexOf(',', i);
                if (comma < 0) { result.Add(line[i..]); break; }
                result.Add(line[i..comma]);
                i = comma + 1;
            }
        }
        return result.ToArray();
    }

    // ── Programs.csv ───────────────────────────────────────────────────────

    public static List<ProgramEntry> LoadPrograms(string path)
    {
        var list = new List<ProgramEntry>();
        bool first = true;
        foreach (var row in ReadRows(path))
        {
            if (first) { first = false; continue; }
            list.Add(new ProgramEntry(row));
        }
        return list;
    }

    public static void SavePrograms(string path, IEnumerable<ProgramEntry> entries)
    {
        var lines = new List<string> { "Type digit,Media Type,Program Name,Program Path,Options,Send Keys,Send Keys Delay" };
        foreach (var e in entries) lines.Add(e.ToCsvLine());
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    // ── Cards.csv ─────────────────────────────────────────────────────

    /// <summary>
    /// Column names recognised in Cards.csv. Order-independent — matched by header row.
    /// Extra or missing columns are silently ignored.
    /// </summary>
    public static readonly string[] MediaColumns =
    {
        "Type_Digit", "Title", "QR_Code", "Path", "Art_Path",
        "Art_Fit", "Art_Back_Path", "State", "Text_Side",
        "Text_Font", "Text_Style", "Text_Size", "Text_Color",
        "Front_Bg_Color", "Back_Bg_Color"
    };

    public static List<MediaEntry> LoadMedia(string path)
    {
        var list = new List<MediaEntry>();
        int[]? map = null;
        foreach (var row in ReadRows(path))
        {
            // First row: build column-index map
            if (map == null)
            {
                map = BuildColumnMap(row, MediaColumns);
                continue;
            }
            list.Add(new MediaEntry(RemapRow(row, map, MediaColumns.Length)));
        }
        return list;
    }

    /// <summary>
    /// Builds an array where map[canonicalIndex] = actual CSV column index, or -1 if absent.
    /// Comparison is case-insensitive; common aliases are handled (e.g. Display_Text → Title).
    /// </summary>
    public static int[] BuildColumnMap(string[] headerRow, string[] canonical)
    {
        var map = new int[canonical.Length];
        for (int i = 0; i < map.Length; i++) map[i] = -1;

        for (int col = 0; col < headerRow.Length; col++)
        {
            string h = headerRow[col].Trim();
            // Alias: old "Display_Text" column name → Title
            if (h.Equals("Display_Text", StringComparison.OrdinalIgnoreCase)) h = "Title";

            for (int ci = 0; ci < canonical.Length; ci++)
            {
                if (canonical[ci].Equals(h, StringComparison.OrdinalIgnoreCase))
                { map[ci] = col; break; }
            }
        }
        return map;
    }

    /// <summary>
    /// Remaps a data row using a column map, returning a fixed-length array aligned to
    /// canonical column order. Missing columns produce empty strings.
    /// </summary>
    public static string[] RemapRow(string[] row, int[] map, int length)
    {
        var result = new string[length];
        for (int i = 0; i < length; i++)
        {
            int col = map[i];
            result[i] = col >= 0 && col < row.Length ? row[col] : "";
        }
        return result;
    }

    public static void SaveMedia(string path, IEnumerable<MediaEntry> entries)
    {
        var list = entries.ToList();

        // Deduplicate QR codes at save time
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in list)
        {
            if (string.IsNullOrWhiteSpace(e.QrCode)) continue;

            string code = MakeUniqueQrCode(e.QrCode, seen);
            if (code != e.QrCode)
                e.SetQrCodeSilent(code);
            seen.Add(code);
        }

        var lines = new List<string> { "Type_Digit,Title,QR_Code,Path,Art_Path,Art_Fit,Art_Back_Path,State,Text_Side,Text_Font,Text_Style,Text_Size,Text_Color,Front_Bg_Color,Back_Bg_Color" };
        foreach (var e in list) lines.Add(e.ToCsvLine());
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    /// <summary>
    /// Returns a QR code string that does not exist in <paramref name="seen"/>.
    /// Appends/increments a numeric suffix (2–99) within the 32-character limit.
    /// The base is always trimmed to leave room for the suffix.
    /// </summary>
    public static string MakeUniqueQrCode(string code, HashSet<string> seen)
    {
        code = QrCodeValidator.TrimToQrLimit(code);
        if (!seen.Contains(code)) return code;

        // Strip any existing numeric suffix (2-99)
        string stem = code;
        if (stem.Length >= 1 && char.IsDigit(stem[^1]))
        {
            int i = stem.Length - 1;
            while (i > 0 && char.IsDigit(stem[i - 1])) i--;
            int existingNum = int.Parse(stem[i..]);
            if (existingNum >= 2)
                stem = stem[..i];
        }

        // Trim stem so a 2-digit suffix (up to "99") fits in 32 bytes
        // Reserve 2 chars for "99" worst case
        const int MaxBase = 32 - 2; // = 30
        if (Encoding.UTF8.GetByteCount(stem) > MaxBase)
        {
            // Trim UTF-8 bytes safely
            var bytes = Encoding.UTF8.GetBytes(stem);
            int limit = MaxBase;
            while (limit > 0)
            {
                try { Encoding.UTF8.GetString(bytes, 0, limit); break; }
                catch { limit--; }
            }
            stem = Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(stem), 0, limit);
        }

        for (int n = 2; n <= 99; n++)
        {
            string candidate = stem + n;
            if (!seen.Contains(candidate))
                return candidate;
        }

        // Extremely unlikely fallback
        return stem + "99";
    }

    /// <summary>
    /// Appends or increments a trailing digit to make a QR code unique.
    /// Kept for internal compatibility; prefer MakeUniqueQrCode for new code.
    /// </summary>
    private static string IncrementQrCode(string code)
    {
        if (string.IsNullOrEmpty(code)) return "2";

        char last = code[^1];
        string stem = code[..^1];

        string candidate;
        if (last >= '2' && last <= '8')
            candidate = stem + (char)(last + 1);          // 2→3 … 8→9
        else if (last == '9')
            candidate = stem + '0';                       // 9→0
        else if (last == '1')
            candidate = stem + '2';                       // 1→2
        else
            candidate = code + '2';                       // non-digit: append 2

        // Trim to QR v2 limit
        candidate = Services.QrCodeValidator.TrimToQrLimit(candidate);
        return candidate;
    }

    // ── IRCodes.csv ────────────────────────────────────────────────────────

    public static List<IrCodeEntry> LoadIrCodes(string path)
    {
        var list = new List<IrCodeEntry>();
        bool first = true;
        foreach (var row in ReadRows(path))
        {
            if (first) { first = false; continue; }
            list.Add(new IrCodeEntry(row));
        }
        return list;
    }

    public static void SaveIrCodes(string path, IEnumerable<IrCodeEntry> entries)
    {
        var lines = new List<string> { "Type digit,IR Code,Key Send,Remote label,Grid Row,Grid Col" };
        foreach (var e in entries) lines.Add(e.ToCsvLine());
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }
}
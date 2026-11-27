using System.Text;

namespace GameVault.Core;

/// <summary>Comma-separated text as spreadsheets read it (ticket #98).</summary>
public static class Csv
{
    /// <summary>
    /// One cell. Text that a spreadsheet would run as a formula (starting, after any blanks, with = + - or @) gets a leading apostrophe; cells with commas,
    /// quotes or line breaks are quoted.
    /// </summary>
    public static string Cell(string? value)
    {
        var text = value ?? "";
        var start = text.TrimStart(' ', '\t', '\r', '\n');
        if (start.Length > 0 && start[0] is '=' or '+' or '-' or '@') text = "'" + text;
        return text.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static string Table(IEnumerable<string> header, IEnumerable<IEnumerable<string?>> rows)
    {
        var text = new StringBuilder();
        text.AppendJoin(',', header.Select(Cell)).Append("\r\n");
        foreach (var row in rows) text.AppendJoin(',', row.Select(Cell)).Append("\r\n");
        return text.ToString();
    }
}

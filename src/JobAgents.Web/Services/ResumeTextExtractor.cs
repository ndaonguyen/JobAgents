using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace JobAgents.Web.Services;

/// <summary>
/// Extracts plain text from an uploaded resume file. Supports PDF (PdfPig), Word .docx
/// (OpenXml), and plain text (.txt / .md). Used by the Home page when the user uploads instead
/// of pasting.
/// </summary>
public sealed class ResumeTextExtractor
{
    public static readonly string[] SupportedExtensions = [".txt", ".md", ".pdf", ".docx"];

    public bool IsSupported(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts text from a seekable stream, dispatching on the file extension.</summary>
    public string Extract(string fileName, Stream content)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => ExtractPdf(content),
            ".docx" => ExtractDocx(content),
            ".txt" or ".md" => ExtractPlainText(content),
            _ => throw new NotSupportedException(
                $"Unsupported file type '{extension}'. Use {string.Join(", ", SupportedExtensions)}."),
        };
    }

    private static string ExtractPdf(Stream content)
    {
        using var document = PdfDocument.Open(content);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            // Layout-aware extraction keeps reading order and line breaks; raw page.Text does not.
            string pageText;
            try
            {
                pageText = ContentOrderTextExtractor.GetText(page);
            }
            catch
            {
                pageText = page.Text;
            }

            builder.AppendLine(pageText);
            builder.AppendLine();
        }

        return Normalize(builder.ToString());
    }

    private static string ExtractDocx(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body;
        if (body is null)
            return string.Empty;

        // Render each paragraph on its own line so structure survives, instead of one InnerText blob.
        var paragraphs = body
            .Descendants<DocumentFormat.OpenXml.Wordprocessing.Paragraph>()
            .Select(p => p.InnerText);
        return Normalize(string.Join("\n", paragraphs));
    }

    private static string ExtractPlainText(Stream content)
    {
        using var reader = new StreamReader(content);
        return Normalize(reader.ReadToEnd());
    }

    // Common bullet glyphs PDFs use; we normalise them all to "- ".
    private static readonly Regex BulletPrefix = new(@"^\s*[•●▪◦‣∙·*–—]\s*", RegexOptions.Compiled);

    // A line that is only a page number, e.g. "2", "Page 3", "1 of 4", "1/4".
    private static readonly Regex PageNumberLine =
        new(@"^\s*(page\s+)?\d{1,4}(\s*(of|/)\s*\d{1,4})?\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A word split across a line break by hyphenation, e.g. "micro-\nservices".
    private static readonly Regex Hyphenation = new(@"(\p{L})-\n(\p{L})", RegexOptions.Compiled);

    /// <summary>
    /// Tidies extracted text: normalises newlines, rejoins hyphenated line breaks, standardises bullet
    /// glyphs to "- ", drops page-number lines, collapses whitespace and big gaps.
    /// </summary>
    private static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        text = Hyphenation.Replace(text, "$1$2"); // rejoin "micro-\nservices" -> "microservices"

        var lines = text
            .Split('\n')
            .Select(line => Regex.Replace(line, "[ \t]+", " ").TrimEnd())
            .Select(line => BulletPrefix.Replace(line, "- "))
            .Where(line => !PageNumberLine.IsMatch(line));

        var joined = string.Join("\n", lines);
        joined = Regex.Replace(joined, "\n{3,}", "\n\n"); // at most one blank line between blocks
        return joined.Trim();
    }
}

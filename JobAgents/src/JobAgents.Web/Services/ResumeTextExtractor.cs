using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

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
            builder.AppendLine(page.Text);
        return builder.ToString().Trim();
    }

    private static string ExtractDocx(Stream content)
    {
        using var document = WordprocessingDocument.Open(content, isEditable: false);
        var body = document.MainDocumentPart?.Document.Body;
        return body?.InnerText.Trim() ?? string.Empty;
    }

    private static string ExtractPlainText(Stream content)
    {
        using var reader = new StreamReader(content);
        return reader.ReadToEnd().Trim();
    }
}

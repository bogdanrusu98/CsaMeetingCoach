using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace CsaMeetingCoach.Api;

public static class PdfTextExtractionEngine
{
    public const int MaximumPages = 250;
    public const int MaximumCharacters = 100_000;
    private const int MaximumDecodedStreamBytes = 32 * 1024 * 1024;
    private const long MaximumDecodedDocumentBytes = 96L * 1024 * 1024;

    public static string Extract(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var filterProvider = new BoundedFilterProvider(
                MaximumDecodedStreamBytes,
                MaximumDecodedDocumentBytes);
            var options = new ParsingOptions
            {
                UseLenientParsing = false,
                SkipMissingFonts = true,
                MaxStackDepth = 50,
                FilterProvider = filterProvider
            };
            using var document = PdfDocument.Open(path, options);
            if (document.IsEncrypted)
            {
                throw new PdfExtractionRejectedException(
                    "Password-protected PDF files are not supported.");
            }
            if (document.NumberOfPages > MaximumPages)
            {
                throw new PdfExtractionRejectedException(
                    $"PDF files cannot exceed {MaximumPages} pages.");
            }

            var builder = new StringBuilder();
            for (var pageNumber = 1;
                 pageNumber <= document.NumberOfPages && builder.Length < MaximumCharacters;
                 pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page, true);
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                var remaining = MaximumCharacters - builder.Length;
                builder.Append(pageText.AsSpan(0, Math.Min(pageText.Length, remaining)));
                builder.AppendLine();
            }

            var content = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(content)
                ? throw new PdfExtractionRejectedException(
                    "The PDF contains no extractable text. Image-only PDFs require OCR and are not supported.")
                : content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (PdfExtractionRejectedException)
        {
            throw;
        }
        catch (Exception exception) when (IsRejectedPdfInput(exception))
        {
            throw new PdfExtractionRejectedException(
                "The PDF is malformed, protected, or exceeds safe extraction limits.",
                exception);
        }
    }

    private static bool IsRejectedPdfInput(Exception exception) =>
        exception is PdfDocumentEncryptedException
            or PdfDocumentFormatException
            or PdfDocumentStackDepthException
            or InvalidOperationException
            or ArgumentException
            or IndexOutOfRangeException
            or NotSupportedException
            or OverflowException
            or IOException;

    private sealed class BoundedFilterProvider(
        int maximumStreamBytes,
        long maximumDocumentBytes) : IFilterProvider
    {
        private readonly IFilterProvider _inner = DefaultFilterProvider.Instance;
        private long _decodedDocumentBytes;

        public IReadOnlyList<IFilter> GetFilters(DictionaryToken streamDictionary) =>
            Wrap(_inner.GetFilters(streamDictionary));

        public IReadOnlyList<IFilter> GetNamedFilters(
            IReadOnlyList<NameToken> filterNames) =>
            Wrap(_inner.GetNamedFilters(filterNames));

        public IReadOnlyList<IFilter> GetAllFilters() =>
            Wrap(_inner.GetAllFilters());

        private IReadOnlyList<IFilter> Wrap(IReadOnlyList<IFilter> filters) =>
            filters.Select(filter => (IFilter)new BoundedFilter(filter, this)).ToArray();

        private void CountDecodedBytes(int decodedBytes)
        {
            if (decodedBytes > maximumStreamBytes)
            {
                throw new PdfExtractionRejectedException(
                    "A decoded PDF stream exceeds the safe size limit.");
            }

            var total = Interlocked.Add(ref _decodedDocumentBytes, decodedBytes);
            if (total > maximumDocumentBytes)
            {
                throw new PdfExtractionRejectedException(
                    "Decoded PDF content exceeds the safe aggregate size limit.");
            }
        }

        private sealed class BoundedFilter(
            IFilter inner,
            BoundedFilterProvider owner) : IFilter
        {
            public bool IsSupported => inner.IsSupported;

            public Memory<byte> Decode(
                Memory<byte> input,
                DictionaryToken streamDictionary,
                IFilterProvider filterProvider,
                int filterIndex)
            {
                var decoded = inner.Decode(
                    input,
                    streamDictionary,
                    filterProvider,
                    filterIndex);
                owner.CountDecodedBytes(decoded.Length);
                return decoded;
            }
        }
    }
}

public sealed class PdfExtractionRejectedException : InvalidOperationException
{
    public PdfExtractionRejectedException(string message) : base(message)
    {
    }

    public PdfExtractionRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

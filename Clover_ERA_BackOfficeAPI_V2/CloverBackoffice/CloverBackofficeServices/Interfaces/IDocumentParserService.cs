namespace CloverBackoffice.Services.Interfaces
{
    public interface IDocumentParserService
    {
        Task<string> ExtractTextAsync(Stream fileStream, string fileName);
    }
}

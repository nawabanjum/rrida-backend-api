namespace CloverBackoffice.Infrastructure.DTO
{
    public record ModelEndpoint(
        string Name,
        string Endpoint,
        string ApiKey,
        string ApiVersion,
        double Temperature,
        int MaxTokens);
}

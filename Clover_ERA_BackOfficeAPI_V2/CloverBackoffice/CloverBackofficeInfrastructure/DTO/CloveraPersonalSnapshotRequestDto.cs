using System;

namespace CloverBackoffice.Infrastructure.DTO
{
    /// <summary>
    /// Request body for the Clovera Personal App snapshot endpoint.
    /// The model is not selected by the caller — the service uses the default
    /// active Search-model from ModelConfiguration. Temperature/max-tokens are
    /// fixed in the service.
    /// </summary>
    public class CloveraPersonalSnapshotRequestDto
    {
        public long? ProfileId { get; set; }
        public long? UnitId { get; set; }
        public DateTimeOffset? FromDate { get; set; }
        public DateTimeOffset? ToDate { get; set; }
    }
}

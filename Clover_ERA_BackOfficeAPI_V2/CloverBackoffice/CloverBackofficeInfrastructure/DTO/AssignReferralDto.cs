namespace CloverBackoffice.Infrastructure.DTO
{
    public class AssignReferralDto
    {
        public int ProfileId { get; set; }
        public int PartnerId { get; set; }

        public int? ReferredTo { get; set; }
        public int? ReferredBy { get; set; }
    }
}

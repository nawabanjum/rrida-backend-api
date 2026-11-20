namespace CloverBackoffice.Infrastructure.DTO
{
    public class PlanDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public int MaxTeams { get; set; }
        public int MaxPeople { get; set; }
        public decimal MonthlyPrice { get; set; }
        public decimal AnnualPrice { get; set; }
        public decimal PricePerTeamMonthly { get; set; }
        public int SavingsPercent { get; set; }
        public bool IsCustom { get; set; }
        public bool IsPopular { get; set; }
        public string? Features { get; set; }
        public int TrialDays { get; set; }
        public bool RequiresCreditCard { get; set; }
    }
}

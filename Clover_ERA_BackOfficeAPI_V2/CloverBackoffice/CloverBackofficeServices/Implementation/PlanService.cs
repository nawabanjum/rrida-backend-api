using CloverBackoffice.Infrastructure.DTO;
using CloverBackoffice.Repository.Interfaces;
using CloverBackoffice.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace CloverBackoffice.Services.Implementation
{
    public class PlanService : IPlanService
    {
        private readonly IPlanRepository _planRepository;
        private readonly StripeSettings _stripeSettings;

        public PlanService(IPlanRepository planRepository, IOptions<StripeSettings> stripeSettings)
        {
            _planRepository = planRepository;
            _stripeSettings = stripeSettings.Value;
        }

        public async Task<List<PlanDto>> GetAllActivePlansAsync()
        {
            var plans = await _planRepository.GetAllActiveAsync();
            return plans.Select(MapToDto).ToList();
        }

        public async Task<PlanDto?> GetPlanBySlugAsync(string slug)
        {
            var plan = await _planRepository.GetBySlugAsync(slug);
            return plan == null ? null : MapToDto(plan);
        }

        public async Task<PlanDto?> GetPlanByIdAsync(int id)
        {
            var plan = await _planRepository.GetByIdAsync(id);
            return plan == null ? null : MapToDto(plan);
        }

        public PriceCalculationDto CalculatePrice(int teamCount)
        {
            if (teamCount < 1)
                throw new InvalidOperationException("Team count must be at least 1.");

            // Flat price per license — no tiers, no bulk discount.
            var pricePerTeam = _stripeSettings.PricePerLicenseMonthly;
            var monthlyTotal = pricePerTeam * teamCount;
            var annualTotal = monthlyTotal * 10; // annual billed at 10× monthly

            return new PriceCalculationDto
            {
                TeamCount = teamCount,
                PricePerTeam = pricePerTeam,
                MonthlyTotal = monthlyTotal,
                AnnualTotal = annualTotal,
                TierName = "Standard",
                SavingsPercent = 0
            };
        }

        private static PlanDto MapToDto(Infrastructure.Models.Plan plan) => new PlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            Slug = plan.Slug,
            MaxTeams = plan.MaxTeams,
            MaxPeople = plan.MaxPeople,
            MonthlyPrice = plan.MonthlyPrice,
            AnnualPrice = plan.AnnualPrice,
            PricePerTeamMonthly = plan.PricePerTeamMonthly,
            SavingsPercent = plan.SavingsPercent,
            IsCustom = plan.IsCustom,
            IsPopular = plan.IsPopular,
            Features = plan.Features,
            TrialDays = plan.TrialDays,
            RequiresCreditCard = plan.RequiresCreditCard
        };
    }
}

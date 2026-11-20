using System.Collections.Generic;
using System.Threading.Tasks;
using CloverBackoffice.Components.Enums;
using CloverBackoffice.Infrastructure.Models;
using CloverBackoffice.Repository.Provider;
using CloverBackoffice.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace CloverBackoffice.Services.Implementation
{
    // See ITenantAccessService for the rules. Scoped, so the small per-request
    // cache below lives exactly as long as one request — an action can carry the
    // same company id in several arguments without paying for repeated lookups.
    public class TenantAccessService : ITenantAccessService
    {
        private readonly ApplicationDbContext _context;
        private readonly Dictionary<(int UserId, uint ProfileId), bool> _cache = new();
        private readonly Dictionary<uint, uint?> _unitCompanyCache = new();
        private readonly Dictionary<uint, uint?> _userCompanyCache = new();

        public TenantAccessService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<bool> CanAccessCompanyAsync(User? user, uint profileId)
        {
            if (user == null)
                return false;

            // 0 / unset means "the caller named no company" — the endpoint resolves
            // one from the token itself, so there is nothing to authorize here.
            if (profileId == 0)
                return true;

            var key = (user.UserId, profileId);
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var allowed = await EvaluateAsync(user, profileId);
            _cache[key] = allowed;
            return allowed;
        }

        public async Task<bool> CanAccessUnitAsync(User? user, uint unitId)
        {
            if (user == null)
                return false;

            if (unitId == 0)
                return true;

            if (!_unitCompanyCache.TryGetValue(unitId, out var owningProfileId))
            {
                owningProfileId = await _context.BusinessUnits
                    .AsNoTracking()
                    .Where(u => u.Id == unitId)
                    .Select(u => (uint?)u.Profile.Id)
                    .FirstOrDefaultAsync();
                _unitCompanyCache[unitId] = owningProfileId;
            }

            // A unit id that matches nothing is not a tenant violation — it is a
            // bad request. Let the endpoint answer for it (404 / empty result)
            // rather than reporting "forbidden", which would confirm to a prober
            // which ids exist.
            if (owningProfileId == null)
                return true;

            return await CanAccessCompanyAsync(user, owningProfileId.Value);
        }

        public async Task<bool> CanAccessUserAsync(User? user, uint userId)
        {
            if (user == null)
                return false;

            if (userId == 0)
                return true;

            // Acting on yourself is always allowed, and costs no query. Several
            // endpoints pass the caller's own id back (password change, player id).
            if ((uint)user.UserId == userId)
                return true;

            if (!_userCompanyCache.TryGetValue(userId, out var owningProfileId))
            {
                owningProfileId = await _context.Users
                    .AsNoTracking()
                    .Where(u => u.UserId == userId)
                    .Select(u => (uint?)u.ProfileId)
                    .FirstOrDefaultAsync();
                _userCompanyCache[userId] = owningProfileId;
            }

            // Same reasoning as units: an id that resolves to nothing, or to a user
            // with no profile of their own, is a bad request rather than a tenant
            // violation. Answering 403 here would confirm to a prober which ids
            // exist; let the endpoint return its own not-found instead.
            if (owningProfileId == null || owningProfileId == 0)
                return true;

            return await CanAccessCompanyAsync(user, owningProfileId.Value);
        }

        private async Task<bool> EvaluateAsync(User user, uint profileId)
        {
            // The one role that spans tenants. Every company/unit/user check funnels
            // through here, so this single line is what gives a RRIDA Admin reach
            // across every profile — there is deliberately no second bypass anywhere
            // else to keep in sync.
            if (user.UserGroupId == (int)UserGroups.RridaAdmin)
                return true;

            // A user with no company of their own has nothing to compare against,
            // so they can never reach a company-scoped resource.
            if (!user.ProfileId.HasValue || user.ProfileId.Value <= 0)
                return false;

            var ownProfileId = (uint)user.ProfileId.Value;
            if (ownProfileId == profileId)
                return true;

            // Beyond this point the caller is asking for a company that is not
            // theirs. The only legitimate case is a partner reaching one of its
            // assigned customers, which requires BOTH that the caller's own
            // company is a partner AND that the target is assigned to it.
            var callerIsPartner = await _context.Profiles
                .AsNoTracking()
                .AnyAsync(c => c.Id == ownProfileId && c.IsPartner && !c.IsDel);
            if (!callerIsPartner)
                return false;

            return await _context.Profiles
                .AsNoTracking()
                .AnyAsync(c => c.Id == profileId && c.PartnerId == ownProfileId && !c.IsDel);
        }
    }
}

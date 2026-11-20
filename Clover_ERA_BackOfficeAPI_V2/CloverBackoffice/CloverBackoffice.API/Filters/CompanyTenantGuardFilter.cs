using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using CloverBackoffice.Components.Helpers;
using CloverBackoffice.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace CloverBackoffice.API.Filters
{
    /// <summary>
    /// Enforces multi-tenant isolation on every company-scoped request.
    ///
    /// Authentication alone only proves WHO is calling; it says nothing about
    /// WHICH tenant they may read. Endpoints that take a company id were relying
    /// on the client to send an honest one, so a valid token for company A also
    /// returned company B's dashboards, teams and users by swapping the id.
    ///
    /// This filter closes that by inspecting the bound arguments of every action
    /// and rejecting the request when any company id in them is one the caller
    /// may not touch (see <see cref="ITenantAccessService"/> for the rules).
    ///
    /// It runs AFTER model binding, so it sees query-string values, route values
    /// and body DTOs uniformly as CLR objects — no body re-reading needed.
    ///
    /// Deliberately silent in three cases:
    ///   * the action carries no company id at all — nothing to authorize;
    ///   * no user can be resolved from the token (anonymous endpoints, expired
    ///     tokens) — authentication is the endpoint's own business, and denying
    ///     here would turn "not logged in" into a confusing 403;
    ///   * the id is 0 / null, which every endpoint treats as "use my own".
    /// </summary>
    public sealed class CompanyTenantGuardFilter : IAsyncActionFilter
    {
        // Property lookups are reflection-based; cache the resolved properties
        // per DTO type so this costs one dictionary hit per request, not a scan.
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> TenantProperties = new();

        // The owning-organisation id is now called ProfileId everywhere. "companyid"
        // is still accepted because this match is what makes an endpoint tenant-
        // guarded at all: a parameter or DTO property whose name is not recognised
        // here contributes no id, and the guard then waves the request through. If
        // any straggler is ever reintroduced under the old name, it stays guarded
        // rather than silently unguarded.
        private static readonly string[] ProfileIdNames = { "profileid", "companyid" };

        // Matched EXACTLY, unlike the unit names below. "userid" is the only form
        // the request surface uses for "the user being acted on"; a suffix match
        // would also swallow audit columns such as createdByUserId, which name a
        // user the caller legitimately may not share a profile with.
        private const string UserIdName = "userid";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            if (HasSkipAttribute(context))
            {
                await next();
                return;
            }

            var refs = CollectTenantReferences(context.ActionArguments);
            if (refs.ProfileIds.Count == 0 && refs.UnitIds.Count == 0 && refs.UserIds.Count == 0)
            {
                await next();
                return;
            }

            var services = context.HttpContext.RequestServices;
            var tokenReader = services.GetRequiredService<ITokenReaderService>();

            // Resolved only once a tenant id is actually present, so unrelated
            // endpoints pay nothing for this filter.
            var user = await tokenReader.GetCurrentUserAsync();
            if (user == null)
            {
                await next();
                return;
            }

            var tenantAccess = services.GetRequiredService<ITenantAccessService>();

            foreach (var profileId in refs.ProfileIds)
            {
                if (!await tenantAccess.CanAccessCompanyAsync(user, profileId))
                {
                    context.Result = Denied();
                    return;
                }
            }

            foreach (var unitId in refs.UnitIds)
            {
                if (!await tenantAccess.CanAccessUnitAsync(user, unitId))
                {
                    context.Result = Denied();
                    return;
                }
            }

            foreach (var userId in refs.UserIds)
            {
                if (!await tenantAccess.CanAccessUserAsync(user, userId))
                {
                    context.Result = Denied();
                    return;
                }
            }

            await next();
        }

        private static ObjectResult Denied() =>
            new(ApiResponseHandler.Fail<string>(ResponseMessages.Errors.CrossCompanyAccessDenied))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };

        private static bool HasSkipAttribute(ActionExecutingContext context) =>
            context.ActionDescriptor.EndpointMetadata.Any(m => m is SkipTenantGuardAttribute);

        private sealed class TenantReferences
        {
            public HashSet<uint> ProfileIds { get; } = new();
            public HashSet<uint> UnitIds { get; } = new();
            public HashSet<uint> UserIds { get; } = new();
        }

        // Every tenant-identifying id the request carries, in either shape the API
        // uses: a scalar parameter (query string / route value) or a property on a
        // bound body DTO.
        //
        //   profileId / ProfileId               → company reference
        //   unitId, businessUnitId, and any
        //   other *UnitId (from/to/complimentary) → unit reference
        //
        // Ids of 0 (and nulls) are dropped — they mean "unspecified", and the
        // endpoint resolves the scope from the token instead.
        private static TenantReferences CollectTenantReferences(IDictionary<string, object?> arguments)
        {
            var refs = new TenantReferences();

            foreach (var (name, value) in arguments)
            {
                if (value == null)
                    continue;

                if (IsProfileIdName(name))
                {
                    AddId(refs.ProfileIds, value);
                    continue;
                }

                if (IsUnitIdName(name))
                {
                    AddId(refs.UnitIds, value);
                    continue;
                }

                if (IsUserIdName(name))
                {
                    AddId(refs.UserIds, value);
                    continue;
                }

                // A collection of DTOs (bulk operations) is still tenant surface.
                if (value is IEnumerable enumerable && value is not string)
                {
                    foreach (var item in enumerable)
                        AddFromProperties(refs, item);
                    continue;
                }

                AddFromProperties(refs, value);
            }

            return refs;
        }

        private static void AddFromProperties(TenantReferences refs, object? model)
        {
            if (model == null || IsScalar(model.GetType()))
                return;

            foreach (var property in TenantProperties.GetOrAdd(model.GetType(), FindTenantProperties))
            {
                var raw = property.GetValue(model);
                if (IsProfileIdName(property.Name))
                    AddId(refs.ProfileIds, raw);
                else if (IsUserIdName(property.Name))
                    AddId(refs.UserIds, raw);
                else
                    AddId(refs.UnitIds, raw);
            }
        }

        private static PropertyInfo[] FindTenantProperties(Type type) =>
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead
                            && (IsProfileIdName(p.Name) || IsUnitIdName(p.Name) || IsUserIdName(p.Name))
                            && IsIntegral(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType))
                .ToArray();

        private static bool IsProfileIdName(string name) =>
            ProfileIdNames.Contains(name, StringComparer.OrdinalIgnoreCase);

        // Matches unitId, businessUnitId, fromBusinessUnitId, toBusinessUnitId,
        // complimentaryBusinessUnitId … every id that names a business unit.
        private static bool IsUnitIdName(string name) =>
            name.EndsWith("UnitId", StringComparison.OrdinalIgnoreCase);

        private static bool IsUserIdName(string name) =>
            string.Equals(name, UserIdName, StringComparison.OrdinalIgnoreCase);

        private static void AddId(HashSet<uint> ids, object? raw)
        {
            if (raw == null)
                return;

            var type = raw.GetType();
            if (!IsIntegral(type))
                return;

            // Negative / oversized values can never name a real company; leave them
            // to the endpoint's own validation rather than reporting them as a
            // tenant violation.
            var value = Convert.ToInt64(raw);
            if (value <= 0 || value > uint.MaxValue)
                return;

            ids.Add((uint)value);
        }

        private static bool IsIntegral(Type type) =>
            type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) ||
            type == typeof(short) || type == typeof(ushort);

        private static bool IsScalar(Type type) =>
            type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) ||
            type == typeof(TimeSpan);
    }
}

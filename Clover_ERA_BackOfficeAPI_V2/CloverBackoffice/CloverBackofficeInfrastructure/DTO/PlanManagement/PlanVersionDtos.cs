using System;
using System.Collections.Generic;

namespace CloverBackoffice.Infrastructure.DTO.PlanManagement
{
    public class PlanVersionDto
    {
        public int VersionId { get; set; }
        public int VersionNumber { get; set; }
        public bool IsCurrent { get; set; }
        public string? Reason { get; set; }
        public int CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }

        /// <summary>Live licences on this version (a licence with none counts as version 1).</summary>
        public int LicenceCount { get; set; }

        public PlanTerms Terms { get; set; } = new();
    }

    public class PlanVersionListDto
    {
        public int PlanId { get; set; }
        public int CurrentVersionNumber { get; set; }
        public List<PlanVersionDto> Versions { get; set; } = new();
    }

    /// <summary>POST api/admin/plans/{id}/migrate-licences: move licences to the newest version.</summary>
    public class MigratePlanLicencesDto
    {
        /// <summary>Only licences on this version (null = every older version).</summary>
        public int? FromVersionNumber { get; set; }

        /// <summary>Only this company's licences (null = every company).</summary>
        public uint? ProfileId { get; set; }

        public string? Reason { get; set; }
    }

    public class PlanMigrationResultDto
    {
        public int PlanId { get; set; }
        public int ToVersionNumber { get; set; }
        public int LicencesMigrated { get; set; }
        public int SubscriptionsMigrated { get; set; }
    }
}

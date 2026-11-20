using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.DTO
{
    public class UpsertBusinessUnitDto
    {
        public int Id { get; set; }
        public string TeamName { get; set; }
        //public string EmailAddress { get; set; }
        public int ProfileId { get; set; }
        public int? ParentId { get; set; }

        /// <summary>
        /// Number of people in the team. Required when creating (Id == 0); optional when
        /// editing, where a null leaves the stored value unchanged.
        /// </summary>
        public int? TeamSize { get; set; }
        [JsonIgnore]
        public int CreatedBy { get; set; }
        [JsonIgnore]
        public string? message { get; set; }

        /// <summary>
        /// Set by Add User only. That flow already reserved a free licence under the
        /// per-profile advisory lock and assigns it to the team itself, so the create
        /// below must not reserve a SECOND licence for the same team. Every other
        /// caller leaves this false and the create reserves + assigns the licence — a
        /// team never exists without one.
        /// </summary>
        [JsonIgnore]
        public bool LicenseAlreadyReserved { get; set; }
    }
}

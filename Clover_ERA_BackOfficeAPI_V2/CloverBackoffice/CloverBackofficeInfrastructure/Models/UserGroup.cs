using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class UserGroup
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsAdmin { get; set; }
        public int UnitId { get; set; }
        public int? OrganizationId { get; set; }
        public string? CreatedBy { get; set; }
        public string? Description { get; set; }

        [JsonIgnore]
        public ICollection<User> Users { get; set; }
        public ICollection<AssignUserRight> AssignUserRights { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.BusinessModel
{
    public class LookupItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public class Industry
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class OpportunityType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class CurrentRelationshipStatus
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class NumberOfEmployees
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

}

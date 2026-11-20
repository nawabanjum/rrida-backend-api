using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Infrastructure.Models
{
    public class ResetPasswordLinks
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string ResetLink { get; set; }
        public bool IsLinkUsed { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime CreatedBy { get; set; }
        public DateTime ExpiryDateTime { get; set; }
        public bool IsDel { get; set; }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Interfaces
{
    public interface IRateLimiterService
    {
        Task ThrottleAsync(int tokensUsed);
    }
}

using CloverBackoffice.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CloverBackoffice.Services.Implementation
{
    public class RateLimiterService : IRateLimiterService
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly int _rpm;
        private readonly int _tpm;
        private readonly TimeSpan _window;
        private readonly Queue<DateTime> _requestLog = new();
        private readonly Queue<(DateTime, int)> _tokenLog = new();

        public RateLimiterService(int rpm = 50, int tpm = 50000)
        {
            _rpm = rpm;
            _tpm = tpm;
            _window = TimeSpan.FromMinutes(1);
        }

        public async Task ThrottleAsync(int tokensUsed)
        {
            await _semaphore.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;

                // clean old requests
                while (_requestLog.Count > 0 && now - _requestLog.Peek() > _window)
                    _requestLog.Dequeue();

                // clean old token usage
                while (_tokenLog.Count > 0 && now - _tokenLog.Peek().Item1 > _window)
                    _tokenLog.Dequeue();

                // calculate totals
                var tokenSum = _tokenLog.Sum(t => t.Item2);

                // check if we exceed limits
                if ((_requestLog.Count >= _rpm) || (tokenSum + tokensUsed > _tpm))
                {
                    if (_requestLog.Count > 0)
                    {
                        var waitTime = _window - (now - _requestLog.Peek());
                        if (waitTime > TimeSpan.Zero)
                        {
                            await Task.Delay(waitTime);
                        }
                    }
                    else
                    {
                        // fallback: if no requests logged but we exceeded token limit
                        await Task.Delay(1000); // backoff 1 second
                    }
                }

                // log request + tokens
                _requestLog.Enqueue(DateTime.UtcNow);
                _tokenLog.Enqueue((DateTime.UtcNow, tokensUsed));
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }

}

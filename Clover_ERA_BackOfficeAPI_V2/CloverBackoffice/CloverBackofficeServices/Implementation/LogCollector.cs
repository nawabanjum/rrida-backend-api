using CloverBackoffice.Services.Interfaces;
using System;
using System.Text;

namespace CloverBackoffice.Services.Implementation
{
    public class LogCollector : ILogCollector
    {
        private readonly StringBuilder _sb = new();

        public bool HasEntries => _sb.Length > 0;

        public void Add(string level, string message)
        {
            _sb.AppendLine($"[{DateTime.UtcNow:HH:mm:ss.fff} {level}] {message}");
        }

        public string GetAll() => _sb.ToString();
    }
}

namespace CloverBackoffice.Services.Interfaces
{
    public interface ILogCollector
    {
        void Add(string level, string message);
        string GetAll();
        bool HasEntries { get; }
    }
}

namespace ZeroBuffer.Tests.Services
{
    public interface IBufferNamingService
    {
        string GetUniqueBufferName(string baseName);
    }
}
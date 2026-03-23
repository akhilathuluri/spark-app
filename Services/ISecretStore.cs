namespace Spark.Services;

/// <summary>
/// Abstraction for storing sensitive values outside regular settings payloads.
/// </summary>
public interface ISecretStore
{
    string? Get(string key);
    void Set(string key, string value);
    void Remove(string key);
}

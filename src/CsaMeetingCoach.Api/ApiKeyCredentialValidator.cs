using System.Security.Cryptography;
using System.Text;

namespace CsaMeetingCoach.Api;

internal static class ApiKeyCredentialValidator
{
    public static bool IsValid(
        IHeaderDictionary headers,
        string headerName,
        string expectedCredential)
    {
        if (!headers.TryGetValue(headerName, out var suppliedValues)
            || suppliedValues.Count != 1
            || string.IsNullOrWhiteSpace(suppliedValues[0]))
        {
            return false;
        }

        var suppliedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(suppliedValues[0]!));
        var expectedHash = SHA256.HashData(
            Encoding.UTF8.GetBytes(expectedCredential));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedHash);
    }
}

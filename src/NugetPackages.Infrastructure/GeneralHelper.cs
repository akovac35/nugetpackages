using System.Security.Cryptography;
using System.Text;

namespace NugetPackages.Infrastructure
{
    public static class GeneralHelper
    {
        public static string ComputeSha256(byte[] data)
        {
            byte[] hash = SHA256.HashData(data);
            return BitConverter.ToString(hash).Replace("-", "");
        }

        public static async Task<string> ComputeSha256ForFile(FileInfo file, CancellationToken cancellationToken = default)
        {
            var bytes = await File.ReadAllBytesAsync(file.FullName, cancellationToken);
            return ComputeSha256(bytes);
        }

        public static string ComputeSha256(string asciiData)
        {
            byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes(asciiData));
            return BitConverter.ToString(hash).Replace("-", "");
        }
    }
}

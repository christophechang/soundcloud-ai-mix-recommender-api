using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Changsta.Ai.Infrastructure.Tests.Helpers
{
    internal static class TestDataFile
    {
        public static async Task<string> ReadAllTextAsync(string relativePath)
        {
            // NUnit exposes the test run directory here, reliable on CI and locally
            string root = TestContext.CurrentContext.TestDirectory;
            string fullPath = Path.Combine(root, relativePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Test data file not found: {fullPath}");
            }

            return await File.ReadAllTextAsync(fullPath);
        }
    }
}
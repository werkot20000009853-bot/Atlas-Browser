using System;
using System.IO;
using Atlas;

namespace Atlas.Tests;

public class VersionHelperTests
{
    [Theory]
    [InlineData("1.0.1", "1.0.0", true)]
    [InlineData("1.1.0", "1.0.9", true)]
    [InlineData("2.0.0", "1.99.99", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.1", false)]
    [InlineData("v1.2.0", "1.1.0", true)]
    public void IsNewer_ComparesVersionsCorrectly(string latest, string current, bool expected)
    {
        Assert.Equal(expected, VersionHelper.IsNewer(latest, current));
    }
}

public class UrlHelperTests
{
    [Theory]
    [InlineData("google", "тест", "https://www.google.com/search?q=%D1%82%D0%B5%D1%81%D1%82")]
    [InlineData("duckduckgo", "test", "https://duckduckgo.com/?q=test")]
    [InlineData("bing", "test", "https://www.bing.com/search?q=test")]
    [InlineData("yandex", "test", "https://yandex.ru/search/?text=test")]
    [InlineData("", "test", "https://www.google.com/search?q=test")]
    public void BuildSearchUrl_UsesSelectedEngine(string engine, string query, string expected)
    {
        Assert.Equal(expected, UrlHelper.BuildSearchUrl(engine, query));
    }
}

public class CryptoServiceTests
{
    [Fact]
    public void ProtectUnprotect_RoundTrips()
    {
        string path = Path.Combine(Path.GetTempPath(), "atlas_crypto_test_" + Guid.NewGuid().ToString("N") + ".dat");
        try
        {
            const string json = "{\"key\":\"value\"}";
            CryptoService.WriteProtected(path, json);
            Assert.Equal(json, CryptoService.ReadProtected(path));
            Assert.DoesNotContain("value", File.ReadAllText(path)); // зашифровано
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ReadProtected_MissingFile_ReturnsNull()
    {
        string path = Path.Combine(Path.GetTempPath(), "atlas_missing_" + Guid.NewGuid().ToString("N") + ".dat");
        Assert.Null(CryptoService.ReadProtected(path));
    }
}

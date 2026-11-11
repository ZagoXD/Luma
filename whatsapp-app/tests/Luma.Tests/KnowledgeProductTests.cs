using System.Text.Json;
using System.Text.RegularExpressions;
using Luma.Api.Services.Assistant;

namespace Luma.Tests;

public sealed class KnowledgeProductTests
{
    [Fact]
    public void Product_prices_match_the_frontend_catalog_and_have_no_public_internal_links()
    {
        var corpus = JsonSerializer.Deserialize<KnowledgeDocument[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Knowledge/corpus.json")), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "web/lib/luma-api.ts"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var frontend = File.ReadAllText(Path.Combine(directory!.FullName, "web/lib/luma-api.ts"));
        var prices = Regex.Matches(frontend, "(?:monthlyPrice|annualPrice): \\\"([^\\\"]+)\\\"");
        Assert.Equal(4, prices.Count);
        Assert.All(prices.Cast<Match>(), price => Assert.Contains(corpus, document => document.Content.Contains(price.Groups[1].Value, StringComparison.Ordinal)));
        Assert.All(corpus.Where(document => document.SourceId == "luma-produto"), document => Assert.Equal("assistant", document.Visibility));
        Assert.DoesNotContain(corpus, document => document.Visibility == "restricted" || document.Content.Contains('\uFFFD'));
    }
}

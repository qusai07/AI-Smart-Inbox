using System.Text.Json;
using Anthropic.Models.Beta.Messages;
using SmartInbox.Application;
using SmartInbox.Infrastructure.Ai;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

public class AiContractTests
{
    [Fact]
    public void OutputSchema_RequiresEveryProperty()
    {
        using var schema = JsonDocument.Parse(AnalysisPrompt.OutputSchema);
        var properties = schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToHashSet();
        var required = schema.RootElement.GetProperty("required").EnumerateArray().Select(e => e.GetString()!).ToHashSet();

        Assert.Equal(properties, required);
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void SystemPrompt_ListsAllDepartments_AndIsStable()
    {
        var first = AnalysisPrompt.BuildSystemPrompt(DepartmentSeed.All);
        var second = AnalysisPrompt.BuildSystemPrompt(DepartmentSeed.All.Reverse().ToArray());

        // Same bytes regardless of input order, so prompt caching keeps hitting.
        Assert.Equal(first, second);
        foreach (var d in DepartmentSeed.All)
        {
            Assert.Contains(d.NameEn, first);
            Assert.Contains(d.NameAr, first);
        }
    }

    [Fact]
    public void Fallbacks_SerializeAsDefault()
    {
        BetaFallbacksParam fallbacks = new Default();
        Assert.Equal("\"default\"", JsonSerializer.Serialize(fallbacks));
    }

    [Theory]
    [InlineData("letter.PDF", "application/pdf")]
    [InlineData("scan.jpeg", "image/jpeg")]
    [InlineData("mail.eml", "message/rfc822")]
    [InlineData("virus.exe", null)]
    [InlineData("noextension", null)]
    public void ContentType_ComesFromExtension(string fileName, string? expected) =>
        Assert.Equal(expected, UploadRules.ResolveContentType(fileName));
}

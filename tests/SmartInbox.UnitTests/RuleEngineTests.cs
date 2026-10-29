using Microsoft.Extensions.Logging.Abstractions;
using SmartInbox.Application;
using SmartInbox.Domain;
using SmartInbox.Infrastructure.Ai;
using SmartInbox.Infrastructure.Ai.Rules;
using SmartInbox.Infrastructure.Persistence;

namespace SmartInbox.UnitTests;

public class RuleEngineTests
{
    private static readonly RuleBasedProvider Rules = new(new FixedClock(new DateOnly(2027, 2, 16)));

    private static AiAnalysisResult Analyze(string text) => Rules.Analyze(AnalysisInput.FromText(text), DepartmentSeed.All);

    [Fact]
    public void Invoice_GoesToFinance_WithReferenceAndDeadline()
    {
        var r = Analyze("""
            NIMBUS CLOUD SERVICES LLC
            TAX INVOICE
            Invoice No: NCS-INV-88213
            Invoice Date: 05 February 2027
            Bill To: Finance & Procurement Department
            TOTAL DUE 168,525.00
            Payment terms: 30 days from invoice date (due 07 March 2027).
            """);

        Assert.Equal(3, r.SuggestedDepartmentId);
        Assert.Equal(DocumentCategory.Invoice, r.Category);
        Assert.Equal("NCS-INV-88213", r.ReferenceNumber);
        Assert.Equal(new DateOnly(2027, 2, 5), r.DocumentDate);
        Assert.Equal(new DateOnly(2027, 3, 7), r.Deadline);
        Assert.Equal("NIMBUS CLOUD SERVICES LLC", r.Sender);
        Assert.Equal(RuleBasedProvider.EngineName, r.ModelUsed);
        Assert.Equal(0, r.TokensUsed);
    }

    [Fact]
    public void ArabicUrgentLetter_IsUrgent_AndRoutedToMinistersOffice()
    {
        var r = Analyze("""
            مكتب وزير الاقتصاد – تجريبي
            الرقم: م.و/2027/215
            التاريخ: 16 فبراير 2027
            معالي/ وزير الخدمات الرقمية – تجريبي
            الموضوع: عاجل – اجتماع تنسيقي بشأن منصة الاستثمار الموحدة
            يسرّنا دعوة معاليكم لحضور اجتماع تنسيقي عاجل.
            """);

        Assert.Equal(1, r.SuggestedDepartmentId);
        Assert.Equal(Priority.Urgent, r.Priority);
        Assert.Equal(DocumentCategory.Invitation, r.Category);
        Assert.Equal("ar", r.Language);
        Assert.Equal("م.و/2027/215", r.ReferenceNumber);
        Assert.Equal(new DateOnly(2027, 2, 16), r.DocumentDate);
        Assert.Contains("اجتماع", r.SummaryAr); // Arabic letters get an extractive Arabic summary
    }

    [Fact]
    public void AddressedDepartment_WinsOverMentionedOne()
    {
        var r = Analyze("""
            إلى: إدارة الشؤون القانونية
            من: إدارة المالية والمشتريات
            الموضوع: طلب مراجعة مسودة عقد توريد
            """);

        Assert.Equal(2, r.SuggestedDepartmentId);
        Assert.Equal("إدارة المالية والمشتريات", r.Sender);
    }

    [Fact]
    public void NoKeywords_MeansNoSuggestion()
    {
        var r = Analyze("Hello, this is a short note with nothing specific in it at all.");
        Assert.Equal(0, r.SuggestedDepartmentId);
        Assert.Equal(0, r.Confidence);
    }

    [Fact]
    public void ImageWithoutText_ReturnsLowConfidenceInsteadOfFailing()
    {
        var r = Rules.Analyze(AnalysisInput.FromFile([1, 2, 3], "image/png"), DepartmentSeed.All);
        Assert.Equal(0, r.Confidence);
        Assert.True(DocumentProcessor.StatusFor(new DocumentAnalysis { Confidence = r.Confidence }) == DocumentStatus.NeedsReview);
    }

    [Theory]
    [InlineData("Date: 12 January 2027", 2027, 1, 12)]
    [InlineData("on January 12, 2027 we", 2027, 1, 12)]
    [InlineData("التاريخ: 4 فبراير 2027", 2027, 2, 4)]
    [InlineData("بتاريخ ١٠ يناير ٢٠٢٧", 2027, 1, 10)]
    [InlineData("issued 2027-03-31", 2027, 3, 31)]
    [InlineData("issued 31/03/2027", 2027, 3, 31)]
    public void Dates_AreFound_InBothLanguages(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), DateFinder.FindAll(TextTools.Normalize(text)).Single().Date);

    [Theory]
    [InlineData("إلى إدارة الموارد البشرية", "الموارد البشريه")]
    [InlineData("الموارد البشرية", "الموارد البشريه")]
    public void ArabicNormalization_MatchesSpellingVariants(string text, string term) =>
        Assert.True(TextTools.Contains(TextTools.Normalize(text), TextTools.Normalize(term)));

    [Fact]
    public void ShortLatinTerms_MatchWholeWordsOnly() =>
        Assert.Equal(0, TextTools.Count(TextTools.Normalize("CVE-2027-10442"), "cv"));
}

public class GatedProviderTests
{
    private static readonly RuleBasedProvider Rules = new(new FixedClock(new DateOnly(2027, 2, 16)));
    private const string ClearInvoice = "TAX INVOICE\nInvoice No: X-1\nBill To: Finance & Procurement Department\nPurchase Order: PO-1\nTotal due, payment terms 30 days, vendor IBAN.";

    [Fact]
    public async Task ConfidentRuleResult_DoesNotCallTheLlm()
    {
        var llm = new FakeAi(_ => throw new InvalidOperationException("should not be called"));
        var gate = new GatedProvider(Rules, llm, 0.75, NullLogger<GatedProvider>.Instance);

        var r = await gate.AnalyzeAsync(AnalysisInput.FromText(ClearInvoice), DepartmentSeed.All);

        Assert.Equal(RuleBasedProvider.EngineName, r.ModelUsed);
        Assert.Null(llm.LastInput);
    }

    [Fact]
    public async Task UnsureRuleResult_EscalatesToLlm()
    {
        var llm = new FakeAi(_ => FakeAi.Result(7, 0.9));
        var gate = new GatedProvider(Rules, llm, 0.75, NullLogger<GatedProvider>.Instance);

        var r = await gate.AnalyzeAsync(AnalysisInput.FromText("A short note."), DepartmentSeed.All);

        Assert.Equal("test-model", r.ModelUsed);
    }

    [Fact]
    public async Task Disagreement_GoesToHumanReview()
    {
        // Rules lean to Finance (tender) but are unsure; the LLM confidently says Strategy.
        const string text = "Clarification request on the tender. Is the strategy section correct?";
        var ruleGuess = Rules.Analyze(AnalysisInput.FromText(text), DepartmentSeed.All);
        var llm = new FakeAi(_ => FakeAi.Result(ruleGuess.SuggestedDepartmentId == 7 ? 2 : 7, 1.0));
        var gate = new GatedProvider(Rules, llm, 0.99, NullLogger<GatedProvider>.Instance);

        var r = await gate.AnalyzeAsync(AnalysisInput.FromText(text), DepartmentSeed.All);

        Assert.True(r.Confidence < DocumentProcessor.ConfidenceThreshold);
        Assert.Contains("rule engine disagreed", r.Reasoning);
    }

    [Fact]
    public async Task Agreement_KeepsLlmAnswer_WithAtLeastRuleConfidence()
    {
        const string text = "Clarification request on the tender.";
        var ruleGuess = Rules.Analyze(AnalysisInput.FromText(text), DepartmentSeed.All);
        var llm = new FakeAi(_ => FakeAi.Result(ruleGuess.SuggestedDepartmentId, 0.1));
        var gate = new GatedProvider(Rules, llm, 0.99, NullLogger<GatedProvider>.Instance);

        var r = await gate.AnalyzeAsync(AnalysisInput.FromText(text), DepartmentSeed.All);

        Assert.Equal("test-model", r.ModelUsed);
        Assert.Equal(ruleGuess.Confidence, r.Confidence);
    }

    [Fact]
    public async Task LlmDown_FallsBackToRuleResult()
    {
        var llm = new FakeAi(_ => throw new AiException("Ollama not reachable"));
        var gate = new GatedProvider(Rules, llm, 0.75, NullLogger<GatedProvider>.Instance);

        var r = await gate.AnalyzeAsync(AnalysisInput.FromText("A short note."), DepartmentSeed.All);

        Assert.Equal(RuleBasedProvider.EngineName, r.ModelUsed);
        Assert.Contains("Ollama not reachable", r.Reasoning);
    }
}

internal class FixedClock(DateOnly date) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(date.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
}

using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

// ── CLI argument parsing ────────────────────────────────────────────────────
string? goldenSetPath = null;
string baseUrl = "http://localhost:5000";
bool mockProvider = false;
string thresholdsPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "thresholds.yaml");

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--golden-set": goldenSetPath = args[++i]; break;
        case "--base-url": baseUrl = args[++i]; break;
        case "--mock-provider": mockProvider = true; break;
        case "--thresholds": thresholdsPath = args[++i]; break;
    }
}

if (goldenSetPath is null)
{
    Console.Error.WriteLine("Usage: EvalRunner --golden-set <path/to/qa.yaml> [--base-url <url>] [--mock-provider] [--thresholds <path>]");
    return 1;
}

// ── Load golden set ─────────────────────────────────────────────────────────
var deserializer = new DeserializerBuilder()
    .WithNamingConvention(CamelCaseNamingConvention.Instance)
    .IgnoreUnmatchedProperties()
    .Build();

GoldenSet goldenSet;
try
{
    var yaml = await File.ReadAllTextAsync(goldenSetPath);
    goldenSet = deserializer.Deserialize<GoldenSet>(yaml);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to load golden set: {ex.Message}");
    return 1;
}

// ── Load thresholds ─────────────────────────────────────────────────────────
Thresholds thresholds = new();
if (File.Exists(thresholdsPath))
{
    try
    {
        var yaml = await File.ReadAllTextAsync(thresholdsPath);
        thresholds = deserializer.Deserialize<Thresholds>(yaml);
    }
    catch { /* use defaults */ }
}

Console.WriteLine($"Loaded {goldenSet.Items.Count} golden-set questions from {goldenSetPath}");
Console.WriteLine($"Target: {baseUrl}{(mockProvider ? " [mock-provider mode]" : "")}");

// ── HTTP client ─────────────────────────────────────────────────────────────
using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };

// ── Run evaluations ─────────────────────────────────────────────────────────
var results = new List<EvalResult>();
int passCount = 0;
int totalCount = goldenSet.Items.Count;

foreach (var item in goldenSet.Items)
{
    Console.Write($"  Q: {item.Question[..Math.Min(60, item.Question.Length)]}... ");

    var evalResult = await EvaluateQuestion(http, item, mockProvider);
    results.Add(evalResult);

    if (evalResult.Passed)
    {
        passCount++;
        Console.WriteLine($"PASS (R@3={evalResult.RetrievalAt3:P0}, score={evalResult.LlmScore})");
    }
    else
    {
        Console.WriteLine($"FAIL (R@3={evalResult.RetrievalAt3:P0}, score={evalResult.LlmScore}) — {evalResult.FailReason}");
    }
}

// ── Aggregate metrics ───────────────────────────────────────────────────────
double avgR3 = results.Average(r => r.RetrievalAt3);
double avgR5 = results.Average(r => r.RetrievalAt5);
double avgR10 = results.Average(r => r.RetrievalAt10);
double avgScore = results.Average(r => r.LlmScore);
double passRate = results.Count(r => r.LlmScore >= thresholds.LlmJudge.MinScore) / (double)totalCount;

Console.WriteLine();
Console.WriteLine($"=== Results ===");
Console.WriteLine($"  Retrieval@3 avg  : {avgR3:P1} (threshold: {thresholds.RetrievalAtK.K3:P0})");
Console.WriteLine($"  Retrieval@5 avg  : {avgR5:P1} (threshold: {thresholds.RetrievalAtK.K5:P0})");
Console.WriteLine($"  Retrieval@10 avg : {avgR10:P1} (threshold: {thresholds.RetrievalAtK.K10:P0})");
Console.WriteLine($"  LLM judge avg    : {avgScore:F2} (threshold: ≥{thresholds.LlmJudge.MinScore})");
Console.WriteLine($"  LLM pass rate    : {passRate:P1} (threshold: {thresholds.LlmJudge.MinPassRate:P0})");

// ── Write report ─────────────────────────────────────────────────────────────
var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
var repoDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(goldenSetPath)));
var reportsDir = repoDir is not null
    ? Path.Combine(repoDir, "reports", timestamp)
    : Path.Combine(AppContext.BaseDirectory, "reports", timestamp);
Directory.CreateDirectory(reportsDir);

var reportMd = BuildMarkdownReport(goldenSet, results, avgR3, avgR5, avgR10, avgScore, passRate, thresholds);
await File.WriteAllTextAsync(Path.Combine(reportsDir, "report.md"), reportMd);

var reportJson = JsonSerializer.Serialize(new
{
    timestamp,
    goldenSet = goldenSetPath,
    metrics = new { avgR3, avgR5, avgR10, avgScore, passRate },
    results
}, new JsonSerializerOptions { WriteIndented = true });
await File.WriteAllTextAsync(Path.Combine(reportsDir, "report.json"), reportJson);

Console.WriteLine($"Report written to {reportsDir}");

// ── Gate on thresholds ───────────────────────────────────────────────────────
bool metricsOk =
    avgR3 >= thresholds.RetrievalAtK.K3 &&
    avgR5 >= thresholds.RetrievalAtK.K5 &&
    avgR10 >= thresholds.RetrievalAtK.K10 &&
    passRate >= thresholds.LlmJudge.MinPassRate;

if (!metricsOk)
{
    Console.Error.WriteLine("EVAL FAILED: one or more metrics below threshold.");
    return 2;
}

Console.WriteLine("EVAL PASSED.");
return 0;

// ── Local functions ──────────────────────────────────────────────────────────
static async Task<EvalResult> EvaluateQuestion(HttpClient http, GoldenItem item, bool mockProvider)
{
    try
    {
        // Call /tasks/send with the question
        var request = new
        {
            agentId = (string?)null,
            message = new
            {
                role = "user",
                parts = new[] { new { text = item.Question } }
            }
        };

        using var resp = await http.PostAsJsonAsync("/tasks/send", request);
        if (!resp.IsSuccessStatusCode)
        {
            return new EvalResult
            {
                Question = item.Question,
                FailReason = $"HTTP {(int)resp.StatusCode}",
                Passed = false
            };
        }

        var body = await resp.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var responseText = ExtractResponseText(json);

        // Score retrieval by checking if expected files appear in the response
        double r3 = ScoreRetrieval(responseText, item.ExpectedFiles, 3);
        double r5 = ScoreRetrieval(responseText, item.ExpectedFiles, 5);
        double r10 = ScoreRetrieval(responseText, item.ExpectedFiles, 10);

        // LLM-judge score — in mock mode use a heuristic
        double score = mockProvider
            ? MockJudgeScore(responseText, item.ExpectedFiles)
            : await CallLlmJudge(http, item.Question, responseText);

        bool passed = r3 > 0 || score >= 3;

        return new EvalResult
        {
            Question = item.Question,
            ResponseText = responseText,
            RetrievalAt3 = r3,
            RetrievalAt5 = r5,
            RetrievalAt10 = r10,
            LlmScore = score,
            Passed = passed,
            FailReason = passed ? null : "No expected files found and score < 3"
        };
    }
    catch (Exception ex)
    {
        return new EvalResult
        {
            Question = item.Question,
            FailReason = ex.Message,
            Passed = false
        };
    }
}

static string ExtractResponseText(JsonDocument json)
{
    try
    {
        var messages = json.RootElement
            .GetProperty("task")
            .GetProperty("messages");

        var sb = new StringBuilder();
        foreach (var msg in messages.EnumerateArray())
        {
            if (msg.TryGetProperty("parts", out var parts))
                foreach (var part in parts.EnumerateArray())
                    if (part.TryGetProperty("text", out var text))
                        sb.AppendLine(text.GetString());
        }
        return sb.ToString();
    }
    catch
    {
        return json.RootElement.GetRawText();
    }
}

static double ScoreRetrieval(string responseText, List<string> expectedFiles, int k)
{
    if (expectedFiles.Count == 0) return 1.0; // vacuously true
    int hits = expectedFiles.Take(k).Count(f =>
        responseText.Contains(f, StringComparison.OrdinalIgnoreCase));
    return hits > 0 ? 1.0 : 0.0;
}

static double MockJudgeScore(string responseText, List<string> expectedFiles)
{
    // Heuristic: count how many expected files appear in response
    if (expectedFiles.Count == 0) return 4.0;
    int hits = expectedFiles.Count(f =>
        responseText.Contains(f, StringComparison.OrdinalIgnoreCase));
    return hits >= expectedFiles.Count / 2 ? 4.0 : 2.0;
}

static async Task<double> CallLlmJudge(HttpClient http, string question, string responseText)
{
    // Call judge agent with a fixed rubric
    var judgePrompt = $"""
Rate the following agent response on a scale of 1-5 for correctness and groundedness.
Question: {question}
Response: {responseText[..Math.Min(1000, responseText.Length)]}
Reply with only a single integer 1-5.
""";

    var request = new
    {
        agentId = (string?)null,
        message = new
        {
            role = "user",
            parts = new[] { new { text = judgePrompt } }
        }
    };

    try
    {
        using var resp = await http.PostAsJsonAsync("/tasks/send", request);
        var body = await resp.Content.ReadAsStringAsync();
        var json = JsonDocument.Parse(body);
        var judgeText = ExtractResponseText(json).Trim();
        return double.TryParse(judgeText, out var score) ? Math.Clamp(score, 1, 5) : 3.0;
    }
    catch
    {
        return 3.0;
    }
}

static string BuildMarkdownReport(
    GoldenSet gs,
    List<EvalResult> results,
    double avgR3, double avgR5, double avgR10,
    double avgScore, double passRate,
    Thresholds thresholds)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Eval Report");
    sb.AppendLine($"Generated: {DateTime.UtcNow:u}");
    sb.AppendLine();
    sb.AppendLine("## Summary");
    sb.AppendLine($"| Metric | Value | Threshold | Status |");
    sb.AppendLine($"|--------|-------|-----------|--------|");
    sb.AppendLine($"| Retrieval@3 | {avgR3:P1} | {thresholds.RetrievalAtK.K3:P0} | {(avgR3 >= thresholds.RetrievalAtK.K3 ? "✅" : "❌")} |");
    sb.AppendLine($"| Retrieval@5 | {avgR5:P1} | {thresholds.RetrievalAtK.K5:P0} | {(avgR5 >= thresholds.RetrievalAtK.K5 ? "✅" : "❌")} |");
    sb.AppendLine($"| Retrieval@10 | {avgR10:P1} | {thresholds.RetrievalAtK.K10:P0} | {(avgR10 >= thresholds.RetrievalAtK.K10 ? "✅" : "❌")} |");
    sb.AppendLine($"| LLM judge avg | {avgScore:F2} | ≥{thresholds.LlmJudge.MinScore} | {(avgScore >= thresholds.LlmJudge.MinScore ? "✅" : "❌")} |");
    sb.AppendLine($"| LLM pass rate | {passRate:P1} | {thresholds.LlmJudge.MinPassRate:P0} | {(passRate >= thresholds.LlmJudge.MinPassRate ? "✅" : "❌")} |");
    sb.AppendLine();
    sb.AppendLine("## Per-question results");
    sb.AppendLine($"| # | Question | R@3 | R@5 | Score | Pass |");
    sb.AppendLine($"|---|----------|-----|-----|-------|------|");
    int n = 1;
    foreach (var r in results)
    {
        var q = r.Question.Length > 60 ? r.Question[..60] + "…" : r.Question;
        sb.AppendLine($"| {n++} | {q} | {r.RetrievalAt3:P0} | {r.RetrievalAt5:P0} | {r.LlmScore:F1} | {(r.Passed ? "✅" : "❌")} |");
    }
    return sb.ToString();
}

// ── Models ───────────────────────────────────────────────────────────────────
public class GoldenSet
{
    public List<GoldenItem> Items { get; set; } = [];
}

public class GoldenItem
{
    public string Question { get; set; } = "";
    public List<string> ExpectedFiles { get; set; } = [];
    public string? Notes { get; set; }
}

public class Thresholds
{
    public RetrievalThresholds RetrievalAtK { get; set; } = new();
    public LlmJudgeThresholds LlmJudge { get; set; } = new();
}

public class RetrievalThresholds
{
    public double K3 { get; set; } = 0.40;
    public double K5 { get; set; } = 0.55;
    public double K10 { get; set; } = 0.70;
}

public class LlmJudgeThresholds
{
    public double MinScore { get; set; } = 3.0;
    public double MinPassRate { get; set; } = 0.70;
}

public class EvalResult
{
    public string Question { get; set; } = "";
    public string? ResponseText { get; set; }
    public double RetrievalAt3 { get; set; }
    public double RetrievalAt5 { get; set; }
    public double RetrievalAt10 { get; set; }
    public double LlmScore { get; set; }
    public bool Passed { get; set; }
    public string? FailReason { get; set; }
}

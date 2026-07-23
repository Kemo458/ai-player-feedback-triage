namespace PlayerFeedback.Core.Analysis;

public class LlmOptions
{
    public string Provider { get; set; } = "Ollama"; // Ollama | Mock
    public string BaseUrl { get; set; } = "http://ollama:11434";
    public string Model { get; set; } = "qwen2.5:3b";
    public int ReviewContextTokens { get; set; } = 4096;
    public int SummaryContextTokens { get; set; } = 8192;
    public int TimeoutSeconds { get; set; } = 90;
    public string PromptVersion { get; set; } = "feedback-analysis-v1";
}

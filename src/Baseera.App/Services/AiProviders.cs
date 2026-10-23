namespace Baseera.App.Services;

internal sealed record AiProvider(string Id, string Name, string BaseUrl, string Protocol = "chat", string? ModelsUrl = null)
{
    public override string ToString() => Name;
}

internal static class AiProviders
{
    // Requests go directly to the selected service, using only its own key.
    internal static readonly IReadOnlyList<AiProvider> All = Array.AsReadOnly<AiProvider>([
        new("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/", "gemini"),
        new("openai", "OpenAI (ChatGPT models)", "https://api.openai.com/v1/", "responses"),
        new("anthropic", "Anthropic Claude", "https://api.anthropic.com/v1/", "anthropic"),
        new("xai", "xAI Grok", "https://api.x.ai/v1/", "responses", "https://api.x.ai/v1/language-models"),
        new("mistral", "Mistral AI", "https://api.mistral.ai/v1/"),
        new("cohere", "Cohere", "https://api.cohere.com/v2/", "cohere", "https://api.cohere.com/v1/models?page_size=1000"),
        new("qwen", "Alibaba Qwen (International)", "https://dashscope-intl.aliyuncs.com/compatible-mode/v1/"),
        new("zai", "Z.ai GLM", "https://api.z.ai/api/paas/v4/"),
        new("moonshot", "Moonshot Kimi", "https://api.moonshot.ai/v1/"),
        new("stepfun", "StepFun", "https://api.stepfun.com/v1/"),
        new("together", "Together AI", "https://api.together.xyz/v1/"),
        new("fireworks", "Fireworks AI", "https://api.fireworks.ai/inference/v1/"),
        new("groq", "Groq", "https://api.groq.com/openai/v1/"),
        new("nvidia", "NVIDIA NIM", "https://integrate.api.nvidia.com/v1/"),
        new("sambanova", "SambaNova", "https://api.sambanova.ai/v1/"),
        new("nebius", "Nebius Token Factory", "https://api.tokenfactory.nebius.com/v1/"),
        new("deepinfra", "DeepInfra", "https://api.deepinfra.com/v1/openai/"),
        new("siliconflow", "SiliconFlow (International)", "https://api.siliconflow.com/v1/"),
        new("novita", "Novita AI", "https://api.novita.ai/v3/openai/"),
        new("hyperbolic", "Hyperbolic", "https://api.hyperbolic.ai/inference/v1/")
    ]);
    internal static AiProvider Get(string id) => All.FirstOrDefault(p => p.Id == id)
        ?? throw new ArgumentException("Unknown AI service.", nameof(id));
    internal static bool Contains(string id) => All.Any(p => p.Id == id);
}

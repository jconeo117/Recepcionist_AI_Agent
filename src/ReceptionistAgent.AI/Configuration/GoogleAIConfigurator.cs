using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Google;

namespace ReceptionistAgent.AI.Configuration;

/// <summary>
/// Configurador para Google AI (Gemini).
/// Lee ModelId y ApiKey desde la sección AI:Google del appsettings.
/// </summary>
public class GoogleAIConfigurator : IAIProviderConfigurator
{
    public string ProviderName => "Google";

    private int _maxTokens = 1500;

    public void ConfigureKernel(IKernelBuilder builder, IConfiguration configuration)
    {
        var modelId = configuration["AI:Google:ModelId"]
            ?? throw new InvalidOperationException("AI:Google:ModelId is required in configuration.");
        var apiKey = configuration["AI:Google:ApiKey"]
            ?? throw new InvalidOperationException("AI:Google:ApiKey is required in configuration.");

        if (int.TryParse(configuration["AI:Google:MaxTokens"], out var tokens))
        {
            _maxTokens = tokens;
        }

        builder.Services.AddGoogleAIGeminiChatCompletion(
            modelId: modelId,
            apiKey: apiKey
        );
    }

    public PromptExecutionSettings CreateExecutionSettings()
    {
        return new GeminiPromptExecutionSettings
        {
            ToolCallBehavior = GeminiToolCallBehavior.AutoInvokeKernelFunctions,
            Temperature = 0.7,
            MaxTokens = _maxTokens
        };
    }
}

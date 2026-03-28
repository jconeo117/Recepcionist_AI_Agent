using Microsoft.Extensions.Configuration;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Google;
using Polly;
using Polly.Extensions.Http;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;

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

        // --- POLYLL RETRY POLICY ---
        // 3 retries with exponential backoff (1s, 2s, 4s)
        var retryPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt - 1)));

        // Circuit breaker: open after 5 failed attempts in a row for 30 seconds
        var circuitBreakerPolicy = HttpPolicyExtensions
            .HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));

        // Configure HttpClient with Polly
        builder.Services.AddHttpClient("GoogleAIClient")
            .AddPolicyHandler(retryPolicy)
            .AddPolicyHandler(circuitBreakerPolicy);

        // Inject the Google AI Gemini completion using the custom client
        builder.AddGoogleAIGeminiChatCompletion(
            modelId: modelId,
            apiKey: apiKey,
            httpClient: builder.Services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("GoogleAIClient")
        );
    }

    public PromptExecutionSettings CreateExecutionSettings()
    {
        return new GeminiPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
            Temperature = 0.7,
            MaxTokens = _maxTokens
        };
    }
}

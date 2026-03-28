using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace ReceptionistAgent.AI.Agents;

/// <summary>
/// Agente recepcionista que procesa mensajes de usuario usando Semantic Kernel.
/// Ya NO depende del nombre del proveedor — recibe PromptExecutionSettings genéricos.
/// </summary>
public class RecepcionistAgent : IRecepcionistAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chatCompletionService;
    private readonly PromptExecutionSettings _settings;

    public RecepcionistAgent(Kernel kernel, PromptExecutionSettings settings)
    {
        _kernel = kernel;
        _chatCompletionService = kernel.GetRequiredService<IChatCompletionService>();
        _settings = settings;
    }

    public async Task<string> RespondAsync(string userMessage, ChatHistory chatHistory)
    {
        if (!string.IsNullOrWhiteSpace(userMessage))
        {
            chatHistory.AddUserMessage(userMessage);
        }

        var result = await _chatCompletionService.GetChatMessageContentAsync(
            chatHistory,
            _settings,
            _kernel
        );

        chatHistory.AddAssistantMessage(result.Content ?? string.Empty);

        return result.Content ?? "Lo siento, no pude procesar su solicitud.";
    }
}
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Ai;

/// <summary>Generates text with Claude, through the Anthropic API (official Anthropic SDK).</summary>
public class ClaudeTextGenerator : IAiTextGenerator
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>
    /// Models that support server-side refusal fallbacks (<c>fallbacks: "default"</c>): if Claude's safety
    /// classifiers decline the request, Anthropic retries it on a suitable other model in the same call.
    /// </summary>
    internal static readonly HashSet<string> ServerSideFallbackModels =
        ["claude-opus-5-5", "claude-opus-5", "claude-fable-5-1", "claude-sonnet-5-5"];

    internal const string ServerSideFallbackBeta = "server-side-fallback-2026-07-01";

    public AiProvider Provider => AiProvider.Claude;

    public bool IsConfigured(AppSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ClaudeApiKey) && !string.IsNullOrWhiteSpace(settings.ClaudeModel);

    public string Describe(AppSettings settings) => $"Claude ({settings.ClaudeModel.Trim()})";

    public async Task<string> GenerateAsync(AppSettings settings, AiPrompt prompt, CancellationToken cancellationToken)
    {
        var model = settings.ClaudeModel.Trim();
        var useFallback = ServerSideFallbackModels.Contains(model);

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 16000,
            System = prompt.System,
            Messages = [new BetaUserMessageParam { Content = prompt.User }],
        };
        if (useFallback)
        {
            parameters = parameters with { Betas = [ServerSideFallbackBeta], Fallbacks = new Default() };
        }

        var response = await CallAsync(settings, client => client.Beta.Messages.Create(parameters, cancellationToken));

        if (response.StopReason == "refusal")
            throw new InvalidOperationException("Claude weigerde dit verslag te schrijven.");

        var text = string.Concat(response.Content.Select(b => b.Value).OfType<BetaTextBlock>().Select(b => b.Text)).Trim();
        if (text.Length == 0) throw new InvalidOperationException("Claude gaf een leeg antwoord.");
        return text;
    }

    public async Task<IReadOnlyList<string>> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ClaudeApiKey))
            throw new InvalidOperationException("Vul een API-sleutel in.");

        var page = await CallAsync(settings, client => client.Models.List(cancellationToken: cancellationToken));
        return page.Items.Select(m => m.ID).ToList();
    }

    /// <summary>Calls the API and turns API errors into a Dutch message for the user.</summary>
    internal static async Task<T> CallAsync<T>(AppSettings settings, Func<AnthropicClient, Task<T>> call)
    {
        var client = new AnthropicClient { ApiKey = settings.ClaudeApiKey.Trim() };
        try
        {
            return await call(client);
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new InvalidOperationException("De Claude API-sleutel is ongeldig.");
        }
        catch (AnthropicNotFoundException)
        {
            throw new InvalidOperationException($"Het Claude-model '{settings.ClaudeModel}' bestaat niet.");
        }
        catch (AnthropicRateLimitException)
        {
            throw new InvalidOperationException("Te veel verzoeken aan Claude; probeer het later opnieuw.");
        }
        catch (Anthropic5xxException)
        {
            throw new InvalidOperationException("Claude is tijdelijk niet bereikbaar; probeer het later opnieuw.");
        }
        catch (AnthropicApiException e)
        {
            throw new InvalidOperationException($"Claude gaf een fout: {e.Message}");
        }
        catch (AnthropicIOException)
        {
            throw new InvalidOperationException("Geen verbinding met de Claude API.");
        }
    }
}

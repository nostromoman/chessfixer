using System.Text.RegularExpressions;
using ChessFixer.Server.Data;
using ChessFixer.Shared.Enums;

namespace ChessFixer.Server.Services.Ai;

/// <summary>Generates text with an Ollama server (https://ollama.com), through its REST API.</summary>
public partial class OllamaTextGenerator(IHttpClientFactory httpClientFactory) : IAiTextGenerator
{
    public const string HttpClientName = "Ollama";

    public AiProvider Provider => AiProvider.Ollama;

    public bool IsConfigured(AppSettings settings) =>
        TryGetBaseUrl(settings, out _) && !string.IsNullOrWhiteSpace(settings.OllamaModel);

    public string Describe(AppSettings settings) => $"Ollama ({settings.OllamaModel.Trim()})";

    public async Task<string> GenerateAsync(AppSettings settings, AiPrompt prompt, CancellationToken cancellationToken)
    {
        var request = new ChatRequest(
            settings.OllamaModel.Trim(),
            [new ChatMessage("system", prompt.System), new ChatMessage("user", prompt.User)],
            Stream: false);

        using var message = CreateRequest(settings, HttpMethod.Post, "api/chat");
        message.Content = JsonContent.Create(request);

        using var response = await Client.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken);
        var text = RemoveThinking(result?.Message?.Content ?? "").Trim();
        if (text.Length == 0) throw new InvalidOperationException("Ollama gaf een leeg antwoord.");
        return text;
    }

    public async Task<IReadOnlyList<string>> TestConnectionAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        using var message = CreateRequest(settings, HttpMethod.Get, "api/tags");
        using var response = await Client.SendAsync(message, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var tags = await response.Content.ReadFromJsonAsync<TagsResponse>(cancellationToken);
        return tags?.Models?.Select(m => m.Name).Where(n => !string.IsNullOrEmpty(n)).Order().ToList() ?? [];
    }

    private HttpClient Client => httpClientFactory.CreateClient(HttpClientName);

    private static bool TryGetBaseUrl(AppSettings settings, out Uri baseUrl) =>
        Uri.TryCreate(settings.OllamaUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute, out baseUrl!)
        && baseUrl.Scheme is "http" or "https";

    private static HttpRequestMessage CreateRequest(AppSettings settings, HttpMethod method, string path)
    {
        if (!TryGetBaseUrl(settings, out var baseUrl))
            throw new InvalidOperationException("De Ollama-URL is ongeldig.");

        return new HttpRequestMessage(method, new Uri(baseUrl, path));
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (body.Length > 300) body = body[..300];
        throw new HttpRequestException(
            $"Ollama antwoordde met {(int)response.StatusCode} {response.ReasonPhrase}: {body}".TrimEnd(' ', ':'));
    }

    /// <summary>Reasoning models may put their thoughts in think tags before the answer.</summary>
    private static string RemoveThinking(string text) => ThinkTags().Replace(text, "");

    [GeneratedRegex("<think>.*?</think>", RegexOptions.Singleline)]
    private static partial Regex ThinkTags();

    private sealed record ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages, bool Stream);
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ChatResponse(ChatMessage? Message);
    private sealed record TagsResponse(IReadOnlyList<TagModel>? Models);
    private sealed record TagModel(string Name);
}

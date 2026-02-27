using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ZemaxMCP.OllamaBridge.Models;

namespace ZemaxMCP.OllamaBridge;

/// <summary>
/// Client for the Ollama REST API.
/// </summary>
public class OllamaClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public OllamaClient(string baseUrl = "http://localhost:11434")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<OllamaChatResponse> ChatAsync(OllamaChatRequest request)
    {
        var json = JsonConvert.SerializeObject(request, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync($"{_baseUrl}/api/chat", content);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Ollama API error ({response.StatusCode}): {responseBody}");

        return JsonConvert.DeserializeObject<OllamaChatResponse>(responseBody)
            ?? throw new InvalidOperationException("Failed to parse Ollama response");
    }

    public async Task<List<string>> ListModelsAsync()
    {
        var response = await _http.GetAsync($"{_baseUrl}/api/tags");
        var body = await response.Content.ReadAsStringAsync();
        var obj = JObject.Parse(body);
        var models = new List<string>();
        var modelsArray = obj["models"] as JArray;
        if (modelsArray != null)
        {
            foreach (var m in modelsArray)
                models.Add(m["name"]?.ToString() ?? "unknown");
        }
        return models;
    }

    public void Dispose() => _http.Dispose();
}

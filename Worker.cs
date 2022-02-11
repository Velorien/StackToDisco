using System.Text.Json;
using Microsoft.Extensions.Options;

namespace StackToDisco;

class Worker : BackgroundService
{
    private const string StackApiUrl =
        "https://api.stackexchange.com/2.3/questions?fromdate={0}&todate={1}&order=asc&sort=creation&tagged=orleans&site=stackoverflow";
    private const string DiscordApiUrl = "https://discord.com/api/v9/channels/{0}/messages";

    private readonly HttpClient _soClient = new(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip });
    private readonly HttpClient _discordClient = new();

    private readonly JsonSerializerOptions _serializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<Worker> _logger;
    private readonly WorkerSettings _settings;

    public Worker(ILogger<Worker> logger, IOptions<WorkerSettings> options)
    {
        _logger = logger;
        _settings = options.Value;

        _discordClient.DefaultRequestHeaders.Authorization = new("Bot", _settings.DiscordBotToken);
        _discordClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
        _soClient.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var questions = await GetStackOverflowQuestions();

            if (questions is null) continue;

            await SendDiscordMessages(questions);

            await Task.Delay(_settings.QueryInterval, stoppingToken);
        }
    }

    private async Task SendDiscordMessages(StackQueryResponse questions)
    {
        List<List<string>> batches = new() { new() }; // absolutely horrifying collection initializer

        if (questions.Items.Any())
        {
            batches[0].Add("Here are Orleans questions I've found:");
            int batchLength = batches[0][0].Length;
            foreach (var item in questions.Items)
            {
                // discord message can be 2000 chars long at most
                if (batchLength + item.Link.Length + batches[^1].Count > 2000) 
                {
                    batches.Add(new ());
                    batchLength = 0;
                }

                batches[^1].Add(item.Link);
                batchLength += item.Link.Length;
            }
        }
        else
        {
            batches[0].Add("No questions about Orleans found this time. Bummer!");
        }

        foreach (var batch in batches)
        {
            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["content"] = string.Join("\n", batch)
            });

            using var request = new HttpRequestMessage
            {
                RequestUri = new Uri(string.Format(DiscordApiUrl, _settings.DiscordChannelId)),
                Method = HttpMethod.Post,
                Content = content
            };

            var response = await _discordClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "Discord api api did not return a success. Status: {status}. Response: {response}",
                    response.StatusCode,
                    await response.Content.ReadAsStringAsync());
            }
        }
    }

    private async Task<StackQueryResponse?> GetStackOverflowQuestions()
    {
        var dateTo = ((DateTimeOffset)DateTime.Today).ToUnixTimeSeconds();
        var dateFrom = dateTo - _settings.QueryInterval.TotalSeconds;

        using var request = new HttpRequestMessage
        {
            RequestUri = new Uri(string.Format(StackApiUrl, dateFrom, dateTo)),
            Method = HttpMethod.Get,
        };

        var response = await _soClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "StackExchange api api did not return a success. Status: {status}. Response: {response}",
                response.StatusCode,
                await response.Content.ReadAsStringAsync());
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<StackQueryResponse>(json, _serializerOptions);
    }
}

record StackQueryResponse(IEnumerable<StackResponse> Items);

record StackResponse(string Link);

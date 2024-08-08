using Discord;
using Newtonsoft.Json;

public static class DCExtensions
{
    private class DiscordThreadSearchResult
    {
        [JsonProperty("threads")]
        public DiscordThread[] Threads;

        [JsonProperty("total_results")]
        public uint TotalResults;

        [JsonProperty("has_more")]
        public bool HasMore;
    }

    private static string FormatThreadSearchUrl(ulong channelID, uint pageLen, uint currentPage)
        => $"/channels/{channelID}/threads/search?sort_by=last_message_time&sort_order=desc&limit={pageLen}&offset={pageLen * currentPage}";

    private static async Task<DiscordThreadSearchResult> SearchChannelForThreadCount(this DiscordClient _client, ulong channelID)
    {
        return (await _client.HttpClient.GetAsync(FormatThreadSearchUrl(channelID, 1, 0))).Deserialize<DiscordThreadSearchResult>();
    }

    public static async Task<IList<DiscordThread>> SearchChannelForThreads(this DiscordClient _client, ulong channelID)
    {
        var threads = new List<DiscordThread>();

        var info = await _client.SearchChannelForThreadCount(channelID);
        const uint PAGE_LEN = 25;
        object mutex = new();
        var range = Enumerable.Range(0, (int)Math.Ceiling(info.TotalResults / (decimal)PAGE_LEN));

        const int DELAY = 3000;
        Scheduler.Process(range.TakeLast(1), DELAY, async page =>
        {
            var stuff = (await _client.HttpClient.GetAsync(FormatThreadSearchUrl(channelID, PAGE_LEN, (uint)page))).Deserialize<DiscordThreadSearchResult>();
            lock (mutex)
                threads.AddRange(stuff.Threads);
            return true;
        });

        return threads;
    }
}
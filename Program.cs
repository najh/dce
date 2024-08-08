using Discord;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

public class Program
{
    private static ManualResetEvent mre = new ManualResetEvent(false);
    public static void Main(string[] args)
    {
        Task.Factory.StartNew(new Program().MainAsync);
        mre.WaitOne();
    }

    private class ChannelInfo
    {
        public string Name { get; set; }
        public IReadOnlyList<DiscordMessage> Threads { get; set; }
    }

    async Task<string> DumpChannel(DiscordClient _client, GuildChannel channel)
    {
        var threads = (await _client.SearchChannelForThreads(channel.Id));

        var channelDict = new Dictionary<ulong, ChannelInfo>();
        object mutex = new();

        Scheduler.Process(threads, 3000, async thread =>
        {
            var messages = await _client.GetChannelMessagesAsync(thread.Id);
            lock (mutex)
                channelDict[thread.Id] = new ChannelInfo() { Name = thread.Name, Threads = messages };
            return true;
        });

        var sb2 = new StringBuilder();
        sb2.AppendLine("var threads = {");

        foreach (var kvp in channelDict.OrderBy(x => x.Value.Threads.Last().Id))
        {
            var thread = kvp.Value;
            var messages = thread.Threads;
            sb2.AppendLine($"{kvp.Key}: {{");
            sb2.AppendLine($"name: \"{HttpUtility.JavaScriptStringEncode(thread.Name)}\",");
            sb2.AppendLine("posts: [");
            foreach (var m in messages.OrderBy(x => x.Id))
            {
                sb2.AppendLine("{");
                sb2.AppendLine($"text: \"{HttpUtility.JavaScriptStringEncode(Sanitise(m.Content))}\",");
                sb2.AppendLine($"embed: \"{HttpUtility.JavaScriptStringEncode(m.Embed?.Thumbnail?.Url ?? string.Empty)}\",");
                sb2.AppendLine("attachments: [");
                foreach (var at in m.Attachments)
                {
                    switch (at.ContentType)
                    {
                        case "image/png":
                        case "image/jpeg":
                        case "image/gif":
                            sb2.AppendLine($"{{url: \"{HttpUtility.JavaScriptStringEncode(at.Url)}\", name: \"{HttpUtility.JavaScriptStringEncode(at.FileName)}\", type: \"image\"}},");
                            break;
                        default:
                            sb2.AppendLine($"{{url: \"{HttpUtility.JavaScriptStringEncode(at.Url)}\", name: \"{HttpUtility.JavaScriptStringEncode(at.FileName)}\", type: \"file\"}},");
                            break;
                    }
                }
                sb2.AppendLine("]");
                sb2.AppendLine("},");
            }
            sb2.AppendLine("]");
            sb2.AppendLine("},");

        }
        sb2.AppendLine("};");
        return sb2.ToString();
    }

    const string tokenFile = "token.txt";

    private bool TryLogin(string token, out DiscordClient _client)
    {
        try
        {
            _client = new DiscordClient(token);
            Console.Clear();
            Console.WriteLine($"Logged in as user: {_client.User.Username}");
            File.WriteAllText(tokenFile, token);
            return true;
        }
        catch (Exception)
        {
            Console.Clear();
            Console.WriteLine($"Failed to log in.");
        }
        _client = null;
        return false;
    }

    public async Task MainAsync()
    {
        bool loggedIn = false;

        string token = null;

        if(File.Exists(tokenFile))
            token = File.ReadAllText(tokenFile);

        DiscordClient _client;
        while (true)
        {
            if (token == null)
            {
                Console.Write("Discord auth token: ");
                token = Console.ReadLine();
            }
            if (TryLogin(token, out _client))
                break;
            else
                token = null;
            
        }

        var guildNo = -1;
        var guilds = await _client.GetGuildsAsync();
        const int guildsPerPage = 15;
        var guildOffset = 0;
        while(guildNo == -1)
        {
            for (var i = guildOffset; i < Math.Min(guildOffset + guildsPerPage, guilds.Count); i++)
                Console.WriteLine($"[{i,3}] - {guilds[i].Name}");

            Console.Write("Enter a server number, - or + to change page: ");

            var input = Console.ReadLine();
            switch (input)
            {
                case "+":
                    Console.Clear();
                    var maxGuildOffset = Math.Max(0, ((int)Math.Ceiling(guilds.Count / (float)guildsPerPage) - 1)) * guildsPerPage;
                    if (guildOffset < maxGuildOffset)
                        guildOffset = Math.Min(maxGuildOffset, guildOffset + guildsPerPage);
                    break;
                case "-":
                    Console.Clear();
                    if (guildOffset > 0)
                        guildOffset = Math.Max(0, guildOffset - guildsPerPage);
                    break;
                default:
                    if(int.TryParse(input, out guildNo))
                        break;
                    else
                    {
                        Console.Clear();
                        Console.WriteLine("Failed to parse input.");
                        break;
                    }
            }

        }

        Console.Clear();

        try
        {
            var target = guilds[guildNo];
            var channels = await target.GetChannelsAsync();

            var categories = channels.Where(x => x.ParentId == null).OrderBy(x => x.Position).ToDictionary(x => x.Id, x => x.Name);
            var children = channels.Where(x => x.ParentId != null).GroupBy(x => x.ParentId).ToDictionary(x => x.Key, x => x.AsEnumerable());


            IEnumerable<GuildChannel> targetChannels;

            while (true)
            {
                Console.Clear();
                foreach (var category in categories)
                {
                    Console.WriteLine(category.Value);
                    if (children.TryGetValue(category.Key, out IEnumerable<GuildChannel> categoryChannels))
                    {
                        foreach (var child in categoryChannels)
                        {
                            Console.WriteLine($"\t{child.Name}");
                        }
                    }
                }

                var input = Console.ReadLine().Split(",", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                targetChannels = channels.Where(x => input.Contains(x.Name)).Skip(1);
                if (targetChannels.Count() > 0)
                    break;
            }


            const string THREADDIR = "THREADS";
            if(!Directory.Exists(THREADDIR))
                Directory.CreateDirectory(THREADDIR);

            foreach (var channel in targetChannels)
            {
                Console.WriteLine($"Dumping: {channel.Name}");
                File.WriteAllText(Path.Combine(THREADDIR, $"{channel}.txt"), await DumpChannel(_client, channel));
            }
            
            mre.Set();
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.Message);
        }
    }

    private string Sanitise(string content)
    {
        var ret = content;
        ret = Regex.Replace(ret, @"<br/>", "\n");
        ret = Regex.Replace(ret, @"\|\|", "\n");
        ret = Regex.Replace(ret, @"\n+", "\n");
        ret = Regex.Replace(ret, @"<@\d+>", "<>");
        ret = Regex.Replace(ret, @"\n", "<br/>");
        return ret;
    }
}

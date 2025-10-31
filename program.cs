using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

class Program
{
    static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    static readonly Regex _href = new(@"href\s*=\s*[""'](?<u>[^""'#>]+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    static async Task Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("Usage: <startUrl> <maxDepth>"); return; }
        var root = new Uri(args[0]);
        var maxDepth = int.Parse(args[1]);
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var visited = new HashSet<string>();
        var channel = Channel.CreateUnbounded<(Uri uri, int depth)>();
        await channel.Writer.WriteAsync((root, 0));

        var workers = new List<Task>();
        for (int i = 0; i < 8; i++)
            workers.Add(Task.Run(() => Worker(channel.Reader, channel.Writer, visited, maxDepth, cts.Token)));

        await Task.WhenAll(workers);
    }

    static async Task Worker(ChannelReader<(Uri uri, int depth)> reader, ChannelWriter<(Uri uri, int depth)> writer,
        HashSet<string> visited, int maxDepth, CancellationToken ct)
    {
        while (await reader.WaitToReadAsync(ct))
        {
            while (reader.TryRead(out var item))
            {
                if (ct.IsCancellationRequested) { writer.Complete(); return; }
                var key = item.uri.GetLeftPart(UriPartial.Path);
                lock (visited) { if (visited.Contains(key)) continue; visited.Add(key); }
                Console.WriteLine($"[{item.depth}] {item.uri}");
                if (item.depth >= maxDepth) continue;
                try
                {
                    var html = await _http.GetStringAsync(item.uri, ct);
                    foreach (Match m in _href.Matches(html))
                    {
                        var val = m.Groups["u"].Value;
                        if (Uri.TryCreate(item.uri, val, out var next))
                        {
                            if (next.Scheme == "http" || next.Scheme == "https")
                                await writer.WriteAsync((next, item.depth + 1), ct);
                        }
                    }
                }
                catch (Exception ex) { Console.WriteLine($"error fetching {item.uri}: {ex.Message}"); }
            }
        }
        writer.Complete();
    }
}

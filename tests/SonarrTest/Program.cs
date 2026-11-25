using Jellyfin.Plugin.WatchTracker.Configuration; using Jellyfin.Plugin.WatchTracker.Services;
var c = new PluginConfiguration { SonarrUrl = "127.0.0.1:18989", SonarrApiKey = "k" };
var s = new SonarrClient(new F());
Console.WriteLine(await s.TestAsync("127.0.0.1:18989", "k", default)); Console.WriteLine(await s.TestAsync("http://127.0.0.1:18989/", "k", default)); try { await s.TestAsync("", "k", default); } catch (Exception e) { Console.WriteLine("empty: " + e.Message); } try { await s.TestAsync("127.0.0.1:18989", "bad", default); } catch (Exception e) { Console.WriteLine("badkey: " + e.Message); } Console.WriteLine("ok");
Console.WriteLine(await s.UnmonitorAsync(c, "123", 1, default));
Console.WriteLine(await s.UnmonitorAsync(c, "123", null, default));
Console.WriteLine(await s.UnmonitorAsync(c, "999", 1, default));
Console.WriteLine(await s.UnmonitorAsync(c, null, 1, default));
try { await s.UnmonitorAsync(new PluginConfiguration { SonarrUrl = c.SonarrUrl, SonarrApiKey = "bad" }, "123", 1, default); } catch (Exception e) { Console.WriteLine("bad key throws: " + e.Message); }
class F : IHttpClientFactory { public HttpClient CreateClient(string n) => new HttpClient(new HttpClientHandler { UseProxy = false }); }

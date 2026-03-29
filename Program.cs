using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

var opts = ParseArgs(args);
var builder = WebApplication.CreateBuilder([]);
builder.WebHost.UseUrls($"http://0.0.0.0:{opts.Port}");
builder.Services.AddSingleton(opts);
builder.Services.AddHttpClient("wx");
var app = builder.Build();

var detailHtml = LoadHtml();

app.MapGet("/", () => "csharp-wxpush is running...");
app.MapGet("/detail", () => Results.Content(detailHtml, "text/html; charset=utf-8"));

app.MapGet("/wxsend", async (HttpContext ctx, IHttpClientFactory hf, AppOptions o) =>
{
    var q = ctx.Request.Query;
    return await Send(new(q["title"], q["content"], q["appid"], q["secret"],
        q["userid"], q["template_id"], q["base_url"], q["tz"]), hf.CreateClient("wx"), o);
});

app.MapPost("/wxsend", async (ReqBody r, IHttpClientFactory hf, AppOptions o) =>
    await Send(new(r.Title, r.Content, r.AppId, r.Secret,
        r.UserId, r.TemplateId, r.BaseUrl, r.Tz), hf.CreateClient("wx"), o));

Console.WriteLine($"Server is running on: http://127.0.0.1:{opts.Port}");
app.Run();

// --- core ---

static async Task<IResult> Send(Params p, HttpClient http, AppOptions o)
{
    var appId = Or(p.AppId, o.AppId);
    var secret = Or(p.Secret, o.Secret);
    var userId = Or(p.UserId, o.UserId);
    var tmplId = Or(p.TemplateId, o.TemplateId);

    if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(secret) ||
        string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tmplId))
        return Results.Json(new { error = "Missing required parameters" }, statusCode: 400);

    var title = Or(p.Title, o.Title, "测试标题");
    var content = Or(p.Content, o.Content, "测试内容");
    var baseUrl = Or(p.BaseUrl, o.BaseUrl, $"http://127.0.0.1:{o.Port}");
    var tz = Or(p.Tz, o.Tz, "Asia/Shanghai");

    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetTz(tz));
    var timeStr = now.ToString("yyyy-MM-dd HH:mm:ss");
    var detailUrl = $"{baseUrl}/detail?title={WebUtility.UrlEncode(title)}&message={WebUtility.UrlEncode(content)}&date={WebUtility.UrlEncode(timeStr)}";

    try
    {
        // get access_token
        var tokenBody = await PostJson(http, "https://api.weixin.qq.com/cgi-bin/stable_token",
            new { grant_type = "client_credential", appid = appId, secret, force_refresh = false });
        var token = JsonSerializer.Deserialize<JsonElement>(tokenBody);

        if (!token.TryGetProperty("access_token", out var at) || at.GetString() is not { Length: > 0 } accessToken)
        {
            var code = token.TryGetProperty("errcode", out var ec) ? ec.GetInt32() : -1;
            var msg = token.TryGetProperty("errmsg", out var em) ? em.GetString() : tokenBody;
            return Results.Json(new { error = $"Failed to get access token: errcode={code}, errmsg={msg}" }, statusCode: 500);
        }

        // send template message
        var sendBody = await PostJson(http,
            $"https://api.weixin.qq.com/cgi-bin/message/template/send?access_token={accessToken}",
            new
            {
                touser = userId, template_id = tmplId, url = detailUrl,
                data = new { title = new { value = title }, content = new { value = content } }
            });
        return Results.Content(sendBody, "application/json");
    }
    catch (Exception ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 500);
    }
}

static async Task<string> PostJson(HttpClient http, string url, object data)
{
    var json = JsonSerializer.Serialize(data);
    var resp = await http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
    return await resp.Content.ReadAsStringAsync();
}

static string Or(string? a, string b, string fallback = "") =>
    !string.IsNullOrEmpty(a) ? a : !string.IsNullOrEmpty(b) ? b : fallback;

static TimeZoneInfo GetTz(string tz)
{
    try { return TimeZoneInfo.FindSystemTimeZoneById(tz); }
    catch { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai"); }
}

static string LoadHtml()
{
    var asm = Assembly.GetExecutingAssembly();
    var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("msg_detail.html"));
    if (name == null) return "<html><body>not found</body></html>";
    using var s = asm.GetManifestResourceStream(name)!;
    using var r = new StreamReader(s);
    return r.ReadToEnd();
}

static AppOptions ParseArgs(string[] args)
{
    var o = new AppOptions();
    for (var i = 0; i < args.Length - 1; i++)
    {
        var k = args[i].TrimStart('-').ToLower();
        var v = args[++i];
        switch (k)
        {
            case "title": o.Title = v; break; case "content": o.Content = v; break;
            case "appid": o.AppId = v; break; case "secret": o.Secret = v; break;
            case "userid": o.UserId = v; break; case "template_id": o.TemplateId = v; break;
            case "base_url": o.BaseUrl = v; break; case "tz": o.Tz = v; break;
            case "port": if (int.TryParse(v, out var p)) o.Port = p; break;
        }
    }
    return o;
}

// --- models ---

record Params(string? Title, string? Content, string? AppId, string? Secret,
    string? UserId, string? TemplateId, string? BaseUrl, string? Tz);

class AppOptions
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Secret { get; set; } = "";
    public string UserId { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int Port { get; set; } = 5566;
    public string Tz { get; set; } = "Asia/Shanghai";
}

class ReqBody
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
    [JsonPropertyName("appid")] public string? AppId { get; set; }
    [JsonPropertyName("secret")] public string? Secret { get; set; }
    [JsonPropertyName("userid")] public string? UserId { get; set; }
    [JsonPropertyName("template_id")] public string? TemplateId { get; set; }
    [JsonPropertyName("base_url")] public string? BaseUrl { get; set; }
    [JsonPropertyName("tz")] public string? Tz { get; set; }
}

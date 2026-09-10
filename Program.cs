// GufoFAQ 資料 API 範例（對應 README.md）
//
// 涵蓋兩組以 X-API-Key 認證的端點：
//   /api/v1/*                      資料集與資料的 CRUD
//   /api/extract/country-or-time   國家／時間萃取
//
// 跑法：dotnet run -- <指令>
//   datasets / records / files / delete / extract / all
//
// 設定：改下面兩個常數，或設環境變數 GUFOFAQ_BASE_URL、GUFOFAQ_API_KEY（環境變數優先）。

using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

const string BaseUrlDefault = "https://<你的 GufoFAQ 網域>";
const string ApiKeyDefault = "sk_你的金鑰";

const string SampleDatasetName = "API 範例資料集";

var baseUrl = (Environment.GetEnvironmentVariable("GUFOFAQ_BASE_URL") ?? BaseUrlDefault).TrimEnd('/');
var apiKey = Environment.GetEnvironmentVariable("GUFOFAQ_API_KEY") ?? ApiKeyDefault;

if (baseUrl.Contains('<') || apiKey.StartsWith("sk_你的"))
{
    Console.Error.WriteLine("請先設定 GUFOFAQ_BASE_URL 與 GUFOFAQ_API_KEY（或改 Program.cs 最上面的常數）。");
    return 1;
}

// 只有在對「自簽憑證的測試站」呼叫時才需要；正式環境不要開。
var handler = new HttpClientHandler();
if (Environment.GetEnvironmentVariable("GUFOFAQ_INSECURE_TLS") == "1")
    handler.ServerCertificateCustomValidationCallback = (_, _, _, _) => true;

using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
// 錯誤訊息的語言（值域 zh-TW / en）。不帶就用租戶自己的設定。
client.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("zh-TW"));

var command = args.Length > 0 ? args[0] : "all";

try
{
    switch (command)
    {
        case "datasets": await DemoDatasets(); break;
        case "records": await DemoRecords(); break;
        case "files": await DemoFiles(args.Length > 1 ? args[1] : null); break;
        case "delete": await DemoDelete(); break;
        case "extract": await DemoExtract(); break;
        case "all":
            await DemoDatasets();
            await DemoRecords();
            await DemoFiles(null);
            await DemoExtract();
            Console.WriteLine("\n（範例資料集保留著，要清掉請跑 `dotnet run -- delete`）");
            break;
        default:
            Console.Error.WriteLine($"不認得的指令：{command}");
            Console.Error.WriteLine("可用：datasets / records / files [xlsx路徑] / delete / extract / all");
            return 1;
    }
}
catch (ApiException e)
{
    // 分流一律看狀態碼與 code，不要比對訊息字面（訊息會隨語言變）。
    Console.Error.WriteLine($"\n呼叫失敗：HTTP {(int)e.Status} code={e.Code ?? "(無)"} 關聯編號={e.CorrelationId ?? "(無)"}");
    Console.Error.WriteLine($"  {ForDisplay(e.Message)}");
    if (e.RetryAfter is not null)
        Console.Error.WriteLine($"  Retry-After: {e.RetryAfter} 秒後可再試");
    return 2;
}

return 0;

// ── 一、資料集 ────────────────────────────────────────────────────────────────

async Task DemoDatasets()
{
    Console.WriteLine("== 建立資料集 ==");
    // description 會影響問答（系統靠它判斷一個問題該找哪個資料集），要寫清楚裝了什麼。
    var created = await PostJson<DatasetSummary>("/api/v1/datasets", new
    {
        name = SampleDatasetName,
        description = "範例：客服常見問題與產品說明",
    });
    Console.WriteLine($"  id={created.Id} index_name={created.IndexName} name={created.Name}");

    Console.WriteLine("== 列出資料集 ==");
    var list = await GetJson<List<DatasetSummary>>("/api/v1/datasets");
    foreach (var d in list)
        Console.WriteLine($"  #{d.Id} {d.Name}（group_id={d.GroupId?.ToString() ?? "null"}, {d.FileCount} 份資料）");

    Console.WriteLine("== 讀單一資料集 ==");
    var one = await GetJson<DatasetSummary>($"/api/v1/datasets/{created.Id}");
    Console.WriteLine($"  #{one.Id} {one.Name}");

    Console.WriteLine("== 改名 ==");
    var renamed = await PatchJson<DatasetSummary>($"/api/v1/datasets/{created.Id}",
        new { name = SampleDatasetName });  // 改成原本的名字：唯一性檢查會排除自己，不會 409
    Console.WriteLine($"  name={renamed.Name}（index_name 不變：{renamed.IndexName}）");
}

// ── 二、結構化記錄 ────────────────────────────────────────────────────────────

async Task DemoRecords()
{
    var ds = await FindSampleDataset();

    Console.WriteLine("== 上傳記錄（逐筆各自成敗）==");
    var payload = new
    {
        // 槽名固定（content 必填），完整清單見 README〈五、欄位槽〉。
        records = new object[]
        {
            new { external_key = "FAQ-001", title = "如何退貨", content = "收到商品七日內可申請退貨。", note1 = "客服組", date = "2026-03-01" },
            new { title = "營業時間", content = "週一至週五 09:00–18:00。" },
            new { title = "這一筆會失敗", content = "   " },   // content 只有空白 → content_required
            new { content = "槽名打錯的那一筆", bogus_slot = "x" },  // → unknown_slot
        },
        // 內容已經是純文字／Markdown 就關掉，免得被當 HTML 改寫。
        convert_html = false,
    };
    var res = await PostJson<ImportResult>($"/api/v1/datasets/{ds.Id}/records", payload);
    PrintImportResult(res);

    Console.WriteLine("== 同一個 external_key 再送一次（取代舊版）==");
    var again = await PostJson<ImportResult>($"/api/v1/datasets/{ds.Id}/records", new
    {
        records = new object[]
        {
            new { external_key = "FAQ-001", title = "如何退貨", content = "收到商品十四日內可申請退貨（條款更新）。" },
        },
        convert_html = false,
    });
    PrintImportResult(again);
    Console.WriteLine("  ↑ updated=1、該筆帶 replaced_file_id ＝舊版已被取代（不是多一份）");

    Console.WriteLine("== 列出這個資料集裡的資料 ==");
    var listing = await GetJson<RecordListing>($"/api/v1/datasets/{ds.Id}/records");
    foreach (var f in listing.Files)
        Console.WriteLine($"  #{f.Id} {f.Filename}（{f.FileType}/{f.ConnectorType}, {f.RecordCount} 筆, {f.UploadedAt}）");

    if (listing.Files.Count > 0)
    {
        var last = listing.Files[^1];
        Console.WriteLine($"== 刪掉一份資料（#{last.Id}）==");
        var deleted = await SendJson<DeletedIdResult>(HttpMethod.Delete, $"/api/v1/datasets/{ds.Id}/records/{last.Id}", null);
        Console.WriteLine($"  deleted={deleted.Deleted}");
    }
}

// ── 三、上傳檔案 ──────────────────────────────────────────────────────────────

async Task DemoFiles(string? path)
{
    var ds = await FindSampleDataset();

    byte[] bytes;
    string filename;
    if (path is not null)
    {
        bytes = await File.ReadAllBytesAsync(path);
        filename = Path.GetFileName(path);
    }
    else
    {
        // 範例自帶一個 xlsx，讓這一段不必先準備檔案。正式整合請直接讀你自己的檔。
        bytes = MinimalXlsx.Build(
            new[] { "標題", "內容", "承辦" },
            new[]
            {
                new[] { "運費怎麼算", "訂單滿 1000 元免運，未滿酌收 80 元。", "物流組" },
                new[] { "可以改地址嗎", "出貨前可於訂單頁自行修改。", "客服組" },
            });
        filename = "常見問題.xlsx";
    }

    Console.WriteLine($"== 上傳檔案（{filename}）==");
    using var form = new MultipartFormDataContent();
    var file = new ByteArrayContent(bytes);
    file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
    form.Add(file, "files", filename);

    if (filename.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
    {
        // 帶 mapping ＝欄對到槽、**一列一份資料**（與產品介面的匯入精靈同一條路）。
        // 不帶 mapping 的 xlsx 是「整個檔一份資料」，也不支援 external_keys。
        form.Add(new StringContent(JsonConvert.SerializeObject(new Dictionary<string, string[]>
        {
            ["content"] = new[] { "內容" },   // content 必填
            ["title"] = new[] { "標題" },
            ["note1"] = new[] { "承辦" },
        }), Encoding.UTF8), "mapping");
    }
    form.Add(new StringContent("false"), "convert_html");
    // 逐檔一個外部主鍵：帶了就是「這份資料的新版」，同鍵舊版會被取代。
    form.Add(new StringContent(JsonConvert.SerializeObject(new Dictionary<string, string>
    {
        [filename] = "SHEET-FAQ",
    }), Encoding.UTF8), "external_keys");

    var res = await SendForm<ImportResult>($"/api/v1/datasets/{ds.Id}/files", form);
    PrintImportResult(res);
}

// ── 四、刪除資料集 ────────────────────────────────────────────────────────────

async Task DemoDelete()
{
    var ds = await FindSampleDataset();
    Console.WriteLine($"== 刪除資料集 #{ds.Id}（連帶其中所有資料，不可復原）==");
    var deleted = await SendJson<DeletedIdResult>(HttpMethod.Delete, $"/api/v1/datasets/{ds.Id}", null);
    Console.WriteLine($"  deleted={deleted.Deleted}");
}

// ── 五、萃取 ──────────────────────────────────────────────────────────────────

async Task DemoExtract()
{
    // mode 是整數：1 只萃國家、2 只萃時間、3 兩者。
    // 「萃不到」是字串「無」；「這個模式不回這一欄」是 null——兩者不同。
    foreach (var (mode, text, reference) in new (int, string, string?)[]
    {
        (1, "2026 年 3 月 15 日，美國總統在白宮發表演說。", null),
        (2, "明天下午三點將舉行記者會", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        (3, "上個月我去了東京旅遊", "2026-03-15 10:00:00"),
    })
    {
        Console.WriteLine($"== 萃取 mode={mode} ==");
        var r = await PostJson<ExtractResult>("/api/extract/country-or-time", new
        {
            text,
            mode,
            time_reference = reference,
        });
        Console.WriteLine($"  country={r.Country ?? "null"}  time={r.Time ?? "null"}");
    }
}

// ── 共用 ──────────────────────────────────────────────────────────────────────

async Task<DatasetSummary> FindSampleDataset()
{
    var list = await GetJson<List<DatasetSummary>>("/api/v1/datasets");
    var found = list.FirstOrDefault(d => d.Name == SampleDatasetName);
    if (found is not null) return found;
    Console.WriteLine($"（找不到「{SampleDatasetName}」，先建一個）");
    var created = await PostJson<DatasetSummary>("/api/v1/datasets", new
    {
        name = SampleDatasetName,
        description = "範例：客服常見問題與產品說明",
    });
    return new DatasetSummary { Id = created.Id, Name = created.Name, IndexName = created.IndexName };
}

void PrintImportResult(ImportResult r)
{
    Console.WriteLine($"  inserted={r.Inserted} updated={r.Updated} failed={r.Failed}"
                      + (r.Imported is not null ? $" imported={r.Imported}" : ""));
    foreach (var (entry, i) in r.Results.Select((e, i) => (e, i)))
    {
        var who = entry.Filename ?? $"第 {i + 1} 筆";
        if (entry.Ok)
        {
            var id = entry.Doc ?? entry.FileId;
            Console.Write($"  ✓ {who} → id={id} sync_state={entry.SyncState ?? "(未回報)"}");
            if (entry.ReplacedFileId is not null) Console.Write($" 取代了 #{entry.ReplacedFileId}");
            if (entry.DataCount is not null) Console.Write($" 匯入 {entry.DataCount} 列");
            Console.WriteLine();
            // 內容在存入時被改寫了什麼——有值就代表存進去的與送出的不完全一樣。
            if (entry.DroppedLinks is { Count: > 0 })
                Console.WriteLine($"      ⚠ 轉換時被剝掉的連結：{entry.DroppedLinks.Count} 個");
            if (entry.UnprocessableTables is { Count: > 0 })
                Console.WriteLine($"      ⚠ 無法保真轉換的表格：{entry.UnprocessableTables.Count} 個");
        }
        else
        {
            Console.WriteLine($"  ✗ {who} → code={entry.Code ?? "(無)"} {entry.Error}");
        }
    }
    // health_scan.ok=false 只代表匯入後的自動健檢沒跑完，匯入本身仍然成功。
    if (r.HealthScan is not null && r.HealthScan.Value<bool?>("ok") == false)
        Console.WriteLine("  （匯入後的自動健檢這一次沒跑完；匯入結果不受影響）");
}

/// <summary>錯誤訊息印到終端機用：非 JSON 的回應（例如打錯網域時的整頁 HTML）截短。</summary>
static string ForDisplay(string message) =>
    message.Length <= 300 ? message : message.Substring(0, 300) + $"…（共 {message.Length} 字）";

Task<T> GetJson<T>(string path) => SendJson<T>(HttpMethod.Get, path, null);
Task<T> PostJson<T>(string path, object body) => SendJson<T>(HttpMethod.Post, path, body);
Task<T> PatchJson<T>(string path, object body) => SendJson<T>(HttpMethod.Patch, path, body);

async Task<T> SendJson<T>(HttpMethod method, string path, object? body)
{
    using var req = new HttpRequestMessage(method, baseUrl + path);
    if (body is not null)
    {
        // NullValueHandling.Ignore：沒有值的選填欄位不要送 null 過去。
        var json = JsonConvert.SerializeObject(body, new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
        });
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
    }
    return await ReadOrThrow<T>(req);
}

async Task<T> SendForm<T>(string path, MultipartFormDataContent form)
{
    using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + path) { Content = form };
    return await ReadOrThrow<T>(req);
}

async Task<T> ReadOrThrow<T>(HttpRequestMessage req)
{
    using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseContentRead);
    var text = await resp.Content.ReadAsStringAsync();
    var correlationId = resp.Headers.TryGetValues("X-Correlation-ID", out var cid) ? cid.FirstOrDefault() : null;

    if (!resp.IsSuccessStatusCode)
        throw ApiException.From(resp.StatusCode, text, correlationId,
            resp.Headers.RetryAfter?.Delta?.TotalSeconds);

    var parsed = JsonConvert.DeserializeObject<T>(text);
    if (parsed is null) throw new ApiException(resp.StatusCode, "回應不是預期的 JSON 形狀", null, correlationId, null);
    return parsed;
}

// ── 資料結構 ──────────────────────────────────────────────────────────────────

/// <summary>資料集摘要（`POST /datasets` 只回 id／index_name／name，其餘欄位為 null）。</summary>
public class DatasetSummary
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("name")] public string Name { get; set; } = "";
    [JsonProperty("index_name")] public string IndexName { get; set; } = "";
    [JsonProperty("group_id")] public int? GroupId { get; set; }
    [JsonProperty("file_count")] public int FileCount { get; set; }
}

/// <summary>records／files 兩支上傳端點共用的回應形狀。</summary>
public class ImportResult
{
    /// <summary>只有 records 那一支有；＝inserted + updated。</summary>
    [JsonProperty("imported")] public int? Imported { get; set; }
    [JsonProperty("inserted")] public int Inserted { get; set; }
    [JsonProperty("updated")] public int Updated { get; set; }
    [JsonProperty("failed")] public int Failed { get; set; }
    /// <summary>與送出的順序一一對應。**逐筆／逐檔各自成敗**，HTTP 200 不代表每一筆都成功。</summary>
    [JsonProperty("results")] public List<ImportEntry> Results { get; set; } = new();
    /// <summary>匯入後自動健檢的結果；`ok:false` 不影響匯入。</summary>
    [JsonProperty("health_scan")] public JObject? HealthScan { get; set; }
}

public class ImportEntry
{
    [JsonProperty("ok")] public bool Ok { get; set; }
    /// <summary>檔案那一支才有。</summary>
    [JsonProperty("filename")] public string? Filename { get; set; }
    /// <summary>records 那一支的資料編號。</summary>
    [JsonProperty("doc")] public int? Doc { get; set; }
    /// <summary>files 那一支的資料編號。</summary>
    [JsonProperty("file_id")] public int? FileId { get; set; }
    [JsonProperty("updated")] public bool? Updated { get; set; }
    [JsonProperty("replaced_file_id")] public int? ReplacedFileId { get; set; }
    /// <summary>索引同步狀態：pending／succeeded／failed。送進來不等於已經可被檢索。</summary>
    [JsonProperty("sync_state")] public string? SyncState { get; set; }
    [JsonProperty("task_id")] public string? TaskId { get; set; }
    /// <summary>excel／folder（帶不帶欄位對照）。</summary>
    [JsonProperty("connector")] public string? Connector { get; set; }
    /// <summary>帶對照的 xlsx 實際匯進去的資料列數。</summary>
    [JsonProperty("data_count")] public int? DataCount { get; set; }
    [JsonProperty("error")] public string? Error { get; set; }
    [JsonProperty("code")] public string? Code { get; set; }
    [JsonProperty("dropped_links")] public List<JToken>? DroppedLinks { get; set; }
    [JsonProperty("unprocessable_tables")] public List<JToken>? UnprocessableTables { get; set; }
}

public class RecordListing
{
    [JsonProperty("dataset_id")] public int DatasetId { get; set; }
    /// <summary>依上傳時間降冪。</summary>
    [JsonProperty("files")] public List<RecordFile> Files { get; set; } = new();
}

public class RecordFile
{
    [JsonProperty("id")] public int Id { get; set; }
    [JsonProperty("filename")] public string Filename { get; set; } = "";
    [JsonProperty("file_type")] public string FileType { get; set; } = "";
    [JsonProperty("connector_type")] public string ConnectorType { get; set; } = "";
    [JsonProperty("size_bytes")] public long SizeBytes { get; set; }
    [JsonProperty("uploaded_at")] public string UploadedAt { get; set; } = "";
    /// <summary>實際進到索引的筆數。</summary>
    [JsonProperty("record_count")] public int? RecordCount { get; set; }
}

public class DeletedIdResult
{
    [JsonProperty("deleted")] public int Deleted { get; set; }
}

/// <summary>萃取結果。「萃不到」是字串「無」；「這個 mode 不回這一欄」是 null。</summary>
public class ExtractResult
{
    [JsonProperty("country")] public string? Country { get; set; }
    [JsonProperty("time")] public string? Time { get; set; }
}

/// <summary>
/// 失敗回應。`detail` 有兩種形狀：一句話，或 `{code, detail}`。
/// 分流一律用 HTTP 狀態碼 ＋ `code`，不要比對句子（句子會隨 Accept-Language 變）。
/// </summary>
public class ApiException : Exception
{
    public HttpStatusCode Status { get; }
    public string? Code { get; }
    public string? CorrelationId { get; }
    public double? RetryAfter { get; }

    public ApiException(HttpStatusCode status, string message, string? code, string? correlationId, double? retryAfter)
        : base(message)
    {
        Status = status;
        Code = code;
        CorrelationId = correlationId;
        RetryAfter = retryAfter;
    }

    public static ApiException From(HttpStatusCode status, string body, string? correlationId, double? retryAfter)
    {
        string? code = null;
        var message = body;
        try
        {
            var detail = JObject.Parse(body)["detail"];
            if (detail is JObject o)
            {
                code = o.Value<string>("code");
                message = o.Value<string>("detail") ?? body;
            }
            else if (detail is not null)
            {
                message = detail.ToString();
            }
            else
            {
                // 部分端點（額度、上游故障）回的是 {"error": …, "code": …}。
                var flat = JObject.Parse(body);
                code = flat.Value<string>("code");
                message = flat.Value<string>("error") ?? body;
            }
        }
        catch (JsonException)
        {
            // 不是 JSON（例如反向代理直接回的錯誤頁）：原樣留著給人看。
        }
        return new ApiException(status, message, code, correlationId, retryAfter);
    }
}

/// <summary>
/// 產生一個最小可讀的 xlsx，**只是為了讓這支範例不必先準備檔案**。
/// 正式整合請直接讀你自己的檔案，不需要這一段。
/// </summary>
public static class MinimalXlsx
{
    public static byte[] Build(string[] header, string[][] rows)
    {
        var all = new List<string[]> { header };
        all.AddRange(rows);

        var sheet = new StringBuilder();
        sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""");
        sheet.Append("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var r = 0; r < all.Count; r++)
        {
            sheet.Append($"<row r=\"{r + 1}\">");
            for (var c = 0; c < all[r].Length; c++)
                sheet.Append($"<c r=\"{Col(c)}{r + 1}\" t=\"inlineStr\"><is><t>{Esc(all[r][c])}</t></is></c>");
            sheet.Append("</row>");
        }
        sheet.Append("</sheetData></worksheet>");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(zip, "_rels/.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(zip, "xl/workbook.xml",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Sheet1" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(zip, "xl/_rels/workbook.xml.rels",
                """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }
        return ms.ToArray();
    }

    static void Add(ZipArchive zip, string name, string content)
    {
        using var w = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    static string Col(int index)
    {
        var s = "";
        for (var i = index; ; i = i / 26 - 1)
        {
            s = (char)('A' + i % 26) + s;
            if (i < 26) return s;
        }
    }

    static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

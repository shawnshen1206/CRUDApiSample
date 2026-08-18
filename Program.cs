using Newtonsoft.Json;

//共用HttpClient
var handler = new HttpClientHandler();
handler.ServerCertificateCustomValidationCallback =
    (httpRequestMessage, cert, cetChain, policyErrors) =>
    {
        return true;
    };
HttpClient client = new HttpClient(handler); //如果碰到SSL憑證問題，可能可以嘗試加上或拿掉handler

//生成範例兩筆資料
var data1 = new UploadedJsonData()
{
    FieldTitle = "標題1",
    FieldTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    FieldContent = "我是內容1",
    CusField = ["1", "2", "3"]
};
var data2 = new UploadedJsonData()
{
    FieldTitle = "標題2",
    FieldTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
    FieldContent = "我是內容2",
    CusField = ["1", "2", "3"]
};



//主程式(新增)
//----------------------------------------------------------
//UploadedJsonVM uploadedJsonVM = new UploadedJsonVM()
//{
//    FileName = "檔案名稱",
//    ApiKey = "your_key",
//    FolderSn = 1234,
//    Action = (int)UploadedJsonAction.Create,
//    UploadedJsonDatas = [data1, data2]
//};
//await PostUploadedJsonVM(uploadedJsonVM);
//----------------------------------------------------------



//主程式(刪除)
//----------------------------------------------------------
//UploadedJsonVM uploadedJsonVM = new UploadedJsonVM()
//{
//    FileName = "20240731154038_檔案名稱.jsonstring",
//    ApiKey = "your_key",
//    FolderSn = 1234,
//    Action = (int)UploadedJsonAction.Delete,
//    UploadedJsonDatas = [] //不能漏掉這項
//};
//await PostUploadedJsonVM(uploadedJsonVM);
//----------------------------------------------------------



//主程式(文字萃取 - Mode 1: 只萃取國家)
//----------------------------------------------------------
//ExtractCountryOrTimeVM extractVM1 = new ExtractCountryOrTimeVM()
//{
//    Text = "2024年3月15日，美國總統在白宮發表演說，討論與中國的貿易關係。",
//    Mode = "1",
//    ApiKey = "your_key",
//    TimeReference = "2024-03-15 10:00:00"
//};
//await PostExtractCountryOrTime(extractVM1);
//----------------------------------------------------------



//主程式(文字萃取 - Mode 2: 只萃取時間)
//----------------------------------------------------------
ExtractCountryOrTimeVM extractVM2 = new ExtractCountryOrTimeVM()
{
    Text = "明天下午3點將舉行記者會",
    Mode = "2",
    ApiKey = "your_key",
    TimeReference = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
};
await PostExtractCountryOrTime(extractVM2);
//----------------------------------------------------------



//主程式(文字萃取 - Mode 3: 同時萃取國家和時間)
//----------------------------------------------------------
//ExtractCountryOrTimeVM extractVM3 = new ExtractCountryOrTimeVM()
//{
//    Text = "2024年3月15日，美國總統在白宮發表演說",
//    Mode = "3",
//    ApiKey = "your_key",
//    TimeReference = null // TimeReference 是可選的
//};
//await PostExtractCountryOrTime(extractVM3);
//----------------------------------------------------------




//HttpPost副程式(上傳與刪除)
async Task PostUploadedJsonVM(UploadedJsonVM uploadedJsonVM)
{
    var url = "https://gufofaq.gufolab.com/api/JsonUploadInputApi";
    var jsonInputFile = JsonConvert.SerializeObject(uploadedJsonVM);
    MultipartFormDataContent form = new MultipartFormDataContent();
    form.Add(new StringContent(jsonInputFile), "jsonInputFile");

    var response = await client.PostAsync(url, form);
    if (response.IsSuccessStatusCode)
    {
        var responseContent = await response.Content.ReadAsStringAsync();
        var jsonFormat = JsonConvert.DeserializeObject<JsonFormat>(responseContent);
        var returnJsonData = JsonConvert.DeserializeObject<ReturnJsonData>(jsonFormat.JsonData);
        Console.WriteLine(returnJsonData.FolderSn + " " + returnJsonData.FileName);
    }
    else
    {
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            var error = JsonConvert.DeserializeObject<ECustomError>(errorContent);
            Console.WriteLine($"Handled Error: {error.Message}, Code: {error.Code}");
        }
        else
        {
            Console.WriteLine($"Failed to post chatRoomVM. Status code: {response.StatusCode}");
        }
    }
}



//HttpPost副程式(文字萃取)
async Task PostExtractCountryOrTime(ExtractCountryOrTimeVM extractVM)
{
    var url = "https://gp.gufolab.com/api/JsonUploadInputApi/ExtractCountryOrTime";
    var extractCountryOrTimeVM = JsonConvert.SerializeObject(extractVM);
    MultipartFormDataContent form = new MultipartFormDataContent();
    form.Add(new StringContent(extractCountryOrTimeVM), "extractCountryOrTimeVM");

    var response = await client.PostAsync(url, form);
    if (response.IsSuccessStatusCode)
    {
        var responseContent = await response.Content.ReadAsStringAsync();
        var jsonFormat = JsonConvert.DeserializeObject<JsonFormat>(responseContent);
        var result = JsonConvert.DeserializeObject<ExtractCountryOrTimeResultVM>(jsonFormat.JsonData);

        Console.WriteLine("=== 萃取結果 ===");
        Console.WriteLine($"國家: {result.Country ?? "null"}");
        Console.WriteLine($"時間: {result.Time ?? "null"}");
    }
    else
    {
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            var error = JsonConvert.DeserializeObject<ECustomError>(errorContent);
            Console.WriteLine($"Handled Error: {error.Message}, Code: {error.Code}");
        }
        else
        {
            Console.WriteLine($"Failed to extract. Status code: {response.StatusCode}");
        }
    }
}



//資料結構
public enum UploadedJsonAction : int
{
    Create = 0,
    Delete = 2
}

public class JsonFormat
{
    public string JsonData { get; set; }
}

public class UploadedJsonData
{
    public string FieldTitle { get; set; }
    public string FieldTime { get; set; }
    public string FieldContent { get; set; }
    public string[] CusField { get; set; }
}

public class UploadedJsonVM
{
    public string ApiKey { get; set; }
    public string FileName { get; set; }
    public int FolderSn { get; set; }
    public int Action { get; set; }
    public UploadedJsonData[] UploadedJsonDatas { get; set; }
}

public class ReturnJsonData
{
    public string FileName { get; set; }
    public int FolderSn { get; set; }
}

public class ExtractCountryOrTimeVM
{
    public string Text { get; set; }
    public string Mode { get; set; }
    public string ApiKey { get; set; }
    public string? TimeReference { get; set; }
}

public class ExtractCountryOrTimeResultVM
{
    public string? Country { get; set; }
    public string? Time { get; set; }
}

public class ECustomError
{
    public string Message { get; set; }
    public string Code { get; set; }
}

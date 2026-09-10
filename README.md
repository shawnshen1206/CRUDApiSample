# GufoFAQ 資料 API 使用說明

以 `X-API-Key` 認證的**程式化整合介面**，給客戶自己的後端程式呼叫。兩組端點：

| 組 | 路徑 | 做什麼 |
|---|---|---|
| 資料 API | `/api/v1/*` | 資料集與其中資料的建立、查詢、更新、刪除 |
| 萃取 API | `/api/extract/country-or-time` | 從一段文字萃取國家／時間 |

兩組共用同一把金鑰、同一組共同閘門與錯誤信封，但**功能開通是分開的**（見〈共同閘門〉）。

---

## 一、開始之前

### 基底網址

```
https://<你的 GufoFAQ 網域>
```

由我們提供。本文件之後一律以 `$BASE` 代表它。

### 取得 API 金鑰

租戶管理者在產品介面的〈萃取 API 金鑰〉頁按下產生即可拿到，形如 `sk_` 開頭的字串。

- **只在產生的那一刻看得到**（系統只留雜湊，之後查詢一律回 `null` ＋ 後四碼提示）。抄下來收好。
- **一個租戶只有一把**。再按一次是**輪替**：新的立刻生效，舊的立刻失效。
- 這把金鑰代表整個租戶，沒有細分權限。請放在你後端的祕密管理裡，不要放進前端或版控。

### 共同標頭

| 標頭 | 值 | 用在 |
|---|---|---|
| `X-API-Key` | 你的金鑰 | 全部端點 |
| `Content-Type` | `application/json` | 送 JSON body 的端點 |
| `Content-Type` | `multipart/form-data` | 只有 `POST /api/v1/datasets/{id}/files` |

**請求與回應都是純 JSON**：body 直接就是資料本身，不包在字串化的信封裡、也不用表單欄位夾帶
JSON 字串。

### 共同閘門

每一個請求依序過四道，任一道不通就不會進到業務邏輯：

| 順序 | 檢查 | 不通時 |
|---|---|---|
| 1 | 來源 IP 的請求速率 | `429` ＋ `Retry-After` |
| 2 | `X-API-Key` 有效 | `401`，`detail` 為 `invalid or missing X-API-Key` |
| 3 | 租戶使用期 | `403`，`detail.code` 為 `subscription_expired`／`account_frozen`／`disclaimer_required` |
| 4 | 租戶功能開通 | `403`，`detail` 指名缺哪一項功能（資料 API 要 `data`、萃取 API 要 `extract`） |
| 5 | 該租戶的請求速率（預設每分鐘 120 次） | `429` ＋ `Retry-After` |

第 4 道是**分開的**：平台把某租戶的資料功能關掉時，萃取 API 不受影響，反之亦然。

### 錯誤信封

失敗一律是 JSON，`detail` 有兩種形狀：

```json
{ "detail": "資料集名稱不可為空" }                                  // 只有一句話
{ "detail": { "code": "bad_mapping", "detail": "對照不是合法的 JSON：…" } }  // 帶機器可判的代碼
```

**請用 HTTP 狀態碼 ＋ `detail.code` 分流，不要比對句子。** 句子的語言由**租戶在產品介面設定的
介面語言**決定（`zh-TW`／`en`），呼叫端送 `Accept-Language` 不會改變它——同一個租戶把介面語言
換成英文，你的字串比對就整批失效。

回應標頭一律帶 `X-Correlation-ID`；跨服務故障（`502`／`503`）的訊息裡也會有同一個編號，
回報問題時附上它。

---

## 二、資料集

### 2.1 建立資料集

```
POST $BASE/api/v1/datasets
```

```json
{ "name": "產品手冊", "description": "給客服機器人用的產品說明" }
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `name` | 是 | 資料集名稱。前後空白會被去掉；去掉後為空即 `400`。同租戶內不可重名 |
| `description` | 否 | 說明。**這一欄會影響問答**：系統靠它判斷一個問題該找哪一個資料集，寫清楚這個資料集裝什麼 |

**回應 `201`**

```json
{ "id": 42, "index_name": "t17_9f3c2a10", "name": "產品手冊" }
```

| 欄位 | 說明 |
|---|---|
| `id` | 之後所有操作都用它 |
| `index_name` | 系統內部的索引名稱，供你對照日誌用；不需要拿它做任何事 |

### 2.2 列出資料集

```
GET $BASE/api/v1/datasets
```

**回應 `200`**：陣列，依 `id` 升冪。

```json
[ { "id": 42, "name": "產品手冊", "index_name": "t17_9f3c2a10", "group_id": null, "file_count": 3 } ]
```

| 欄位 | 說明 |
|---|---|
| `group_id` | 這個資料集掛在哪個群組底下。**用 API 建立的一律是 `null`**（租戶層級）；在產品介面建立的會有值 |
| `file_count` | 目前有幾份資料（一筆記錄或一個檔案各算一份） |

清單含**整個租戶**的資料集，不只 API 建立的那些。

### 2.3 讀單一資料集

```
GET $BASE/api/v1/datasets/{id}
```

`200` 回與 2.2 每一筆相同的形狀。不存在或不屬於你的租戶一律 `404`（不分辨這兩者）。

### 2.4 改名

```
PATCH $BASE/api/v1/datasets/{id}
```

```json
{ "name": "產品手冊（2 代）" }
```

`200` 回與 2.2 相同的形狀。`index_name` **不會**跟著變。空名 `400`、與別的資料集同名 `409`
（改成自己原本的名字不算重名）。

### 2.5 刪除資料集

```
DELETE $BASE/api/v1/datasets/{id}
```

**回應 `200`**

```json
{ "deleted": 42 }
```

連帶把索引與其中所有資料刪掉，**不可復原**。刪除同時會把它從各檢索設定檔的清單裡移除。

刪除是「先本地、後索引、成功才落地」：索引那一側失敗時整個操作回捲並回 `502`，資料集**仍在**
——不會留下一個「畫面上沒有、索引裡還在」的半套狀態。

---

## 三、資料集裡的資料（記錄）

### 3.1 上傳結構化記錄

```
POST $BASE/api/v1/datasets/{id}/records
```

```json
{
  "records": [
    { "title": "如何退貨", "content": "七日內可退貨…", "note1": "客服組", "date": "2026-03-01" },
    { "content": "只有內容也可以" }
  ],
  "convert_html": true
}
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `records` | 是 | 每一筆是「欄位槽 → 值」的物件，槽名見〈五、欄位槽〉。**`content` 必填**；可另帶保留鍵 `external_key`（見〈六〉）。一次最多 200000 筆，但整包 JSON 也受 4 MB 限制（見〈九〉） |
| `convert_html` | 否（預設 `true`） | 內容是 HTML 時轉成 Markdown 再存。已經是純文字或 Markdown 就設 `false` |

**回應 `200`**

```json
{
  "imported": 2,
  "inserted": 2,
  "updated": 0,
  "failed": 0,
  "results": [
    { "ok": true, "doc": 901, "updated": false, "task_id": "…", "sync_state": "pending" },
    { "ok": false, "error": "content 欄位不可為空", "code": "content_required" }
  ],
  "health_scan": { "ok": true }
}
```

| 欄位 | 說明 |
|---|---|
| `imported` | 成功筆數（＝`inserted` ＋ `updated`） |
| `inserted`／`updated`／`failed` | 三者互斥、加總等於送出的筆數。`updated` ＝被 `external_key` 取代掉舊版的那些 |
| `results` | **與送出的順序一一對應**，逐筆各自成敗 |
| `health_scan` | 匯入後自動跑一次資料健檢的結果。`ok:false` 只代表健檢本身沒跑完，**匯入仍然成功** |

**逐筆成敗**是這一支的核心語意：某一筆缺 `content`、槽名打錯、儲存空間不足，只有那一筆失敗，
前面已經成功的不會被回捲，後面的照樣繼續。所以**一定要逐筆看 `results`，不要只看 HTTP 200**。

成功條目：

| 欄位 | 說明 |
|---|---|
| `doc` | 這一筆在系統裡的編號，刪除時要用（見 3.3） |
| `updated` | `true` ＝取代了同 `external_key` 的舊版 |
| `task_id` | 索引同步的工作編號 |
| `sync_state` | `pending`／`succeeded`／`failed`——**送進來不等於已經可被檢索**，要確認就用 3.2 覆核 |
| `replaced_file_id` | 被取代掉的舊版編號（只有取代時才有） |
| `dropped_links`／`unprocessable_tables`／`structure` 等 | 內容在轉換時被改寫了什麼。**有值就代表存進去的與你送的不完全一樣**，值得看一眼 |

失敗條目：`{ "ok": false, "error": "<可讀訊息>", "code": "<代碼>" }`。少數未預期的失敗只有
`error`（一句帶關聯編號的話）、沒有 `code`。

### 3.2 列出資料集裡的資料

```
GET $BASE/api/v1/datasets/{id}/records
```

**回應 `200`**

```json
{
  "dataset_id": 42,
  "files": [
    { "id": 901, "filename": "如何退貨", "file_type": "api", "connector_type": "excel",
      "size_bytes": 1234, "uploaded_at": "2026-03-01T02:03:04Z", "record_count": 1 }
  ]
}
```

`files` 依上傳時間**降冪**。`record_count` 是那一份實際進到索引的筆數——結構化記錄是 1，
一個帶對照的 xlsx 是它的資料列數。

### 3.3 刪除一份資料

```
DELETE $BASE/api/v1/datasets/{id}/records/{file_id}
```

`file_id` 取自 3.1 的 `doc` 或 3.2 的 `files[].id`。

**回應 `200`**：`{ "deleted": 901 }`

檔案不屬於這個資料集、或這個資料集不屬於你的租戶，一律 `404`。索引那一側刪除失敗回 `502`
且該份**仍在**（同 2.5 的理由）。

---

## 四、上傳檔案

```
POST $BASE/api/v1/datasets/{id}/files
Content-Type: multipart/form-data
```

| 表單欄位 | 型別 | 說明 |
|---|---|---|
| `files` | 檔案（可多個） | `.pdf`／`.docx`／`.xlsx`。一次最多 200 個 |
| `mapping` | JSON 字串 | **只對 xlsx 有意義**：`{"<槽名>": ["<原始欄名>", …]}`。帶了就一列一份資料；不帶就整個檔當一份 |
| `convert_html` | `true`／`false`（預設 `true`） | 同 3.1 |
| `header_rows` | 整數（預設 `1`） | 表頭佔幾列。>1 時 `mapping` 的鍵要寫串接後的欄名（`上層／下層`） |
| `unpivot` | `true`／`false`（預設 `false`） | 交叉表轉長格式。開啟時 `mapping` 只有「列標題」「欄標題」「值」三個鍵 |
| `unpivot_row_label_cols` | 整數（可多個，預設 `1`） | 交叉表的哪幾欄是列標題（從 1 起算） |
| `external_keys` | JSON 字串 | `{"<原始檔名>": "<external_key>"}`，**逐檔一個鍵**（見〈六〉）。帶對照的 xlsx 與 pdf／docx 才支援 |

`mapping` 的 `content` 槽必填。**同一批多個 xlsx 共用同一份 `mapping`**（欄名要一致）；
結構不同的請分批各帶自己的對照。

**回應 `200`**

```json
{
  "results": [
    { "filename": "手冊.pdf", "ok": true, "file_id": 902, "updated": false, "task_id": "…", "sync_state": "pending" },
    { "filename": "報表.xlsx", "ok": true, "file_id": 903, "connector": "excel", "data_count": 128 },
    { "filename": "忽略.txt", "ok": false, "error": "僅支援 .pdf / .docx / .xlsx" }
  ],
  "inserted": 2, "updated": 0, "failed": 1,
  "health_scan": { "ok": true }
}
```

**逐檔各自成敗**，語意與 3.1 逐筆完全相同。`connector` ＝ `excel`（一列一份）或 `folder`
（整檔一份）；`data_count` 是那個 xlsx 實際匯進去的資料列數。

三種檔各走哪條路：

| 檔 | 帶 `mapping` | 結果 |
|---|---|---|
| `.pdf`／`.docx` | 不影響 | 抽出內文當 `content`，整檔一份資料 |
| `.xlsx` | 有 | 欄對到槽，**一列一份資料**（與產品介面的匯入精靈同一條路） |
| `.xlsx` | 無 | 整檔一份資料（不做欄位對照，也不支援 `external_keys`） |

`mapping` 不是合法 JSON 時**整批擋下**回 `400`（`code` 為 `bad_mapping`）——那是呼叫端的用法
錯誤，不是某一個檔的問題。

---

## 五、欄位槽

記錄與 `mapping` 能用的槽名固定是這些：

| 槽 | 用途 | 備註 |
|---|---|---|
| `content` | 內容 | **必填**，問答檢索的主體 |
| `title` | 標題 | 也是資料的顯示名稱 |
| `source` | 來源 | 隨資料存進索引，不能拿來過濾 |
| `date` | 日期 | 內建日期篩選欄，值要 ISO 日期（`2026-03-01`）或留白 |
| `category` | 分類 | 內建文字篩選欄 |
| `number` | 數值 | 內建數值篩選欄 |
| `flag` | 標記 | 內建布林篩選欄 |
| `tags` | 標籤 | 內建多值篩選欄，值域是**整數**；代碼型的值請放 `note1`~`note10` |
| `privilege` | 權限 | 「誰可以看這份資料」的名單。**不會出現在任何對外問答回應裡** |
| `note1`~`note10` | 自訂文字欄 | 租戶可在產品介面替它們命名、掛標籤維度並拿來過濾檢索 |
| `date2`／`date3` | 自訂日期欄 | 值要 ISO 日期或留白 |
| `internal_note` | 內部註記 | **不進索引、不進問答**。只給你自己記「這筆誰維護」這類的話 |

三個常踩的點：

- **日期槽的值一定要 ISO 或留白**，否則那一筆 `400`。送 `2026/3/1` 這種格式會被擋。
- **租戶宣告了受控詞彙（標籤維度）的槽**，值必須是字典裡真的有的代碼，否則那一筆 `400`
  （`code` 為 `unknown_tag_code`）。
- **槽名打錯是失敗，不是忽略**（`code` 為 `unknown_slot`，訊息會指名是哪幾個鍵）。

---

## 六、`external_key`：讓同一份資料可以更新

不帶 `external_key` 時，每一次上傳都是**新增一份**——同一份資料送兩次就有兩份，問答會同時
命中兩份。

帶了 `external_key`（你自己系統裡那筆資料的主鍵）時：同租戶同資料集裡**同鍵的舊版會被取代**
（舊的移除、新的建立，在同一個交易裡完成）。回應的該筆會有 `updated: true` 與
`replaced_file_id`。

- 記錄：放在 records 每一筆裡，`{"external_key": "SKU-001", "content": "…"}`。它**不是欄位槽**
  ——不會進索引、不會被檢索到、不會出現在答案裡。
- 檔案：走表單欄位 `external_keys`，`{"手冊.pdf": "MANUAL-2026"}`。
- **同一批裡兩筆共用同一個鍵會整批 `400`**（否則後者會 purge 前者而兩筆都回成功——靜默的資料
  遺失）。
- 未帶 `mapping` 的 xlsx **不支援**這個鍵（那條路整檔共用一條管道），該檔會記
  `external_key_unsupported`。

---

## 七、萃取 API

```
POST $BASE/api/extract/country-or-time
```

```json
{ "text": "上個月我去了東京旅遊", "mode": 3, "time_reference": "2026-03-15 10:00:00" }
```

| 欄位 | 必填 | 說明 |
|---|---|---|
| `text` | 是 | 要分析的文字。**前後空白去掉後**長度必須在 2–5000 之間 |
| `mode` | 是 | `1` 只萃國家／`2` 只萃時間／`3` 兩者。**是整數，不是字串** |
| `time_reference` | 否 | 這段文字的發生時間，幫助判斷「上個月」「明天」這類相對時間。最長 200 字 |

**回應 `200`**

```json
{ "country": "日本", "time": "2026-02" }
```

| `mode` | `country` | `time` |
|---|---|---|
| `1` | 萃取結果（萃不到是「無」） | 恆為 `null` |
| `2` | 恆為 `null` | 萃取結果（萃不到是「無」） |
| `3` | 萃取結果 | 萃取結果 |

**「萃不到」與「這個模式不回這一欄」是兩件事**：前者是字串「無」，後者是 `null`。

每一次成功呼叫扣一次萃取額度。額度用完是 `429`（`code` 為 `extraction_exceeded`），
而且**扣量在檢查之後**——被擋下的那一發不會扣。

---

## 八、錯誤一覽

### 共同（每一支都可能）

| HTTP | `detail.code` | 意思 | 該怎麼做 |
|---|---|---|---|
| `401` | （只有句子） | 金鑰缺漏、錯誤或已被輪替 | 換成目前有效的金鑰 |
| `403` | `disclaimer_required` | 租戶還沒接受免責聲明 | 請租戶管理者登入產品介面完成 |
| `403` | `subscription_expired` | 使用期已到 | 聯絡我們 |
| `403` | `account_frozen` | 帳號被凍結 | 聯絡我們 |
| `403` | （句子指名功能） | 該功能未開通（`data`／`extract`） | 聯絡我們 |
| `429` | `rate_limited` | 每分鐘請求數超限 | 照 `Retry-After` 等待後重試 |
| `413` | `upload.too_large` 類 | 單檔或整包 body 超過上限 | 分批送 |
| `422` | （欄位錯誤清單） | body 形狀不合（缺必填、型別錯、多送未知欄位） | 照訊息修正請求 |
| `502` | （一句帶關聯編號） | 跨服務故障 | 附關聯編號回報；不要無限重試 |
| `503` | （一句話） | 上游暫時不可用 | 照 `Retry-After` 重試 |

### 資料 API

| HTTP | `code` | 意思 |
|---|---|---|
| `400` | （句子） | 資料集名稱為空 |
| `409` | （句子） | 資料集名稱重複 |
| `404` | （句子） | 資料集或檔案不存在／不屬於你的租戶（兩者不分辨） |
| `400` | `bad_mapping` | `mapping` 不是合法 JSON |
| `400` | `bad_external_key` | `external_key` 不是字串 |
| `400` | （句子） | 同一批出現重複的 `external_key` |
| `409` | `use_qa_import` | 目標是 QA 集，一般匯入不得寫進去 |
| `409` | `folder_pipeline_not_recorded` | 那份資料的管道資訊不全，拒絕刪除（避免留下孤兒） |
| `400` | `too_many_import_files` | 一次超過 200 個檔 |

**逐筆／逐檔條目**（`results[]` 裡的 `code`，HTTP 仍是 200）：

| `code` | 意思 |
|---|---|
| `content_required` | 那一筆沒有 `content`（或只有空白） |
| `unknown_slot` | 槽名不存在（訊息會指名） |
| `unknown_tag_code` | 標籤代碼不在該維度的字典裡 |
| `unknown_column` | `mapping` 指到檔案裡沒有的欄名 |
| `storage_exceeded` | 儲存空間不足（條目帶 `used_mb`／`limit_mb`） |
| `content_empty_after_conversion` | HTML 轉換後內容變成空的，沒有落地 |
| `external_key_unsupported` | 這條路不支援 `external_keys`（未帶 `mapping` 的 xlsx） |
| （無 `code`，只有 `error`） | 未預期的失敗，訊息帶關聯編號 |

### 萃取 API

| HTTP | `code` | 意思 |
|---|---|---|
| `400` | （句子） | `text` 長度不在 2–5000、或 `mode` 不是 1/2/3、或 `time_reference` 過長 |
| `429` | `extraction_exceeded` | 萃取額度用完 |
| `503` | （句子） | 服務端未設定萃取所需的模型金鑰 |

---

## 九、界線

以下是**預設值**，部署可調整；超過時的回應會帶當下實際生效的值（例如 `413` 的訊息、
`storage_exceeded` 條目的 `limit_mb`）。租戶管理者在產品介面看得到自己這一份。

| 項目 | 預設 |
|---|---|
| 每租戶每分鐘請求數（`/api/v1/*` 與 `/extract/*` 共用一顆閾值，來源 IP 另有同值的一道） | 120 |
| JSON body 大小 | 4 MB |
| 單檔大小 | 50 MB |
| 一次請求的檔案總量 | 200 MB |
| 一次請求的檔案數 | 200 |
| 一次 `records` 的筆數 | 200000（實務上先撞到 4 MB 的 body 上限） |
| 萃取 `text` 長度 | 2–5000 |
| 萃取 `time_reference` 長度 | 200 |
| 儲存空間、問答與萃取額度 | 逐租戶設定 |

---

## 十、從舊版 API 遷移

舊版（`/api/JsonUploadInputApi`）與現行介面的對應：

| 舊版 | 現行 |
|---|---|
| `multipart/form-data` ＋ 表單欄位夾一段 JSON 字串 | **直接送 JSON body**（只有上傳檔案那一支是 multipart） |
| body 裡的 `ApiKey` 欄位 | `X-API-Key` 標頭 |
| `JsonFormat` 信封（`{Error, Message, JsonData}`，`JsonData` 還要再解一次字串） | **回應就是資料本身**，不用二次解析 |
| `Action: 0`（新增）／`Action: 2`（刪除） | 各自獨立的端點與 HTTP 動詞（`POST` 記錄／`DELETE` 記錄） |
| `FolderSn`（資料集編號，要人去網址列抄） | `dataset_id`，由 `POST /api/v1/datasets` 或 `GET /api/v1/datasets` 取得 |
| `FileName`（刪除時要帶回「時間＋檔名」那串） | `file_id`（整數），上傳回應與列表都給得到 |
| 固定四欄 `FieldTitle`／`FieldTime`／`FieldContent`／`CusField[3]` | **22 個具名欄位槽**（見〈五〉），`CusField` 三格對應 `note1`~`note3` |
| 數字錯誤碼（`3001`／`4001`／`4005`…） | HTTP 狀態碼 ＋ `detail.code` 字串（見〈八〉） |
| 整批成敗 | **逐筆／逐檔各自成敗**，`results` 逐項回報 |
| 沒有「更新」的概念，重送就多一份 | `external_key` 取代（見〈六〉） |
| `mode` 是字串 `"1"`／`"2"`／`"3"` | `mode` 是整數 `1`／`2`／`3` |

新增的能力：資料集本身的 CRUD、列出資料、上傳 pdf／docx／xlsx 真檔案、xlsx 欄位對照與交叉表
轉換、索引同步狀態（`sync_state`／`task_id`）、匯入後自動健檢。

---

## 範例程式

`Program.cs` 是一支可直接跑的 .NET 主控台程式，逐段示範上面每一支端點。

```bash
dotnet run -- <指令>
```

| 指令 | 做什麼 |
|---|---|
| `datasets` | 建立 → 列出 → 讀單筆 → 改名 |
| `records` | 上傳兩筆記錄（其中一筆帶 `external_key`）→ 列出 → 再送同一個 key 演示取代 |
| `files` | 上傳一個帶欄位對照的 xlsx（程式自己產一個範例檔） |
| `delete` | 刪掉範例資料集（連帶其中所有資料） |
| `extract` | 三種 `mode` 各跑一次 |
| `all` | 依序跑完上面全部（最後不刪，方便你去產品介面看結果） |

跑之前先改檔案最上面兩個常數：

```csharp
const string BaseUrl = "https://<你的 GufoFAQ 網域>";
const string ApiKey  = "sk_你的金鑰";
```

或用環境變數 `GUFOFAQ_BASE_URL`／`GUFOFAQ_API_KEY`（優先於常數）。

# MaidHome Portal 传输协议（v1）

本文档面向 **Minecraft 模组侧（Java）的开发者**，描述 Mod 与 Unity 游戏《MaidHome》之间
通过 Portal 场景传输数据用的线协议。

文档里带 C# 代码的地方是 **Unity 服务端的真实实现**，字段名、字节序、时序都以它为准。
Java 侧照着实现即可，不确定的地方对着代码抄。

- 服务端：Unity 游戏（Portal 场景），TCP 监听
- 客户端：Minecraft 模组
- 协议版本：`1`
- 默认端口：`7411`（Unity 侧 Inspector 上可改，最终以双方约定为准）
- 编码：所有文本一律 UTF-8，无 BOM

---

## 1. 拓扑与并发

```
Minecraft 模组  ──TCP──▶  Unity 服务端（Portal 场景）
   （客户端）                  （只听一个）
```

- Unity 监听 `IPAddress.Any`，本机测试连 `127.0.0.1` 即可，跨机器连 PC 的局域网 IP。
- **同一时刻只服务一个客户端**。已经有连接时，新连接会被服务端直接关闭（不发任何数据）。
- 一次只跑**一个传输任务**。上一个没结束就发 `upload.begin`，会收到 `error`。
- 连接断开时，没传完的上传会被丢弃（暂存目录删掉），正在发送的女仆**不会**从存档删除。

---

## 2. 帧格式

每一条消息是一帧，结构固定：

```
┌────────────────┬──────────────────────┬────────────────────────┐
│ 4 字节          │ header JSON          │ body                   │
│ 大端 uint32     │ UTF-8，长度如上       │ 原始字节，长度见 header │
└────────────────┴──────────────────────┴────────────────────────┘
```

- 第 1 个字段是 **header 的长度**（不是整帧长度），**大端序（big-endian）**。
- header 必须是一个 JSON object，必须有 `op` 字段。
- body 长度写在 header 的 `body` 字段里；没有该字段就是 0 字节。
- header 上限 256 KB，body 上限 256 MB（超了服务端直接断连接）。

服务端的编解码实现（C#，`Assets/Scripts/Interop/Portal/PortalFrame.cs`）：

```csharp
public static byte[] Encode(string headerJson, byte[] body)
{
    byte[] header = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(headerJson) ? "{}" : headerJson);
    byte[] payload = body != null ? body : EmptyBytes;
    byte[] frame = new byte[4 + header.Length + payload.Length];
    WriteLength(frame, 0, header.Length);          // 大端写 header 长度
    Buffer.BlockCopy(header, 0, frame, 4, header.Length);
    Buffer.BlockCopy(payload, 0, frame, 4 + header.Length, payload.Length);
    return frame;
}

static void WriteLength(byte[] buffer, int offset, int value)
{
    buffer[offset]     = (byte)(value >> 24);
    buffer[offset + 1] = (byte)(value >> 16);
    buffer[offset + 2] = (byte)(value >> 8);
    buffer[offset + 3] = (byte)value;
}
```

Java 侧对应写法：

```java
static void writeFrame(OutputStream out, byte[] headerJson, byte[] body) throws IOException {
    ByteBuffer length = ByteBuffer.allocate(4).order(ByteOrder.BIG_ENDIAN);
    length.putInt(headerJson.length);
    out.write(length.array());
    out.write(headerJson);
    if (body != null) out.write(body);
}
```

### 2.1 应答规则

- **每个请求恰好收到一个应答**：要么是 `<op>.ok`（少数是 `pong`），要么是 `error`。
- `send.begin` / `send.file` / `send.end` 是**服务端主动推的**，不是应答，随时可能出现。
- 客户端最好写成一个「读一帧 → 按 op 分派」的循环，不要假设读完一个应答就没别的了。

出错应答的格式固定：

```json
{ "op": "error", "request": "upload.begin", "message": "kind 不合法: xxx" }
```

`request` 是出错的那个请求的 op。**`message` 是给人看的中文**，别拿来判断分支。

---

## 3. 消息一览

| 方向 | op | body | 说明 |
|---|---|---|---|
| C→S | `hello` | 0 | 握手 |
| S→C | `hello.ok` | 0 | 握手应答 |
| C→S | `ping` | 0 | 心跳 |
| S→C | `pong` | 0 | 心跳应答 |
| C→S | `list.request` | 0 | 查服务端现有的数据 |
| S→C | `list.ok` | 0 | 列表，条目在 header 里 |
| C→S | `upload.begin` | 0 | 开始上传 |
| S→C | `upload.begin.ok` | 0 | 分配 upload_id，回带最终 id |
| C→S | `upload.file` | N | 一个文件的原始字节 |
| S→C | `upload.file.ok` | 0 | 该文件落盘成功 |
| C→S | `upload.commit` | 0 | 提交，正式生效 |
| S→C | `upload.commit.ok` | 0 | 提交成功 |
| C→S | `upload.abort` | 0 | 放弃这次上传 |
| S→C | `upload.abort.ok` | 0 | 已丢弃 |
| S→C | `send.begin` | 0 | 服务端要发一只女仆过来 |
| S→C | `send.file` | N | 女仆文件字节 |
| S→C | `send.end` | 0 | 发完了，等客户端确认 |
| C→S | `send.ack` | 0 | **客户端确认收到，服务端据此删存档** |

`kind` 取值固定三个：`maid` / `house` / `sound`。

---

## 4. 消息详解

### 4.1 hello

请求：

```json
{ "op": "hello", "version": 1, "client": "touhou_little_maid/1.5.3" }
```

- `version`：协议版本，现在是 `1`。版本不一致服务端不会拒绝，只在 UI 上提示，方便先联调。
- `client`：客户端自报的名字，会显示在 Unity 面板上。随便填，能认出是谁就行。

应答：

```json
{ "op": "hello.ok", "version": 1, "server": "MaidHome", "kinds": ["maid", "house", "sound"] }
```

不握手也能传数据，但面板上看不到客户端名字。

### 4.2 list.request

```json
{ "op": "list.request", "kind": "maid" }
```

应答（条目放在 header 里，不占 body）：

```json
{
  "op": "list.ok",
  "kind": "maid",
  "items": [
    { "id": "81cfd989-34c0-4cc7-ac51-bd1e7f005c0a", "name": "酒狐", "bytes": 279666, "sendable": true }
  ]
}
```

- `kind` = `maid` 时列出的是**背包里、可以被服务端发回来的女仆**（已放置在世界里的不列）。
- `kind` = `house` 时列 `saves/house` 下的房子；`kind` = `sound` 时列 `saves/sounds` 下的音效包。
- `sendable` 只对女仆有意义：为 `false` 表示缺 `maid_data.maid`，发不了。
- 音效包的 `name` 等于 `id`。

### 4.3 上传流程

#### ① upload.begin

```json
{
  "op": "upload.begin",
  "kind": "maid",
  "id": "81cfd989-34c0-4cc7-ac51-bd1e7f005c0a",
  "name": "酒狐",
  "file_count": 5,
  "total_bytes": 279666
}
```

| 字段 | 必填 | 说明 |
|---|---|---|
| `kind` | 是 | `maid` / `house` / `sound` |
| `id` | 否 | 目标文件夹名。**空着服务端会随机生成一个 uuid**（女仆推荐这样）并用应答回带 |
| `name` | 否 | 显示名。留空的话服务端会自己从传上来的 `maid.json` / `house.json` 里读 |
| `file_count` | 否 | 文件个数，仅用于展示 |
| `total_bytes` | 否 | 所有文件字节数之和，**填了 Unity 面板才有进度条** |

应答：

```json
{ "op": "upload.begin.ok", "upload_id": "9a1f...", "kind": "maid", "id": "81cfd989-..." }
```

`id` 是**最终生效的 id**，客户端要用它来记自己传了什么。

#### ② upload.file（每个文件一帧）

```json
{ "op": "upload.file", "path": "winefox.json", "sha256": "3f2a...", "body": 82655 }
```

- `path` 是**相对上传根目录**的路径，用 `/` 分隔，可以带子目录（房子里有 `textures/minecraft/block/oak_log.png`）。
- **不允许** `..`、绝对路径、盘符、`:`；踩到会收到 `error` 并且这次上传作废。
- `sha256` 可选。填了服务端会校验，不匹配就报错。
- 紧跟在这个 header 后面就是 `body` 字节的原始文件内容。

应答：`{ "op": "upload.file.ok", "path": "winefox.json", "bytes": 82655 }`

**建议一个文件传完等一个 ok 再传下一个**，这样出错定位清楚，也不用自己处理窗口拥塞。

#### ③ upload.commit

```json
{ "op": "upload.commit" }
```

服务端收到后把暂存目录**原子地**顶掉正式目录（同 id 就覆盖），然后应答：

```json
{ "op": "upload.commit.ok", "kind": "maid", "id": "81cfd989-...", "name": "酒狐" }
```

到这里 Unity 侧才会触发"上传成功"，`name` 就是文档里说的"可用名称"。

#### ④ upload.abort（可选）

```json
{ "op": "upload.abort" }
```

丢弃这次上传的暂存文件，应答 `upload.abort.ok`。中途放弃、或者用户取消时调用。

### 4.4 服务端发女仆回客户端

整个过程是**服务端主动发起**的，客户端只需要被动接收 + 最后确认一次。

#### ① send.begin

```json
{
  "op": "send.begin",
  "kind": "maid",
  "id": "81cfd989-34c0-4cc7-ac51-bd1e7f005c0a",
  "name": "酒狐",
  "total_bytes": 153,
  "data": {}
}
```

- `data` 是**随包的一段自定义 JSON**，现在固定是空对象 `{}`，以后 Unity 侧往里塞额外信息。
  客户端原样存下来即可，不要因为不认识字段就报错。
- **只发 `maid_data.maid` 一个文件**，服务端不会把模型贴图动画发过来。

#### ② send.file

```json
{ "op": "send.file", "path": "maid_data.maid", "sha256": "ab12...", "body": 153 }
```

后面跟 `body` 字节的文件内容。校验 `sha256`，不匹配就**不要**回 ok。

#### ③ send.end

```json
{ "op": "send.end", "id": "81cfd989-...", "total_bytes": 153 }
```

#### ④ 客户端回 send.ack

```json
{ "op": "send.ack", "id": "81cfd989-...", "ok": true, "message": "" }
```

- **`ok: true` 是唯一的删除条件。** 服务端收到它之后才会把这只女仆从存档里删掉
  （`saves/maid/<id>/` + 缓存 + `slot0.json` 里的记录）。
- 写盘失败、sha256 对不上、用户不想收，就回 `{"op":"send.ack","id":"...","ok":false,"message":"原因"}`。
  服务端**不会删**，女仆留在 Unity 那边，玩家可以再发一次。
- `send.ack` 没有应答，回完就结束了。
- 如果连接在回 ack 之前断了，服务端同样不会删。

---

## 5. 时序

### 上传一只女仆

```
客户端                                    服务端
  | hello ------------------------------> |
  | <------------------------ hello.ok    |
  | upload.begin {kind:maid, id:"", ...}> |   建 tmp/portal/<upload_id>/
  | <----------- upload.begin.ok {id}     |
  | upload.file {path:maid.json} + bytes> |   写暂存文件
  | <------------------------ upload.file.ok
  | ...（每个文件一次）...                  |
  | upload.commit ----------------------> |   暂存 -> saves/maid/<id>/
  | <----------- upload.commit.ok {name}  |   （Unity 触发 Succeeded 事件）
```

### 服务端发一只女仆

```
客户端                                    服务端
  | <-- send.begin {id, name, data, ...}   |   面板上点了「发送」
  | <-- send.file {path:maid_data.maid} + bytes
  | <-- send.end {id}                      |
  | 写盘 + 校验 sha256                      |
  | send.ack {id, ok:true} -------------> |   删存档 + 缓存 + slot0 记录
```

---

## 6. 服务端写死的语义

客户端实现时可以直接依赖这几条：

1. **上传先写暂存，commit 才生效**
   暂存目录在 `persistentDataPath/tmp/portal/<upload_id>/`。没 commit 就断开，暂存会被删掉，
   正式目录**一个字节都没动过**。

2. **同 id 覆盖是原子的**
   覆盖时旧目录先改名成 `<id>.old`，新的搬进去成功后才删 `.old`；中途失败会把 `.old` 改回来。

3. **女仆只有在背包里才能被发送**
   `slot0.json` 里 `in_bag` 为 `false`（已经放在房子里）的女仆，Unity 面板上发送按钮是灰的，
   服务端也会拒绝。

4. **删除发生在 ack 之后**
   顺序永远是：发完 → 客户端 ack ok → 服务端删。没有 ack 就没有删除。

5. **一次一个传输**
   同一时间只允许一个上传或一个发送。发 `upload.begin` 时如果服务端正在发女仆，会收到 `error`。

6. **进度不在线协议里**
   进度是 Unity 面板自己按字节数算的。客户端想显示进度自己按已发字节算就行。

---

## 7. 磁盘结构

Unity 的存档根目录（Windows）：
`%USERPROFILE%\Documents\MaidHome\saves\`

Android 上暂时退回应用的私有目录 `persistentDataPath/saves/`（还没做公共目录的 Java 桥）。

### 7.1 女仆 saves/maid/<uuid>/

上传时**整个文件夹的内容都要传**（`path` 用相对路径）：

```
saves/maid/<uuid>/
|- maid.json                 女仆元数据
|- maid_data.maid            MC 侧序列化的女仆本体（二进制，Unity 不解析，只存和转发）
|- <模型>.json               基岩版几何
|- <贴图>.png
|- <动画>.json
```

`maid.json` 的字段（Unity 侧读写的就是这些）：

```json
{
  "model": "winefox.json",
  "texture": "winefox.png",
  "anim": "winefox.animation.json",
  "name": "酒狐",
  "level": 10,
  "owner_name": "5112151111121",
  "owner_uuid": "91bd580f-5f17-4e30-872f-2e480dd9a220",
  "scale": 0.65,
  "simple_bedrock_model": false,
  "sound": "touhou_little_maid",
  "sound_freq": 1.0
}
```

| 字段 | 说明 |
|---|---|
| `model` / `texture` / `anim` | **相对本文件夹**的文件名 |
| `scale` | 模型整体缩放倍率，缺省 1 |
| `simple_bedrock_model` | `true` 表示是 SimpleBedrockModel，Unity 用程序化走路动画 |
| `sound` | 音效包 id，对应 `saves/sounds/<id>/` |
| `sound_freq` | 语音触发频率 0~1，缺省 1，0 = 不播 |
| `maid_data.maid` | **不在 json 里引用，靠固定文件名约定**。没有这个文件的女仆不能被发回客户端 |

> Unity 的存档编辑器会重写 `maid.json`，只会写上面这些字段。
> 如果要加新字段，提前说一声，否则编辑器保存一次会把新字段丢掉。

### 7.2 房子 saves/house/<id>/

```
saves/house/<id>/
|- house.json
|- <模型>.gltf / .glb       （以及它引用的 .bin / textures/ …）
|- house.bin / house.usda   导出器产生的中间文件，Unity 不解析，但会一起存
|- report.json
```

`house.json`：

```json
{
  "size": [7, 6, 7],
  "name": "房屋1",
  "origin": [4, 1, 4],
  "model": "house",
  "walkable": ["0000000\n...", "..."]
}
```

`walkable` 是每层一个字符串，层序 y 递增，层内行按 z 递增、字符按 x 递增；
`'1'` 表示**实体能站在这一格的底面上**，脚底高度就等于这一格的 y。
这个判断完全由 MC 侧负责（下面是不是实心、头顶够不够 1.8 格、是不是台阶桌子），
Unity 侧不重新推。

### 7.3 音效包 saves/sounds/<id>/

```
saves/sounds/<id>/maid/<分类>/<事件><序号>.ogg
```

分类四个：`ai` / `mode` / `environment` / `other`。事件 id 按
`maid.<分类>.<去序号的事件名>` 组成（`other` 分类特殊，直接是 `maid.<事件名>`），例如：

```
maid/ai/hurt1.ogg              -> maid.ai.hurt
maid/mode/idle37.ogg           -> maid.mode.idle
maid/environment/morning5.ogg  -> maid.environment.morning
maid/other/credit.ogg          -> maid.credit
```

服务端查询时有回退链（`maid.mode.attack` 找不到会退到 `maid.attack`），客户端导出时按原样放就行。

---

## 8. 服务端实现参考（C#）

分发逻辑在 `Assets/Scripts/Interop/Portal/PortalServer.cs`，核心就是这一张 switch：

```csharp
void Handle(Connection connection, JsonValue header, byte[] body)
{
    string op = header["op"].AsString("");
    switch (op)
    {
        case "hello": OnHello(connection, header); break;
        case "upload.begin": OnUploadBegin(connection, header); break;
        case "upload.file": OnUploadFile(connection, header, body); break;
        case "upload.commit": OnUploadCommit(connection); break;
        case "upload.abort": OnUploadAbort(connection); break;
        case "list.request": OnListRequest(connection, header); break;
        case "send.ack": OnSendAck(connection, header); break;
        case "ping": Send(connection, new JsonWriter("pong").ToString(), null, false); break;
        default: SendError(connection, op, "不认识的操作: " + op); break;
    }
}
```

上传落盘（`OnUploadFile` 的核心），路径校验和 sha256 校验都在这里：

```csharp
string relative = header["path"].AsString("");
if (!PortalStorage.IsSafeRelativePath(relative))          // 挡 ../ 和绝对路径
{
    session.Broken = true;
    SendError(connection, "upload.file", "非法路径: " + relative);
    return;
}

byte[] payload = body != null ? body : EmptyBytes;
string expected = header["sha256"].AsString("");
if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, Sha256(payload), StringComparison.OrdinalIgnoreCase))
{
    session.Broken = true;
    SendError(connection, "upload.file", "sha256 对不上: " + relative);
    return;
}

PortalStorage.WriteStagingFile(session.Folder, relative, payload);
Interlocked.Add(ref _transferDone, payload.Length);        // 面板进度
Send(connection, new JsonWriter("upload.file.ok")
    .Add("path", relative)
    .Add("bytes", payload.Length)
    .ToString(), null, false);
```

把女仆发出去（`SendMaidWorker` 的核心）：

```csharp
PortalPackage package = PortalStorage.BuildMaidPackage(maidId, out error);  // 只装 maid_data.maid
long total = package.TotalBytes();
Interlocked.Exchange(ref _transferTotal, total);

Send(connection, new JsonWriter("send.begin")
    .Add("kind", package.Kind.ToWire())
    .Add("id", package.Id)
    .Add("name", package.Name)
    .Add("total_bytes", total)
    .Raw("data", "{}")                     // 目前固定空对象
    .ToString(), null, false);

for (int i = 0; i < package.Files.Count; i++)
{
    string relative = package.Files[i];
    byte[] payload = File.ReadAllBytes(Path.Combine(package.Root, relative));
    Send(connection, new JsonWriter("send.file")
        .Add("path", relative)
        .Add("sha256", Sha256(payload))
        .Add("body", payload.Length)
        .ToString(), payload, true);
}

connection.SendPending = true;             // 先立标志再发 end，避免 ack 抢在前面
Send(connection, new JsonWriter("send.end")
    .Add("id", package.Id)
    .Add("total_bytes", total)
    .ToString(), null, false);
```

收到确认之后才删：

```csharp
void OnSendAck(Connection connection, JsonValue header)
{
    if (!connection.SendPending)                       // 没在等 ack 就忽略
    {
        return;
    }

    connection.SendPending = false;
    bool ok = header["ok"].AsBool(false);
    if (ok)
    {
        string error;
        PortalStorage.DeleteMaid(connection.SendMaidId, out error);
        // error 非空只是本地删失败，女仆已经发出去了，不改 ok
    }
    // ok = false 时什么都不删
}
```

---

## 9. Java 侧建议骨架

```java
public final class PortalClient implements AutoCloseable {
    private final Socket socket;
    private final DataInputStream in;
    private final DataOutputStream out;

    public PortalClient(String host, int port) throws IOException {
        socket = new Socket(host, port);
        socket.setTcpNoDelay(true);
        in = new DataInputStream(socket.getInputStream());
        out = new DataOutputStream(socket.getOutputStream());
    }

    private void send(String headerJson, byte[] body) throws IOException {
        byte[] header = headerJson.getBytes(StandardCharsets.UTF_8);
        out.writeInt(header.length);        // DataOutputStream 默认就是大端
        out.write(header);
        if (body != null) {
            out.write(body);
        }
        out.flush();
    }

    /** 读一帧；没有 body 时 body 为 null */
    private Frame read() throws IOException {
        int headerLength = in.readInt();
        byte[] headerBytes = new byte[headerLength];
        in.readFully(headerBytes);
        JsonObject header = JsonParser.parseString(
                new String(headerBytes, StandardCharsets.UTF_8)).getAsJsonObject();
        int bodyLength = header.has("body") ? header.get("body").getAsInt() : 0;
        byte[] body = null;
        if (bodyLength > 0) {
            body = new byte[bodyLength];
            in.readFully(body);
        }
        return new Frame(header, body);
    }

    /** 发一个请求，等它对应的应答；途中把 send.* 通知分派掉 */
    private JsonObject request(String op, JsonObject payload) throws IOException {
        payload.addProperty("op", op);
        send(payload.toString(), null);
        while (true) {
            Frame frame = read();
            String replyOp = frame.header.get("op").getAsString();
            if ("error".equals(replyOp)) {
                throw new IOException(frame.header.get("message").getAsString());
            }
            if (isNotification(replyOp)) {     // send.begin / send.file / send.end
                handleNotification(frame);
                continue;
            }
            return frame.header;
        }
    }
}
```

上传一个文件（注意 header 和 body 是一帧的两部分，中间不要插别的）：

```java
private JsonObject uploadFile(String relativePath, Path file) throws IOException {
    byte[] payload = Files.readAllBytes(file);
    JsonObject header = new JsonObject();
    header.addProperty("op", "upload.file");
    header.addProperty("path", relativePath);
    header.addProperty("sha256", sha256Hex(payload));
    header.addProperty("body", payload.length);
    send(header.toString(), payload);          // header 和 body 一次发出去
    return waitFor("upload.file.ok");
}
```

几个 Java 侧容易踩的点：

- `DataInputStream` / `DataOutputStream` 默认就是**大端**，直接 `readInt` / `writeInt` 就行；
  如果用 `ByteBuffer` 记得 `.order(ByteOrder.BIG_ENDIAN)`。
- header 里的中文用 `new String(bytes, StandardCharsets.UTF_8)`，别用平台默认编码，Windows 上会乱。
- `Socket` 的读是阻塞的。**读线程和写线程要分开**，或者严格按"一问一答"来写；
  否则服务端推 `send.begin` 的时候你正好在等别的应答，整个流就错位了。
- JSON 库随便挑（Gson / Jackson / 手写都行），字段名完全按本文档。

---

## 10. 常见错误对照

| 收到 / 现象 | 原因 |
|---|---|
| `error: 不认识的操作: xxx` | op 拼错，或用了本文档没有的消息 |
| `error: kind 不合法: xxx` | `kind` 只能填 `maid` / `house` / `sound` |
| `error: id 里有非法字符: xxx` | `id` 里有 `/` `\` `:` 或者 `.` `..` |
| `error: 非法路径: xxx` | `path` 里出现 `..` 或绝对路径 |
| `error: sha256 对不上: xxx` | body 内容和 header 里的 sha256 不一致，检查字节流是不是写错了 |
| `error: 上一个上传还没结束` | 没 `commit` / `abort` 就发了新的 `upload.begin` |
| `error: 服务端正在往客户端发女仆，等它结束` | 服务端在发女仆，等 ack 之后再传 |
| 连接被服务端直接关闭 | 已经有别的客户端连着，或者上一帧踩了协议错误（超长 header 之类） |
| 上传完但文件夹里是空的 | 忘了发 `upload.commit` |
| 女仆发不回来 | 那只女仆的 `maid_data.maid` 不存在，或者它已经被放在房子里（不在背包） |

---

## 11. 怎么自测

Unity 侧把 Portal 场景跑起来之后，仓库里 `Tools/portal_test_client.py` 是一个照着本协议写的
Python 参考客户端，可以先拿它验证 Unity 端通了没：

```powershell
# 0. Unity 里 Play（Portal 场景），Console 出现「正在监听 7411 端口」

# 1. 握手
python Tools/portal_test_client.py hello

# 2. 看服务端有什么
python Tools/portal_test_client.py list maid
python Tools/portal_test_client.py list house
python Tools/portal_test_client.py list sound

# 3. 上传一只女仆（--id 省略就让服务端随机生成 uuid）
python Tools/portal_test_client.py upload maid "Tools\out\portal_testdata\maid" --id portal-test-maid

# 4. 挂着等服务端发女仆过来（这一步必须在 Unity 面板点「发送」之前开）
python Tools/portal_test_client.py --out Tools\out\got watch
```

`Tools/portal_test_client.py` 顶部有完整的消息注释表，**可以直接当 Java 实现的对照参考**。
它实现的东西就是 Java 侧要做的一切：握手、列举、上传、接收、回 ack。

---

## 12. 需要双方对齐才改的东西

- **协议版本号**（现在是 `1`）：加/改消息时一起往上加，`hello` 里带着。
- **端口**：默认 `7411`。
- **`kind` 的三个取值**。
- **`maid.json` / `house.json` 的字段名**：加字段没关系，但请提前说一声
  （Unity 的存档编辑器会把 `maid.json` 整个重写，只写它认识的字段）。
- **`maid_data.maid` 的内部格式**：Unity 完全不解析，只负责原样存和原样转发，
  所以它的格式随便变，**不需要通知 Unity 侧**。这是故意留的解耦点。

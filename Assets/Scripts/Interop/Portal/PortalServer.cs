using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MaidHome.Core.Json;
using MaidHome.Core.Storage;
using MaidHome.Interop.House;
using MaidHome.Interop.Maid;

namespace MaidHome.Interop.Portal
{
    /// <summary>
    /// Portal 场景的传输服务端：只接一个客户端，收客户端上传的女仆/房子/音效包，
    /// 也能把背包里的女仆发回去（客户端确认收到后才从存档删）。
    ///
    /// 线程模型：accept / read / write 三条后台线程，读到的结果丢进 _notices，
    /// 主线程每帧调 TryDequeueNotice 取。所有 Unity 对象只有在主线程那边才碰。
    /// 一次只跑一个传输，进度靠 _transferDone / _transferTotal 两个计数。
    /// </summary>
    public sealed class PortalServer : IDisposable
    {
        public const int DefaultPort = 7411;
        public const int ProtocolVersion = 1;

        static readonly byte[] EmptyBytes = new byte[0];

        readonly int _port;
        readonly ConcurrentQueue<PortalNotice> _notices = new ConcurrentQueue<PortalNotice>();
        readonly object _lock = new object();

        TcpListener _listener;
        Thread _acceptThread;
        Connection _connection;
        volatile bool _running;

        long _transferDone;
        long _transferTotal;
        int _transferOperation;
        int _transferKind;
        int _hasTransfer;

        public PortalServer(int port)
        {
            _port = port > 0 ? port : DefaultPort;
        }

        public int Port
        {
            get { return _port; }
        }

        public bool IsRunning
        {
            get { return _running; }
        }

        public bool IsClientConnected
        {
            get { return _connection != null; }
        }

        public string ClientName
        {
            get
            {
                Connection connection = _connection;
                return connection == null ? "" : connection.Name;
            }
        }

        public long TransferDone
        {
            get { return Interlocked.Read(ref _transferDone); }
        }

        public long TransferTotal
        {
            get { return Interlocked.Read(ref _transferTotal); }
        }

        // ------------------------------------------------------------ 生命周期

        public bool Start(out string error)
        {
            error = "";
            if (_running)
            {
                return true;
            }

            try
            {
                TcpListener listener = new TcpListener(IPAddress.Any, _port);
                listener.Start();
                _listener = listener;
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "PortalAccept";
                _acceptThread.Start();
                return true;
            }
            catch (Exception exception)
            {
                error = "监听 " + _port + " 端口失败: " + exception.Message;
                _running = false;
                _listener = null;
                return false;
            }
        }

        public void Stop()
        {
            if (!_running && _listener == null && _connection == null)
            {
                return;
            }

            _running = false;

            TcpListener listener = _listener;
            _listener = null;
            if (listener != null)
            {
                try
                {
                    listener.Stop();
                }
                catch (Exception)
                {
                    // 关闭监听失败没有补救手段
                }
            }

            Connection connection = _connection;
            if (connection != null)
            {
                CloseConnection(connection, "服务已停止");
            }

            JoinThread(_acceptThread);
            _acceptThread = null;
            if (connection != null)
            {
                JoinThread(connection.ReadThread);
                JoinThread(connection.WriteThread);
            }
        }

        public void Dispose()
        {
            Stop();
        }

        // ------------------------------------------------------------ 主线程接口

        public bool TryDequeueNotice(out PortalNotice notice)
        {
            return _notices.TryDequeue(out notice);
        }

        /// <summary>当前有没有传输在跑；有就顺手给出方向和进度（0~1）。</summary>
        public bool TryGetTransfer(out PortalOperation operation, out PortalKind kind, out float progress)
        {
            operation = PortalOperation.Upload;
            kind = PortalKind.Maid;
            progress = 0f;

            if (Volatile.Read(ref _hasTransfer) == 0)
            {
                return false;
            }

            operation = (PortalOperation)Volatile.Read(ref _transferOperation);
            kind = (PortalKind)Volatile.Read(ref _transferKind);

            long total = Interlocked.Read(ref _transferTotal);
            if (total > 0)
            {
                double value = (double)Interlocked.Read(ref _transferDone) / total;
                progress = value <= 0.0 ? 0f : (value >= 1.0 ? 1f : (float)value);
            }

            return true;
        }

        /// <summary>
        /// 把背包里的女仆发给客户端。客户端回 ack 成功后才删存档，失败或断线都不删。
        /// 建包和发帧在后台线程做，主线程不会因为读文件卡住。
        /// </summary>
        public bool TrySendMaid(string maidId, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(maidId))
            {
                error = "没指定女仆";
                return false;
            }

            if (!_running)
            {
                error = "服务还没启动";
                return false;
            }

            Connection connection = _connection;
            if (connection == null)
            {
                error = "还没有客户端连接";
                return false;
            }

            if (Interlocked.CompareExchange(ref _hasTransfer, 1, 0) != 0)
            {
                error = "已有传输在进行，等它结束";
                return false;
            }

            Volatile.Write(ref _transferOperation, (int)PortalOperation.Send);
            Volatile.Write(ref _transferKind, (int)PortalKind.Maid);
            Interlocked.Exchange(ref _transferDone, 0);
            Interlocked.Exchange(ref _transferTotal, 0);

            Task.Run(() => SendMaidWorker(connection, maidId));
            return true;
        }

        // ------------------------------------------------------------ accept / 读写线程

        void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    break;
                }

                if (!_running)
                {
                    SafeClose(client);
                    break;
                }

                bool busy;
                lock (_lock)
                {
                    busy = _connection != null;
                }

                if (busy)
                {
                    // 只服务一个客户端，多余的直接挂断
                    SafeClose(client);
                    continue;
                }

                StartConnection(client);
            }
        }

        void StartConnection(TcpClient client)
        {
            Connection connection = new Connection();
            connection.Client = client;
            connection.Endpoint = client.Client.RemoteEndPoint != null
                ? client.Client.RemoteEndPoint.ToString()
                : "unknown";

            try
            {
                client.NoDelay = true;
                client.ReceiveTimeout = 0;
                connection.Stream = client.GetStream();
            }
            catch (Exception exception)
            {
                SafeClose(client);
                Enqueue(new PortalNotice
                {
                    Type = PortalNoticeType.ClientDisconnected,
                    Message = "建立连接失败: " + exception.Message
                });
                return;
            }

            lock (_lock)
            {
                _connection = connection;
            }

            connection.WriteThread = new Thread(() => WriteLoop(connection));
            connection.WriteThread.IsBackground = true;
            connection.WriteThread.Name = "PortalWrite";
            connection.ReadThread = new Thread(() => ReadLoop(connection));
            connection.ReadThread.IsBackground = true;
            connection.ReadThread.Name = "PortalRead";
            connection.WriteThread.Start();
            connection.ReadThread.Start();

            Enqueue(new PortalNotice
            {
                Type = PortalNoticeType.ClientConnected,
                Message = "socket 已连上 " + connection.Endpoint + "，等客户端握手"
            });
        }

        void ReadLoop(Connection connection)
        {
            string reason = "客户端断开";
            try
            {
                while (_running)
                {
                    JsonValue header;
                    byte[] body;
                    if (!PortalFrame.TryRead(connection.Stream, out header, out body))
                    {
                        break;
                    }

                    Handle(connection, header, body);
                }
            }
            catch (Exception exception)
            {
                reason = "读连接出错: " + exception.Message;
            }
            finally
            {
                CloseConnection(connection, reason);
            }
        }

        void WriteLoop(Connection connection)
        {
            try
            {
                foreach (Outgoing item in connection.Outbound.GetConsumingEnumerable())
                {
                    connection.Stream.Write(item.Bytes, 0, item.Bytes.Length);
                    if (item.CountProgress)
                    {
                        Interlocked.Add(ref _transferDone, item.BodyBytes);
                    }
                }
            }
            catch (Exception)
            {
                // 连接没了，交给 CloseConnection 收尾
            }

            CloseConnection(connection, "写连接结束");
        }

        void CloseConnection(Connection connection, string reason)
        {
            lock (_lock)
            {
                if (_connection != connection)
                {
                    return;
                }

                _connection = null;
            }

            try
            {
                connection.Outbound.CompleteAdding();
            }
            catch (Exception)
            {
                // 已经 CompleteAdding 过了
            }

            try
            {
                connection.Client.Close();
            }
            catch (Exception)
            {
                // 已关
            }

            UploadSession upload = connection.Upload;
            connection.Upload = null;
            if (upload != null)
            {
                PortalStorage.DeleteStaging(upload.Folder);
                Enqueue(new PortalNotice
                {
                    Type = PortalNoticeType.TransferFinished,
                    Operation = PortalOperation.Upload,
                    Kind = upload.Kind,
                    Id = upload.Id,
                    Name = DisplayName(upload.Kind, upload.Id, upload.Name),
                    Ok = false,
                    Message = "连接断了，上传没完成（暂存已清掉）"
                });
            }

            if (connection.SendPending)
            {
                connection.SendPending = false;
                Enqueue(new PortalNotice
                {
                    Type = PortalNoticeType.TransferFinished,
                    Operation = PortalOperation.Send,
                    Kind = PortalKind.Maid,
                    Id = connection.SendMaidId,
                    Name = connection.SendMaidName,
                    Ok = false,
                    Message = "连接断了，女仆没有发出去，存档保持不动"
                });
            }

            EndTransfer();
            Enqueue(new PortalNotice
            {
                Type = PortalNoticeType.ClientDisconnected,
                Message = reason
            });
        }

        // ------------------------------------------------------------ 协议分发

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

        void OnHello(Connection connection, JsonValue header)
        {
            connection.Name = header["client"].AsString("");
            string warning = header["version"].AsInt(0) == ProtocolVersion
                ? ""
                : "协议版本不一致（客户端 " + header["version"].AsInt(0) + "，服务端 " + ProtocolVersion + "）";

            string reply = new JsonWriter("hello.ok")
                .Add("version", ProtocolVersion)
                .Add("server", "MaidHome")
                .Raw("kinds", "[\"maid\",\"house\",\"sound\"]")
                .ToString();
            Send(connection, reply, null, false);

            Enqueue(new PortalNotice
            {
                Type = PortalNoticeType.ClientConnected,
                Name = connection.Name,
                Message = warning
            });
        }

        void OnUploadBegin(Connection connection, JsonValue header)
        {
            if (connection.Upload != null)
            {
                SendError(connection, "upload.begin", "上一个上传还没结束");
                return;
            }

            if (connection.SendPending)
            {
                SendError(connection, "upload.begin", "服务端正在往客户端发女仆，等它结束");
                return;
            }

            PortalKind kind;
            if (!PortalKindExtensions.TryParse(header["kind"].AsString(""), out kind))
            {
                SendError(connection, "upload.begin", "kind 不合法: " + header["kind"].AsString(""));
                return;
            }

            string id = header["id"].AsString("");
            if (string.IsNullOrEmpty(id))
            {
                id = Guid.NewGuid().ToString();
            }
            else if (!PortalStorage.IsSafeName(id))
            {
                SendError(connection, "upload.begin", "id 里有非法字符: " + id);
                return;
            }

            UploadSession session = new UploadSession();
            session.UploadId = Guid.NewGuid().ToString("N");
            session.Kind = kind;
            session.Id = id;
            session.Name = header["name"].AsString("");
            session.ExpectedFiles = header["file_count"].AsInt(0);

            try
            {
                session.Folder = PortalStorage.CreateStaging(session.UploadId);
            }
            catch (Exception exception)
            {
                SendError(connection, "upload.begin", "建暂存目录失败: " + exception.Message);
                return;
            }

            connection.Upload = session;
            BeginTransfer(PortalOperation.Upload, kind, header["total_bytes"].AsInt(0));

            string reply = new JsonWriter("upload.begin.ok")
                .Add("upload_id", session.UploadId)
                .Add("kind", kind.ToWire())
                .Add("id", id)
                .ToString();
            Send(connection, reply, null, false);
        }

        void OnUploadFile(Connection connection, JsonValue header, byte[] body)
        {
            UploadSession session = connection.Upload;
            if (session == null)
            {
                SendError(connection, "upload.file", "没有进行中的上传");
                return;
            }

            string relative = header["path"].AsString("");
            if (!PortalStorage.IsSafeRelativePath(relative))
            {
                session.Broken = true;
                session.Error = "非法路径: " + relative;
                SendError(connection, "upload.file", session.Error);
                return;
            }

            byte[] payload = body != null ? body : EmptyBytes;
            string expected = header["sha256"].AsString("");
            if (!string.IsNullOrEmpty(expected) && !string.Equals(expected, Sha256(payload), StringComparison.OrdinalIgnoreCase))
            {
                session.Broken = true;
                session.Error = "sha256 对不上: " + relative;
                SendError(connection, "upload.file", session.Error);
                return;
            }

            try
            {
                PortalStorage.WriteStagingFile(session.Folder, relative, payload);
            }
            catch (Exception exception)
            {
                session.Broken = true;
                session.Error = "写文件失败 " + relative + ": " + exception.Message;
                SendError(connection, "upload.file", session.Error);
                return;
            }

            session.ReceivedFiles++;
            Interlocked.Add(ref _transferDone, payload.Length);

            string reply = new JsonWriter("upload.file.ok")
                .Add("path", relative)
                .Add("bytes", payload.Length)
                .ToString();
            Send(connection, reply, null, false);
        }

        void OnUploadCommit(Connection connection)
        {
            UploadSession session = connection.Upload;
            if (session == null)
            {
                SendError(connection, "upload.commit", "没有进行中的上传");
                return;
            }

            connection.Upload = null;
            EndTransfer();

            string error = session.Error;
            bool ok = !session.Broken
                && PortalStorage.PromoteStaging(session.Folder, session.Kind, session.Id, out error);

            if (!ok)
            {
                PortalStorage.DeleteStaging(session.Folder);
                Enqueue(new PortalNotice
                {
                    Type = PortalNoticeType.TransferFinished,
                    Operation = PortalOperation.Upload,
                    Kind = session.Kind,
                    Id = session.Id,
                    Name = DisplayName(session.Kind, session.Id, session.Name),
                    Ok = false,
                    Message = string.IsNullOrEmpty(error) ? "上传失败" : error
                });
                SendError(connection, "upload.commit", string.IsNullOrEmpty(error) ? "上传失败" : error);
                return;
            }

            string name = PortalStorage.ResolveUploadName(session.Kind, session.Id, session.Name);
            Enqueue(new PortalNotice
            {
                Type = PortalNoticeType.TransferFinished,
                Operation = PortalOperation.Upload,
                Kind = session.Kind,
                Id = session.Id,
                Name = name,
                Ok = true
            });

            string reply = new JsonWriter("upload.commit.ok")
                .Add("kind", session.Kind.ToWire())
                .Add("id", session.Id)
                .Add("name", name)
                .ToString();
            Send(connection, reply, null, false);
        }

        void OnUploadAbort(Connection connection)
        {
            UploadSession session = connection.Upload;
            connection.Upload = null;
            EndTransfer();

            if (session != null)
            {
                PortalStorage.DeleteStaging(session.Folder);
            }

            Send(connection, new JsonWriter("upload.abort.ok").ToString(), null, false);
        }

        void OnListRequest(Connection connection, JsonValue header)
        {
            PortalKind kind;
            if (!PortalKindExtensions.TryParse(header["kind"].AsString(""), out kind))
            {
                SendError(connection, "list.request", "kind 不合法: " + header["kind"].AsString(""));
                return;
            }

            StringBuilder items = new StringBuilder("[");
            if (kind == PortalKind.Maid)
            {
                List<MaidSaveData> maids = PortalStorage.ListBagMaids();
                for (int i = 0; i < maids.Count; i++)
                {
                    MaidSaveData maid = maids[i];
                    AppendItem(items, i, maid.Id, PortalStorage.DisplayNameOf(maid.Id, maid.Name),
                        PortalStorage.FolderBytes(maid.Folder), PortalStorage.CanSend(maid));
                }
            }
            else if (kind == PortalKind.House)
            {
                List<HouseSaveData> houses = PortalStorage.ListHouses();
                for (int i = 0; i < houses.Count; i++)
                {
                    HouseSaveData house = houses[i];
                    AppendItem(items, i, house.Id, PortalStorage.DisplayNameOf(house.Id, house.Name),
                        PortalStorage.FolderBytes(house.Folder), true);
                }
            }
            else
            {
                List<string> packs = PortalStorage.ListSoundPackIds();
                for (int i = 0; i < packs.Count; i++)
                {
                    string folder = Path.Combine(AppPaths.SoundsRoot, packs[i]);
                    AppendItem(items, i, packs[i], packs[i], PortalStorage.FolderBytes(folder), true);
                }
            }

            items.Append(']');
            string reply = new JsonWriter("list.ok")
                .Add("kind", kind.ToWire())
                .Raw("items", items.ToString())
                .ToString();
            Send(connection, reply, null, false);
        }

        void OnSendAck(Connection connection, JsonValue header)
        {
            if (!connection.SendPending)
            {
                return;
            }

            connection.SendPending = false;
            string maidId = connection.SendMaidId;
            string maidName = connection.SendMaidName;
            bool ok = header["ok"].AsBool(false);
            string message = header["message"].AsString("");
            EndTransfer();

            PortalNotice notice = new PortalNotice();
            notice.Type = PortalNoticeType.TransferFinished;
            notice.Operation = PortalOperation.Send;
            notice.Kind = PortalKind.Maid;
            notice.Id = maidId;
            notice.Name = maidName;
            notice.Ok = ok;
            notice.Message = message;

            if (ok)
            {
                string error;
                if (!PortalStorage.DeleteMaid(maidId, out error))
                {
                    notice.Message = "已经发过去了，但从存档删的时候出错: " + error;
                }
            }

            Enqueue(notice);
        }

        // ------------------------------------------------------------ 发送女仆

        void SendMaidWorker(Connection connection, string maidId)
        {
            try
            {
                string error;
                PortalPackage package = PortalStorage.BuildMaidPackage(maidId, out error);
                if (package == null)
                {
                    FailTransfer(connection, PortalOperation.Send, PortalKind.Maid, maidId, "", error);
                    return;
                }

                long total = package.TotalBytes();
                Interlocked.Exchange(ref _transferTotal, total);
                connection.SendMaidId = package.Id;
                connection.SendMaidName = package.Name;

                string begin = new JsonWriter("send.begin")
                    .Add("kind", package.Kind.ToWire())
                    .Add("id", package.Id)
                    .Add("name", package.Name)
                    .Add("total_bytes", total)
                    .Raw("data", string.IsNullOrEmpty(package.DataJson) ? "{}" : package.DataJson)
                    .ToString();
                if (!Send(connection, begin, null, false))
                {
                    FailTransfer(connection, PortalOperation.Send, PortalKind.Maid, package.Id, package.Name, "连接不可用");
                    return;
                }

                for (int i = 0; i < package.Files.Count; i++)
                {
                    string relative = package.Files[i];
                    byte[] payload = File.ReadAllBytes(Path.Combine(package.Root, relative));
                    string fileHeader = new JsonWriter("send.file")
                        .Add("path", relative)
                        .Add("sha256", Sha256(payload))
                        .Add("body", payload.Length)
                        .ToString();
                    if (!Send(connection, fileHeader, payload, true))
                    {
                        FailTransfer(connection, PortalOperation.Send, PortalKind.Maid, package.Id, package.Name, "连接不可用");
                        return;
                    }
                }

                // 先立等 ack 的标志再发 send.end，客户端只可能在收到 end 之后回 ack
                connection.SendPending = true;
                string end = new JsonWriter("send.end")
                    .Add("id", package.Id)
                    .Add("total_bytes", total)
                    .ToString();
                if (!Send(connection, end, null, false))
                {
                    connection.SendPending = false;
                    FailTransfer(connection, PortalOperation.Send, PortalKind.Maid, package.Id, package.Name, "连接不可用");
                }
            }
            catch (Exception exception)
            {
                connection.SendPending = false;
                FailTransfer(connection, PortalOperation.Send, PortalKind.Maid, maidId, "", exception.Message);
            }
        }

        void FailTransfer(Connection connection, PortalOperation operation, PortalKind kind, string id, string name, string message)
        {
            EndTransfer();
            Enqueue(new PortalNotice
            {
                Type = PortalNoticeType.TransferFinished,
                Operation = operation,
                Kind = kind,
                Id = id,
                Name = name,
                Ok = false,
                Message = string.IsNullOrEmpty(message) ? "传输失败" : message
            });
        }

        // ------------------------------------------------------------ 工具

        void BeginTransfer(PortalOperation operation, PortalKind kind, long total)
        {
            Volatile.Write(ref _transferOperation, (int)operation);
            Volatile.Write(ref _transferKind, (int)kind);
            Interlocked.Exchange(ref _transferDone, 0);
            Interlocked.Exchange(ref _transferTotal, total);
            Volatile.Write(ref _hasTransfer, 1);
        }

        void EndTransfer()
        {
            Volatile.Write(ref _hasTransfer, 0);
            Interlocked.Exchange(ref _transferDone, 0);
            Interlocked.Exchange(ref _transferTotal, 0);
        }

        bool Send(Connection connection, string headerJson, byte[] body, bool countProgress)
        {
            Outgoing item = new Outgoing();
            item.Bytes = PortalFrame.Encode(headerJson, body);
            item.BodyBytes = body != null ? body.Length : 0;
            item.CountProgress = countProgress;

            try
            {
                if (connection.Outbound.TryAdd(item, 10000))
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // CompleteAdding 过了，连接已经结束
            }

            return false;
        }

        void SendError(Connection connection, string request, string message)
        {
            string json = new JsonWriter("error")
                .Add("request", request)
                .Add("message", message)
                .ToString();
            Send(connection, json, null, false);
        }

        void Enqueue(PortalNotice notice)
        {
            _notices.Enqueue(notice);
        }

        static void AppendItem(StringBuilder text, int index, string id, string name, long bytes, bool sendable)
        {
            if (index > 0)
            {
                text.Append(',');
            }

            text.Append("{\"id\":").Append(PortalFrame.Quote(id))
                .Append(",\"name\":").Append(PortalFrame.Quote(name))
                .Append(",\"bytes\":").Append(bytes)
                .Append(",\"sendable\":").Append(sendable ? "true" : "false")
                .Append('}');
        }

        static string DisplayName(PortalKind kind, string id, string name)
        {
            // 音效包没有名字，直接用 id
            return kind == PortalKind.SoundPack ? id : PortalStorage.DisplayNameOf(id, name);
        }

        static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2"));
                }

                return text.ToString();
            }
        }

        static void SafeClose(TcpClient client)
        {
            if (client == null)
            {
                return;
            }

            try
            {
                client.Close();
            }
            catch (Exception)
            {
                // 已关
            }
        }

        static void JoinThread(Thread thread)
        {
            if (thread == null || thread == Thread.CurrentThread)
            {
                return;
            }

            try
            {
                thread.Join(500);
            }
            catch (Exception)
            {
                // 线程已经没了
            }
        }

        // ------------------------------------------------------------ 内部状态

        sealed class Connection
        {
            public TcpClient Client;
            public NetworkStream Stream;
            public string Name = "";
            public string Endpoint = "";
            public Thread ReadThread;
            public Thread WriteThread;
            public readonly BlockingCollection<Outgoing> Outbound = new BlockingCollection<Outgoing>(256);
            public UploadSession Upload;
            public volatile bool SendPending;
            public volatile string SendMaidId = "";
            public volatile string SendMaidName = "";
        }

        sealed class Outgoing
        {
            public byte[] Bytes;
            public int BodyBytes;
            public bool CountProgress;
        }

        sealed class UploadSession
        {
            public string UploadId = "";
            public PortalKind Kind;
            public string Id = "";
            public string Name = "";
            public string Folder = "";
            public int ExpectedFiles;
            public int ReceivedFiles;
            public bool Broken;
            public string Error = "";
        }

        /// <summary>手写 JSON 对象，省得为几个固定的包再引一个序列化库。</summary>
        sealed class JsonWriter
        {
            readonly StringBuilder _text = new StringBuilder();

            public JsonWriter(string op)
            {
                _text.Append("{\"op\":").Append(PortalFrame.Quote(op));
            }

            public JsonWriter Add(string key, string value)
            {
                _text.Append(',').Append(PortalFrame.Quote(key)).Append(':').Append(PortalFrame.Quote(value));
                return this;
            }

            public JsonWriter Add(string key, long value)
            {
                _text.Append(',').Append(PortalFrame.Quote(key)).Append(':').Append(value);
                return this;
            }

            /// <summary>value 已经是合法 JSON 片段（对象、数组、true/false）时用。</summary>
            public JsonWriter Raw(string key, string value)
            {
                _text.Append(',').Append(PortalFrame.Quote(key)).Append(':').Append(value);
                return this;
            }

            public override string ToString()
            {
                return _text.Append('}').ToString();
            }
        }
    }
}

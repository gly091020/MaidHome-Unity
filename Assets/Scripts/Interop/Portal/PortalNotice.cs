namespace MaidHome.Interop.Portal
{
    /// <summary>
    /// 后台线程丢给主线程的一条通知，由 PortalServer.Pump 派发。
    /// ClientConnected 时 Name 是客户端自报的名字（握手前为空）；TransferFinished 时
    /// Ok 为假表示失败，Message 是给玩家看的原因（Ok 为真时 Message 也可能带提醒）。
    /// </summary>
    public sealed class PortalNotice
    {
        public PortalNoticeType Type;
        public PortalOperation Operation;
        public PortalKind Kind;
        public string Id = "";
        public string Name = "";
        public bool Ok;
        public string Message = "";
    }
}

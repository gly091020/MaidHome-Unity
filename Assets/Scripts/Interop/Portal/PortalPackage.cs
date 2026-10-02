using System.Collections.Generic;
using System.IO;

namespace MaidHome.Interop.Portal
{
    /// <summary>
    /// 准备发给客户端的一包数据。Files 是相对 Root 的路径，线上按原样还原。
    /// </summary>
    public sealed class PortalPackage
    {
        public PortalKind Kind;
        public string Id = "";
        public string Name = "";
        public string Root = "";
        /// <summary>随包发的一段自定义 JSON，目前固定是空对象。</summary>
        public string DataJson = "{}";
        public readonly List<string> Files = new List<string>();

        public long TotalBytes()
        {
            long total = 0;
            for (int i = 0; i < Files.Count; i++)
            {
                string path = Path.Combine(Root, Files[i]);
                if (File.Exists(path))
                {
                    total += new FileInfo(path).Length;
                }
            }

            return total;
        }
    }
}

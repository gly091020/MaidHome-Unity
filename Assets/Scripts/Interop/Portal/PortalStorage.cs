using System;
using System.Collections.Generic;
using System.IO;
using MaidHome.Core.Save;
using MaidHome.Core.Storage;
using MaidHome.Interop.House;
using MaidHome.Interop.Maid;

namespace MaidHome.Interop.Portal
{
    /// <summary>
    /// Portal 的文件落地：上传先写 tmp，收全了再顶掉正式目录；发送只取 maid_data.maid。
    /// 全部是同步文件操作，调用方负责放到后台线程。
    /// </summary>
    public static class PortalStorage
    {
        /// <summary>MC 侧序列化的女仆数据，发送时唯一要传的文件。</summary>
        public const string MaidDataFileName = "maid_data.maid";

        public static string StagingRoot
        {
            get { return Path.Combine(AppPaths.TmpRoot, "portal"); }
        }

        public static string RootOf(PortalKind kind)
        {
            switch (kind)
            {
                case PortalKind.House: return AppPaths.HouseSaveRoot;
                case PortalKind.SoundPack: return AppPaths.SoundsRoot;
                default: return AppPaths.MaidSaveRoot;
            }
        }

        // ------------------------------------------------------------ 列举

        /// <summary>存档里的全部女仆，不管在不在背包。UI 用这个，别自己过滤。</summary>
        public static List<MaidSaveData> ListAllMaids()
        {
            return MaidSaveData.ScanRoot(AppPaths.MaidSaveRoot);
        }

        /// <summary>背包里的女仆（slot0.json 里 in_bag 为真，没记录的按在背包处理）。</summary>
        public static List<MaidSaveData> ListBagMaids()
        {
            List<MaidSaveData> all = MaidSaveData.ScanRoot(AppPaths.MaidSaveRoot);
            MaidWorldSave world = MaidWorldSaveStore.Load();
            List<MaidSaveData> result = new List<MaidSaveData>();
            for (int i = 0; i < all.Count; i++)
            {
                MaidWorldRecord record = world.Find(all[i].Id);
                if (record == null || record.InBag)
                {
                    result.Add(all[i]);
                }
            }

            return result;
        }

        public static List<HouseSaveData> ListHouses()
        {
            return HouseSaveData.ScanRoot(AppPaths.HouseSaveRoot);
        }

        public static List<string> ListSoundPackIds()
        {
            List<string> result = new List<string>();
            if (!Directory.Exists(AppPaths.SoundsRoot))
            {
                return result;
            }

            string[] folders = Directory.GetDirectories(AppPaths.SoundsRoot);
            for (int i = 0; i < folders.Length; i++)
            {
                result.Add(Path.GetFileName(folders[i]));
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public static string DisplayNameOf(string id, string name)
        {
            return string.IsNullOrEmpty(name) ? id : name;
        }

        /// <summary>
        /// 上传落地后取个能显示的名字：客户端报了就用它的，没报就从刚写下去的
        /// maid.json / house.json 里读；音效包没有名字，一律用 id。
        /// </summary>
        public static string ResolveUploadName(PortalKind kind, string id, string fromClient)
        {
            if (kind == PortalKind.SoundPack)
            {
                return id;
            }

            if (!string.IsNullOrEmpty(fromClient))
            {
                return fromClient;
            }

            try
            {
                string folder = Path.Combine(RootOf(kind), id);
                if (kind == PortalKind.Maid)
                {
                    string file = Path.Combine(folder, "maid.json");
                    if (File.Exists(file))
                    {
                        return DisplayNameOf(id, MaidSaveData.ParseFile(file).Name);
                    }
                }
                else if (kind == PortalKind.House)
                {
                    string file = Path.Combine(folder, "house.json");
                    if (File.Exists(file))
                    {
                        return DisplayNameOf(id, HouseSaveData.ParseFile(file).Name);
                    }
                }
            }
            catch (Exception)
            {
                // 刚传上来的文件解析不了也不该让上传失败，退回用 id
            }

            return id;
        }

        /// <summary>能不能发；不能发时 reason 里写清楚为什么，可以原样显示给玩家。</summary>
        public static bool CanSend(MaidSaveData maid, out string reason)
        {
            reason = "";
            if (maid == null)
            {
                reason = "没有这只女仆";
                return false;
            }

            if (string.IsNullOrEmpty(maid.Folder) || !File.Exists(Path.Combine(maid.Folder, MaidDataFileName)))
            {
                reason = "缺 " + MaidDataFileName;
                return false;
            }

            MaidWorldRecord record = MaidWorldSaveStore.Load().Find(maid.Id);
            if (record != null && !record.InBag)
            {
                reason = "已经放在房子里了，先收回背包";
                return false;
            }

            return true;
        }

        public static bool CanSend(MaidSaveData maid)
        {
            string reason;
            return CanSend(maid, out reason);
        }

        // ------------------------------------------------------------ 上传落地

        public static string StagingFolder(string uploadId)
        {
            return Path.Combine(StagingRoot, uploadId);
        }

        public static string CreateStaging(string uploadId)
        {
            string folder = StagingFolder(uploadId);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            return folder;
        }

        public static void WriteStagingFile(string folder, string relative, byte[] bytes)
        {
            string path = Path.Combine(folder, relative);
            string parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllBytes(path, bytes);
        }

        /// <summary>把暂存目录顶掉正式目录（重名先删旧的）。失败时暂存目录原样留着。</summary>
        public static bool PromoteStaging(string stagingFolder, PortalKind kind, string id, out string error)
        {
            error = "";
            if (!IsSafeName(id))
            {
                error = "id 里有非法字符: " + id;
                return false;
            }

            if (!Directory.Exists(stagingFolder))
            {
                error = "暂存目录不存在: " + stagingFolder;
                return false;
            }

            string target = Path.Combine(RootOf(kind), id);
            string backup = target + ".old";
            bool movedOld = false;
            try
            {
                AppPaths.EnsureDirectory(RootOf(kind));
                if (Directory.Exists(target))
                {
                    // 先把旧的挪开再放新的，搬一半失败还能把旧的放回去
                    DeleteDirectory(backup);
                    Directory.Move(target, backup);
                    movedOld = true;
                }

                MoveDirectory(stagingFolder, target);
                DeleteDirectory(backup);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (movedOld && !Directory.Exists(target) && Directory.Exists(backup))
                {
                    try
                    {
                        Directory.Move(backup, target);
                    }
                    catch (Exception)
                    {
                        // 还原也失败就只能把 .old 留着，别删
                    }
                }

                return false;
            }
        }

        public static void DeleteStaging(string folder)
        {
            try
            {
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception)
            {
                // 暂存目录删不掉无所谓，下次同名会先删
            }
        }

        // ------------------------------------------------------------ 发送 / 删除

        public static PortalPackage BuildMaidPackage(string maidId, out string error)
        {
            error = "";
            MaidSaveData maid = null;
            List<MaidSaveData> all = MaidSaveData.ScanRoot(AppPaths.MaidSaveRoot);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Id == maidId)
                {
                    maid = all[i];
                    break;
                }
            }

            if (maid == null)
            {
                error = "存档里找不到这只女仆: " + maidId;
                return null;
            }

            string reason;
            if (!CanSend(maid, out reason))
            {
                error = DisplayNameOf(maid.Id, maid.Name) + " 发不了: " + reason;
                return null;
            }

            PortalPackage package = new PortalPackage();
            package.Kind = PortalKind.Maid;
            package.Id = maid.Id;
            package.Name = DisplayNameOf(maid.Id, maid.Name);
            package.Root = maid.Folder;
            package.Files.Add(MaidDataFileName);
            return package;
        }

        /// <summary>发送成功后清干净：存档目录 + 缓存 + slot0.json 里的那条记录。</summary>
        public static bool DeleteMaid(string maidId, out string error)
        {
            error = "";
            try
            {
                MaidAssetCache.DeleteFolder(maidId);
                DeleteDirectory(Path.Combine(AppPaths.MaidSaveRoot, maidId));

                MaidWorldSave world = MaidWorldSaveStore.Load();
                if (world.Remove(maidId))
                {
                    MaidWorldSaveStore.Save(world);
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        // ------------------------------------------------------------ 工具

        public static long FolderBytes(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return 0;
            }

            long total = 0;
            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                total += new FileInfo(files[i]).Length;
            }

            return total;
        }

        public static void DeleteDirectory(string folder)
        {
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>客户端给的 id / 相对路径都不可信，挡掉 . 和 .. 以及路径分隔符。</summary>
        public static bool IsSafeName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 128)
            {
                return false;
            }

            if (name == "." || name == ".." || name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
            {
                return false;
            }

            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
        }

        public static bool IsSafeRelativePath(string relative)
        {
            if (string.IsNullOrEmpty(relative) || relative.Length > 512)
            {
                return false;
            }

            if (relative.IndexOf(':') >= 0 || relative.StartsWith("/") || relative.StartsWith("\\"))
            {
                return false;
            }

            string[] parts = relative.Replace('\\', '/').Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (!IsSafeName(parts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>跨盘符时 Directory.Move 会失败，退回复制 + 删源。</summary>
        static void MoveDirectory(string source, string target)
        {
            try
            {
                Directory.Move(source, target);
                return;
            }
            catch (IOException)
            {
                CopyDirectory(source, target);
                Directory.Delete(source, true);
            }
        }

        static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                File.Copy(files[i], Path.Combine(target, Path.GetFileName(files[i])), true);
            }

            string[] folders = Directory.GetDirectories(source);
            for (int i = 0; i < folders.Length; i++)
            {
                CopyDirectory(folders[i], Path.Combine(target, Path.GetFileName(folders[i])));
            }
        }
    }
}

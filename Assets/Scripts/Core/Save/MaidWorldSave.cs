using System.Collections.Generic;

namespace MaidHome.Core.Save
{
    /// <summary>游戏自己的存档：只存女仆位置和背包状态，不改 MC 导出的 maid.json。</summary>
    public sealed class MaidWorldSave
    {
        public const int CurrentVersion = 1;

        public int Version = CurrentVersion;
        public string HouseId = "";
        public readonly List<MaidWorldRecord> Maids = new List<MaidWorldRecord>();

        public MaidWorldRecord Find(string id)
        {
            for (int i = 0; i < Maids.Count; i++)
            {
                if (Maids[i].Id == id)
                {
                    return Maids[i];
                }
            }

            return null;
        }

        public MaidWorldRecord GetOrCreate(string id)
        {
            MaidWorldRecord record = Find(id);
            if (record != null)
            {
                return record;
            }

            record = new MaidWorldRecord();
            record.Id = id;
            Maids.Add(record);
            return record;
        }
    }
}

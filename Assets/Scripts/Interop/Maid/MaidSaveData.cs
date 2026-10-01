using System.Collections.Generic;
using System.IO;
using MaidHome.Core.Json;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 一份女仆存档：saves/maid/&lt;uuid&gt;/maid.json。
    /// model / texture / anim 都是相对本文件夹的文件名，同一文件夹里还有 MC 侧塞进来的二进制 maid_data.maid（Unity 不碰）。
    /// </summary>
    public sealed class MaidSaveData
    {
        public string Folder = "";
        public string Id = "";
        public string Name = "";
        public int Level;
        public string OwnerName = "";
        public string OwnerUuid = "";
        public string ModelFile = "";
        public string TextureFile = "";
        public string AnimationFile = "";
        /// <summary>模型整体缩放倍率，1 = 原始大小。不参与缓存（改了不用重转，套用时现算）</summary>
        public float Scale = 1f;
        /// <summary>MC 侧标记的 SimpleBedrockModel 模型，走路用程序化动画而不是 GeckoLib clip</summary>
        public bool SimpleBedrockModel;
        /// <summary>声音包 id，对应 saves/sounds/&lt;id&gt; 文件夹</summary>
        public string SoundPackId = "";
        /// <summary>这只女仆的语音频率，0 不播，1 全播</summary>
        public float SoundFrequency = 1f;
        public readonly List<string> Warnings = new List<string>();

        public string ModelPath { get { return PathOf(ModelFile); } }
        public string TexturePath { get { return PathOf(TextureFile); } }
        public string AnimationPath { get { return PathOf(AnimationFile); } }

        public static MaidSaveData ParseFile(string maidJsonPath)
        {
            MaidSaveData maid = Parse(MiniJson.ParseFile(maidJsonPath), Path.GetDirectoryName(maidJsonPath));
            return maid;
        }

        public static MaidSaveData Parse(string json, string folder)
        {
            return Parse(MiniJson.Parse(json), folder);
        }

        public static MaidSaveData Parse(JsonValue root, string folder)
        {
            MaidSaveData maid = new MaidSaveData();
            maid.Folder = folder ?? "";
            maid.Id = string.IsNullOrEmpty(folder) ? "" : Path.GetFileName(folder.TrimEnd('/', '\\'));
            maid.Name = root["name"].AsString("");
            maid.Level = root["level"].AsInt(0);
            maid.OwnerName = root["owner_name"].AsString("");
            maid.OwnerUuid = root["owner_uuid"].AsString("");
            maid.ModelFile = root["model"].AsString("");
            maid.TextureFile = root["texture"].AsString("");
            maid.AnimationFile = root["anim"].AsString("");
            maid.Scale = root["scale"].AsFloat(1f);
            maid.SimpleBedrockModel = root["simple_bedrock_model"].AsBool(false);
            maid.SoundPackId = root["sound"].AsString("");
            maid.SoundFrequency = root["sound_freq"].AsFloat(1f);
            if (maid.SoundFrequency < 0f)
            {
                maid.Warnings.Add("sound_freq 不是正数，按 0 处理: " + maid.SoundFrequency);
                maid.SoundFrequency = 0f;
            }
            else if (maid.SoundFrequency > 1f)
            {
                maid.SoundFrequency = 1f;
            }

            if (maid.Scale <= 0f)
            {
                maid.Warnings.Add("scale 不是正数，按 1 处理: " + maid.Scale);
                maid.Scale = 1f;
            }

            maid.CheckFiles();
            return maid;
        }

        /// <summary>扫女仆存档根目录，每个子文件夹里的 maid.json 就是一份女仆。</summary>
        public static List<MaidSaveData> ScanRoot(string maidRoot)
        {
            List<MaidSaveData> result = new List<MaidSaveData>();
            if (string.IsNullOrEmpty(maidRoot) || !Directory.Exists(maidRoot))
            {
                return result;
            }

            string[] folders = Directory.GetDirectories(maidRoot);
            for (int i = 0; i < folders.Length; i++)
            {
                string file = Path.Combine(folders[i], "maid.json");
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    result.Add(ParseFile(file));
                }
                catch (System.Exception error)
                {
                    MaidSaveData broken = new MaidSaveData();
                    broken.Folder = folders[i];
                    broken.Id = Path.GetFileName(folders[i]);
                    broken.Name = "(解析失败)";
                    broken.Warnings.Add("maid.json 解析失败: " + error.Message);
                    result.Add(broken);
                }
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return result;
        }

        void CheckFiles()
        {
            if (string.IsNullOrEmpty(ModelFile))
            {
                Warnings.Add("maid.json 里没有 model 字段");
            }
            else if (!File.Exists(ModelPath))
            {
                Warnings.Add("模型文件不存在: " + ModelFile);
            }

            if (!string.IsNullOrEmpty(TextureFile) && !File.Exists(TexturePath))
            {
                Warnings.Add("贴图文件不存在: " + TextureFile);
            }

            if (!string.IsNullOrEmpty(AnimationFile) && !File.Exists(AnimationPath))
            {
                Warnings.Add("动画文件不存在: " + AnimationFile);
            }
        }

        string PathOf(string file)
        {
            return string.IsNullOrEmpty(file) ? "" : Path.Combine(Folder, file);
        }
    }
}

using System.Collections.Generic;
using MaidHome.Core.Json;

namespace MaidHome.Interop.Maid
{
    /// <summary>
    /// 女仆脸上挂的面具：戴没戴 + 贴的那张照片（存女仆文件夹里的文件名，空 = 只戴面具不贴照片）。
    /// 照片本体是 saves/maid/&lt;uuid&gt;/mask.png，导入见 MaidMaskPhoto。
    /// </summary>
    public sealed class MaidMaskData
    {
        public bool Enabled;
        /// <summary>照片文件名（相对女仆文件夹），空 = 没贴</summary>
        public string Photo = "";

        public bool HasPhoto
        {
            get { return !string.IsNullOrEmpty(Photo); }
        }

        public MaidMaskData Clone()
        {
            MaidMaskData copy = new MaidMaskData();
            copy.Enabled = Enabled;
            copy.Photo = Photo;
            return copy;
        }

        /// <summary>取下面具（照片记录一起清掉，文件留在文件夹里不删）</summary>
        public void Clear()
        {
            Enabled = false;
            Photo = "";
        }

        public static MaidMaskData Parse(JsonValue value)
        {
            MaidMaskData data = new MaidMaskData();
            if (value == null || !value.IsObject)
            {
                return data;
            }

            data.Enabled = value["enabled"].AsBool(false);
            // 文件名来自存档，不受信任：只认纯文件名，别让它指到文件夹外面去
            data.Photo = SafeFileName(value["photo"].AsString(""));
            return data;
        }

        public JsonValue ToJson()
        {
            Dictionary<string, JsonValue> fields = new Dictionary<string, JsonValue>();
            fields["enabled"] = JsonValue.FromBool(Enabled);
            fields["photo"] = JsonValue.FromString(SafeFileName(Photo));
            return JsonValue.FromObject(fields);
        }

        static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "";
            }

            string trimmed = name.Trim();
            if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0 || trimmed.IndexOf(':') >= 0
                || trimmed.IndexOf("..") >= 0)
            {
                return "";
            }

            return trimmed;
        }
    }
}

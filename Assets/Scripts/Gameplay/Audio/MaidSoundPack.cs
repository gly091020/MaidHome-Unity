using System;
using System.Collections.Generic;

namespace MaidHome.Gameplay.Audio
{
    /// <summary>一个声音包：事件 id -> 该事件的 OGG 文件列表。</summary>
    public sealed class MaidSoundPack
    {
        public readonly string Id;
        public readonly string Root;

        readonly Dictionary<string, List<string>> _events =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public MaidSoundPack(string id, string root)
        {
            Id = id;
            Root = root;
        }

        public IEnumerable<string> EventIds
        {
            get { return _events.Keys; }
        }

        public int EventCount
        {
            get { return _events.Count; }
        }

        /// <summary>索引里第一条音频路径（启动预热用）</summary>
        public string FirstPath
        {
            get
            {
                foreach (List<string> paths in _events.Values)
                {
                    if (paths.Count > 0)
                    {
                        return paths[0];
                    }
                }

                return null;
            }
        }

        public bool TryGet(string eventId, out List<string> paths)
        {
            return _events.TryGetValue(eventId, out paths);
        }

        public void Add(string eventId, string path)
        {
            List<string> paths;
            if (!_events.TryGetValue(eventId, out paths))
            {
                paths = new List<string>();
                _events.Add(eventId, paths);
            }

            paths.Add(path);
        }
    }
}

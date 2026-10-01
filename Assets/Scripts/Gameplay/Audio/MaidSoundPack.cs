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

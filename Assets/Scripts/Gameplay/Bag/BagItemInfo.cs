namespace MaidHome.Gameplay.Bag
{
    /// <summary>背包面板里显示的一条物品。Kind 用来找对应的 provider。</summary>
    public sealed class BagItemInfo
    {
        public string Kind = "";
        public string Id = "";
        public string DisplayName = "";
        public string Subtitle = "";
        public bool InBag = true;
        public bool CanPlace = true;
    }
}

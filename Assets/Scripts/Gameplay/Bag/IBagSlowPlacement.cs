namespace MaidHome.Gameplay.Bag
{
    /// <summary>
    /// 可选：provider 想让放置面板知道"这一下要加载东西"（女仆第一次是整条模型转换，可能好几秒），
    /// 面板就会先把加载进度条亮出来再干活。**不实现这个接口就当快**，直接放，不会闪一下进度条。
    /// </summary>
    public interface IBagSlowPlacement
    {
        bool NeedsLoading(string id);
    }
}

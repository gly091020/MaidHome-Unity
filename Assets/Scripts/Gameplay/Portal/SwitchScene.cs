using MaidHome.Core.Scene;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SwitchScene : MonoBehaviour
{
    public string SceneName;

    public void Switch()
    {
        // 镜头上天 → 切场景 → 新场景降下来；过场组件不可用（场景不在 Build Settings 里之类）就退回硬切
        if (!SceneTransition.Go(SceneName))
        {
            SceneManager.LoadScene(SceneName);
        }
    }
}

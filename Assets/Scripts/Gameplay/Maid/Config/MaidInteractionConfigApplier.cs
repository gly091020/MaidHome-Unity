using System.Reflection;
using UnityEngine;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 把配置资源里的同名字段覆盖到 interaction 上。字段名和类型必须一致；
    /// 配置里留空的对象 / 数组不覆盖，场景里原来的值继续生效。
    /// </summary>
    public static class MaidInteractionConfigApplier
    {
        const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>配置必须显式挂上；没挂直接报错，不再从 Resources 猜。</summary>
        public static bool ApplyRequired(ScriptableObject config, object target, Object context, string label)
        {
            if (config == null)
            {
                Debug.LogError(label + " 没有挂配置，请把 Assets/Resources/Settings 里对应的配置拖到 Settings 字段", context);
                return false;
            }

            if (target == null)
            {
                return false;
            }

            FieldInfo[] source = config.GetType().GetFields(Flags);
            System.Type targetType = target.GetType();
            for (int i = 0; i < source.Length; i++)
            {
                FieldInfo from = source[i];
                FieldInfo to = targetType.GetField(from.Name, Flags);
                if (to == null || to.FieldType != from.FieldType)
                {
                    continue;
                }

                object value = from.GetValue(config);
                if (value == null && !from.FieldType.IsValueType)
                {
                    continue;
                }

                to.SetValue(target, value);
            }

            return true;
        }
    }
}

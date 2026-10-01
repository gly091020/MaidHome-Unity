using System.Collections.Generic;
using MaidHome.Gameplay.House;
using UnityEngine;
using UnityEngine.AI;

namespace MaidHome.Gameplay.Maid
{
    /// <summary>
    /// 沿 NavMesh 路径走的状态：算一次路径，然后一个拐点一个拐点地消费。
    ///
    /// 路径来自 NavMesh.CalculatePath（房子烘出来的那张网，内部障碍已经按格数据挖掉了）；
    /// 目的地从房子包围盒里随机采点、再吸附到网上。格表现在只负责"哪些地方要挖洞"。
    /// </summary>
    public sealed class MaidNavigator
    {
        readonly HouseNavMesh _house;
        readonly NavMeshPath _path = new NavMeshPath();
        readonly List<Vector3> _waypoints = new List<Vector3>();
        int _index;

        public MaidNavigator(HouseNavMesh house)
        {
            _house = house;
        }

        public bool HasWaypoint
        {
            get { return _index < _waypoints.Count; }
        }

        public Vector3 CurrentWaypoint
        {
            get { return _waypoints[_index]; }
        }

        public void Advance()
        {
            _index++;
        }

        public void Clear()
        {
            _waypoints.Clear();
            _index = 0;
        }

        /// <summary>算一条 from → to 的路径。不可达（或者有一端不在网上）返回 false。</summary>
        public bool SetDestination(Vector3 from, Vector3 to)
        {
            Clear();

            // 两端都先吸附到网上再算：女仆脚底因为 CharacterController 的 skinWidth 会比网面高一点，
            // 直接拿原始坐标算，有可能被判成"不在 NavMesh 上"。
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(from, out hit, 2f, NavMesh.AllAreas))
            {
                Warn("起点吸附不到 NavMesh: " + from);
                return false;
            }

            Vector3 start = hit.position;
            if (!NavMesh.SamplePosition(to, out hit, 2f, NavMesh.AllAreas))
            {
                Warn("目标吸附不到 NavMesh: " + to);
                return false;
            }

            Vector3 end = hit.position;
            if (!NavMesh.CalculatePath(start, end, NavMesh.AllAreas, _path))
            {
                Warn("CalculatePath 返回 false: " + start + " → " + end);
                return false;
            }

            if (_path.status != NavMeshPathStatus.PathComplete)
            {
                Warn("路径不完整(" + _path.status + "): " + start + " → " + end);
                return false;
            }

            // 第一个拐点就是起点自己，跳过
            for (int i = 1; i < _path.corners.Length; i++)
            {
                _waypoints.Add(_path.corners[i]);
            }

            return HasWaypoint;
        }

        /// <summary>在房子范围里随机挑一个落在网上的点，且离当前位置够远。</summary>
        public bool TryPickRandomDestination(Vector3 from, float minDistance, System.Random rng, int maxTries,
            out Vector3 point)
        {
            point = from;
            if (_house == null)
            {
                return false;
            }

            Bounds bounds = _house.Bounds;
            if (bounds.size.sqrMagnitude < 0.0001f)
            {
                Warn("房子的包围盒是空的，没法采点");
                return false;
            }

            for (int i = 0; i < maxTries; i++)
            {
                // y 用女仆自己的高度：房子高 6 格，按包围盒中高采点离地板 3 格，
                // SamplePosition 的 2 格半径根本够不着；而且同层才是想去的地方
                Vector3 candidate = new Vector3(
                    Mathf.Lerp(bounds.min.x, bounds.max.x, (float)rng.NextDouble()),
                    from.y,
                    Mathf.Lerp(bounds.min.z, bounds.max.z, (float)rng.NextDouble()));

                NavMeshHit hit;
                if (!NavMesh.SamplePosition(candidate, out hit, 2f, NavMesh.AllAreas))
                {
                    continue;
                }

                Vector3 delta = hit.position - from;
                delta.y = 0f;
                if (delta.magnitude < minDistance)
                {
                    continue;
                }

                point = hit.position;
                return true;
            }

            Warn("采点 " + maxTries + " 次都没落到 NavMesh 上，包围盒 " + bounds.min + " ~ " + bounds.max);
            return false;
        }

        /// <summary>把脚底吸附到网上。女仆可能被摆在半空、或者摆在格表之外。</summary>
        public bool TrySnap(Vector3 world, float maxDistance, out Vector3 snapped)
        {
            NavMeshHit hit;
            if (NavMesh.SamplePosition(world, out hit, maxDistance, NavMesh.AllAreas))
            {
                snapped = hit.position;
                return true;
            }

            snapped = world;
            return false;
        }

        float _nextWarnTime;

        void Warn(string message)
        {
            if (Time.unscaledTime < _nextWarnTime)
            {
                return;
            }

            _nextWarnTime = Time.unscaledTime + 5f;
            Debug.LogWarning("[寻路] " + message);
        }

    }
}

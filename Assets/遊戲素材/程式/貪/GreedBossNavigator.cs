using System.Collections.Generic;
using UnityEngine;

namespace PilgrimOfSin.StateMachine
{
    /// <summary>
    /// 貪 Boss 的追擊路徑規劃（純 C# 類別，由 GreedBossController 持有）。
    ///
    /// 為什麼需要它：Boss 是 kinematic、直線朝玩家走，場上有很寬的天秤跟滿地錢袋。
    /// 只做「貼著牆繞」的區域閃避，遇到兩個障礙物夾住的走廊（天秤面 + 錢袋）就會在原地左右擺盪、
    /// 永遠繞不出去；放行穿過去又會穿模。這裡改成真正規劃路線：
    ///   1. 直線走得通 → 直接走（絕大多數時候，只花一次 CapsuleCast）。
    ///   2. 走不通 → 在 Boss 與玩家周圍的格子地圖上跑 A*，把固定物與錢袋都當障礙，
    ///      再把路徑拉直成「看得到的最遠一個路點」讓 Boss 朝它走。
    /// 格子是否被擋用 Physics.CheckCapsule 現場查，所以滾動中的錢袋也會被算進去（路徑定期重算）。
    /// 實際移動仍由 GreedBossController 的防穿模邏輯把關，這裡只負責「該往哪走」。
    /// </summary>
    public class GreedBossNavigator
    {
        private const float Cell = 0.5f;            // 格子邊長（m）
        private const float Margin = 6f;            // 地圖範圍：Boss 與玩家的外框再往外擴多少
        private const int MaxCells = 200;           // 單邊最多格數（保護上限）
        private const int MaxExpansions = 12000;    // A* 最多展開節點數
        private const float RefreshInterval = 0.35f;
        private const float ReachDistance = 0.6f;
        private const float LineBackoff = 0.3f;     // 直線檢查起點往後退的距離
        private const float ProbeBottom = 0.3f;     // 探測膠囊離地高度（避開地板）
        private const float ProbeTop = 2.7f;

        private readonly float _gridRadius;         // 格子地圖用的半徑（Boss 半徑 + 餘裕）
        private readonly float _lineRadius;         // 直線檢查用的半徑（Boss 實際半徑）
        private readonly int _mask;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private readonly List<int> _heap = new List<int>();
        private readonly List<Vector3> _path = new List<Vector3>();

        // 格子地圖（每次重算時重建；狀態 0=未查 1=可走 2=被擋）
        private int _w, _h;
        private float _ox, _oz;
        private byte[] _state = new byte[0];
        private float[] _g = new float[0];
        private float[] _f = new float[0];
        private int[] _parent = new int[0];
        private bool[] _closed = new bool[0];

        private Vector3 _target;
        private bool _hasTarget;
        private float _nextRefresh;

        public GreedBossNavigator(float bossRadius, int obstacleMask)
        {
            _lineRadius = bossRadius;
            _gridRadius = bossRadius + 0.15f;
            _mask = obstacleMask;
        }

        /// <summary>Boss 現在該朝哪個點走。直線走得通就回傳玩家位置本身。</summary>
        public Vector3 GetSteerTarget(Vector3 from, Vector3 to)
        {
            if (LineClear(from, to))
            {
                _hasTarget = false;
                return to;
            }

            Vector3 flatFrom = from; flatFrom.y = 0f;
            bool reached = _hasTarget && (Flat(_target) - flatFrom).sqrMagnitude < ReachDistance * ReachDistance;
            if (!_hasTarget || reached || Time.time >= _nextRefresh)
            {
                _target = PlanTarget(from, to);
                _hasTarget = true;
                _nextRefresh = Time.time + RefreshInterval;
            }
            return _target;
        }

        public void Reset() => _hasTarget = false;

        // ════════════════════════════════════════════════════════════
        //  直線檢查
        // ════════════════════════════════════════════════════════════

        private bool LineClear(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from; d.y = 0f;
            float dist = d.magnitude;
            if (dist < 0.01f) return true;
            d /= dist;

            // Boss 常常剛好「貼著」天秤的面（間隙 0）：從原地掃出去，面前那面牆會回報 distance = 0，
            // 分不清是「正對著牆」還是「只是貼在旁邊」，只好忽略，結果就是誤判成直線暢通、一頭撞上去。
            // 所以起點往後退一小段再掃，面前的牆就會是 distance > 0 的正常命中。
            Vector3 origin = from - d * LineBackoff;
            Vector3 p1 = new Vector3(origin.x, from.y + ProbeBottom + _lineRadius, origin.z);
            Vector3 p2 = new Vector3(origin.x, from.y + ProbeTop - _lineRadius, origin.z);
            int n = Physics.CapsuleCastNonAlloc(p1, p2, _lineRadius, d, _hits, dist + LineBackoff, _mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                // 起點就重疊的（例如退後那一小段剛好碰到身後的牆）、或只是平行擦過表面的不算擋路
                if (_hits[i].distance <= 0.001f) continue;
                if (Vector3.Dot(d, _hits[i].normal) >= -0.01f) continue;
                return false;
            }
            return true;
        }

        // ════════════════════════════════════════════════════════════
        //  A* 規劃
        // ════════════════════════════════════════════════════════════

        private Vector3 PlanTarget(Vector3 from, Vector3 to)
        {
            BuildGrid(from, to);

            int start = CellIndex(from);
            int goal = CellIndex(to);
            _state[start] = 1;   // 起點、終點一律當可走（Boss 貼牆、玩家站在錢袋旁時格子會被餘裕判成被擋）
            _state[goal] = 1;

            int bestNode = RunAStar(start, goal);   // 到得了就是 goal；到不了是「離玩家最近的已展開格」
            BuildPath(start, bestNode, from.y);

            // 把路徑拉直：取從 Boss 位置「直線看得到」的最遠路點（只在重算時做，成本可接受）
            for (int i = _path.Count - 1; i >= 1; i--)
                if (LineClear(from, _path[i])) return _path[i];
            return _path.Count > 1 ? _path[1] : to;
        }

        private void BuildGrid(Vector3 from, Vector3 to)
        {
            float minX = Mathf.Min(from.x, to.x) - Margin, maxX = Mathf.Max(from.x, to.x) + Margin;
            float minZ = Mathf.Min(from.z, to.z) - Margin, maxZ = Mathf.Max(from.z, to.z) + Margin;
            _ox = minX; _oz = minZ;
            _w = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / Cell), 4, MaxCells);
            _h = Mathf.Clamp(Mathf.CeilToInt((maxZ - minZ) / Cell), 4, MaxCells);

            int size = _w * _h;
            if (_state.Length < size)
            {
                _state = new byte[size];
                _g = new float[size];
                _f = new float[size];
                _parent = new int[size];
                _closed = new bool[size];
            }
            System.Array.Clear(_state, 0, size);
            System.Array.Clear(_closed, 0, size);
            for (int i = 0; i < size; i++) _g[i] = float.PositiveInfinity;
            _heap.Clear();
        }

        private int RunAStar(int start, int goal)
        {
            int goalX = goal % _w, goalZ = goal / _w;
            int best = start;
            float bestH = Heuristic(start % _w, start / _w, goalX, goalZ);

            _g[start] = 0f;
            _f[start] = bestH;
            _parent[start] = -1;
            HeapPush(start);

            int expansions = 0;
            while (_heap.Count > 0 && expansions < MaxExpansions)
            {
                int cur = HeapPop();
                if (_closed[cur]) continue;
                _closed[cur] = true;
                expansions++;

                if (cur == goal) return goal;

                int cx = cur % _w, cz = cur / _w;
                float h = Heuristic(cx, cz, goalX, goalZ);
                if (h < bestH) { bestH = h; best = cur; }

                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= _w || nz >= _h) continue;
                        int ni = nz * _w + nx;
                        if (_closed[ni] || !IsFree(nx, nz)) continue;
                        // 斜走不能切過牆角：兩側的直角鄰格都要可走
                        if (dx != 0 && dz != 0 && (!IsFree(cx + dx, cz) || !IsFree(cx, cz + dz))) continue;

                        float ng = _g[cur] + (dx != 0 && dz != 0 ? 1.4142f : 1f);
                        if (ng >= _g[ni]) continue;   // 沒有比已知的路更短

                        _g[ni] = ng;
                        _f[ni] = ng + Heuristic(nx, nz, goalX, goalZ);
                        _parent[ni] = cur;
                        HeapPush(ni);
                    }
                }
            }
            return best;
        }

        private void BuildPath(int start, int end, float y)
        {
            _path.Clear();
            for (int n = end; n != -1; n = _parent[n])
            {
                _path.Add(CellCenter(n, y));
                if (n == start) break;
            }
            _path.Reverse();
        }

        // ════════════════════════════════════════════════════════════
        //  格子工具
        // ════════════════════════════════════════════════════════════

        private bool IsFree(int x, int z)
        {
            int i = z * _w + x;
            if (_state[i] == 0)
            {
                Vector3 c = CellCenterXZ(x, z);
                Vector3 p1 = new Vector3(c.x, ProbeBottom + _gridRadius, c.z);
                Vector3 p2 = new Vector3(c.x, ProbeTop - _gridRadius, c.z);
                _state[i] = (byte)(Physics.CheckCapsule(p1, p2, _gridRadius, _mask, QueryTriggerInteraction.Ignore) ? 2 : 1);
            }
            return _state[i] == 1;
        }

        private int CellIndex(Vector3 p)
        {
            int x = Mathf.Clamp(Mathf.FloorToInt((p.x - _ox) / Cell), 0, _w - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt((p.z - _oz) / Cell), 0, _h - 1);
            return z * _w + x;
        }

        private Vector3 CellCenterXZ(int x, int z) => new Vector3(_ox + (x + 0.5f) * Cell, 0f, _oz + (z + 0.5f) * Cell);
        private Vector3 CellCenter(int index, float y)
        {
            Vector3 c = CellCenterXZ(index % _w, index / _w);
            c.y = y;
            return c;
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        private static float Heuristic(int x, int z, int gx, int gz)
        {
            float dx = Mathf.Abs(x - gx), dz = Mathf.Abs(z - gz);
            return (dx + dz) + (1.4142f - 2f) * Mathf.Min(dx, dz);   // octile
        }

        // ════════════════════════════════════════════════════════════
        //  二元堆（依 _f 由小到大）
        // ════════════════════════════════════════════════════════════

        private void HeapPush(int node)
        {
            _heap.Add(node);
            int i = _heap.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (_f[_heap[p]] <= _f[_heap[i]]) break;
                (_heap[p], _heap[i]) = (_heap[i], _heap[p]);
                i = p;
            }
        }

        private int HeapPop()
        {
            int top = _heap[0];
            int last = _heap[_heap.Count - 1];
            _heap.RemoveAt(_heap.Count - 1);
            if (_heap.Count > 0)
            {
                _heap[0] = last;
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1, r = l + 1, m = i;
                    if (l < _heap.Count && _f[_heap[l]] < _f[_heap[m]]) m = l;
                    if (r < _heap.Count && _f[_heap[r]] < _f[_heap[m]]) m = r;
                    if (m == i) break;
                    (_heap[m], _heap[i]) = (_heap[i], _heap[m]);
                    i = m;
                }
            }
            return top;
        }
    }
}

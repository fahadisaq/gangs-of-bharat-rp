using System;
using System.Collections.Generic;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Shared.World
{
    /// <summary>
    /// Uniform grid over the city (default 64 m cells, 128 x 128 for 8 km). Entities are kept in
    /// intrusive doubly linked lists per cell, so insert, move and remove are O(1) and queries do
    /// not allocate. Entity ids are small non-negative integers (array indices).
    /// </summary>
    public sealed class SpatialHashGrid
    {
        public const float DefaultCellSize = 64f;

        private readonly float _cellSize;
        private readonly float _invCellSize;
        private readonly float _minX;
        private readonly float _minZ;
        private readonly int _columns;
        private readonly int _rows;
        private readonly int[] _cellHead;

        private int[] _next = new int[0];
        private int[] _prev = new int[0];
        private int[] _cellOf = new int[0];
        private float[] _x = new float[0];
        private float[] _z = new float[0];
        private bool[] _present = new bool[0];
        private int _count;

        public SpatialHashGrid()
            : this(DefaultCellSize, WorldBounds.MinX, WorldBounds.MinZ, WorldBounds.Size, WorldBounds.Size)
        {
        }

        public SpatialHashGrid(float cellSize)
            : this(cellSize, WorldBounds.MinX, WorldBounds.MinZ, WorldBounds.Size, WorldBounds.Size)
        {
        }

        public SpatialHashGrid(float cellSize, float minX, float minZ, float width, float depth)
        {
            if (cellSize <= 0f)
            {
                throw new ArgumentOutOfRangeException("cellSize");
            }
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
            _minX = minX;
            _minZ = minZ;
            _columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
            _rows = Math.Max(1, (int)Math.Ceiling(depth / cellSize));
            _cellHead = new int[_columns * _rows];
            for (int i = 0; i < _cellHead.Length; i++)
            {
                _cellHead[i] = -1;
            }
            EnsureCapacity(256);
        }

        public float CellSize
        {
            get { return _cellSize; }
        }

        public int Count
        {
            get { return _count; }
        }

        public int Columns
        {
            get { return _columns; }
        }

        public int Rows
        {
            get { return _rows; }
        }

        private void EnsureCapacity(int id)
        {
            if (id < _present.Length)
            {
                return;
            }
            int size = Math.Max(16, _present.Length);
            while (size <= id)
            {
                size *= 2;
            }
            Array.Resize(ref _next, size);
            Array.Resize(ref _prev, size);
            Array.Resize(ref _cellOf, size);
            Array.Resize(ref _x, size);
            Array.Resize(ref _z, size);
            Array.Resize(ref _present, size);
        }

        private int ColumnOf(float x)
        {
            int c = (int)((x - _minX) * _invCellSize);
            if (c < 0)
            {
                return 0;
            }
            return c >= _columns ? _columns - 1 : c;
        }

        private int RowOf(float z)
        {
            int r = (int)((z - _minZ) * _invCellSize);
            if (r < 0)
            {
                return 0;
            }
            return r >= _rows ? _rows - 1 : r;
        }

        public bool Contains(int id)
        {
            return id >= 0 && id < _present.Length && _present[id];
        }

        public bool TryGetPosition(int id, out float x, out float z)
        {
            if (!Contains(id))
            {
                x = 0f;
                z = 0f;
                return false;
            }
            x = _x[id];
            z = _z[id];
            return true;
        }

        public void Insert(int id, float x, float z)
        {
            if (id < 0)
            {
                throw new ArgumentOutOfRangeException("id");
            }
            EnsureCapacity(id);
            if (_present[id])
            {
                Move(id, x, z);
                return;
            }
            _present[id] = true;
            _x[id] = x;
            _z[id] = z;
            Link(id, RowOf(z) * _columns + ColumnOf(x));
            _count++;
        }

        public void Move(int id, float x, float z)
        {
            if (!Contains(id))
            {
                Insert(id, x, z);
                return;
            }
            _x[id] = x;
            _z[id] = z;
            int cell = RowOf(z) * _columns + ColumnOf(x);
            if (cell != _cellOf[id])
            {
                Unlink(id);
                Link(id, cell);
            }
        }

        public bool Remove(int id)
        {
            if (!Contains(id))
            {
                return false;
            }
            Unlink(id);
            _present[id] = false;
            _count--;
            return true;
        }

        private void Link(int id, int cell)
        {
            int head = _cellHead[cell];
            _next[id] = head;
            _prev[id] = -1;
            if (head >= 0)
            {
                _prev[head] = id;
            }
            _cellHead[cell] = id;
            _cellOf[id] = cell;
        }

        private void Unlink(int id)
        {
            int cell = _cellOf[id];
            int next = _next[id];
            int prev = _prev[id];
            if (prev >= 0)
            {
                _next[prev] = next;
            }
            else
            {
                _cellHead[cell] = next;
            }
            if (next >= 0)
            {
                _prev[next] = prev;
            }
            _next[id] = -1;
            _prev[id] = -1;
        }

        /// <summary>
        /// Clears <paramref name="results"/> and fills it with ids within radius of (x, z)
        /// (inclusive). No allocation once the list has grown to its working size.
        /// </summary>
        public void QueryRadius(float x, float z, float radius, List<int> results)
        {
            results.Clear();
            float r2 = radius * radius;
            int c0 = ColumnOf(x - radius);
            int c1 = ColumnOf(x + radius);
            int r0 = RowOf(z - radius);
            int r1 = RowOf(z + radius);
            for (int row = r0; row <= r1; row++)
            {
                int rowBase = row * _columns;
                for (int col = c0; col <= c1; col++)
                {
                    int id = _cellHead[rowBase + col];
                    while (id >= 0)
                    {
                        float dx = _x[id] - x;
                        float dz = _z[id] - z;
                        if (dx * dx + dz * dz <= r2)
                        {
                            results.Add(id);
                        }
                        id = _next[id];
                    }
                }
            }
        }
    }
}

using System.Collections;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Proxy.Mesh
{
    public static partial class MeshUtils
    {
        public static void ComputeVertexNeighbours(
    NativeArray<int> triangles,
    int vertexCount,
    out NativeArray<int> neighbourCounts,
    out NativeArray<int> neighbourIndices,
    out NativeArray<int> neighbourStartOffsets,
    int maxNeighbours = int.MaxValue,
    Allocator allocator = Allocator.Temp)
        {
            int triangleCount = triangles.Length / 3;

            // 1. Собираем все рёбра (каноническая форма: меньший индекс первым)
            NativeArray<int2> edges = new NativeArray<int2>(triangleCount * 3, Allocator.Temp);
            for (int i = 0; i < triangleCount; i++)
            {
                int i0 = triangles[i * 3];
                int i1 = triangles[i * 3 + 1];
                int i2 = triangles[i * 3 + 2];

                edges[i * 3] = new int2(math.min(i0, i1), math.max(i0, i1));
                edges[i * 3 + 1] = new int2(math.min(i1, i2), math.max(i1, i2));
                edges[i * 3 + 2] = new int2(math.min(i2, i0), math.max(i2, i0));
            }

            // 2. Сортируем рёбра
            edges.Sort(new Int2Comparer());

            // 3. Удаляем дубликаты
            NativeList<int2> uniqueEdges = new NativeList<int2>(Allocator.Temp);
            if (edges.Length > 0)
            {
                uniqueEdges.Add(edges[0]);
                for (int i = 1; i < edges.Length; i++)
                {
                    if (!edges[i].Equals(uniqueEdges[uniqueEdges.Length - 1]))
                        uniqueEdges.Add(edges[i]);
                }
            }

            // 4. Подсчитываем фактическое количество уникальных соседей
            NativeArray<int> actualCounts = new NativeArray<int>(vertexCount, Allocator.Temp);
            for (int i = 0; i < uniqueEdges.Length; i++)
            {
                int2 edge = uniqueEdges[i];
                actualCounts[edge.x]++;
                actualCounts[edge.y]++;
            }

            // 5. Ограничиваем количество соседей по maxNeighbours
            neighbourCounts = new NativeArray<int>(vertexCount, allocator);
            int totalNeighbours = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                int limited = actualCounts[i];
                if (maxNeighbours > 0 && limited > maxNeighbours)
                    limited = maxNeighbours;
                neighbourCounts[i] = limited;
                totalNeighbours += limited;
            }

            // 6. Вычисляем смещения (префиксная сумма)
            neighbourStartOffsets = new NativeArray<int>(vertexCount, allocator);
            int offset = 0;
            for (int i = 0; i < vertexCount; i++)
            {
                neighbourStartOffsets[i] = offset;
                offset += neighbourCounts[i];
            }

            // 7. Заполняем плоский массив соседей
            neighbourIndices = new NativeArray<int>(totalNeighbours, allocator);
            NativeArray<int> currentPos = new NativeArray<int>(vertexCount, Allocator.Temp);
            NativeArray<int>.Copy(neighbourStartOffsets, currentPos, vertexCount);

            for (int i = 0; i < uniqueEdges.Length; i++)
            {
                int2 edge = uniqueEdges[i];
                int v0 = edge.x;
                int v1 = edge.y;

                // Добавляем v1 как соседа для v0, если лимит не исчерпан
                if (currentPos[v0] - neighbourStartOffsets[v0] < neighbourCounts[v0])
                {
                    neighbourIndices[currentPos[v0]] = v1;
                    currentPos[v0]++;
                }

                // Добавляем v0 как соседа для v1, если лимит не исчерпан
                if (currentPos[v1] - neighbourStartOffsets[v1] < neighbourCounts[v1])
                {
                    neighbourIndices[currentPos[v1]] = v0;
                    currentPos[v1]++;
                }
            }

            // Очистка временных массивов
            edges.Dispose();
            uniqueEdges.Dispose();
            actualCounts.Dispose();
            currentPos.Dispose();
        }

        private struct Int2Comparer : System.Collections.Generic.IComparer<int2>
        {
            public int Compare(int2 a, int2 b)
            {
                if (a.x < b.x) return -1;
                if (a.x > b.x) return 1;
                if (a.y < b.y) return -1;
                if (a.y > b.y) return 1;
                return 0;
            }
        }
    }
}
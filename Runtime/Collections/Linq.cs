using System.Collections.Generic;
using UnityEngine;

namespace Proxy.Mesh
{
    public static class Linq
    {
        public static int GetIndex<T>(this IEnumerable<T> enumerable, T item)
        {
            int index = 0;
            foreach(T U in enumerable)
            {
                if (Equals(U, item))
                {
                    return index;
                }
                index++;
            }
            return -1;
        }
    }
}
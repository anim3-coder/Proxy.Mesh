namespace Proxy.Collections
{
    using System;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Burst;

    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct NativeArray2D<T> : IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        internal void* m_Buffer; // Указатель на данные
        internal int m_Length;   // Общее количество элементов
        internal int m_Width;    // Ширина
        internal int m_Height;   // Высота
        internal Allocator m_AllocatorLabel;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<NativeArray2D<T>>();
#endif

        public NativeArray2D(int width, int height, Allocator allocator)
        {
            long totalSize = UnsafeUtility.SizeOf<T>() * (long)width * height;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent", nameof(allocator));
            if (!UnsafeUtility.IsBlittable<T>())
                throw new ArgumentException($"Type {typeof(T)} must be blittable.");
#endif

            m_Buffer = UnsafeUtility.Malloc(totalSize, UnsafeUtility.AlignOf<T>(), allocator);
            UnsafeUtility.MemClear(m_Buffer, totalSize);

            m_Length = width * height;
            m_Width = width;
            m_Height = height;
            m_AllocatorLabel = allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = m_Length - 1;
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<NativeArray2D<T>>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }

        // Индексатор для доступа по двум координатам
        public T this[int y, int x]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                if (x < 0 || x >= m_Width || y < 0 || y >= m_Height)
                    throw new IndexOutOfRangeException();
#endif
                // Плоский индекс = y * ширина + x
                return UnsafeUtility.ReadArrayElement<T>(m_Buffer, y * m_Width + x);
            }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                if (x < 0 || x >= m_Width || y < 0 || y >= m_Height)
                    throw new IndexOutOfRangeException();
#endif
                UnsafeUtility.WriteArrayElement(m_Buffer, y * m_Width + x, value);
            }
        }

        public int Width => m_Width;
        public int Height => m_Height;

        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            m_Buffer = null;
            m_Length = 0;
        }
    }
}
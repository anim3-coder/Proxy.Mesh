namespace Proxy.Collections
{
    using System;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;
    using Unity.Burst;

    [NativeContainer]
    [NativeContainerSupportsMinMaxWriteRestriction]
    public unsafe struct NativeJaggedArray<T> : IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        internal void* m_Buffer; // Указатель на массив указателей
        internal int m_NumRows;
        internal Allocator m_AllocatorLabel;
        internal int* m_RowLengths; // Указатель на массив длин строк

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;
        internal int m_MinIndex;
        internal int m_MaxIndex;
        private static readonly SharedStatic<int> s_staticSafetyId = SharedStatic<int>.GetOrCreate<NativeJaggedArray<T>>();
#endif

        public NativeJaggedArray(int numRows, Allocator allocator)
        {
            m_NumRows = numRows;
            m_AllocatorLabel = allocator;

            // Выделяем память под массив указателей и массив длин строк
            long ptrArraySize = UnsafeUtility.SizeOf<IntPtr>() * numRows;
            long lengthsArraySize = UnsafeUtility.SizeOf<int>() * numRows;

            m_Buffer = UnsafeUtility.Malloc(ptrArraySize, UnsafeUtility.AlignOf<IntPtr>(), allocator);
            m_RowLengths = (int*)UnsafeUtility.Malloc(lengthsArraySize, UnsafeUtility.AlignOf<int>(), allocator);

            UnsafeUtility.MemClear(m_Buffer, ptrArraySize);
            UnsafeUtility.MemClear(m_RowLengths, lengthsArraySize);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_MinIndex = 0;
            m_MaxIndex = numRows - 1;
            m_Safety = CollectionHelper.CreateSafetyHandle(allocator);
            CollectionHelper.SetStaticSafetyId<NativeJaggedArray<T>>(ref m_Safety, ref s_staticSafetyId.Data);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif
        }

        // Установить размер и выделить память для конкретной строки
        public void AllocateRow(int row, int length)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            if (row < 0 || row >= m_NumRows)
                throw new IndexOutOfRangeException();
#endif
            IntPtr* rowPtrs = (IntPtr*)m_Buffer;
            if (rowPtrs[row] != IntPtr.Zero)
            {
                UnsafeUtility.Free((void*)rowPtrs[row], m_AllocatorLabel);
            }

            long size = UnsafeUtility.SizeOf<T>() * length;
            void* newRow = UnsafeUtility.Malloc(size, UnsafeUtility.AlignOf<T>(), m_AllocatorLabel);
            UnsafeUtility.MemClear(newRow, size);

            rowPtrs[row] = (IntPtr)newRow;
            m_RowLengths[row] = length;
        }

        // Доступ к элементу в рваном массиве
        public T this[int row, int col]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                if (row < 0 || row >= m_NumRows || col < 0 || col >= m_RowLengths[row])
                    throw new IndexOutOfRangeException();
#endif
                IntPtr* rowPtrs = (IntPtr*)m_Buffer;
                return UnsafeUtility.ReadArrayElement<T>((void*)rowPtrs[row], col);
            }
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                if (row < 0 || row >= m_NumRows || col < 0 || col >= m_RowLengths[row])
                    throw new IndexOutOfRangeException();
#endif
                IntPtr* rowPtrs = (IntPtr*)m_Buffer;
                UnsafeUtility.WriteArrayElement((void*)rowPtrs[row], col, value);
            }
        }

        public int GetRowLength(int row)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            if (row < 0 || row >= m_NumRows)
                throw new IndexOutOfRangeException();
#endif
            return m_RowLengths[row];
        }

        public void Dispose()
        {
            IntPtr* rowPtrs = (IntPtr*)m_Buffer;
            for (int i = 0; i < m_NumRows; i++)
            {
                if (rowPtrs[i] != IntPtr.Zero)
                {
                    UnsafeUtility.Free((void*)rowPtrs[i], m_AllocatorLabel);
                }
            }

            UnsafeUtility.Free(m_Buffer, m_AllocatorLabel);
            UnsafeUtility.Free(m_RowLengths, m_AllocatorLabel);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            CollectionHelper.DisposeSafetyHandle(ref m_Safety);
#endif
            m_Buffer = null;
            m_RowLengths = null;
            m_NumRows = 0;
        }
    }
}
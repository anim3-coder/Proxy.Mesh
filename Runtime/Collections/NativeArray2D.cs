namespace Proxy.Collections
{
    using System;
    using System.Diagnostics;
    using Unity.Collections;
    using Unity.Collections.LowLevel.Unsafe;

    [DebuggerDisplay("Length0 = {Length0}, Length1 = {Length1}")]
    [DebuggerTypeProxy(typeof(NativeArray2DDebugView<>))]
    [NativeContainer]
    [NativeContainerSupportsDeallocateOnJobCompletion]
    public unsafe struct NativeArray2D<T> : IDisposable where T : unmanaged
    {
        [NativeDisableUnsafePtrRestriction]
        private void* m_Buffer;

        private int m_Length0;

        private int m_Length1;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;

        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel m_DisposeSentinel;
#endif

        internal Allocator m_Allocator;

        public NativeArray2D(
            int length0,
            int length1,
            Allocator allocator,
            NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            int length = length0 * length1;

            m_Buffer = UnsafeUtility.Malloc(
                length * UnsafeUtility.SizeOf<T>(),
                UnsafeUtility.AlignOf<T>(),
                allocator);
            m_Length0 = length0;
            m_Length1 = length1;
            m_Allocator = allocator;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(
                out m_Safety,
                out m_DisposeSentinel,
                1,
                allocator);
#endif
            if ((options & NativeArrayOptions.ClearMemory)
                == NativeArrayOptions.ClearMemory)
            {
                UnsafeUtility.MemClear(
                    m_Buffer,
                    Length * (long)UnsafeUtility.SizeOf<T>());
            }
        }

        public int Length
        {
            get
            {
                return m_Length0 * m_Length1;
            }
        }

        public int Length0
        {
            get
            {
                return m_Length0;
            }
        }

        public int Length1
        {
            get
            {
                return m_Length1;
            }
        }

        public T this[int index0, int index1]
        {
            get
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
                if (index0 < 0 || index0 >= m_Length0)
                {
                    throw new IndexOutOfRangeException();
                }
                if (index1 < 0 || index1 >= m_Length1)
                {
                    throw new IndexOutOfRangeException();
                }
#endif

                int index = index1 * m_Length0 + index0;
                return UnsafeUtility.ReadArrayElement<T>(m_Buffer, index);
            }

            [WriteAccessRequired]
            set
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
                if (index0 < 0 || index0 >= m_Length0)
                {
                    throw new IndexOutOfRangeException();
                }
                if (index1 < 0 || index1 >= m_Length1)
                {
                    throw new IndexOutOfRangeException();
                }
#endif

                int index = index1 * m_Length0 + index0;
                UnsafeUtility.WriteArrayElement(m_Buffer, index, value);
            }
        }

        public NativeSlice<T> GetSlice(int subArrayIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
            if (subArrayIndex < 0 || subArrayIndex >= m_Length1)
                throw new IndexOutOfRangeException();
#endif
            void* sliceStart = (byte*)m_Buffer + subArrayIndex * m_Length0 * UnsafeUtility.SizeOf<T>();

            var slice = NativeSliceUnsafeUtility.ConvertExistingDataToNativeSlice<T>(
                sliceStart,
                UnsafeUtility.SizeOf<T>(),
                m_Length0);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            NativeSliceUnsafeUtility.SetAtomicSafetyHandle(ref slice, m_Safety);
#endif
            return slice;
        }

        public bool IsEmpty
        {
            get
            {
                return (IntPtr)m_Buffer == IntPtr.Zero;
            }
        }


        public bool IsCreated
        {
            get
            {
                return (IntPtr)m_Buffer != IntPtr.Zero;
            }
        }

        [WriteAccessRequired]
        public void Dispose()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
            DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif
            UnsafeUtility.Free(m_Buffer, m_Allocator);
            m_Buffer = null;
            m_Length0 = 0;
            m_Length1 = 0;
        }

        public T[,] ToArray()
        {
            T[,] dst = new T[m_Length0, m_Length1];
            Copy(this, dst);
            return dst;
        }

        private static void Copy(NativeArray2D<T> src, T[,] dest)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(src.m_Safety);
#endif

            if (src.Length0 != dest.GetLength(0)
                || src.Length1 != dest.GetLength(1))
            {
                throw new ArgumentException("Arrays must have the same size");
            }

            for (int index0 = 0; index0 < src.Length0; ++index0)
            {
                for (int index1 = 0; index1 < src.Length1; ++index1)
                {
                    dest[index0, index1] = src[index0, index1];
                }
            }
        }
    }

    internal sealed class NativeArray2DDebugView<T> where T : unmanaged
    {
        private readonly NativeArray2D<T> m_Array;

        public NativeArray2DDebugView(NativeArray2D<T> array)
        {
            m_Array = array;
        }
        public T[,] Items
        {
            get
            {
                return m_Array.ToArray();
            }
        }
    }
}
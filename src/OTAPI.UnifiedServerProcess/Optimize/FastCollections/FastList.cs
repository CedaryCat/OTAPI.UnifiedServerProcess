using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Optimize.FastCollections
{
    public sealed class FastList<T> where T : class
    {
        private const int InitialCapacity = 32;
        // private const int MaxCapacity = 256;

        private T[] items;
        private int count;

        public int Count => count;
        public int Capacity => items.Length;

        public T this[int index] => items[index];

        public FastList() {
            items = new T[InitialCapacity];
            count = 0;
        }
        public FastList(FastList<T> other) {
            items = new T[other.items.Length];
            count = other.count;

            Array.Copy(
                sourceArray: other.items,
                destinationArray: items,
                length: count);
        }
        public FastList<T> Copy() {
            return new FastList<T>(this);
        }

        public void Add(T item) {
            if (count < items.Length) {
                items[count] = item;
                count++;
            }
            else {
                AddWithGrow(item);
            }
        }
        public T Last() => items[count - 1];

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void AddWithGrow(T item) {
            GrowArray();
            items[count] = item;
            count++;
        }

        private void GrowArray() {
            int newSize = items.Length * 2;
            //    switch {
            //    < 64 => items.Length * 2,
            //    < 128 => 128,
            //    < 256 => 256,
            //    _ => throw new InvalidOperationException(
            //        $"Exceeded maximum capacity of {MaxCapacity}")
            //};

            var newArray = new T[newSize];
            Array.Copy(items, newArray, count);
            items = newArray;
        }
        public void Reverse() {
            switch (count) {
                case 0: case 1: return;
                case 2: Swap(0, 1); return;
                case 3: Swap(0, 2); return;
                case 4:
                    Swap(0, 3);
                    Swap(1, 2);
                    return;
                case 5:
                    Swap(0, 4);
                    Swap(1, 3);
                    return;
                case 6:
                    Swap(0, 5);
                    Swap(1, 4);
                    Swap(2, 3);
                    return;
                case 7:
                    Swap(0, 6);
                    Swap(1, 5);
                    Swap(2, 4);
                    return;
                case 8:
                    Swap(0, 7);
                    Swap(1, 6);
                    Swap(2, 5);
                    Swap(3, 4);
                    return;
            }

            int left = 0;
            int right = count - 1;

            int blocks = count / 8;
            while (blocks-- > 0) {
                Swap(left, right);
                Swap(left + 1, right - 1);
                Swap(left + 2, right - 2);
                Swap(left + 3, right - 3);
                left += 4;
                right -= 4;
            }

            while (left < right) {
                Swap(left, right);
                left++;
                right--;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Swap(int i, int j) {
            (items[i], items[j]) = (items[j], items[i]);
        }
        public FastList<T> CopyReversed() {
            var copy = new FastList<T> {
                items = new T[this.Capacity],
                count = this.count
            };

            for (int i = 0; i < count; i++) {
                copy.items[i] = this.items[count - 1 - i];
            }

            return copy;
        }
        public T[] ReverseToArray() {
            var copy = new T[this.count];

            for (int i = 0; i < count; i++) {
                copy[i] = this.items[count - 1 - i];
            }

            return copy;
        }

        public T[] ToArray() {
            var result = new T[count];
            Array.Copy(items, result, count);
            return result;
        }

        public void Clear() {
            Array.Clear(items, 0, count);
            count = 0;
        }
    }
}

using System;
using System.Collections.Generic;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class CollectionExt
    {
        public static List<T> CopyWithCapacity<T>(this List<T> list) {
            List<T> copy = new List<T>(Math.Max(list.Capacity, list.Count));
            foreach (var item in list) {
                copy.Add(item);
            }
            return copy;
        }
        public static List<T> CopyAddCapacity<T>(this List<T> list, int capacity) {
            List<T> copy = new List<T>(list.Capacity + capacity);
            foreach (var item in list) {
                copy.Add(item);
            }
            return copy;
        }
    }
}

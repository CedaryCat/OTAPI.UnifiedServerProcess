using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OTAPI.UnifiedServerProcess.Extensions
{
    public static class CollectionExt
    {
        public static List<T> CopyWithCapacity<T>(this List<T> list) {
            var copy = new List<T>(Math.Max(list.Capacity, list.Count));
            foreach (var item in list) { 
                copy.Add(item);
            }
            return copy;
        }
        public static List<T> CopyAddCapacity<T>(this List<T> list, int capacity) {
            var copy = new List<T>(list.Capacity + capacity);
            foreach (var item in list) {
                copy.Add(item);
            }
            return copy;
        }
    }
}

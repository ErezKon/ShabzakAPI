using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BL.Extensions
{
    /// <summary>
    /// Extension methods for IEnumerable collections.
    /// </summary>
    public static class EnumerableExtension
    {
        private static Random rng = new();

        /// <summary>
        /// Randomly shuffles an enumerable using the Fisher-Yates algorithm.
        /// Used by the auto-assign engine to randomize candidate order.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <param name="enumerable">The collection to shuffle.</param>
        /// <returns>A new shuffled list.</returns>
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> enumerable)
        {
            var list = enumerable.ToList();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                T value = list[k];
                list[k] = list[n];
                list[n] = value;
            }
            return list;
        }
    }
}

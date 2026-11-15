using System;
using System.Runtime.CompilerServices;

namespace Arenas
{
    /// <summary>
    /// Introsort over a span, ordered by a supplied comparison function pointer (there are no
    /// interfaces to hang an IComparer on).
    ///
    /// Quicksort with median-of-three, insertion sort for small ranges, and a heapsort fallback
    /// once recursion gets too deep - so adversarial input degrades to O(n log n) rather than
    /// O(n^2), and the depth bound keeps the recursion shallow enough for a small stack.
    /// </summary>
    public static unsafe class Sorting
    {
        private const int InsertionThreshold = 16;

        public static void Sort<T>(Span<T> items, delegate*<in T, in T, int> compare)
        {
            if (items.Length < 2)
                return;

            if (compare == null)
                Fail.Msg("Sorting: comparison function is null");

            Introsort(items, 0, items.Length - 1, 2 * Log2(items.Length), compare);
        }

        public static void Sort<T>(ArenaSlice<T> items, delegate*<in T, in T, int> compare)
            => Sort(items.AsSpan(), compare);

        public static bool IsSorted<T>(Span<T> items, delegate*<in T, in T, int> compare)
        {
            for (int i = 1; i < items.Length; i++)
            {
                if (compare(in items[i - 1], in items[i]) > 0)
                    return false;
            }

            return true;
        }

        /// <summary>Index of a matching element, or -1. The span must already be sorted.</summary>
        public static int BinarySearch<T>(Span<T> items, in T value, delegate*<in T, in T, int> compare)
        {
            int low = 0;
            int high = items.Length - 1;

            while (low <= high)
            {
                int middle = low + ((high - low) >> 1);
                int order = compare(in items[middle], in value);

                if (order == 0)
                    return middle;

                if (order < 0)
                    low = middle + 1;
                else
                    high = middle - 1;
            }

            return -1;
        }

        private static void Introsort<T>(Span<T> items, int low, int high, int depthLimit, delegate*<in T, in T, int> compare)
        {
            while (low < high)
            {
                int length = high - low + 1;

                if (length <= InsertionThreshold)
                {
                    InsertionSort(items, low, high, compare);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSort(items, low, high, compare);
                    return;
                }

                depthLimit--;

                int pivot = Partition(items, low, high, compare);

                // Recurse into the smaller side, loop on the larger: bounds stack depth.
                if (pivot - low < high - pivot)
                {
                    Introsort(items, low, pivot - 1, depthLimit, compare);
                    low = pivot + 1;
                }
                else
                {
                    Introsort(items, pivot + 1, high, depthLimit, compare);
                    high = pivot - 1;
                }
            }
        }

        private static int Partition<T>(Span<T> items, int low, int high, delegate*<in T, in T, int> compare)
        {
            int middle = low + ((high - low) >> 1);

            // Median of three, placed at high - 1.
            SwapIfGreater(items, low, middle, compare);
            SwapIfGreater(items, low, high, compare);
            SwapIfGreater(items, middle, high, compare);

            T pivot = items[middle];
            Swap(items, middle, high - 1);

            int left = low;
            int right = high - 1;

            while (left < right)
            {
                while (compare(in items[++left], in pivot) < 0)
                {
                    if (left == high)
                        break;
                }

                while (compare(in pivot, in items[--right]) < 0)
                {
                    if (right == low)
                        break;
                }

                if (left >= right)
                    break;

                Swap(items, left, right);
            }

            Swap(items, left, high - 1);
            return left;
        }

        private static void InsertionSort<T>(Span<T> items, int low, int high, delegate*<in T, in T, int> compare)
        {
            for (int i = low + 1; i <= high; i++)
            {
                T value = items[i];
                int j = i - 1;

                while (j >= low && compare(in value, in items[j]) < 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }

                items[j + 1] = value;
            }
        }

        private static void HeapSort<T>(Span<T> items, int low, int high, delegate*<in T, in T, int> compare)
        {
            int count = high - low + 1;

            for (int i = count / 2; i >= 1; i--)
                SiftDown(items, i, count, low, compare);

            for (int i = count; i > 1; i--)
            {
                Swap(items, low, low + i - 1);
                SiftDown(items, 1, i - 1, low, compare);
            }
        }

        private static void SiftDown<T>(Span<T> items, int start, int count, int low, delegate*<in T, in T, int> compare)
        {
            T value = items[low + start - 1];

            while (start <= count / 2)
            {
                int child = 2 * start;

                if (child < count && compare(in items[low + child - 1], in items[low + child]) < 0)
                    child++;

                if (compare(in items[low + child - 1], in value) < 0)
                    break;

                items[low + start - 1] = items[low + child - 1];
                start = child;
            }

            items[low + start - 1] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap<T>(Span<T> items, int a, int b)
        {
            if (a == b)
                return;

            T temporary = items[a];
            items[a] = items[b];
            items[b] = temporary;
        }

        private static void SwapIfGreater<T>(Span<T> items, int a, int b, delegate*<in T, in T, int> compare)
        {
            if (a != b && compare(in items[a], in items[b]) > 0)
                Swap(items, a, b);
        }

        private static int Log2(int value)
        {
            int result = 0;
            while (value > 1)
            {
                value >>= 1;
                result++;
            }

            return result;
        }
    }
}

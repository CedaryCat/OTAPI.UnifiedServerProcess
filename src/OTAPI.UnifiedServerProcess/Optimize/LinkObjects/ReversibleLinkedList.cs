namespace OTAPI.UnifiedServerProcess.Optimize.LinkObjects
{
    public class ReversibleLinkedList<TItem>
    {
        public ReversibleLinkedList() { }
        public ReversibleLinkedList(ReversibleLinkedList<TItem> copy) {
            tail = copy.tail;
        }
        Node? tail;
        public void Add(TItem item) {
            tail = new Node(tail, item);
        }
        public TItem[] ReverseToArray() {
            if (tail is null) {
                return [];
            }
            var current = tail;
            var result = new TItem[current.currentIndex + 1];

            var indexMax = current.currentIndex;

            while (current != null) {
                result[indexMax - current.currentIndex] = current.item;
                current = current.previous;
            }
            return result;
        }
        public TItem[] ToArray() {
            if (tail is null) {
                return [];
            }
            var curr = tail;
            var result = new TItem[curr.currentIndex + 1];
            while (curr != null) {
                result[curr.currentIndex] = curr.item;
                curr = curr.previous;
            }
            return result;
        }
        public int Count => tail?.currentIndex + 1 ?? 0;
        class Node(Node? previous, TItem item)
        {
            public readonly Node? previous = previous;
            public readonly int currentIndex = previous == null ? 0 : previous.currentIndex + 1;
            public readonly TItem item = item;
        }
    }
}

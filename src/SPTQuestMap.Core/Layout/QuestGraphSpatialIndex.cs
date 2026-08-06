using SPTQuestMap.Core.Models;

namespace SPTQuestMap.Core.Layout;

public sealed class QuestGraphSpatialIndex
{
    private readonly double _bucketSize;
    private readonly IReadOnlyDictionary<string, QuestNodePosition> _positions;
    private readonly Dictionary<(int X, int Y), string[]> _buckets;

    public QuestGraphSpatialIndex(IReadOnlyDictionary<string, QuestNodePosition> positions, double bucketSize = 512)
    {
        if (bucketSize <= 0) throw new ArgumentOutOfRangeException(nameof(bucketSize));
        _positions = positions ?? throw new ArgumentNullException(nameof(positions));
        _bucketSize = bucketSize;
        var buckets = new Dictionary<(int X, int Y), HashSet<string>>();
        foreach (var position in positions.Values)
        {
            var minX = Bucket(position.X);
            var maxX = Bucket(position.X + position.Width);
            var minY = Bucket(position.Y);
            var maxY = Bucket(position.Y + position.Height);
            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            {
                var key = (x, y);
                if (!buckets.TryGetValue(key, out var ids)) buckets[key] = ids = new HashSet<string>(StringComparer.Ordinal);
                ids.Add(position.QuestId);
            }
        }

        _buckets = buckets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<string> Query(QuestGraphRect bounds, double padding = 0)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        for (var x = Bucket(bounds.X - padding); x <= Bucket(bounds.Right + padding); x++)
        for (var y = Bucket(bounds.Y - padding); y <= Bucket(bounds.Bottom + padding); y++)
        {
            if (!_buckets.TryGetValue((x, y), out var ids)) continue;
            foreach (var id in ids)
            {
                if (_positions.TryGetValue(id, out var position) && bounds.Intersects(position, padding)) result.Add(id);
            }
        }

        return result.OrderBy(id => id, StringComparer.Ordinal).ToArray();
    }

    private int Bucket(double value) => (int)Math.Floor(value / _bucketSize);
}

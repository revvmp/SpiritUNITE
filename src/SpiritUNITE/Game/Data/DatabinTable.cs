using System.Collections.Frozen;

namespace SpiritUNITE.Game.Data;

public sealed class DatabinTable<TRow>
    where TRow : class
{
    private readonly FrozenDictionary<long, TRow> rowsByKey;

    public DatabinTable(string tableName, IReadOnlyList<TRow> rows, Func<TRow, long> keySelector)
    {
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"Game data table {tableName} has no rows");
        }

        var keyedRows = new Dictionary<long, TRow>(rows.Count);
        foreach (var row in rows)
        {
            if (!keyedRows.TryAdd(keySelector(row), row))
            {
                throw new InvalidDataException($"Game data table {tableName} repeats key {keySelector(row)}");
            }
        }

        TableName = tableName;
        Rows = rows;
        rowsByKey = keyedRows.ToFrozenDictionary();
    }

    public string TableName { get; }
    public IReadOnlyList<TRow> Rows { get; }

    public bool Contains(long key) => rowsByKey.ContainsKey(key);

    public TRow? Find(long key) => rowsByKey.GetValueOrDefault(key);
}

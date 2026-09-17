using System.Text;
















internal static class SqliteReader
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SQLite format 3\0");

    
    
    internal static List<string> ReadRowTexts(byte[] file)
    {
        var result = new List<string>();
        if (file == null || file.Length < 100) return result;
        try
        {
            if (!file.AsSpan(0, 16).SequenceEqual(Magic)) return result;

            int pageSize = (file[16] << 8) | file[17]; 
            if (pageSize == 1) pageSize = 65536;
            if (pageSize < 512 || pageSize > 65536 || (pageSize & (pageSize - 1)) != 0) return result;

            
            var roots = new List<int>();
            ReadTablePage(file, 1, pageSize, row =>
            {
                if (row.Count >= 4 && row[0] == "table" && int.TryParse(row[3], out var root) && root > 1)
                    roots.Add(root);
            });

            foreach (var root in roots)
            {
                ReadTablePage(file, root, pageSize, row =>
                {
                    var sb = new StringBuilder();
                    foreach (var col in row)
                    {
                        if (sb.Length > 0) sb.Append('\0');
                        sb.Append(col);
                    }
                    result.Add(sb.ToString());
                });
            }
        }
        catch { }
        return result;
    }

    
    
    private static void ReadTablePage(byte[] file, int page, int pageSize, Action<List<string>> onRow)
    {
        int pageStart = (page - 1) * pageSize;
        int contentStart = pageStart + (page == 1 ? 100 : 0);
        if (contentStart + 8 > file.Length) return;

        byte type = file[contentStart];
        int cellCount = ReadU16(file, contentStart + 3);
        if (cellCount < 0 || cellCount > 8192) return;

        if (type == 13) 
        {
            for (int i = 0; i < cellCount; i++)
            {
                int cellOff = ReadU16(file, contentStart + 8 + 2 * i);
                int cellAbs = pageStart + cellOff; 
                if (cellAbs < contentStart || cellAbs + 2 > file.Length) continue;
                int cp = cellAbs;
                var payloadLen = ReadVarint(file, ref cp);
                _ = ReadVarint(file, ref cp); 
                if (payloadLen > (ulong)(pageSize - 40)) continue; 
                if (cp + (int)payloadLen > file.Length) continue;
                var row = ParseRecord(file.AsSpan(cp, (int)payloadLen));
                if (row != null) onRow(row);
            }
        }
        else if (type == 5) 
        {
            int rightChild = ReadU32BE(file, contentStart + 8);
            if (rightChild > 1) ReadTablePage(file, rightChild, pageSize, onRow);
            for (int i = 0; i < cellCount; i++)
            {
                int cellOff = ReadU16(file, contentStart + 12 + 2 * i);
                int cellAbs = pageStart + cellOff; 
                if (cellAbs < contentStart || cellAbs + 5 > file.Length) continue;
                int child = ReadU32BE(file, cellAbs);
                if (child > 1) ReadTablePage(file, child, pageSize, onRow);
            }
        }
        
    }

    
    
    
    private static List<string>? ParseRecord(ReadOnlySpan<byte> record)
    {
        int pos = 0;
        var headerLen = ReadVarint(record, ref pos);
        if (headerLen == 0 || headerLen > (ulong)record.Length) return null;
        int headerEnd = (int)headerLen;

        var types = new List<long>();
        while (pos < headerEnd && pos < record.Length)
        {
            var t = ReadVarint(record, ref pos);
            types.Add((long)t);
        }
        if (pos != headerEnd) return null;

        var row = new List<string>(types.Count);
        foreach (var t in types)
        {
            if (t == 0) { row.Add(string.Empty); continue; }              
            if (t >= 13 && (t & 1) == 1)                                  
            {
                int len = (int)((t - 13) / 2);
                if (pos + len > record.Length) return null;
                row.Add(Encoding.UTF8.GetString(record.Slice(pos, len)));
                pos += len;
            }
            else if (t >= 12 && (t & 1) == 0)                             
            {
                int len = (int)((t - 12) / 2);
                if (pos + len > record.Length) return null;
                row.Add(Encoding.UTF8.GetString(record.Slice(pos, len)));
                pos += len;
            }
            else if (t == 8) { row.Add("0"); }                            
            else if (t == 9) { row.Add("1"); }                            
            else if (t == 7) { pos += 8; row.Add(string.Empty); }         
            else if (t >= 1 && t <= 6)                                    
            {
                int len = t switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 6, _ => 8 };
                if (pos + len > record.Length) return null;
                long v = 0;
                for (int i = len - 1; i >= 0; i--) v = (v << 8) | record[pos + i];
                pos += len;
                row.Add(v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            else { return null; } 
        }
        return row;
    }

    private static int ReadU16(byte[] file, int pos)
    {
        if (pos + 1 >= file.Length) return 0;
        return (file[pos] << 8) | file[pos + 1];
    }

    private static int ReadU32BE(byte[] file, int pos)
    {
        if (pos + 3 >= file.Length) return 0;
        return (file[pos] << 24) | (file[pos + 1] << 16) | (file[pos + 2] << 8) | file[pos + 3];
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int pos)
    {
        ulong result = 0;
        for (int i = 0; i < 8 && pos < data.Length; i++)
        {
            byte b = data[pos++];
            result = (result << 7) | (b & 0x7Fu);
            if ((b & 0x80u) == 0) return result;
        }
        if (pos < data.Length) result = (result << 8) | data[pos++];
        return result;
    }
}

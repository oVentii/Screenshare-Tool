





internal static class PrefetchBounds
{
    
    public static bool IsRangeValid(int offset, int length, int bufferLength)
        => length >= 0
        && offset >= 0
        && (long)offset + length <= bufferLength;

    
    public static bool CanRead(int offset, int size, int bufferLength)
        => size > 0 && IsRangeValid(offset, size, bufferLength);

    
    public static bool TryGetArrayLength(int count, int entrySize, out long totalBytes)
    {
        totalBytes = 0;
        if (count < 0 || entrySize <= 0)
            return false;
        totalBytes = (long)count * entrySize;
        return totalBytes <= int.MaxValue;
    }

    
    public static bool IsArrayInBounds(int offset, int count, int entrySize, int bufferLength)
        => count >= 0
        && offset >= 0
        && entrySize > 0
        && TryGetArrayLength(count, entrySize, out long total)
        && (long)offset + total <= bufferLength;

    
    public static bool IsPlausibleCount(int value, int max)
        => value > 0 && value <= max;

    
    public static bool IsPlausibleOffset(int offset, int bufferLength)
        => offset > 0 && offset < bufferLength;
}

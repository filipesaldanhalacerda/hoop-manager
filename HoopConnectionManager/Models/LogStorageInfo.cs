namespace HoopConnectionManager.Models;

public sealed record LogStorageInfo(long TotalBytes, int FileCount, DateTime? OldestEntryDate);

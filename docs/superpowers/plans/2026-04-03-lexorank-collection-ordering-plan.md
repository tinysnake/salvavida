# LexoRank Collection Ordering Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace integer-index-based ID ordering with LexoRank in ObservableListSavable and ObservableLazyListSavable, shifting ID list persistence to the Serializer layer.

**Architecture:** LexoRank algorithm generates sortable rank strings (format: "A~abc123") used as element SvIds. Serializer provides paginated ID listing via prefix-scoped key scans. Collections track deleted IDs in `_idsDeleted` for cleanup at Serialize time. Bucket metadata enables O(log buckets) page-seeking for lazy loading.

**Tech Stack:** C# (.NET/Unity), Span&lt;T&gt; for performance, existing Salvavida serialization framework.

**Spec:** `docs/superpowers/specs/2026-04-03-lexorank-collection-ordering-design.md`

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `Salvavida/Package/Runtime/LexoRank.cs` | Create | LexoRank algorithm (Between, Rebalance, NeedsRebalance, Compare) |
| `Salvavida/Package/Runtime/LazyLoading/BucketMeta.cs` | Create | BucketMeta struct definition |
| `Salvavida/Package/Runtime/LazyLoading/CollectionMetadata.cs` | Modify | Add BucketMetas, BucketMultiplier fields |
| `Salvavida/Package/Runtime/Serializer.cs` | Modify | Add ListCollectionIds abstract methods |
| `Salvavida/Package/Runtime/ObservableList.cs` | Modify | ObservableListSavable: LexoRank Insert/Remove, _idsDeleted, Serialize with rebalance |
| `Salvavida/Package/Runtime/ObservableLazyListSavable.cs` | Modify | LexoRank Insert/Remove, bucket seeking, _idsDeleted, Serialize with split/merge |
| `Salvavida.Tests/LexoRankTests.cs` | Create | LexoRank unit tests |
| `Salvavida.Tests/LexoRankCollectionIntegrationTests.cs` | Create | Collection integration tests |

---

### Task 1: BucketMeta Struct

**Files:**
- Create: `Salvavida/Package/Runtime/LazyLoading/BucketMeta.cs`

- [ ] **Step 1: Create BucketMeta.cs**

```csharp
using System;

namespace Salvavida
{
    /// <summary>
    /// Metadata for a single bucket in LexoRank-based collections.
    /// Stored in CollectionMetadata.BucketMetas.
    /// </summary>
    [Serializable]
    public struct BucketMeta
    {
        /// <summary>
        /// Bucket prefix (e.g. "A", "B", "AA"). Sequential Base62 assignment.
        /// </summary>
        public string BucketId;

        /// <summary>
        /// Number of elements in this bucket.
        /// </summary>
        public int Count;
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/BucketMeta.cs
git commit -m "feat: add BucketMeta struct for LexoRank bucket system"
```

---

### Task 2: CollectionMetadata Extension

**Files:**
- Modify: `Salvavida/Package/Runtime/LazyLoading/CollectionMetadata.cs`

- [ ] **Step 1: Add BucketMetas and BucketMultiplier fields**

Read the existing file, then add two new properties:

```csharp
// Add after existing properties (before closing brace):

/// <summary>
/// Bucket metadata table for LexoRank-based ordering.
/// </summary>
public BucketMeta[]? BucketMetas { get; set; }

/// <summary>
/// Multiplier for bucket size calculation: bucketSize = BucketMultiplier * PageSize.
/// Default is 3.
/// </summary>
public int BucketMultiplier { get; set; } = 3;
```

- [ ] **Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/LazyLoading/CollectionMetadata.cs
git commit -m "feat: extend CollectionMetadata with BucketMetas and BucketMultiplier"
```

---

### Task 3: LexoRank Core Algorithm

**Files:**
- Create: `Salvavida/Package/Runtime/LexoRank.cs`
- Test: `Salvavida.Tests/LexoRankTests.cs`

- [ ] **Step 1: Write failing tests for LexoRank.Between**

Create `Salvavida.Tests/LexoRankTests.cs`:

```csharp
using NUnit.Framework;

namespace Salvavida.Tests
{
    public class LexoRankTests
    {
        [Test]
        public void Between_NullNull_ReturnsInitialRank()
        {
            var result = LexoRank.Between(null, null);
            Assert.AreEqual("A~m", result);
        }

        [Test]
        public void Between_SameBucket_ReturnsMidpoint()
        {
            var result = LexoRank.Between("A~a", "A~z");
            Assert.That(result, Does.StartWith("A~"));
            Assert.That(result, Is.GreaterThan("A~a"));
            Assert.That(result, Is.LessThan("A~z"));
        }

        [Test]
        public void Between_CrossBucket_AppendsToPrev()
        {
            var result = LexoRank.Between("A~z", "B~a");
            Assert.AreEqual("A~z0", result);
        }

        [Test]
        public void Between_PrecisionExpansion_AppendsMinimumChar()
        {
            var result = LexoRank.Between("A~a", "A~b");
            Assert.That(result, Does.StartWith("A~a"));
            Assert.That(result.Length, Is.GreaterThan(3));
            Assert.That(result, Is.GreaterThan("A~a"));
            Assert.That(result, Is.LessThan("A~b"));
        }

        [Test]
        public void Between_EqualNonNull_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => LexoRank.Between("A~abc", "A~abc"));
        }

        [Test]
        public void Between_InsertAtBeginning()
        {
            var result = LexoRank.Between(null, "A~m");
            Assert.That(result, Is.LessThan("A~m"));
        }

        [Test]
        public void Between_InsertAtEnd()
        {
            var result = LexoRank.Between("A~m", null);
            Assert.That(result, Is.GreaterThan("A~m"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test Salvavida.Tests --filter "LexoRankTests" --no-build 2>&1 | head -20
```
Expected: Build fails (LexoRank class doesn't exist yet)

- [ ] **Step 3: Implement LexoRank class**

Create `Salvavida/Package/Runtime/LexoRank.cs`:

```csharp
using System;
using System.Buffers;

namespace Salvavida
{
    public static class LexoRank
    {
        // Base62 charset: 0-9 (48-57), A-Z (65-90), a-z (97-122) in ASCII order
        public const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
        public const int REBALANCE_LENGTH_THRESHOLD = 4;
        public const string DEFAULT_PREFIX = "A";
        public const string INITIAL_RANK = DEFAULT_PREFIX + "~m";

        // Precomputed charset index lookup for O(1) decode (avoids string.IndexOf per char)
        private static readonly byte[] s_charsetIndex = BuildCharsetIndex();

        private static byte[] BuildCharsetIndex()
        {
            var map = new byte[128]; // ASCII range
            for (byte i = 0; i < CHARSET.Length; i++)
                map[CHARSET[i]] = i;
            return map;
        }

        public static string Between(string? prev, string? next)
        {
            if (prev == null && next == null)
                return INITIAL_RANK;

            // Parse prefix and lexoValue using Span to avoid allocations
            ReadOnlySpan<char> prevSpan = prev.AsSpan();
            ReadOnlySpan<char> nextSpan = next.AsSpan();

            int pSepIdx = prev != null ? prev.IndexOf('~') : -1;
            int nSepIdx = next != null ? next.IndexOf('~') : -1;

            ReadOnlySpan<char> prevPrefix = pSepIdx >= 0 ? prevSpan.Slice(0, pSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> nextPrefix = nSepIdx >= 0 ? nextSpan.Slice(0, nSepIdx) : ReadOnlySpan<char>.Empty;
            ReadOnlySpan<char> prevLexo = pSepIdx >= 0 ? prevSpan.Slice(pSepIdx + 1) : prevSpan;
            ReadOnlySpan<char> nextLexo = nSepIdx >= 0 ? nextSpan.Slice(nSepIdx + 1) : nextSpan;

            // Determine prefix: use prev's if available, else next's, else default
            ReadOnlySpan<char> prefix = !prevPrefix.IsEmpty ? prevPrefix : (!nextPrefix.IsEmpty ? nextPrefix : DEFAULT_PREFIX);

            // Cross-bucket: different prefixes
            if (!prevPrefix.IsEmpty && !nextPrefix.IsEmpty && !prevPrefix.SequenceEqual(nextPrefix))
            {
                // Step 2b: append minimum char to prev's lexoValue
                // Use stackalloc for small lexoValues to avoid heap allocation
                int newLen = prevLexo.Length + 1;
                var buffer = newLen <= 64 ? stackalloc char[64] : new char[newLen];
                prevLexo.CopyTo(buffer);
                buffer[prevLexo.Length] = '0';
                var sepSpan = "~".AsSpan();
                // Build result: prefix + "~" + lexoValue + "0"
                int totalLen = prefix.Length + 1 + newLen;
                var resultBuffer = totalLen <= 128 ? stackalloc char[128] : new char[totalLen];
                prefix.CopyTo(resultBuffer);
                sepSpan.CopyTo(resultBuffer.Slice(prefix.Length));
                buffer.Slice(0, newLen).CopyTo(resultBuffer.Slice(prefix.Length + 1));
                return resultBuffer.Slice(0, totalLen).ToString();
            }

            // Same bucket: compute midpoint using big integer arithmetic on stack
            // For typical LexoRank lengths (< 16 chars), we can use stackalloc
            int maxLen = Math.Max(prevLexo.Length, nextLexo.Length) + 1;
            Span<byte> prevBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];
            Span<byte> nextBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];
            Span<byte> sumBytes = maxLen <= 64 ? stackalloc byte[64] : new byte[maxLen];

            int prevLen = prevLexo.IsEmpty ? 0 : DecodeToBytes(prevLexo, prevBytes);
            int nextLen = nextLexo.IsEmpty ? 0 : DecodeToBytes(nextLexo, nextBytes);

            // Handle null boundaries
            if (prevLexo.IsEmpty && nextLexo.IsEmpty)
                return INITIAL_RANK;
            if (prevLexo.IsEmpty)
            {
                // Insert at beginning: next / 2
                DivideByTwo(nextBytes, nextLen, sumBytes, out int sumLen);
                return EncodeResult(prefix, sumBytes, sumLen);
            }
            if (nextLexo.IsEmpty)
            {
                // Insert at end: prev + large offset
                // Use prev * 2 as approximation for "bigger than prev"
                // Actually: we need a value > prev. Simplest: prev + 1
                AddOne(prevBytes, prevLen, sumBytes, out sumLen);
                return EncodeResult(prefix, sumBytes, sumLen);
            }

            // Midpoint: (prev + next) / 2
            int totalLen2 = AddBigNumbers(prevBytes, prevLen, nextBytes, nextLen, sumBytes);
            int midLen = DivideByTwo(sumBytes, totalLen2, sumBytes, out int _);

            // Check if adjacent (midpoint == prev means no room)
            if (midLen == prevLen && sumBytes.Slice(0, midLen).SequenceEqual(prevBytes.Slice(0, prevLen)))
            {
                // Precision expansion: append minimum char
                int newLen2 = prevLexo.Length + 1;
                var buf = newLen2 <= 64 ? stackalloc char[64] : new char[newLen2];
                prevLexo.CopyTo(buf);
                buf[prevLexo.Length] = '0';
                int totalLen3 = prefix.Length + 1 + newLen2;
                var res = totalLen3 <= 128 ? stackalloc char[128] : new char[totalLen3];
                prefix.CopyTo(res);
                "~".AsSpan().CopyTo(res.Slice(prefix.Length));
                buf.Slice(0, newLen2).CopyTo(res.Slice(prefix.Length + 1));
                return res.Slice(0, totalLen3).ToString();
            }

            return EncodeResult(prefix, sumBytes, midLen);
        }

        /// <summary>
        /// Decode Base62 string to big-endian byte array on the given span.
        /// Returns the number of bytes written.
        /// </summary>
        private static int DecodeToBytes(ReadOnlySpan<char> lexo, Span<byte> outBytes)
        {
            // Simple approach: accumulate in bytes (base-256 representation)
            // For small ranks this is efficient
            int byteLen = 1;
            outBytes[0] = 0;
            foreach (char c in lexo)
            {
                byte digit = s_charsetIndex[c];
                // Multiply current number by 62, add digit
                byte carry = 0;
                for (int i = byteLen - 1; i >= 0; i--)
                {
                    int val = outBytes[i] * 62 + carry;
                    outBytes[i] = (byte)(val % 256);
                    carry = (byte)(val / 256);
                }
                // Add digit
                int pos = byteLen - 1;
                int sum = outBytes[pos] + digit;
                outBytes[pos] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
                while (carry > 0 && pos > 0)
                {
                    pos--;
                    sum = outBytes[pos] + carry;
                    outBytes[pos] = (byte)(sum % 256);
                    carry = (byte)(sum / 256);
                }
                if (carry > 0 && byteLen < outBytes.Length)
                {
                    // Shift right to make room
                    for (int i = byteLen; i > 0; i--)
                        outBytes[i] = outBytes[i - 1];
                    outBytes[0] = carry;
                    byteLen++;
                }
            }
            return byteLen;
        }

        /// <summary>
        /// Add two big-endian big integers. Returns total bytes written to outBytes.
        /// </summary>
        private static int AddBigNumbers(ReadOnlySpan<byte> a, int aLen, ReadOnlySpan<byte> b, int bLen, Span<byte> outBytes)
        {
            int maxLen = Math.Max(aLen, bLen);
            byte carry = 0;
            for (int i = 0; i < maxLen; i++)
            {
                int av = i < aLen ? a[aLen - 1 - i] : 0;
                int bv = i < bLen ? b[bLen - 1 - i] : 0;
                int sum = av + bv + carry;
                outBytes[maxLen - i] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
            }
            outBytes[0] = carry;
            return maxLen + (carry > 0 ? 1 : 0);
        }

        private static int DivideByTwo(ReadOnlySpan<byte> num, int len, Span<byte> outBytes, out int outLen)
        {
            byte remainder = 0;
            for (int i = 0; i < len; i++)
            {
                int val = (remainder << 8) | num[i];
                outBytes[i] = (byte)(val / 2);
                remainder = (byte)(val % 2);
            }
            outLen = len;
            // Trim leading zeros
            while (outLen > 1 && outBytes[0] == 0)
            {
                outBytes.Slice(1, outLen - 1).CopyTo(outBytes);
                outLen--;
            }
            return outLen;
        }

        private static void AddOne(ReadOnlySpan<byte> num, int len, Span<byte> outBytes, out int outLen)
        {
            num.Slice(0, len).CopyTo(outBytes);
            outLen = len;
            byte carry = 1;
            for (int i = outLen - 1; i >= 0 && carry > 0; i--)
            {
                int sum = outBytes[i] + carry;
                outBytes[i] = (byte)(sum % 256);
                carry = (byte)(sum / 256);
            }
            if (carry > 0 && outLen < outBytes.Length)
            {
                for (int i = outLen; i > 0; i--)
                    outBytes[i] = outBytes[i - 1];
                outBytes[0] = carry;
                outLen++;
            }
        }

        private static string EncodeResult(ReadOnlySpan<char> prefix, ReadOnlySpan<byte> bytes, int len)
        {
            // Encode big-endian bytes to Base62 using ArrayPool for the output buffer
            int maxChars = len * 2 + 2; // upper bound
            char[]? rented = maxChars > 128 ? ArrayPool<char>.Shared.Rent(maxChars) : null;
            Span<char> buf = rented != null ? rented.AsSpan(0, maxChars) : stackalloc char[128];

            // Copy bytes to a working span
            Span<byte> work = len <= 64 ? stackalloc byte[64] : new byte[len];
            bytes.Slice(0, len).CopyTo(work);

            int pos = 0;
            while (true)
            {
                // Check if all zeros
                bool allZero = true;
                for (int i = 0; i < len; i++)
                {
                    if (work[i] != 0) { allZero = false; break; }
                }
                if (allZero) break;

                // Divide by 62, collect remainder
                byte remainder = 0;
                for (int i = 0; i < len; i++)
                {
                    int val = (remainder << 8) | work[i];
                    work[i] = (byte)(val / 62);
                    remainder = (byte)(val % 62);
                }
                buf[pos++] = CHARSET[remainder];
            }

            if (pos == 0) buf[pos++] = CHARSET[0];

            // Build final: prefix + "~" + reversed base62
            int totalLen = prefix.Length + 1 + pos;
            char[]? rented2 = totalLen > 128 ? ArrayPool<char>.Shared.Rent(totalLen) : null;
            Span<char> result = rented2 != null ? rented2.AsSpan(0, totalLen) : stackalloc char[128];
            prefix.CopyTo(result);
            result[prefix.Length] = '~';
            // Reverse the base62 chars
            for (int i = 0; i < pos; i++)
                result[prefix.Length + 1 + i] = buf[pos - 1 - i];

            string str = result.Slice(0, totalLen).ToString();
            if (rented != null) ArrayPool<char>.Shared.Return(rented);
            if (rented2 != null) ArrayPool<char>.Shared.Return(rented2);
            return str;
        }

        public static bool NeedsRebalance(int elementCount, int maxRankLength)
        {
            if (elementCount <= 1) return false;
            if (maxRankLength <= REBALANCE_LENGTH_THRESHOLD) return false;
            var minLength = MinLengthForCount(elementCount);
            return maxRankLength > minLength + 1;
        }

        public static string[] Rebalance(int count)
        {
            if (count <= 0) return Array.Empty<string>();
            var result = new string[count];
            var range = (long)Math.Pow(62, Math.Max(1, MinLengthForCount(count))) - 1;
            var step = range / (count + 1);
            for (int i = 0; i < count; i++)
            {
                result[i] = Base62Encode(step * (i + 1));
            }
            return result;
        }

        public static int Compare(string a, string b) => string.CompareOrdinal(a, b);

        private static int MinLengthForCount(int count)
        {
            int len = 1;
            long capacity = 62;
            while (capacity < count && len < 10)
            {
                len++;
                capacity *= 62;
            }
            return len;
        }

        // Simple long-based encode for Rebalance (short ranks, no big number needed)
        private static string Base62Encode(long value)
        {
            if (value == 0) return CHARSET.Substring(0, 1);
            Span<char> buf = stackalloc char[16];
            int pos = 16;
            while (value > 0)
            {
                buf[--pos] = CHARSET[(int)(value % 62)];
                value /= 62;
            }
            return buf.Slice(pos, 16 - pos).ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test Salvavida.Tests --filter "LexoRankTests" -v minimal
```

- [ ] **Step 5: Commit**

```bash
git add Salvavida/Package/Runtime/LexoRank.cs Salvavida.Tests/LexoRankTests.cs
git commit -m "feat: implement LexoRank core algorithm with tests"
```

---

### Task 4: Serializer ListCollectionIds Abstract Methods

**Files:**
- Modify: `Salvavida/Package/Runtime/Serializer.cs`

- [ ] **Step 1: Add abstract methods to Serializer**

Read `Serializer.cs` and find the abstract method section (around line 560+ where `DoDelete`/`DoDeleteAll` are). Add two new abstract methods before the closing brace:

```csharp
/// <summary>
/// Get all ordered IDs under a collection path (lexicographic order = LexoRank order).
/// Implementation: scan all child keys under the collection path, exclude __ob_metadata__,
/// sort lexicographically, and return.
/// </summary>
public abstract IEnumerable<string> ListCollectionIds(SerializeContext ctx, string propName);

/// <summary>
/// Get a page of IDs starting from a specific bucket.
/// Implementation: scan keys matching {bucketId}~*, skip skipCount, collect pageSize.
/// </summary>
public abstract string[] ListCollectionIds(
    SerializeContext ctx, string propName, string? bucketId, int skipCount, int pageSize);
```

- [ ] **Step 2: Commit**

```bash
git add Salvavida/Package/Runtime/Serializer.cs
git commit -m "feat: add ListCollectionIds abstract methods to Serializer"
```

---

### Task 5: ObservableListSavable — Deserialization via ListCollectionIds

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableList.cs` (ObservableListSavable class)
- Modify: `Salvavida/Package/Runtime/Serializer.cs` (LoadCollectionSavable methods)

- [ ] **Step 1: Update LoadCollectionSavable to not depend on metadata.Ids**

In `Serializer.cs`, find the `LoadCollectionSavable` method for lists (line 335). Replace the `metadata?.Ids` usage:

```csharp
// Replace lines 348-365:
// OLD: var ids = metadata?.Ids; if (ids == null) { ... } src = new List<T?>(ids.Length); foreach (var id in ids) { ... }
// NEW:
var ob = new ObservableListSavable<T>(propName.ToString(), null, saveSeparately);
if (saveSeparately)
{
    ob.Deserialize(this, ctx);
    src = ob.RetrieveSource();
}
return ob;
```

The ObservableListSavable.Deserialize will now call ListCollectionIds internally.

- [ ] **Step 2: Update ObservableListSavable.Deserialize**

In `ObservableList.cs`, find the `Deserialize` method. Replace the metadata.Ids reading with ListCollectionIds:

```csharp
// In Deserialize:
// OLD: Read __ob_metadata__ to get ids array, then iterate ids
// NEW:
using var listScope = ctx.Path.UsePush(_propertyName, PathBuilder.Type.Property);
var ids = serializer.ListCollectionIds(ctx, _propertyName);
_list = new List<T?>();
foreach (var id in ids)
{
    var item = serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection);
    _list.Add(item);
}
_idsDeleted = new HashSet<string>(); // Initialize empty deleted set
```

- [ ] **Step 3: Add _idsDeleted field**

In `ObservableListSavable`, replace `_idsOnDeserialized` with:

```csharp
private HashSet<string> _idsDeleted = new();
```

- [ ] **Step 4: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableList.cs Salvavida/Package/Runtime/Serializer.cs
git commit -m "refactor: ObservableListSavable deserialization uses ListCollectionIds"
```

---

### Task 6: ObservableListSavable — Insert/Remove with LexoRank

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableList.cs`

- [ ] **Step 1: Update Insert method**

Find the `Insert` method in `ObservableListSavable`. Replace with:

```csharp
public override void Insert(int index, T? item)
{
    string? prevId = index > 0 ? _list[index - 1]?.SvId : null;
    string? nextId = index < _list.Count ? _list[index]?.SvId : null;
    
    item.SvId = LexoRank.Between(prevId, nextId);
    
    _list.Insert(index, item);
    OnItemSet(item, index);
    OnCollectionChange(CollectionChangeInfo<ObservableListSavable<T>, T?>.Add(this, item, index));
}
```

- [ ] **Step 2: Update RemoveAt method**

Find the `RemoveAt` method. Add the deleted ID to `_idsDeleted`:

```csharp
public override void RemoveAt(int index)
{
    var item = _list[index];
    if (item != null && !string.IsNullOrEmpty(item.SvId))
    {
        _idsDeleted.Add(item.SvId);
    }
    _list.RemoveAt(index);
    OnCollectionChange(CollectionChangeInfo<ObservableListSavable<T>, T?>.Remove(this, item, index));
    TryUnWatch(item);
}
```

- [ ] **Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableList.cs
git commit -m "feat: ObservableListSavable Insert/Remove with LexoRank and _idsDeleted"
```

---

### Task 7: ObservableListSavable — Serialize with Rebalance and _idsDeleted Cleanup

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableList.cs`

- [ ] **Step 1: Add pending rebalance/merge tracking fields**

Add to ObservableListSavable:

```csharp
private bool _needsRebalance;
private bool _needsBucketMerge;
```

- [ ] **Step 2: Update Insert to set _needsRebalance flag**

After computing the new rank in Insert, add:

```csharp
// Check if rebalance is needed
var rankParts = item.SvId.Split('~');
if (rankParts.Length == 2 && rankParts[1].Length > LexoRank.REBALANCE_LENGTH_THRESHOLD)
    _needsRebalance = true;
```

- [ ] **Step 3: Update Serialize method**

Replace the entire Serialize method body with:

```csharp
public override void Serialize(Serializer serializer, SerializeContext ctx)
{
    // Step 1: Handle pending rebalance
    if (_needsRebalance)
    {
        var newRanks = LexoRank.Rebalance(_list.Count);
        var oldIds = new List<string>();
        for (int i = 0; i < _list.Count; i++)
        {
            if (_list[i] != null && !string.IsNullOrEmpty(_list[i].SvId))
                oldIds.Add(_list[i].SvId);
            _list[i].SvId = LexoRank.DEFAULT_PREFIX + "~" + newRanks[i];
        }
        // Save at new paths
        for (int i = 0; i < _list.Count; i++)
        {
            serializer.Save(_list[i], ctx, PathBuilder.Type.Collection);
        }
        // Delete old paths
        foreach (var oldId in oldIds)
        {
            serializer.Delete(ctx, oldId, PathBuilder.Type.Collection);
        }
        _needsRebalance = false;
    }

    // Step 2: Save dirty elements
    for (int i = 0; i < _list.Count; i++)
    {
        var item = _list[i];
        if (item != null && item.IsDirty)
        {
            serializer.Save(item, ctx, PathBuilder.Type.Collection);
        }
    }

    // Step 3: Delete tracked deleted IDs
    foreach (var deletedId in _idsDeleted)
    {
        serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
    }
    _idsDeleted.Clear();

    // Step 4: Save CollectionMetadata with BucketMetas
    // ... use existing metadata save pattern ...
}
```

- [ ] **Step 4: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableList.cs
git commit -m "feat: ObservableListSavable Serialize with rebalance and _idsDeleted"
```

---

### Task 8: ObservableLazyListSavable — Deserialization with Bucket Index

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

- [ ] **Step 1: Replace _allIds with bucket index fields**

```csharp
// REMOVE: private string[] _allIds;
// ADD:
private int[] _bucketCumulativeIndex; // cumulative count per bucket
```

- [ ] **Step 2: Update Deserialize**

```csharp
public override void Deserialize(Serializer serializer, SerializeContext ctx)
{
    // Read CollectionMetadata
    using var metaScope = ctx.Path.UsePush(SvHelper.PROPNAME_COLLECTION_METADATA, PathBuilder.Type.Collection);
    var metadata = serializer.ReadNoPushPath<CollectionMetadata>(ctx);
    
    _totalElementCount = metadata.Count;
    _pageSize = metadata.PageSize > 0 ? metadata.PageSize : GlobalConfig.DefaultPageSize;
    _maxCachedPages = metadata.MaxCachedPages > 0 ? metadata.MaxCachedPages : GlobalConfig.DefaultMaxCachedPages;
    _cacheStrategy = metadata.CacheStrategy;
    _idsDeleted = new HashSet<string>();
    
    // Build cumulative bucket index
    if (metadata.BucketMetas != null && metadata.BucketMetas.Length > 0)
    {
        _bucketMetas = metadata.BucketMetas;
        _bucketCumulativeIndex = new int[metadata.BucketMetas.Length];
        int cumulative = 0;
        for (int i = 0; i < metadata.BucketMetas.Length; i++)
        {
            cumulative += metadata.BucketMetas[i].Count;
            _bucketCumulativeIndex[i] = cumulative;
        }
    }
    else
    {
        _bucketMetas = Array.Empty<BucketMeta>();
        _bucketCumulativeIndex = Array.Empty<int>();
    }
}
```

Note: Do NOT store SerializeContext as a member. The serializer reference is obtained from the parent root object during page loading (via the existing `_serializer` field pattern). Page loading methods receive the serializer and context as parameters from the caller.

- [ ] **Step 3: Add FindBucketForIndex helper**

```csharp
private (string? bucketId, int skipCount) FindBucketForIndex(int elementIndex)
{
    if (_bucketCumulativeIndex.Length == 0)
    {
        // No buckets yet — all elements in default bucket "A"
        return (LexoRank.DEFAULT_PREFIX, elementIndex);
    }
    
    // Binary search for the bucket
    int bucketIdx = Array.BinarySearch(_bucketCumulativeIndex, elementIndex + 1);
    if (bucketIdx < 0) bucketIdx = ~bucketIdx; // first bucket with cumulative > elementIndex
    
    int countBefore = bucketIdx > 0 ? _bucketCumulativeIndex[bucketIdx - 1] : 0;
    int skipCount = elementIndex - countBefore;
    
    return (_bucketMetas[bucketIdx].BucketId, skipCount);
}
```

Note: Store `_bucketMetas` as a field during Deserialize for access in FindBucketForIndex.

- [ ] **Step 4: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "refactor: ObservableLazyListSavable deserialization with bucket index"
```

---

### Task 9: ObservableLazyListSavable — Insert/Remove O(1) Page Loading

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

- [ ] **Step 1: Update Insert method**

```csharp
public override void Insert(int index, T? item)
{
    _rwLock.EnterWriteLock();
    try
    {
        // Get prev/next ranks
        string? prevId = GetElementRank(index - 1);
        string? nextId = GetElementRank(index);
        
        item.SvId = LexoRank.Between(prevId, nextId);
        
        // Only load the page containing index
        int targetPageIdx = index / _pageSize;
        var page = GetOrLoadPage(targetPageIdx);
        
        // Use List<T>.Insert for in-page shifting (no manual Array.Copy)
        int localIdx = index % _pageSize;
        page.Elements.Insert(localIdx, item);
        
        // If page exceeded capacity, move excess to next page
        while (page.Elements.Count > _pageSize)
        {
            var nextPage = GetOrLoadPage(targetPageIdx + 1);
            var moved = page.Elements[page.Elements.Count - 1];
            page.Elements.RemoveAt(page.Elements.Count - 1);
            nextPage.Elements.Insert(0, moved);
            nextPage.IsDirty = true;
            page.IsDirty = true;
        }
        
        // Update bucket_meta count
        UpdateBucketCountForIndex(index, 1);
        
        _totalElementCount++;
        _hasPendingWrites = true;
        
        // Check if rebalance needed
        var rankParts = item.SvId.Split('~');
        if (rankParts.Length == 2 && rankParts[1].Length > LexoRank.REBALANCE_LENGTH_THRESHOLD)
            _needsRebalance = true;
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

Note: `Page.Elements` is `List<T?>` instead of `T?[]`. This eliminates manual `Array.Copy` for shifting — `List<T>.Insert` and `List<T>.RemoveAt` handle it internally.

- [ ] **Step 2: Update RemoveAt method**

```csharp
public override void RemoveAt(int index)
{
    _rwLock.EnterWriteLock();
    try
    {
        var itemRank = GetElementRank(index);
        if (!string.IsNullOrEmpty(itemRank))
            _idsDeleted.Add(itemRank);
        
        int targetPageIdx = index / _pageSize;
        var page = GetOrLoadPage(targetPageIdx);
        int localIdx = index % _pageSize;
        
        // Use List<T>.RemoveAt for in-page shifting (no manual Array.Copy)
        page.Elements.RemoveAt(localIdx);
        page.IsDirty = true;
        
        // If page is now underfull and next page has elements, pull one in
        if (page.Elements.Count < _pageSize / 2 && targetPageIdx + 1 < GetPageCount())
        {
            var nextPage = GetOrLoadPage(targetPageIdx + 1);
            if (nextPage.Elements.Count > 0)
            {
                page.Elements.Add(nextPage.Elements[0]);
                nextPage.Elements.RemoveAt(0);
                nextPage.IsDirty = true;
                page.IsDirty = true;
            }
        }
        
        UpdateBucketCountForIndex(index, -1);
        _totalElementCount--;
        _hasPendingWrites = true;
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

- [ ] **Step 3: Add helper methods**

```csharp
private string? GetElementRank(int index)
{
    if (index < 0 || index >= _totalElementCount) return null;
    var page = GetOrLoadPage(index / _pageSize);
    var localIdx = index % _pageSize;
    return localIdx < page.Elements.Count ? page.Elements[localIdx]?.SvId : null;
}
```

- [ ] **Step 4: Update GetOrLoadPage to use bucket-aware loading**

```csharp
private string[] GetPageIds(int pageIndex)
{
    var (bucketId, skipCount) = FindBucketForIndex(pageIndex * _pageSize);
    return _serializer.ListCollectionIds(_context, _propertyName, bucketId, skipCount, _pageSize);
}
```

- [ ] **Step 5: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: ObservableLazyListSavable Insert/Remove O(1) with LexoRank"
```

---

### Task 10: ObservableLazyListSavable — Serialize with Bucket Split/Merge

**Files:**
- Modify: `Salvavida/Package/Runtime/ObservableLazyListSavable.cs`

- [ ] **Step 1: Update Serialize method**

```csharp
public override void Serialize(Serializer serializer, SerializeContext ctx)
{
    _rwLock.EnterWriteLock();
    try
    {
        // Step 1: Handle pending bucket splits
        if (_needsBucketSplit)
        {
            PerformBucketSplit(serializer, ctx);
        }
        
        // Step 2: Handle pending bucket merges
        if (_needsBucketMerge)
        {
            PerformBucketMerge(serializer, ctx);
        }
        
        // Step 3: Handle pending rebalance
        if (_needsRebalance)
        {
            PerformRebalance(serializer, ctx);
        }
        
        // Step 4: Flush dirty pages
        FlushDirtyPages(serializer, ctx);
        
        // Step 5: Delete tracked deleted IDs
        foreach (var deletedId in _idsDeleted)
        {
            serializer.Delete(ctx, deletedId, PathBuilder.Type.Collection);
        }
        _idsDeleted.Clear();
        
        // Step 6: Update CollectionMetadata with BucketMetas
        SaveCollectionMetadata(serializer, ctx);
        
        // Step 7: Invalidate cached pages with SvId changes
        InvalidateChangedPages();
    }
    finally
    {
        _rwLock.ExitWriteLock();
    }
}
```

- [ ] **Step 2: Implement PerformBucketSplit**

```csharp
private void PerformBucketSplit(Serializer serializer, SerializeContext ctx)
{
    // Find overflowing bucket
    for (int i = 0; i < _bucketMetas.Length; i++)
    {
        int bucketSize = _bucketMultiplier * _pageSize;
        if (_bucketMetas[i].Count > bucketSize)
        {
            int startIdx = i > 0 ? _bucketCumulativeIndex[i - 1] : 0;
            int endIdx = _bucketCumulativeIndex[i];
            int midIdx = startIdx + (endIdx - startIdx) / 2;
            string newPrefix = GetNextBucketPrefix();
            
            // Rebalance first half (keeps current prefix)
            var firstHalfRanks = LexoRank.Rebalance(midIdx - startIdx);
            for (int j = startIdx; j < midIdx; j++)
            {
                var page = GetOrLoadPage(j / _pageSize);
                var elem = page.Elements[j % _pageSize];
                if (elem != null)
                {
                    elem.SvId = _bucketMetas[i].BucketId + "~" + firstHalfRanks[j - startIdx];
                    serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                }
            }
            
            // Rebalance second half (new prefix)
            var secondHalfRanks = LexoRank.Rebalance(endIdx - midIdx);
            for (int j = midIdx; j < endIdx; j++)
            {
                var page = GetOrLoadPage(j / _pageSize);
                var elem = page.Elements[j % _pageSize];
                if (elem != null)
                {
                    elem.SvId = newPrefix + "~" + secondHalfRanks[j - midIdx];
                    serializer.Save(elem, ctx, PathBuilder.Type.Collection);
                }
            }
            
            // Update BucketMetas
            _bucketMetas[i].Count = midIdx - startIdx;
            // Insert new bucket entry
            // ... array resize and insert ...
            
            _bucketCumulativeIndex = RecomputeCumulativeIndex();
            break;
        }
    }
    _needsBucketSplit = false;
}
```

- [ ] **Step 3: Commit**

```bash
git add Salvavida/Package/Runtime/ObservableLazyListSavable.cs
git commit -m "feat: ObservableLazyListSavable Serialize with bucket split/merge"
```

---

### Task 11: Comprehensive Integration Tests

**Files:**
- Create: `Salvavida.Tests/LexoRankCollectionIntegrationTests.cs`

- [ ] **Step 1: Write comprehensive tests**

```csharp
[TestFixture]
public class LexoRankCollectionIntegrationTests
{
    // === LexoRank Algorithm Tests ===
    
    [Test]
    public void Between_NullNull_ReturnsInitialRank()
    {
        Assert.AreEqual("A~m", LexoRank.Between(null, null));
    }
    
    [Test]
    public void Between_SameBucket_ProducesCorrectOrdering()
    {
        var mid = LexoRank.Between("A~a", "A~z");
        Assert.That(mid, Is.GreaterThan("A~a"));
        Assert.That(mid, Is.LessThan("A~z"));
    }
    
    [Test]
    public void Between_CrossBucket_ProducesCorrectOrdering()
    {
        var result = LexoRank.Between("A~z", "B~a");
        Assert.That(result, Is.GreaterThan("A~z"));
        Assert.That(result, Is.LessThan("B~a"));
    }
    
    [Test]
    public void Rebalance_ProducesSortedRanks()
    {
        var ranks = LexoRank.Rebalance(10);
        for (int i = 1; i < ranks.Length; i++)
        {
            Assert.That(ranks[i], Is.GreaterThan(ranks[i - 1]));
        }
    }
    
    // === ObservableListSavable Tests ===
    
    [Test]
    public void ObservableList_InsertAtBeginning_PreservesOrder()
    {
        // Create list, add 3 items, insert at index 0
        // Serialize, deserialize, verify order: [new, old0, old1, old2]
    }
    
    [Test]
    public void ObservableList_InsertAtMiddle_PreservesOrder()
    {
        // Create list with 4 items, insert at index 2
        // Serialize, deserialize, verify order
    }
    
    [Test]
    public void ObservableList_RemoveAndSerialize_CleansUpDeletedIds()
    {
        // Create list with 3 items, remove middle one, serialize
        // Deserialize, verify 2 items remain
        // Verify deleted ID no longer exists in storage
    }
    
    [Test]
    public void ObservableList_RapidInserts_NoRankCollision()
    {
        // Insert 100 items without serializing
        // Verify all SvIds are unique and in order
    }
    
    // === ObservableLazyListSavable Tests ===
    
    [Test]
    public void ObservableLazyList_Insert_DoesNotLoadAllPages()
    {
        // Create lazy list with 1000 elements (10 pages of 100)
        // Insert at index 500
        // Verify only page 5 was loaded (not all 10 pages)
    }
    
    [Test]
    public void ObservableLazyList_Remove_DoesNotLoadAllPages()
    {
        // Create lazy list with 1000 elements
        // Remove at index 200
        // Verify only page 2 was loaded
    }
    
    [Test]
    public void ObservableLazyList_RoundTrip_PreservesOrder()
    {
        // Create lazy list, add 500 elements, serialize
        // Deserialize, verify order and count
    }
    
    [Test]
    public void ObservableLazyList_PageLoading_RespectsLRU()
    {
        // Create lazy list with maxCachedPages = 2
        // Load pages 0, 1, 2
        // Verify page 0 was evicted
    }
    
    // === Bucket System Tests ===
    
    [Test]
    public void BucketSplit_TriggeredAtThreshold()
    {
        // Create collection with bucketSize = 6
        // Insert 7 elements, serialize
        // Verify BucketMetas has 2 entries
    }
    
    [Test]
    public void BucketMerge_TriggeredAtUnderflow()
    {
        // Create collection with 2 buckets, remove elements until one drops below threshold
        // Serialize, verify buckets merged
    }
    
    [Test]
    public void CrossBucketInsert_ProducesCorrectRank()
    {
        // Create collection with elements in buckets A and B
        // Insert between last A and first B
        // Verify new rank is > last A and < first B
    }
}
```

- [ ] **Step 2: Run all tests**

```bash
dotnet test Salvavida.Tests -v minimal
```

- [ ] **Step 3: Commit**

```bash
git add Salvavida.Tests/LexoRankCollectionIntegrationTests.cs
git commit -m "test: add comprehensive LexoRank collection tests"
```

---

### Task 12: Final Cleanup and Verification

- [ ] **Step 1: Remove all references to old metadata.Ids usage**

Search for remaining `metadata.Ids` or `_idsOnDeserialized` references and remove/update them.

- [ ] **Step 2: Run full test suite**

```bash
dotnet test Salvavida.Tests -v minimal
```

- [ ] **Step 3: Final commit**

```bash
git add -A
git commit -m "chore: final cleanup for LexoRank collection ordering"
```

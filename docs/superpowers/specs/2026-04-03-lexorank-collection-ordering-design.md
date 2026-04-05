# LexoRank-Based Collection Ordering with Serializer-Managed ID Lists

## Overview

Replace the integer-index-based ID system in `ObservableListSavable` and `ObservableLazyListSavable` with LexoRank ordering, and shift ID list persistence from the collection layer to the Serializer layer. This eliminates O(n) array shifting on Insert/Remove operations and removes the need to store ID arrays in `CollectionMetadata`.

## Design Decisions

| Decision | Choice | Rationale |
|----------|--------|-----------|
| LexoRank implementation | Self-implemented in project | No external dependency, full control |
| Character set | 62-char (0-9, A-Z, a-z) | ASCII-ordered for lexicographic comparison |
| ID strategy | LexoRank value IS the SvId | Single source of truth, no separate rank storage |
| ID list persistence | Serializer layer fully manages | Collections no longer store ID arrays |
| ID list update | Batch commit on Serialize | Avoid per-operation IO, keep memory list in sync |
| Bucket mechanism | Prefix-encoded rank with bucket_meta table | Fast page-seeking for lazy loading |
| Bucket metadata storage | CollectionMetadata.BucketMetas | Colocated with collection config |
| Backward compatibility | Not required | Old data will be re-serialized in new format |
| Performance | Span&lt;T&gt; for all string processing | Minimal allocations, best throughput |

## Architecture

### 1. LexoRank Algorithm

Rank format: "{bucketPrefix}~{lexoValue}"
  - Separator: "~" (ASCII 126), which is > all CHARSET chars (max 'z' = 122)
    - Not a reserved path character on any OS
    - ALL ranks contain the separator — there is no "no-prefix" phase
    - Default prefix: "A" (assigned to the initial bucket)
    - Empty collection first element: rank = "A~m"
    - Prefix comparison: "A" < "B" < "AA" < ... (ASCII order)
    - Since "~" > all CHARSET chars: "A~zzz" < "B~aaa" (correct bucket ordering)
  - bucketPrefix: Base62 string (e.g. "A", "B", "AA"). Never empty.
  - lexoValue: Base62 sortable value (e.g. "abc123")
  - Full rank example: "A~abc123" (prefix "A" + separator "~" + value "abc123")
  - Full ranks are compared as single strings — lexicographic order = LexoRank order

Initial state:
  - Empty collection first element: rank = "A~m" (default prefix "A" + "~" + initial value)
  - The initial bucket has prefix "A"
  - New bucket prefixes are assigned sequentially: "B", "C", ..., "Z", "AA", "AB", ...

Insertion between two ranks:
  Step 1: Parse prev and next into (prefix, lexoValue) by splitting on "~"
    - The separator is always present, so split always succeeds
  Step 2a: If prefixes are equal (same bucket):
    - Convert both lexoValues to Base62 big integers
    - Compute midpoint: (prev + next) / 2
    - Convert back to Base62 string
    - Return: prefix + "~" + midpoint
    - Example: "A~a" and "A~z" -> "A~m"
    - Example: "A~a" and "A~m" -> "A~g"
  Step 2b: If prefixes differ (cross-bucket):
    - Append minimum CHARSET char ("0") to prev's lexoValue
    - Return: prevPrefix + "~" + (prevLexoValue + "0")
    - This always sorts < next because: prevPrefix < nextPrefix (ASCII order)
      and same-prefix-with-suffix < different-prefix
    - Example: "A~z" and "B~a" -> "A~z0"
    - Example: "A~abc" and "C~xyz" -> "A~abc0"

Precision expansion:
  - When adjacent ranks in the same bucket have no midpoint, append minimum character
  - Example: "A~a" and "A~b" -> "A~a0"
  - Cross-bucket insertion always uses precision expansion (Step 2b)

Rebalancing (bucket-level operation):
  - Trigger: any element's lexoValue portion length > REBALANCE_LENGTH_THRESHOLD (default 4)
    AND the bucket is not already in shortest representation
  - "Shortest representation" check: if the bucket's elements can fit in fewer characters
    than their current max lexoValue length, rebalance is worthwhile
  - Redistribute all elements in the affected bucket to evenly-spaced short ranks
  - After rebalance, all affected elements get new SvId paths
  - Rebalance is deferred to Serialize time to avoid mid-operation path changes
  - Note: only the lexoValue portion (after "~") is rebalanced. The bucket prefix stays the same.

Comparison:
  - Full rank string comparison (bucketPrefix + "~" + lexoValue)
  - "~" (126) > all CHARSET chars, ensuring correct cross-bucket ordering
  - Base62 charset preserves lexicographic order: 0-9 < A-Z < a-z

#### Performance Constraints

- Base62 encode/decode uses `Span<char>` / `Span<byte>` to avoid intermediate string allocations
- `Between` computation uses `stackalloc` or `ArrayPool<byte>` for big integer arithmetic
- String comparison uses `MemoryExtensions.Compare` or `ReadOnlySpan<char>` character-by-character
- `Rebalance` batch generation uses pre-allocated `Span<string>` or `ArrayPool`

#### API

```csharp
public static class LexoRank
{
    // Base62 charset: 0-9 (48-57), A-Z (65-90), a-z (97-122) in ASCII order
    // This ordering ensures lexicographic comparison equals numeric comparison
    // ASSERTION: Do NOT reorder this string. The ASCII ordering of these characters
    // is what makes string comparison equivalent to rank comparison.
    public const string CHARSET = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    // REBALANCE_LENGTH_THRESHOLD = 4 means: trigger rebalance when rank length exceeds 4 (i.e., at length 5).
    // 4-char ranks provide 62^4 ≈ 14.7M unique slots per bucket — the last acceptable state.
    // This balances between frequent rebalances (too low) and long rank strings (too high).
    public const int REBALANCE_LENGTH_THRESHOLD = 4;

    /// <summary>
    /// Generate a rank that sorts between prev and next.
    /// null is used as a boundary sentinel (not a rank value):
    ///   - Between(null, null) → "A~m" (insert into empty collection)
    ///   - Between(null, next) → rank &lt; next (insert at beginning)
    ///   - Between(prev, null) → rank &gt; prev (insert at end)
    /// If prev and next are both non-null and equal, throws ArgumentException (invalid input).
    /// If prev and next are adjacent with no midpoint, expands precision (appends minimum char).
    /// </summary>
    public static string Between(string? prev, string? next);

    /// <summary>
    /// Check if a bucket needs rebalancing based on element count and max rank length.
    /// Returns true if ranks are longer than necessary for the element count.
    /// </summary>
    public static bool NeedsRebalance(int elementCount, int maxRankLength);

    /// <summary>
    /// Generate evenly-spaced ranks for a given count.
    /// Returns bare lexoValues (no bucket prefix) in sorted order.
    /// The caller is responsible for prepending the bucket prefix.
    /// Uses the shortest possible representation.
    /// Example: Rebalance(3) -> ["A", "M", "Z"] (caller prepends prefix)
    /// </summary>
    public static string[] Rebalance(int count);

    /// <summary>
    /// Compare two full rank strings. Equivalent to string.CompareOrdinal(a, b).
    /// Provided for API clarity; delegates to string.CompareOrdinal internally.
    /// </summary>
    public static int Compare(string a, string b);
}
```

### 2. Bucket System

Bucket purpose:
  - Group elements to enable fast page-seeking without scanning all keys
  - bucketSize = BucketMultiplier * PageSize (BucketMultiplier default = 3)
  - Bucket prefixes are assigned sequentially from a Base62 counter (initial: "A")

Rank structure evolution:
  - Phase 1 (no bucket split yet): All elements have ranks with default prefix "A": "A~m", "A~g", "A~t", etc.
    The initial bucket has BucketId = "A".
  - Phase 2 (first split):
    1. The initial bucket (prefix "A") is split into two
    2. Elements are divided evenly: first half keeps prefix "A", second half gets prefix "B"
    3. Each half is rebalanced to short ranks: "A~<rank>", "B~<rank>"
    4. BucketMetas is initialized with two entries: [{BucketId:"A", Count:n/2}, {BucketId:"B", Count:n/2}]
    5. Second half elements get new SvId paths (save at new, delete at old); first half keeps prefix "A" but may get new lexoValues from rebalance
  - Phase 3 (subsequent splits): Individual buckets split, new prefixes assigned sequentially

Bucket splitting:
  - Triggered when a bucket's count exceeds bucketSize
  - For first split (Phase 2, no-prefix bucket): see "Rank structure evolution" above
  - For subsequent splits:
    - Split the bucket into two halves
    - Original bucket keeps its prefix, new bucket gets next sequential Base62 prefix
    - Rebalance both halves to short ranks within their prefixes (e.g. "A~a", "A~m", "B~a", "B~m")
    - Update CollectionMetadata.BucketMetas
  - Cost analysis: requires loading all elements in the bucket to re-assign ranks.
    For ObservableLazyListSavable, this means loading all pages that overlap with the bucket.
    This is an O(bucketSize) operation, amortized over many inserts.

Bucket merging (anti-fragmentation):
  - Triggered when a bucket's count drops below bucketSize / 4 after removals
  - Merge with adjacent bucket (prefer the one with fewer elements)
  - ALL elements from both buckets get new SvIds with the merged bucket's prefix
    - This is an expensive operation: every element must be saved at new path and deleted at old
    - Cost: O(mergedBucket.Count) writes + deletes
    - Amortized: only triggers after many removals, so the cost is spread out
  - Rebalance merged bucket to short ranks
  - Update CollectionMetadata.BucketMetas
  - Prevents bucket proliferation after heavy deletion

Cross-bucket insertion:
  - When inserting between elements in different buckets, use Step 2b (precision expansion)
  - Append "0" to prev element's lexoValue: newRank = prevPrefix + "~" + (prevLexoValue + "0")
  - The new element belongs to the prev bucket (its rank prefix matches prev bucket)
  - This may cause the prev bucket to overflow, triggering a split at Serialize time

Page seeking:
  - BucketMeta stores cumulative count for index-to-bucket mapping
  - To find pageIndex: iterate BucketMetas, accumulating counts until reaching the target index
  - Once target bucket is found, compute offset within bucket: offset = pageIndex - cumulativeCountBefore
  - Scan from that bucket, collecting IDs across bucket boundaries until pageSize is reached

#### BucketMeta Structure

```csharp
public struct BucketMeta
{
    public string BucketId;      // bucket prefix, e.g. "A", "B", "AA" (sequential Base62)
    public int Count;            // number of elements in this bucket
    // Note: MinRank/MaxRank are NOT stored here.
    // The bucket's rank range is implicit from the ordered key listing.
    // Storing MinRank/MaxRank would require updates on every insert/remove, defeating the purpose.
}
```

### 3. CollectionMetadata Extension

```csharp
public class CollectionMetadata
{
    // New fields
    public BucketMeta[]? BucketMetas { get; set; }   // bucket metadata table
    public int BucketMultiplier { get; set; }        // n value, bucketSize = n * pageSize, default 3

    // Deprecated — no longer stores full ID array
    // public string[]? Ids { get; set; }

    // Existing fields
    public int Count { get; set; }
    public int PageSize { get; set; }
    public int MaxCachedPages { get; set; }
    public CacheStrategy CacheStrategy { get; set; }
    public bool IsLazyLoaded { get; set; }
}
```

#### Initial Bucket State

- When a collection is first created (empty): `BucketMetas = null` or empty array
- First element inserted: rank = "A~m" (default prefix "A" + "~" + initial value)
- The initial bucket (prefix = "A") is implicit — not stored in BucketMetas until first split
- `BucketMetas` is initialized when the first bucket split occurs

### 4. Serializer New Abstract Methods

```csharp
public abstract class Serializer
{
    /// <summary>
    /// Get all ordered IDs under a collection path (lexicographic order = LexoRank order).
    /// Used by ObservableListSavable for full loading.
    /// Implementation: scan all child keys under the collection path, exclude __ob_metadata__,
    /// sort lexicographically, and return.
    /// </summary>
    public abstract IEnumerable<string> ListCollectionIds(SerializeContext ctx, string propName);

    /// <summary>
    /// Get a page of IDs starting from a specific bucket.
    /// Used by ObservableLazyListSavable for on-demand page loading.
    ///
    /// The Collection layer is responsible for:
    ///   1. Building a cumulative bucket index from BucketMetas
    ///   2. Finding the starting bucket for pageIndex via binary search
    ///   3. Computing the skip offset within that bucket
    ///   4. Calling this method with the bucketId and skip offset
    ///
    /// Implementation:
    ///   1. Scan keys under the collection path that start with the given bucketId prefix
    ///      (i.e., keys matching "{bucketId}~*")
    ///   2. Skip `skipCount` keys (offset within the bucket)
    ///   3. Collect up to `pageSize` keys
    ///   4. If more keys are needed (bucket exhausted), continue to the next bucket's prefix
    ///      by scanning all remaining keys in lexicographic order
    ///   5. Return collected IDs
    /// </summary>
    /// <param name="ctx">Serialization context</param>
    /// <param name="propName">Collection property name</param>
    /// <param name="bucketId">Starting bucket prefix (e.g. "A", "B"). Use null for the initial bucket.</param>
    /// <param name="skipCount">Number of keys to skip within the starting bucket</param>
    /// <param name="pageSize">Maximum number of IDs to return</param>
    public abstract string[] ListCollectionIds(
        SerializeContext ctx, string propName, string? bucketId, int skipCount, int pageSize);
}
```

**Behavior notes:**
- `ListCollectionIds(ctx, propName)` — full scan, returns all IDs sorted lexicographically
- `ListCollectionIds(ctx, propName, bucketId, skipCount, pageSize)` — paginated scan:
  1. Collection layer finds starting bucket via binary search on cumulative bucket index
  2. Calls this method with the bucketId and skip offset
  3. Serializer scans keys matching `{bucketId}~*`, skips `skipCount`, collects `pageSize`
  4. If more keys needed, continues scanning remaining keys in lexicographic order
  5. Returns `string[]` only; totalCount comes from `CollectionMetadata.Count`
- **Storage format**: BucketMetas are stored as part of CollectionMetadata in `__ob_metadata__`.
  The Collection reads/writes BucketMetas directly through the metadata — the Serializer does NOT
  expose a GetBucketMetas method.

### 5. ObservableListSavable Changes

#### Deserialization

1. Call Serializer.ListCollectionIds(ctx, propName) to get full ordered ID list
2. For each ID, call serializer.Read<T?>(ctx, id, PathBuilder.Type.Collection) to load element
3. Store IDs in _idsDeleted for later cleanup tracking
4. Build _list from loaded elements

#### Insert(index, item)

1. Find the bucket for the given index via bucket_meta cumulative counts
2. Get the rank of the element at index-1 (prev) and index (next) from _list
3. Compute new rank = LexoRank.Between(prev.SvId, next.SvId)
   - If index == 0, prev = null
   - If index == Count, next = null
4. item.SvId = new rank
5. _list.Insert(index, item)
6. Update bucket_meta count in memory
7. Mark collection as potentially needing rebalance (deferred check)

#### RemoveAt(index)

1. Get the element's SvId (its rank)
2. Add the SvId to _idsDeleted (track for cleanup)
3. _list.RemoveAt(index)
4. Update bucket_meta count in memory
5. TryUnWatch(item)
6. Check if bucket merge is needed (count < bucketSize / 4)

#### Serialize

1. Check pending rebalance: if any bucket triggered rebalance during Insert operations
   a. For each bucket needing rebalance:
      i. Compute new short ranks via LexoRank.Rebalance(bucket.Count)
      ii. For each element: mark old SvId for deletion, assign new SvId
      iii. Save element at new SvId path
      iv. Delete element at old SvId path
   b. Clear rebalance pending flags
2. Check pending bucket merges: if any bucket dropped below threshold
   a. Merge with adjacent bucket, rebalance ranks
   b. Update affected elements' SvId paths
3. Iterate _list, Save each remaining dirty element (path includes its SvId)
4. Delete all IDs in _idsDeleted from storage
5. Update CollectionMetadata (including BucketMetas), save
6. _idsDeleted.Clear()

**SvId mutation safety:**
- Rebalance and bucket merge happen atomically during Serialize
- Old paths are deleted only after new paths are successfully written
- If serialization fails mid-way, the next load will use old paths (data is not lost, just duplicated)
- Duplicate data is cleaned up on the next successful Serialize

**Dirty tracking for SvId changes:**
- Elements have two dirty states: data-dirty and path-dirty
- Data-dirty: element's content changed since last save
- Path-dirty: element's SvId changed (due to rebalance/merge)
- An element can be path-dirty without being data-dirty
- During Serialize step 3, both data-dirty AND path-dirty elements are saved
- Path-dirty flag is set when rebalance/merge assigns a new SvId

#### What no longer happens

- ~~No more saving Ids array in __ob_metadata__~~
- ~~No more Array.Resize / Array.Copy on ID arrays during Insert/Remove~~

### 6. ObservableLazyListSavable Changes

#### Deserialization

1. Read CollectionMetadata (including BucketMetas)
2. Do NOT load any elements
3. Cache _serializer, _context, _totalElementCount
4. Build cumulative bucket index for page-seeking:
   - cumulativeIndex[i] = sum of BucketMetas[0..i].Count
   - Used to map pageIndex -> bucket in O(log buckets) via binary search

#### GetOrLoadPage(pageIndex)

1. Check cache, return if hit
2. Use cumulative bucket index to locate the starting bucket for pageIndex
3. Compute skipCount = pageIndex * pageSize - countBeforeThisBucket
4. Call Serializer.ListCollectionIds(ctx, propName, bucketId, skipCount, pageSize) to get page IDs
5. For each ID, read element into PageData
6. Add to LRU cache

#### Insert(index, item) — KEY IMPROVEMENT

BEFORE: Load ALL pages from index to end, shift every element O(n)
AFTER:
1. Find bucket for index via cumulative bucket index
2. Get prev and next ranks from the loaded page (or adjacent pages if at boundary)
   - Only load the page containing index, plus one adjacent page if needed for rank context
3. Compute new rank = LexoRank.Between(prev.SvId, next.SvId)
4. item.SvId = new rank
5. Place item in the page (may require loading the page if not cached)
6. Mark page dirty
7. Update bucket_meta count in memory
8. _totalElementCount++
9. Check if bucket split is needed (deferred to Serialize)

#### RemoveAt(index) — KEY IMPROVEMENT

BEFORE: Load ALL pages from index to end, shift every element O(n)
AFTER:
1. Only load the page containing index
2. Remove element from page
3. Mark page dirty
4. Update bucket_meta count in memory
5. _totalElementCount--
6. Check if bucket merge is needed (deferred to Serialize)

#### Serialize

1. Check pending bucket splits: for each bucket exceeding bucketSize
   a. Load all pages overlapping with the bucket (may evict other pages from cache)
   b. Split bucket, rebalance ranks
   c. Update affected elements' SvId paths (save at new path, delete at old)
2. Check pending bucket merges: merge underfull buckets
3. Flush all dirty pages (Save elements at their current SvId paths)
4. Delete all IDs in _idsDeleted from storage
5. Update CollectionMetadata (including BucketMetas)
6. _idsDeleted.Clear()
7. Invalidate cached pages that had SvId changes (they reference old paths)

**Cache invalidation after rebalance/split:**
- Pages containing elements whose SvId changed are invalidated
- Invalidated pages are removed from LRU cache
- Next access will reload from storage at the new SvId path

#### Bucket Splitting (detailed)

1. Triggered when a bucket's count > bucketSize (checked at Serialize time)
2. Load all pages that contain elements from the bucket
   - This may require loading pages that weren't previously cached
   - Cost: O(bucketSize / pageSize) page loads
3. Split elements into two groups (first half, second half)
4. Assign new bucket prefixes:
   - Original bucket keeps its prefix
   - New bucket gets next sequential Base62 prefix (e.g. "A" -> "B")
5. Rebalance each group to short ranks within their prefixes
6. For each element with changed rank:
   a. Save at new SvId path
   b. Delete at old SvId path
7. Update BucketMetas: insert new bucket entry, update counts
8. Invalidate affected cached pages

### 7. Data Flow Diagram

┌─────────────────────────────────────────────────────────────┐
│                        Collection Layer                     │
│                                                             │
│  ObservableListSavable / ObservableLazyListSavable          │
│  ├── In-memory ID list (ordered by LexoRank)                │
│  ├── BucketMeta[] (cached from metadata)                    │
│  └── Elements (full or paged)                               │
│                                                             │
│  Insert:  compute rank → set SvId → update memory           │
│  Remove:  remove from memory → update bucket_meta count     │
│  Serialize: batch save dirty elements → sync ID list        │
└────────────────────────┬────────────────────────────────────┘
                         │
                          │ ListCollectionIds (full / paged)
                          │ (Save/Delete elements by SvId path)
                          │ (BucketMetas read/written via CollectionMetadata)
                         ▼
┌────────────────────────────────────────────────────────────────────────┐
│                       Serializer Layer                                 │
│                                                                        │
│  ListCollectionIds(propName)                    → scan keys, sort      │
│  ListCollectionIds(propName, bucket, skip, size)→ prefix scan, skip    │
│  Save<T>(element, ctx)                → write at SvId path             │
│  Delete(ctx, id)                      → remove at path                 │
└────────────────────────┬───────────────────────────────────────────────┘
                         │
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                   Persistence Backend                       │
│  (Concrete Serializer implementation — not yet built)       │
│                                                             │
│  Storage layout:                                            │
│    collectionPath/A~m/            → element data            │
│    collectionPath/A~abc123/       → element data            │
│    collectionPath/B~def456/       → element data            │
│    collectionPath/__ob_metadata__/ → CollectionMetadata     │
│                                                             │
│  Listing keys under collectionPath returns IDs in           │
│  lexicographic order = LexoRank order                       │
└─────────────────────────────────────────────────────────────┘

## Known Limitations

1. **No backward compatibility**: Existing saves using integer-index IDs will not load. Loading an old-format save will return an empty collection. A migration tool can be added later if needed.
2. **_idsDeleted grows between serializes**: Each RemoveAt adds an ID to _idsDeleted. If many removals occur without Serialize, the set grows. Cleanup only happens at Serialize time.
3. **Duplicate data window during rebalance**: If serialization fails mid-rebalance, old and new SvId paths both exist. Data is not lost, but storage is temporarily duplicated. Cleanup happens on next successful Serialize.
4. **Bucket split/merge cost**: These operations require loading all affected pages and rewriting all SvId paths. They are amortized (triggered infrequently) but can cause a Serialize spike.
5. **Cross-bucket insertion produces non-prefix-aligned ranks**: Inserting between "A~xyz" and "B~abc" produces "A~xyz0". The element belongs to bucket A.

## Error Handling

| Scenario | Behavior |
|----------|----------|
| LexoRank precision exhausted (rank length exceeds threshold) | Mark bucket for rebalance at next Serialize |
| Bucket already in shortest representation | Skip rebalance, continue appending precision (rare edge case) |
| Bucket overflow (count > bucketSize) | Mark for split at next Serialize |
| Bucket underflow (count < bucketSize / 4) | Mark for merge with adjacent bucket at next Serialize |
| Serializer returns empty ID list | Collection treats as empty, first element gets initial rank "A~m" |
| Page load fails (LazyList) | Throw InvalidOperationException with collection path context |
| Deleted element accessed (LazyList) | Throw ArgumentOutOfRangeException |
| Between() called with prev == next | Throw ArgumentException (indicates logic error) |
| Serialization fails mid-rebalance | Old SvId paths remain intact; duplicate data cleaned on next successful Serialize |

## Thread Safety

- `ObservableListSavable`: NOT thread-safe. Standard Unity main-thread usage assumed.
- `ObservableLazyListSavable`: Uses `ReaderWriterLockSlim` for page cache concurrency. Insert/Remove acquire write locks. Page reads acquire read locks.
- `LexoRank` static class: Thread-safe (pure functions, no shared state).
- `CollectionMetadata`: Only mutated during Serialize/Deserialize (single-threaded context).

## Testing Strategy

### LexoRank Algorithm Tests
- `Between(a, b)` always produces a string that sorts strictly between a and b
- `Between(null, null)` returns "A~m" (initial rank)
- `Between("A~a", null)` returns a string > "A~a"
- `Between(null, "A~z")` returns a string < "A~z"
- Precision expansion: `Between("A~a", "A~b")` returns "A~a0" (or equivalent)
- `Rebalance(n)` returns n strings in sorted order, each with minimal length
- `NeedsRebalance` returns false for optimally packed ranks
- `Compare` matches `string.CompareOrdinal` for all rank strings
- CHARSET ordering is correct: 0-9 < A-Z < a-z
- All ranks contain "~" separator with a non-empty prefix (default "A")

### Collection Integration Tests
- Insert at beginning, middle, end — elements maintain correct order after reload
- Remove from beginning, middle, end — remaining elements maintain correct order
- Rapid insertions without Serialize — ranks don't collide
- Serialize → Deserialize round-trip preserves element order
- Dirty element re-save uses correct SvId path
- Deleted element cleanup works (old IDs removed from storage)

### Bucket System Tests
- Bucket split triggers at correct count threshold
- Bucket merge triggers at correct underflow threshold
- Page seeking lands on correct bucket
- Cross-bucket insertion produces correct rank
- Bucket split during Serialize handles cached pages correctly
- Cache invalidation after rebalance works

### ObservableLazyListSavable Tests
- Insert does NOT load pages beyond the insertion point
- Remove does NOT load pages beyond the removal point
- Page loading respects LRU eviction
- Pending operations merge correctly with loaded pages
- Serialize flushes dirty pages and cleans up deleted IDs

## Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| LexoRank.Between | < 1μs | Span-based, no heap allocation for ranks < 16 chars |
| Insert (no rebalance) | O(1) page load (LazyList), O(1) list insert (List) | No array shifting |
| Remove | O(1) page load (LazyList), O(1) list remove (List) | No array shifting |
| Serialize (no rebalance) | O(n) element saves + O(n) ID listing | n = dirty element count |
| Rebalance (bucket) | O(bucketSize) | Amortized: triggered once per bucketSize inserts |
| Page seek (LazyList) | O(log buckets) | Binary search on cumulative bucket index (collection layer) |
| ListCollectionIds (paginated) | O(pageSize) | Prefix-scoped key scan, no BucketMetas iteration |

## File Structure

Salvavida/Package/Runtime/
├── LexoRank.cs                          # NEW: LexoRank algorithm (Span-based)
├── LazyLoading/
│   ├── CollectionMetadata.cs            # MODIFIED: add BucketMetas, BucketMultiplier
│   └── ...existing files...
├── ObservableList.cs                    # MODIFIED: ObservableListSavable
├── ObservableLazyListSavable.cs         # MODIFIED: LexoRank + bucket integration
├── Serializer.cs                        # MODIFIED: add 3 abstract methods
└── ...existing files...

Salvavida.Tests/
├── LexoRankTests.cs                     # NEW: LexoRank unit tests
├── ObservableListLexoRankTests.cs       # NEW: Collection integration tests
└── ObservableLazyListLexoRankTests.cs   # NEW: LazyList integration tests

## Implementation Phases

| Phase | Content | Dependencies |
|-------|---------|--------------|
| 1 | LexoRank core algorithm (Span-based, Base62, Between, Rebalance) | - |
| 2 | BucketMeta struct + CollectionMetadata extension | - |
| 3 | Serializer abstract methods (ListCollectionIds x2 with bucketId param) | Phase 2 |
| 4 | ObservableListSavable: deserialization via ListCollectionIds | Phase 3 |
| 5 | ObservableListSavable: Insert/Remove with LexoRank | Phase 1, 4 |
| 6 | ObservableListSavable: Serialize with batch ID sync | Phase 5 |
| 7 | ObservableLazyListSavable: deserialization with bucket index | Phase 2, 3 |
| 8 | ObservableLazyListSavable: Insert/Remove O(1) page loading | Phase 1, 7 |
| 9 | ObservableLazyListSavable: bucket splitting | Phase 8 |
| 10 | ObservableLazyListSavable: Serialize with ID sync | Phase 8 |
| 11 | Unit tests: LexoRank algorithm | Phase 1 |
| 12 | Unit tests: Collection operations | Phase 6, 10 |
| 13 | Integration tests: save/load round-trip | Phase 6, 10 |

# LexoRank 变体实现方案：

## 普通 LexoRank 相关功能：

- dotnet C#13 netstandard2.1的库
- 需要使用 Span<T> 的技术减少 string 操作带来的内存开销
- 它仅实现 base62编码，即[0-9A-Za-z]
- 它需要实现：GetInitValue(chunkSize), Middle(x, y), GenPrev(x, chunkSize), GenNext(x, chunkSize)
- 还有万能方法： Generate(x?, y?, chunkSize)：
  - x == null 时， 等同于 GenPrev(y)
  - y == null 时， 等同于 GenNext(x)
  - x == null 且 y == null 时， 等同于 GetInitValue()
  - x != null 且 y != null 时， 等同于 Middle(x, y)

## LexoRank 的变体功能：

LexoRank,有 bucket 概念，用来方便重排 rank 的时候不改变数据的排序功能。
我想引入 chunk 概念，即一个 chunk 包含大约x个数据，通过保存 chunkId，能够有效地对数据进行快速遍历。
所以现在的 LexoRank 的字符串格式如下（其中`~`是分隔符）：
`{ChunkId}~{BucketId}~{Rank}`

### ChunkSize

ChunkSize 的存在的意义就是分配 InitialValue 的时候预留足够的空间生成 Prev 或 Next 的值，避免过早遇到需要重排的异常。

假设我们的 chunkSize 是 1000， 精度数字是 2 位数： Math.Ceil(Math.Log(1000, 62))，那么在GenPrev 和 GenNext的时候应当遵照 2 位数精度进行移动，即 GenPrev("5")时应该返回"4s",而不是一位数。

## 其它说明

- 代码放置位置： Salvavida/Package/Runtime/LexoRank/LexoRank.cs
- 命名空间： Salvavida
— GenPrev和 GenNext 的 stepSize 是 8。
- 不允许跨 Chunk 分配 Rank，即 prev 和 next 的 ChunckId 必须是一致的。
- Middle 方法中，当 prev 和 next 相同长度的精度耗尽时（如：prev = "AA", next = "AB"),应当增加精度字符（即返回"AAV")
- 当GetPrev 或 GetNext 精度快要耗尽时，应当使用 Middle 方法防止精度用完，比如 当 GetPrev(1)时，应该调用Middle(0, 1),或 GetNext(y)时，应当调用 Middle(y,z)。
- 生成的 LexoRank 禁止以 0 结尾，如果解析的时候碰到结尾数字是0 时应当抛弃（正常不影响解析）

## 代码参考：

- /Users/snake/workspace/dotnet/LexoRank-master
- 请勿参照 FakeLexoRank 的代码

## 单元测试包含的内容

- LexoRankTests
  - Generate_BothNull_ReturnsInitialValue
  - Generate_ValidFormat
  - Generate_BothNull_LargeBucketSize_ReturnsLongerRank
  - Generate_PrevNull_UsesGenPrev
  - Generate_NextNull_UsesGenNext
  - Generate_BothProvided_UsesMiddle
  - Middle_PrevNull_ThrowsArgumentNullException
  - Middle_NextNull_ThrowsArgumentNullException
  - Middle_BothNull_ThrowsArgumentNullException
  - Middle_SameBucket_ReturnsMidpoint
  - Middle_CrossBucket_ThrowsArgumentException
  - Middle_AdjacentChars_AppendsMiddleChar
  - Middle_PrecisionExpansion_ReturnsValidRank
  - Middle_EqualNonNull_ThrowsArgumentException
  - Middle_PrevIsPrefixOfNext_ReturnsValidRank
  - Middle_AdjacentChars_Insert100Times_NoErrorAndMaintainsOrder
  - Middle_AlwaysProducesValidRank
  - GenNext_NeverRunOutOfPrecision
  - GenPrev_NeverRunOutOfPrecision
  - RapidInsertsAtBeginning_ProducesOrderedRanks
  - RapidInsertsAtEnd_ProducesOrderedRanks
  - RandomInsert_UsingInsertMethod_ProducesOrderedRanks
# Test Classification Matrix

This page lists the trait categories used in the test suite and shows how to execute tests filtered by those categories.

## Trait categories

- **Domain**: Compression, Decompression, Frames, Streaming, Encoder, Decoder, Concurrency, Fuzz
- **Type**: RoundTrip, Edge, Stress, Config, Fuzz, Enumeration, Metadata, MultiFrame, Truncation, Lifecycle, Reset, Flush, HugeFrame, DestinationTooSmallLoop, NeedMoreData, MultiFrameStatus, StaticAPI, ExclusiveOperation, Cancellation
- **Mode**: Sync, Async
- **Size**: Empty, Large
- **Feature**: ManySmallWrites, IrregularChunks, Level, SmallBufferLoop, HalfFrame, MultiFrameOffsets, ContentSizeOptional, StreamingVsInMemory, Skippable, MixedKnownUnknownContentSize, IAsyncEnumerableDecoder, RandomChunks, 4GiBPlus, FlushFrameBoundary, SpanFlushLoop

## Running filtered test sets

- Frames metadata tests
	```
	dotnet test --filter "Domain=Frames&Type=Metadata"
	```

- Async streaming tests (excluding fuzz)
	```
	dotnet test --filter "Domain=Streaming&Mode=Async&Type!=Fuzz"
	```

- Encoder/Decoder status tests
	```
	dotnet test --filter "(Domain=Encoder|Domain=Decoder)"
	```

- Large / huge data boundary checks
	```
	dotnet test --filter "(Size=Large|Type=HugeFrame)"
	```

- Quick smoke (skip stress-heavy categories)
	```
	dotnet test --filter "Type!=Fuzz&Type!=HugeFrame&Type!=Stress"
	```

Update the category lists above whenever new trait values are introduced.

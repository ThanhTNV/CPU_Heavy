# CPU_Heavy

A small collection of CPU-bound workloads built in .NET, each implemented twice — once sequentially and once with a parallel/streaming design — so the speed-up can be measured, not assumed.

The workloads are modelled on what a Digital Asset Management (DAM) system actually does: process bulk images, crunch large log/CSV exports, and index documents for search. Every tool exposes a `Sequential` and a `Parallel` mode and reports wall-clock time, throughput, and peak memory side by side.

## Tools

### 1. Bulk image pipeline

Upload a zip of photos (or use the bundled sample set). For every image the pipeline:

- generates thumbnails at three sizes (e.g. 128 / 512 / 1024 px, longest edge)
- computes a perceptual hash (dHash / pHash)
- extracts the dominant colours (k-means on a downsampled copy)
- flags near-duplicates by Hamming distance between hashes

**Sequential:** one image at a time, results written when everything finishes.

**Parallel:** a bounded `Channel<T>` fed by a single zip reader, `Parallel.ForEachAsync` workers doing the CPU work, and a single writer draining results. Finished items stream to the UI as they complete rather than at the end of the batch. The zip is read and written on one thread each because `ZipArchive` is not thread-safe; only the image work is parallel.

### 2. Large CSV / log analytics

Upload a CSV in the ~200 MB range. The tool parses it, groups by a chosen column, aggregates (count, sum, min/max, mean) and computes percentiles (p50 / p95 / p99).

**Sequential:** single-pass parse into an in-memory aggregate.

**Parallel:** the file is split into byte-range chunks aligned to line boundaries. Each partition parses and aggregates independently into its own dictionary, then a merge step combines partitions. Percentiles are computed from mergeable sketches (t-digest) rather than sorted arrays, so no partition ever needs the whole dataset. The point is to demonstrate partitioning and merge, not just wrapping a loop in `Parallel.ForEach`.

### 3. Full-text indexing + search

Upload a document set (txt / md / html). The tool tokenises, normalises, and builds an inverted index (`term → postings list`), then serves queries against it with basic ranking (TF-IDF or BM25).

**Sequential:** documents indexed one by one into a single index.

**Parallel:** documents are partitioned across workers, each builds a local index, and the local indices are merged term-by-term into the final index. Search itself is single-threaded per query; the parallelism is in the build.

### 4. PDF batch processing (stretch goal)

Text extraction and page rasterising across many PDFs. This is a realistic workload but the heavy lifting lives in the PDF library, so it is harder to demonstrate that the orchestration code — rather than the library — is responsible for the speed-up. Kept as an optional fourth tool for that reason.

## What is being measured

Each run reports:

| Metric | Why |
|---|---|
| Wall-clock time | The headline number |
| Items / second | Throughput, comparable across input sizes |
| Peak working set | Parallel code that wins on time but blows up memory is not a win |
| Speed-up vs sequential | `T_seq / T_par` on the same input and machine |
| Time to first result | Streaming pipelines should show results long before completion |

Benchmarks are run on the same machine, same input, with a warm-up pass. Results are indicative, not scientific.

## Project layout

```
CPU_Heavy/
├── src/
│   ├── CpuHeavy.Web/              # UI: upload, run, live results, timing
│   ├── CpuHeavy.Images/           # Tool 1
│   ├── CpuHeavy.Analytics/        # Tool 2
│   ├── CpuHeavy.Search/           # Tool 3
│   ├── CpuHeavy.Pdf/              # Tool 4 (optional)
│   └── CpuHeavy.Core/             # Shared: pipeline primitives, timing, reporting
├── samples/                       # Sample image set, CSV, document corpus
├── benchmarks/                    # BenchmarkDotNet projects
└── tests/
```

## Getting started

Requirements: .NET 8 SDK or later.

```bash
git clone https://github.com/<you>/CPU_Heavy.git
cd CPU_Heavy
dotnet run --project src/CpuHeavy.Web
```

Open the URL printed in the console, pick a tool, upload an input (or choose the sample set), and run it in each mode.

To run the benchmark suite:

```bash
dotnet run -c Release --project benchmarks/CpuHeavy.Benchmarks
```

## Key libraries

| Purpose | Library |
|---|---|
| Image decoding / resizing / drawing | SixLabors.ImageSharp, SixLabors.ImageSharp.Drawing |
| CSV parsing | CsvHelper (baseline); custom span-based parser for the parallel path |
| Percentile sketches | T-Digest implementation in `CpuHeavy.Core` |
| PDF (stretch) | PDFium via PDFtoImage / PdfPig for text |
| Benchmarking | BenchmarkDotNet |

## Design notes

- **Bounded channels everywhere.** Producers block when consumers fall behind, so memory stays flat regardless of input size.
- **Parallelise the CPU work, serialise the I/O.** Zip archives, output files, and the UI update stream each have exactly one writer.
- **Partition → local aggregate → merge.** Tools 2 and 3 use the same shape. It avoids shared mutable state and lock contention, and it scales to more cores without changing the code.
- **Order is not guaranteed** in streaming mode. Where the UI needs stable order, items carry an index and are re-sorted on the client.

## Roadmap

- [ ] Tool 1: image pipeline
- [ ] Tool 2: CSV analytics
- [ ] Tool 3: full-text index + search
- [ ] Tool 4: PDF batch (stretch)
- [ ] Benchmark results published in `benchmarks/RESULTS.md`

## License

MIT
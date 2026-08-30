using System.IO.Compression;
using System.Threading.Channels;

namespace BulkImagePipeline;

public record ImageEntry(string Name, byte[] Data);

public class BulkImagePipelineProcessor(
        Func<byte[], byte[]> transform,
        int workerCount = 0,
        int bufferSize = 32)
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    private readonly int _workerCount = workerCount > 0 ? workerCount : Environment.ProcessorCount;

    public async Task ProcessAsync(string inputZipPath, string outputZipPath, CancellationToken ct = default)
    {
        var toProcess = Channel.CreateBounded<ImageEntry>(new BoundedChannelOptions(bufferSize)
        {
            SingleWriter = true,
            SingleReader = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        var toWrite = Channel.CreateBounded<ImageEntry>(new BoundedChannelOptions(bufferSize)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = ProduceAsync(inputZipPath, toProcess.Writer, ct);

        var consumers = Enumerable.Range(0, _workerCount)
            .Select(_ => ConsumeAsync(toProcess.Reader, toWrite.Writer, ct))
            .ToArray();

        var writer = WriteAsync(outputZipPath, toWrite.Reader, ct);


        // When all consumers finish, close the output channel so the writer can complete
        var closeOutput = Task.WhenAll(consumers).ContinueWith(
            t => toWrite.Writer.Complete(t.Exception), TaskScheduler.Default);

        await Task.WhenAll(producer, closeOutput, writer);
    }

    private static async Task ProduceAsync(string path, ChannelWriter<ImageEntry> output, CancellationToken ct)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            foreach (var entry in archive.Entries)
            {
                if (!ImageExtensions.Contains(Path.GetExtension(entry.Name))) continue;

                using var es = entry.Open();
                using var ms = new MemoryStream((int)entry.Length);
                await es.CopyToAsync(ms, ct);

                await output.WriteAsync(new ImageEntry(entry.FullName, ms.ToArray()), ct);
            }
            output.Complete();
        }
        catch (Exception ex)
        {
            output.Complete(ex);
            throw;
        }
    }

    private async Task ConsumeAsync(ChannelReader<ImageEntry> input, ChannelWriter<ImageEntry> output, CancellationToken ct)
    {
        await foreach (var entry in input.ReadAllAsync(ct))
        {
            // CPU-bound work; run on a thread pool thread so we don't block the async loop
            var data = await Task.Run(() => transform(entry.Data), ct);
            await output.WriteAsync(entry with { Data = data }, ct);
        }
    }

    private static async Task WriteAsync(string path, ChannelReader<ImageEntry> input, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);

        await foreach (var entry in input.ReadAllAsync(ct))
        {
            var zipEntry = archive.CreateEntry(entry.Name, CompressionLevel.Fastest);
            await using var es = zipEntry.Open();
            await es.WriteAsync(entry.Data, ct);
        }
    }

}

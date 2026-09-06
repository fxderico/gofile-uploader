using System.IO;

namespace GofileUploader;

/// <summary>
/// Wraps a readable stream and reports read progress (0-100) as bytes flow through it.
/// Used to drive the upload progress bar while HttpClient streams the file to gofile.
/// </summary>
internal sealed class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly long _totalLength;
    private readonly IProgress<double>? _progress;
    private long _bytesRead;

    public ProgressStream(Stream inner, long totalLength, IProgress<double>? progress)
    {
        _inner = inner;
        _totalLength = totalLength;
        _progress = progress;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = _inner.Read(buffer, offset, count);
        Report(n);
        return n;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int n = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        Report(n);
        return n;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int n = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Report(n);
        return n;
    }

    private void Report(int n)
    {
        if (n <= 0 || _totalLength <= 0) return;
        _bytesRead += n;
        _progress?.Report((double)_bytesRead / _totalLength * 100.0);
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}

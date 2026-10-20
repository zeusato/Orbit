using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Orbit.App.Imaging;

namespace Orbit.App.Services;

public sealed record JobRecord(string Source, ToolKind Tool, JobState State, string? Output, string? Error, DateTime FinishedUtc, string? ResultDetail = null);

public sealed class ImageJob(ImageRequest request) : INotifyPropertyChanged
{
    public ImageRequest Request { get; } = request;
    public DateTime FinishedUtc { get; internal set; }
    internal static ImageJob Restore(JobRecord record) => new(new ImageRequest(record.Source, record.Tool))
    { _state = record.State, _progress = record.State == JobState.Completed ? 100 : 0, _output = record.Output, _error = record.Error, FinishedUtc = record.FinishedUtc, ResultDetail = record.ResultDetail };
    public string? ResultDetail { get; internal set; }
    public string Name => Path.GetFileName(Request.Source);
    public string Tool => ToolCatalog.Description(Request.Tool);
    public CancellationTokenSource Cancellation { get; } = new();
    private JobState _state = JobState.Queued;
    private int _progress;
    private string? _output;
    private string? _error;
    public JobState State { get => _state; internal set { _state = value; Changed(); Changed(nameof(Status)); Changed(nameof(CanCancel)); Changed(nameof(CanOpen)); } }
    public int Progress { get => _progress; internal set { _progress = value; Changed(); Changed(nameof(Status)); } }
    public string? Output { get => _output; internal set { _output = value; Changed(); Changed(nameof(CanOpen)); } }
    public string? Error { get => _error; internal set { _error = value; Changed(); Changed(nameof(Status)); } }
    public bool CanCancel => State is JobState.Queued or JobState.Running;
    public bool CanOpen => State == JobState.Completed && Output != null;
    public string Status => State switch
    {
        JobState.Queued => "Đang xếp hàng", JobState.Running => $"Đang xử lý · {Progress}%",
        JobState.Completed => "Hoàn tất · " + Path.GetFileName(Output) + (ResultDetail == null ? "" : "\n" + ResultDetail), JobState.Cancelled => "Đã hủy", _ => "Lỗi · " + Error
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class JobQueue : IDisposable
{
    private readonly BlockingCollection<ImageJob> _pending = new(), _media = new();
    private readonly Dispatcher _ui;
    private readonly Thread _worker;
    private readonly ImageEngine _engine = new();
    private bool _disposed;
    public ObservableCollection<ImageJob> Jobs { get; } = [];
    public bool HasPending => Jobs.Any(j => j.CanCancel);
    // Resolved when a job is queued, so changing the setting never moves jobs already waiting.
    public Func<string, string?>? OutputFor { get; set; }
    public event Action<ImageJob>? Finished;

    private readonly string? _historyFile;
    public const int HistoryLimit = 200;
    // History holds only finished rows (paths, tool, result) in the local data folder; never file contents.
    public JobQueue(Dispatcher ui, string? historyFile = null)
    {
        _ui = ui; _historyFile = historyFile;
        if (historyFile != null)
        {
            try
            {
                if (File.Exists(historyFile))
                    foreach (var record in (System.Text.Json.JsonSerializer.Deserialize<JobRecord[]>(File.ReadAllText(historyFile)) ?? []).Take(HistoryLimit))
                        if (record is { Source.Length: > 0 } && Enum.IsDefined(record.Tool) && record.State is JobState.Completed or JobState.Failed or JobState.Cancelled)
                            Jobs.Add(ImageJob.Restore(record));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        Finished += job => { job.FinishedUtc = DateTime.UtcNow; SaveHistory(); };
        _worker = new Thread(() => Work(_pending)) { IsBackground = true, Name = "Orbit image worker" };
        _worker.SetApartmentState(ApartmentState.STA); _worker.Start();
        // Long audio/video jobs run on their own worker so image and PDF jobs never wait behind them.
        var media = new Thread(() => Work(_media)) { IsBackground = true, Name = "Orbit media worker" };
        media.SetApartmentState(ApartmentState.MTA); media.Start();
    }

    public void Add(IEnumerable<ImageRequest> requests)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var queued in requests)
        {
            var request = queued.OutputDirectory == null && OutputFor?.Invoke(queued.Source) is { } output ? queued with { OutputDirectory = output } : queued;
            var job = new ImageJob(request); Jobs.Insert(0, job); (ToolCatalog.IsMediaTool(request.Tool) ? _media : _pending).Add(job);
        }
        // Keep the UI bounded while retaining every pending job.
        for (int i = Jobs.Count - 1; i >= 200; i--) if (!Jobs[i].CanCancel) Jobs.RemoveAt(i);
    }

    private void Post(Action action)
    {
        if (!_ui.HasShutdownStarted) _ui.BeginInvoke(action);
    }

    private void Work(BlockingCollection<ImageJob> pending)
    {
        foreach (var job in pending.GetConsumingEnumerable())
        {
            try
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                Post(() => job.State = JobState.Running);
                var bytesBefore = job.Request.Tool == ToolKind.PdfCompress ? new FileInfo(job.Request.Source).Length : 0;
                var output = ToolCatalog.IsImageTool(job.Request.Tool)
                    ? _engine.Execute(job.Request, job.Cancellation.Token, p => Post(() => job.Progress = p))
                    : ToolCatalog.IsMediaTool(job.Request.Tool) ? MediaEngine.Execute(job.Request, job.Cancellation.Token, p => Post(() => job.Progress = p))
                    : new DocumentEngine().Execute(job.Request, job.Cancellation.Token, p => Post(() => job.Progress = p));
                var detail = job.Request.Tool == ToolKind.PdfCompress ? PdfCompression.Describe(bytesBefore, new FileInfo(output).Length) : null;
                Post(() => { job.Output = output; job.ResultDetail = detail; job.State = JobState.Completed; Finished?.Invoke(job); });
            }
            catch (OperationCanceledException) { Post(() => { job.State = JobState.Cancelled; Finished?.Invoke(job); }); }
            catch (Exception error)
            {
                Post(() => { job.Error = FriendlyError(error); job.State = JobState.Failed; Finished?.Invoke(job); });
            }
        }
    }

    private static string FriendlyError(Exception error) => error switch
    {
        UnauthorizedAccessException => "Không có quyền ghi vào thư mục nguồn. Hãy sao chép ảnh sang thư mục có quyền ghi rồi thử lại.",
        FileFormatException => "Nội dung file không phải ảnh hợp lệ hoặc codec chưa hỗ trợ.",
        IOException => "Không thể đọc hoặc ghi file. " + error.Message,
        _ => error.Message
    };

    public void SaveHistory()
    {
        if (_historyFile == null) return;
        try
        {
            var records = Jobs.Where(j => !j.CanCancel).Take(HistoryLimit).Select(j => new JobRecord(j.Request.Source, j.Request.Tool, j.State, j.Output, j.Error, j.FinishedUtc, j.ResultDetail)).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(_historyFile)!);
            var temp = _historyFile + ".tmp"; File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(records)); File.Move(temp, _historyFile, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
    public void ClearHistory()
    {
        for (int i = Jobs.Count - 1; i >= 0; i--) if (!Jobs[i].CanCancel) Jobs.RemoveAt(i);
        SaveHistory();
    }
    public void CancelAll() { foreach (var job in Jobs.Where(j => j.CanCancel)) job.Cancellation.Cancel(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelAll(); _pending.CompleteAdding(); _media.CompleteAdding();
        // Worker exits after any in-progress native encoder reaches its cancellation boundary.
    }
}

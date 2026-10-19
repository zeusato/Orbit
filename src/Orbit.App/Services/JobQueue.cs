using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using Orbit.App.Imaging;

namespace Orbit.App.Services;

public sealed class ImageJob(ImageRequest request) : INotifyPropertyChanged
{
    public ImageRequest Request { get; } = request;
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
        JobState.Completed => "Hoàn tất · " + Path.GetFileName(Output), JobState.Cancelled => "Đã hủy", _ => "Lỗi · " + Error
    };
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class JobQueue : IDisposable
{
    private readonly BlockingCollection<ImageJob> _pending = new();
    private readonly Dispatcher _ui;
    private readonly Thread _worker;
    private readonly ImageEngine _engine = new();
    private bool _disposed;
    public ObservableCollection<ImageJob> Jobs { get; } = [];
    public bool HasPending => Jobs.Any(j => j.CanCancel);
    public event Action<ImageJob>? Finished;

    public JobQueue(Dispatcher ui)
    {
        _ui = ui;
        _worker = new Thread(Work) { IsBackground = true, Name = "Orbit image worker" };
        _worker.SetApartmentState(ApartmentState.STA); _worker.Start();
    }

    public void Add(IEnumerable<ImageRequest> requests)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (var request in requests)
        {
            var job = new ImageJob(request); Jobs.Insert(0, job); _pending.Add(job);
        }
        // Keep the UI bounded while retaining every pending job.
        for (int i = Jobs.Count - 1; i >= 200; i--) if (!Jobs[i].CanCancel) Jobs.RemoveAt(i);
    }

    private void Post(Action action)
    {
        if (!_ui.HasShutdownStarted) _ui.BeginInvoke(action);
    }

    private void Work()
    {
        foreach (var job in _pending.GetConsumingEnumerable())
        {
            try
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();
                Post(() => job.State = JobState.Running);
                var output = ToolCatalog.IsImageTool(job.Request.Tool)
                    ? _engine.Execute(job.Request, job.Cancellation.Token, p => Post(() => job.Progress = p))
                    : new DocumentEngine().Execute(job.Request, job.Cancellation.Token, p => Post(() => job.Progress = p));
                Post(() => { job.Output = output; job.State = JobState.Completed; Finished?.Invoke(job); });
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

    public void CancelAll() { foreach (var job in Jobs.Where(j => j.CanCancel)) job.Cancellation.Cancel(); }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; CancelAll(); _pending.CompleteAdding();
        // Worker exits after any in-progress native encoder reaches its cancellation boundary.
    }
}

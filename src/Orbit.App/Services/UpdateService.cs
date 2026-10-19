using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace Orbit.App.Services;

public sealed class UpdateService : IDisposable
{
    public const string Repository = "zeusato/Orbit";
    private readonly OrbitApplication _app;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(5) };
    private readonly CancellationTokenSource _stop = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private string? _work;
    private UpdateManifest? _pending;
    private bool _busy, _applying;
    private DateTime _deferUntil;
    private string PendingFile => Path.Combine(AppSettings.DataDirectory, "pending-update.json");
    public static string CurrentVersion { get { var v = typeof(OrbitApplication).Assembly.GetName().Version!; return $"{v.Major}.{v.Minor}.{v.Build}"; } }
    public string Status { get; private set; } = "Chưa kiểm tra bản cập nhật.";
    public bool IsBusy => _busy || _applying;
    public bool CanApply => _pending != null && !IsBusy;
    public event Action? Changed;
    public UpdateService(OrbitApplication app)
    {
        _app = app; _http.DefaultRequestHeaders.UserAgent.ParseAdd("Orbit/" + CurrentVersion);
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _timer.Tick += async (_, _) =>
        {
            if (!_app.Settings.AutoUpdate || IsBusy) return;
            if (_pending != null)
            {
                if (DateTime.UtcNow >= _deferUntil && _app.IsIdleForUpdate(false)) await ApplyAsync(false);
            }
            else if (DateTime.UtcNow - _app.Settings.LastUpdateCheckUtc >= TimeSpan.FromHours(12)) await CheckAsync();
        };
    }
    public async void Start()
    {
        _busy = true;
        try
        {
            if (File.Exists(PendingFile))
            {
                using var saved = JsonDocument.Parse(File.ReadAllText(PendingFile));
                var id = saved.RootElement.GetProperty("id").GetString()!;
                if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Thư mục tải không hợp lệ.");
                var work = UpdatePackage.SafePath(Path.Combine(AppSettings.DataDirectory, "updates"), id);
                var manifest = UpdatePackage.VerifyManifest(File.ReadAllBytes(Path.Combine(work, "manifest.json")), File.ReadAllText(Path.Combine(work, "manifest.sig")));
                if (UpdatePackage.ParseVersion(manifest.Version) > UpdatePackage.ParseVersion(CurrentVersion))
                {
                    await Task.Run(() => UpdatePackage.VerifyArchive(Path.Combine(work, "package.zip"), manifest), _stop.Token);
                    _work = work; _pending = manifest; _deferUntil = saved.RootElement.GetProperty("deferUntil").GetDateTime();
                    SetStatus($"Orbit {manifest.Version} đã tải sẵn; chờ lúc rảnh để cập nhật.");
                }
                else File.Delete(PendingFile);
            }
        }
        catch (Exception error) { if (!_stop.IsCancellationRequested) SetStatus("Không khôi phục được bản tải trước: " + error.Message); }
        finally { _busy = false; if (!_stop.IsCancellationRequested) _timer.Start(); Changed?.Invoke(); }
    }
    private void SetStatus(string value) { Status = value; Changed?.Invoke(); }
    public void Defer()
    {
        _deferUntil = DateTime.UtcNow.AddHours(4);
        try { SavePending(); SetStatus("Đã hoãn cập nhật 4 giờ. Có thể bấm Cập nhật ngay bất cứ lúc nào."); }
        catch (Exception error) { SetStatus("Đã hoãn trong phiên này; chưa lưu được lựa chọn: " + error.Message); }
    }
    private void SavePending()
    {
        if (_work == null) return;
        var temp = PendingFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new { id = Path.GetFileName(_work), deferUntil = _deferUntil })); File.Move(temp, PendingFile, true);
    }

    public static Uri AssetUri(JsonElement release, string name, string version)
    {
        var assets = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("Release thiếu hoặc trùng file " + name);
        var uri = new Uri(assets[0].GetProperty("browser_download_url").GetString()!);
        var expected = $"https://github.com/{Repository}/releases/download/v{version}/{name}";
        if (uri.AbsoluteUri != expected) throw new InvalidDataException("Đường dẫn release không thuộc nguồn Orbit.");
        return uri;
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken token)
    {
        for (int redirects = 0; redirects < 6; redirects++)
        {
            if (uri.Scheme != "https" || !new[] { "api.github.com", "github.com", "release-assets.githubusercontent.com", "objects.githubusercontent.com", "github-releases.githubusercontent.com" }.Contains(uri.Host)) throw new InvalidDataException("Nguồn tải không được phép.");
            var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var location = response.Headers.Location; response.Dispose();
                uri = location == null ? throw new InvalidDataException("Thiếu địa chỉ chuyển tiếp.") : new Uri(uri, location); continue;
            }
            return response;
        }
        throw new InvalidDataException("Quá nhiều lần chuyển tiếp.");
    }
    private async Task Download(Uri uri, Stream output, long limit, CancellationToken token, Action<long>? progress = null)
    {
        using var response = await GetAsync(uri, token); response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("File tải vượt kích thước cho phép.");
        using var input = await response.Content.ReadAsStreamAsync(token); var buffer = new byte[81920]; long received = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, token); if (read == 0) break;
            received += read; if (received > limit) throw new InvalidDataException("File tải vượt kích thước cho phép.");
            await output.WriteAsync(buffer.AsMemory(0, read), token); progress?.Invoke(received);
        }
    }
    private async Task<byte[]> ReadSmall(Uri uri, long limit, CancellationToken token)
    { using var stream = new MemoryStream(); await Download(uri, stream, limit, token); return stream.ToArray(); }

    public async Task CheckAsync()
    {
        if (IsBusy) return;
        if (_pending != null) { SetStatus($"Orbit {_pending.Version} đã tải xong; chờ cập nhật."); return; }
        _busy = true; SetStatus("Đang kiểm tra GitHub Releases…");
        string? attempt = null;
        try
        {
            _app.Settings.LastUpdateCheckUtc = DateTime.UtcNow; _app.Settings.Save();
            using var response = await GetAsync(new Uri($"https://api.github.com/repos/{Repository}/releases/latest"), _stop.Token);
            if (response.StatusCode == HttpStatusCode.NotFound) { SetStatus("Chưa có release ổn định được phát hành."); return; }
            response.EnsureSuccessStatusCode();
            using var stream = await response.Content.ReadAsStreamAsync(_stop.Token); using var data = new MemoryStream();
            var buffer = new byte[8192]; int n;
            while ((n = await stream.ReadAsync(buffer, _stop.Token)) > 0) { if (data.Length + n > 2_000_000) throw new InvalidDataException("Release metadata quá lớn."); data.Write(buffer, 0, n); }
            using var json = JsonDocument.Parse(data.ToArray()); var release = json.RootElement;
            if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) throw new InvalidDataException("Chỉ nhận release ổn định.");
            var tag = release.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith('v')) throw new InvalidDataException("Release phải dùng tag vX.Y.Z.");
            var version = tag[1..];
            if (UpdatePackage.ParseVersion(version) <= UpdatePackage.ParseVersion(CurrentVersion)) { SetStatus($"Orbit {CurrentVersion} là bản mới nhất."); return; }
            var manifestBytes = await ReadSmall(AssetUri(release, "orbit-update.json", version), 16384, _stop.Token);
            var signatureBytes = await ReadSmall(AssetUri(release, "orbit-update.json.sig", version), 4096, _stop.Token);
            var signature = System.Text.Encoding.UTF8.GetString(signatureBytes);
            var manifest = UpdatePackage.VerifyManifest(manifestBytes, signature);
            if (manifest.Version != version) throw new InvalidDataException("Phiên bản manifest không khớp release.");
            var zipUri = AssetUri(release, manifest.Asset, version);
            attempt = Path.Combine(AppSettings.DataDirectory, "updates", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(attempt);
            await File.WriteAllBytesAsync(Path.Combine(attempt, "manifest.json"), manifestBytes, _stop.Token);
            await File.WriteAllTextAsync(Path.Combine(attempt, "manifest.sig"), signature, _stop.Token);
            var archive = Path.Combine(attempt, "package.zip"); int lastPercent = -1;
            using (var file = new FileStream(archive, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await Download(zipUri, file, manifest.Size, _stop.Token, received => { var percent = (int)(received * 100 / manifest.Size); if (percent != lastPercent) { lastPercent = percent; SetStatus($"Đang tải Orbit {version} · {percent}%"); } });
            await Task.Run(() => UpdatePackage.VerifyArchive(archive, manifest), _stop.Token);
            _work = attempt; _pending = manifest; _deferUntil = DateTime.UtcNow.AddSeconds(60);
            SavePending();
            SetStatus($"Orbit {version} đã sẵn sàng. Tự cập nhật khi các cửa sổ đã đóng và không còn tác vụ.");
        }
        catch (OperationCanceledException) { if (!_stop.IsCancellationRequested) SetStatus("Hết thời gian tải; sẽ thử lại sau."); }
        catch (Exception error) { SetStatus("Chưa cập nhật được: " + error.Message); }
        finally
        {
            // Remove only this failed attempt's known download, never arbitrary files.
            if (attempt != null && attempt != _work) { try { File.Delete(Path.Combine(attempt, "package.zip")); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            _busy = false; Changed?.Invoke();
        }
    }
    public async Task ApplyAsync(bool manual)
    {
        if (!CanApply || _work == null) return;
        if (!_app.IsIdleForUpdate(manual)) { SetStatus("Đóng cửa sổ chỉnh sửa và chờ tác vụ/kéo thả hoàn tất trước khi cập nhật."); return; }
        _applying = true; _app.UpdateInProgress = true; SetStatus("Đang chuẩn bị cập nhật…");
        Process? helper = null;
        try
        {
            var root = AppContext.BaseDirectory; var runner = Path.Combine(_work, "runner"); Directory.CreateDirectory(runner);
            foreach (var name in new[] { "Orbit.Updater.exe", "Orbit.Updater.dll", "Orbit.Updater.deps.json", "Orbit.Updater.runtimeconfig.json", "Orbit.Core.dll" }) File.Copy(Path.Combine(root, name), Path.Combine(runner, name), true);
            using var parent = Process.GetCurrentProcess(); var id = Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(_work, "request.json"), JsonSerializer.Serialize(new UpdatePlan(root, parent.Id, parent.StartTime.ToUniversalTime().Ticks, id)));
            using var ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\Orbit.Update.Ready." + id);
            helper = Process.Start(new ProcessStartInfo(Path.Combine(runner, "Orbit.Updater.exe")) { UseShellExecute = false, CreateNoWindow = true, Arguments = "--apply", WorkingDirectory = runner }) ?? throw new IOException("Không khởi động được helper.");
            var signalled = await Task.Run(() => ready.WaitOne(TimeSpan.FromSeconds(30)));
            if (!signalled || helper.HasExited) throw new IOException("Helper chưa sẵn sàng. Kiểm tra quyền ghi thư mục Orbit hoặc file update-error.txt trong thư mục tải.");
            if (!_app.IsIdleForUpdate(manual)) throw new IOException("Đang có tương tác mới; đã hoãn cập nhật.");
            _app.Shutdown();
        }
        catch (Exception error)
        {
            if (helper != null && !helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(); }
            _deferUntil = DateTime.UtcNow.AddHours(12); _pending = null; _work = null;
            try { File.Delete(PendingFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            SetStatus("Chưa áp dụng được: " + error.Message);
        }
        finally { helper?.Dispose(); _applying = false; _app.UpdateInProgress = false; Changed?.Invoke(); }
    }
    public void Dispose() { _timer.Stop(); _stop.Cancel(); _http.Dispose(); }
}

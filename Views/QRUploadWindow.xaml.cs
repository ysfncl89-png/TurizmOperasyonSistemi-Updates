using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Drawing;
using System.Drawing.Imaging;
using QRCoder;
using System.Windows;
using System.Diagnostics;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace TurizmOperasyonSistemi.Views
{
    public partial class QRUploadWindow : Window
    {
        private TcpListener _tcpListener;
        private int _port;
        private readonly Action<string, string> _onFileSaved;
        private readonly string _token;
        private readonly DateTime _tokenCreatedUtc;
        private readonly TimeSpan _tokenLifetime = TimeSpan.FromMinutes(10);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.List<DateTime>> _ipUploadTimestamps =
            new System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Generic.List<DateTime>>();
        // rate limit settings
        private readonly int _maxUploadsPerWindow = 5;
        private readonly TimeSpan _rateLimitWindow = TimeSpan.FromMinutes(1);

        public QRUploadWindow(Action<string, string> onFileSaved)
        {
            InitializeComponent();
            _onFileSaved = onFileSaved;
            _token = Guid.NewGuid().ToString("N");
            _tokenCreatedUtc = DateTime.UtcNow;
            StartListener();
        }

        private bool IsRateLimited(string ip)
        {
            try
            {
                var list = _ipUploadTimestamps.GetOrAdd(ip ?? "unknown", _ => new List<DateTime>());
                lock (list)
                {
                    var cutoff = DateTime.UtcNow - _rateLimitWindow;
                    list.RemoveAll(d => d < cutoff);
                    if (list.Count >= _maxUploadsPerWindow) return true;
                    list.Add(DateTime.UtcNow);
                    return false;
                }
            }
            catch { return false; }
        }

        private void LogEvent(string message)
        {
            try
            {
                var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi", "Logs");
                Directory.CreateDirectory(logDir);
                var logFile = Path.Combine(logDir, "upload.log");
                var line = $"[{DateTime.UtcNow:O}] {message}" + Environment.NewLine;
                File.AppendAllText(logFile, line);
            }
            catch { }
        }

        private bool ScanFileWithWindowsDefender(string path)
        {
            try
            {
                // Common locations for MpCmdRun
                var candidates = new[] {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender", "MpCmdRun.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Defender", "MpCmdRun.exe")
                };
                string exe = candidates.FirstOrDefault(File.Exists);
                if (exe == null) return true; // no scanner available -> treat as pass

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"-Scan -ScanType 3 -File \"{path}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(30000); // wait up to 30s
                    var exit = p.ExitCode;
                    // exit code 0 typically means success/no threats; non-zero may indicate threat or error
                    return exit == 0;
                }
            }
            catch (Exception ex)
            {
                try { LogEvent("Antivirus scan error: " + ex.Message); } catch { }
                return false;
            }
        }

        private void StartListener()
        {
            // choose a random available port
            var rand = new Random();
            _port = rand.Next(30000, 40000);

            // Start a lightweight TCP listener instead of HttpListener to avoid URL ACL issues.
            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, _port);
                _tcpListener.Start();
            }
            catch (Exception ex)
            {
                QrStatus.Text = "QR dinleyici başlatılamadı: " + ex.Message;
                // We still continue to generate QR so user can see the target; listener may be null.
            }

            // generate a QR image pointing to http://<machine-ip>:port/upload/
            var localIp = GetLocalIPAddress() ?? "localhost";
            var target = $"http://{localIp}:{_port}/upload/";
            var qrUrl = $"https://chart.googleapis.com/chart?cht=qr&chs=240x240&chl={Uri.EscapeDataString(target)}";
            try { QrTargetTextBox.Text = target; } catch { }

            // Try to download QR image from public service first
            byte[] imageBytes = null;
            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    imageBytes = wc.DownloadData(qrUrl);
                }
            }
            catch { imageBytes = null; }

            if (imageBytes != null && imageBytes.Length > 0)
            {
                try
                {
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        var bmpImg = new BitmapImage();
                        bmpImg.BeginInit();
                        bmpImg.StreamSource = ms;
                        bmpImg.CacheOption = BitmapCacheOption.OnLoad;
                        bmpImg.EndInit();
                        bmpImg.Freeze();
                        QrImage.Source = bmpImg;
                    }
                }
                catch { }
            }
            else
            {
                // Fallback: generate QR locally using QRCoder
                try
                {
                    var generator = new QRCodeGenerator();
                    var dataQr = generator.CreateQrCode(target, QRCodeGenerator.ECCLevel.Q);
                    var qrCode = new QRCode(dataQr);
                    using (var bitmap = qrCode.GetGraphic(20))
                    using (var ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        ms.Position = 0;
                        var bmpImg = new BitmapImage();
                        bmpImg.BeginInit();
                        bmpImg.StreamSource = ms;
                        bmpImg.CacheOption = BitmapCacheOption.OnLoad;
                        bmpImg.EndInit();
                        bmpImg.Freeze();
                        QrImage.Source = bmpImg;
                    }
                }
                catch (Exception ex)
                {
                    QrStatus.Text = "QR oluşturulamadı: " + ex.Message;
                }
            }

            if (_tcpListener != null)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        while (true)
                        {
                            var client = await _tcpListener.AcceptTcpClientAsync();
                            Task.Run(() => HandleClient(client));
                        }
                    }
                    catch { }
                });
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    // Rate limit check per remote IP
                    string remoteIp = null;
                    try { remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(); } catch { remoteIp = "unknown"; }
                    if (IsRateLimited(remoteIp))
                    {
                        try { LogEvent($"Rate limit exceeded for {remoteIp}"); } catch { }
                        var tooMany = "HTTP/1.1 429 Too Many Requests\r\nContent-Length: 0\r\n\r\n";
                        var tm = System.Text.Encoding.UTF8.GetBytes(tooMany);
                        ns.Write(tm, 0, tm.Length);
                        return;
                    }

                    // Read headers until CRLF CRLF
                    var headerBuffer = new MemoryStream();
                    var buffer = new byte[8192];
                    int bytesRead = 0;
                    bool headersComplete = false;
                    while (!headersComplete && (bytesRead = ns.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        headerBuffer.Write(buffer, 0, bytesRead);
                        var data = headerBuffer.ToArray();
                        var hdrStr = System.Text.Encoding.ASCII.GetString(data);
                        var idx = hdrStr.IndexOf("\r\n\r\n");
                        if (idx >= 0)
                        {
                            headersComplete = true;
                            // separate header and initial body bytes
                            var headerPart = data.Take(idx + 4).ToArray();
                            var bodyStart = data.Skip(idx + 4).ToArray();

                            var headersText = System.Text.Encoding.ASCII.GetString(headerPart);
                            // check method
                            var firstLine = headersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                            if (firstLine.StartsWith("GET", StringComparison.OrdinalIgnoreCase))
                            {
                                // Serve a responsive, theme-matching upload form; include token in action
                                var html = $@"<html><head>
<meta name='viewport' content='width=device-width,initial-scale=1'>
<style>
  :root{{--bg:#0F172A;--panel:#1E293B;--panel2:#1F2937;--accent:#06B6D4;--accent-strong:#38BDF8;--muted:#94A3B8;--white:#FFFFFF}}
  html,body{{height:100%;margin:0;padding:0;background:var(--bg);font-family:Segoe UI,Helvetica,Arial,sans-serif;color:var(--white)}}
  .wrap{{min-height:100%;display:flex;align-items:center;justify-content:center;padding:20px}}
  .panel{{width:100%;max-width:520px;background:var(--panel);border-radius:14px;padding:20px;box-shadow:0 8px 30px rgba(2,6,23,0.6);border:1px solid rgba(255,255,255,0.02)}}
  h3{{margin:0 0 10px 0;font-size:20px;color:var(--white)}}
  .desc{{color:var(--muted);font-size:13px;margin-bottom:12px}}
  .form-row{{margin:14px 0;display:flex;flex-direction:column;gap:8px}}
  input[type=file]{{background:var(--panel2);border:1px solid rgba(255,255,255,0.03);color:var(--white);padding:12px;border-radius:8px}}
  .btn{{display:inline-block;background:linear-gradient(180deg,var(--accent),#0596b8);color:var(--white);border:none;padding:12px 18px;border-radius:10px;font-size:16px;cursor:pointer;box-shadow:0 6px 18px rgba(6,182,212,0.12)}}
  .help{{font-size:12px;color:var(--muted);margin-top:8px}}
  @media (max-width:420px){{.panel{{padding:14px;border-radius:10px}} input[type=file]{{padding:10px}} .btn{{width:100%}}}}
</style>
</head><body>
<div class='wrap'><div class='panel'>
  <h3>Dekont / Belge Yükle</h3>
  <div class='desc'>Telefonunuzdan QR okuyarak hızlıca belge yükleyin. Güvenlik: token ile korumalı, maksimum 10 MB.</div>
  <form method='post' enctype='multipart/form-data' action='/upload?t={_token}'>
    <div class='form-row'>
      <input type='file' name='file' accept='.pdf,image/*' />
    </div>
    <div class='form-row'>
      <button class='btn' type='submit'>Yükle</button>
    </div>
    <div class='help'>İzin verilen türler: PDF, JPG, PNG. Maksimum: 10 MB.</div>
  </form>
</div></div>
</body></html>";
                                var resp = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {System.Text.Encoding.UTF8.GetByteCount(html)}\r\n\r\n{html}";
                                var respBytes = System.Text.Encoding.UTF8.GetBytes(resp);
                                ns.Write(respBytes, 0, respBytes.Length);
                                return;
                            }

                            // POST handling: find Content-Length and validate token
                            var contentLength = 0;
                            var headersLines = headersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in headersLines)
                            {
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var headerParts = line.Split(':');
                                    if (headerParts.Length > 1 && int.TryParse(headerParts[1].Trim(), out var cl)) contentLength = cl;
                                }
                            }

                            // Validate token from request path (firstLine contains method and path)
                            var firstLineParts = firstLine.Split(' ');
                            var reqPath = firstLineParts.Length > 1 ? firstLineParts[1] : "/upload";
                            string reqToken = null;
                            var qidx = reqPath.IndexOf('?');
                            if (qidx >= 0)
                            {
                                var q = reqPath.Substring(qidx + 1);
                                var qparts = q.Split('&');
                                foreach (var qp in qparts)
                                {
                                    if (qp.StartsWith("t=", StringComparison.OrdinalIgnoreCase)) { reqToken = qp.Substring(2); break; }
                                }
                            }

                            if (string.IsNullOrEmpty(reqToken) || reqToken != _token || DateTime.UtcNow - _tokenCreatedUtc > _tokenLifetime)
                            {
                                var forbidden = "HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\n\r\n";
                                var fbytes = System.Text.Encoding.UTF8.GetBytes(forbidden);
                                ns.Write(fbytes, 0, fbytes.Length);
                                return;
                            }

                            // Enforce maximum upload size (10 MB)
                            const int MaxUploadBytes = 10 * 1024 * 1024;
                            if (contentLength > MaxUploadBytes)
                            {
                                var tooLarge = "HTTP/1.1 413 Payload Too Large\r\nContent-Length: 0\r\n\r\n";
                                var tbytes = System.Text.Encoding.UTF8.GetBytes(tooLarge);
                                ns.Write(tbytes, 0, tbytes.Length);
                                return;
                            }

                            var tmpdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi", "Uploads");
                            Directory.CreateDirectory(tmpdir);

                            // Read remaining body bytes
                            var remaining = contentLength - bodyStart.Length;
                            var bodyBytes = new byte[bodyStart.Length + Math.Max(0, remaining)];
                            Array.Copy(bodyStart, 0, bodyBytes, 0, bodyStart.Length);
                            var offset = bodyStart.Length;
                            while (remaining > 0)
                            {
                                var toRead = Math.Min(buffer.Length, remaining);
                                var r = ns.Read(buffer, 0, toRead);
                                if (r <= 0) break;
                                Array.Copy(buffer, 0, bodyBytes, offset, r);
                                offset += r;
                                remaining -= r;
                            }

                            string savedPath = null;
                            string boundaryBefore = null;

                            try
                            {
                                // Try to parse multipart/form-data and extract filename
                                var latin = System.Text.Encoding.Latin1;
                                var bodyStr = latin.GetString(bodyBytes, 0, offset);
                                var boundary = "";
                                var ctLine = headersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .FirstOrDefault(l => l.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase));
                                if (ctLine != null)
                                {
                                    var bidx = ctLine.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
                                    if (bidx >= 0)
                                    {
                                        boundary = ctLine.Substring(bidx + "boundary=".Length).Trim().Trim('"');
                                    }
                                }

                                if (!string.IsNullOrEmpty(boundary))
                                {
                                    boundaryBefore = boundary;
                                    var bodyParts = bodyStr.Split(new string[] { "--" + boundary }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var part in bodyParts)
                                    {
                                        var p = part.TrimEnd('\r','\n');
                                        if (string.IsNullOrWhiteSpace(p) || p == "--") continue;
                                        var idx2 = p.IndexOf("\r\n\r\n");
                                        if (idx2 < 0) continue;
                                        var partHeaders = p.Substring(0, idx2);
                                        var partBodyStr = p.Substring(idx2 + 4);

                                        // find filename
                                        var cdLine = partHeaders.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                            .FirstOrDefault(l => l.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase));
                                        string filename = null;
                                        if (cdLine != null)
                                        {
                                            var fnIdx = cdLine.IndexOf("filename=", StringComparison.OrdinalIgnoreCase);
                                            if (fnIdx >= 0)
                                            {
                                                filename = cdLine.Substring(fnIdx + "filename=".Length).Trim();
                                                filename = filename.Trim('"');
                                                // sanitize
                                                foreach (var c in Path.GetInvalidFileNameChars()) filename = filename.Replace(c, '_');
                                            }
                                        }

                                        if (string.IsNullOrEmpty(filename)) filename = $"upload_{DateTime.Now:yyyyMMddHHmmss}.bin";

                                        var contentBytes = latin.GetBytes(partBodyStr);
                                        // trim possible trailing CRLF
                                        if (contentBytes.Length >= 2 && contentBytes[contentBytes.Length - 2] == 13 && contentBytes[contentBytes.Length - 1] == 10)
                                        {
                                            Array.Resize(ref contentBytes, contentBytes.Length - 2);
                                        }

                                        // Validate extension whitelist and magic bytes
                                        var ext = Path.GetExtension(filename).ToLowerInvariant();
                                        var allowed = new[] { ".pdf", ".jpg", ".jpeg", ".png" };
                                        bool okExt = allowed.Contains(ext);
                                        if (okExt)
                                        {
                                            // Check magic bytes for known types
                                            if (ext == ".jpg" || ext == ".jpeg") okExt = contentBytes.Length >= 2 && contentBytes[0] == 0xFF && contentBytes[1] == 0xD8;
                                            else if (ext == ".png") okExt = contentBytes.Length >= 4 && contentBytes[0] == 0x89 && contentBytes[1] == 0x50 && contentBytes[2] == 0x4E && contentBytes[3] == 0x47;
                                            else if (ext == ".pdf") okExt = contentBytes.Length >= 4 && contentBytes[0] == (byte)'%' && contentBytes[1] == (byte)'P' && contentBytes[2] == (byte)'D' && contentBytes[3] == (byte)'F';
                                        }

                                        if (!okExt)
                                        {
                                            // reject
                                            var bad = "HTTP/1.1 415 Unsupported Media Type\r\nContent-Length: 0\r\n\r\n";
                                            var bbytes = System.Text.Encoding.UTF8.GetBytes(bad);
                                            ns.Write(bbytes, 0, bbytes.Length);
                                            return;
                                        }

                                        var path = Path.Combine(tmpdir, filename);
                                        File.WriteAllBytes(path, contentBytes);
                                        savedPath = path;
                                        var originalName = filename;
                                        break;
                                    }
                                }
                            }
                            catch { savedPath = null; }

                            // If the request was multipart/form-data (had a boundary) but we didn't extract a file,
                            // treat as bad request (user submitted without selecting a file) instead of saving raw bytes.
                            if (!string.IsNullOrEmpty(boundaryBefore) && savedPath == null)
                            {
                                var badReq = "HTTP/1.1 400 Bad Request\r\nContent-Length: 0\r\n\r\n";
                                var br = System.Text.Encoding.UTF8.GetBytes(badReq);
                                ns.Write(br, 0, br.Length);
                                Dispatcher.Invoke(() => { QrStatus.Text = "Yükleme başarısız: dosya seçilmedi."; });
                                return;
                            }

                            // Fallback: if we didn't save via multipart parsing, save raw body
                            string originalFileName = null;
                            if (savedPath == null)
                            {
                                var fileName = $"upload_{DateTime.Now:yyyyMMddHHmmss}.bin";
                                var path = Path.Combine(tmpdir, fileName);
                                using (var fs = File.Create(path))
                                {
                                    fs.Write(bodyBytes, 0, offset);
                                }
                                savedPath = path;
                                originalFileName = Path.GetFileName(path);
                            }
                            else
                            {
                                // if we saved via multipart parsing and set originalName above
                                try { originalFileName = Path.GetFileName(savedPath); } catch { originalFileName = savedPath; }
                            }

                            // Run antivirus scan (Windows Defender) synchronously; if scan fails or reports problem, delete and reject
                            try
                            {
                                var scanOk = ScanFileWithWindowsDefender(savedPath);
                                if (!scanOk)
                                {
                                    try { File.Delete(savedPath); } catch { }
                                    var infected = "HTTP/1.1 415 Unsupported Media Type\r\nContent-Length: 0\r\n\r\n";
                                    var ib = System.Text.Encoding.UTF8.GetBytes(infected);
                                    ns.Write(ib, 0, ib.Length);
                                    LogEvent($"File rejected by antivirus: {savedPath}");
                                    return;
                                }
                            }
                            catch (Exception ex)
                            {
                                // If antivirus check fails unexpectedly, log and continue (do not block all uploads)
                                try { LogEvent($"Antivirus check failed for {savedPath}: {ex.Message}"); } catch { }
                            }

                            var okResp = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: 7\r\n\r\nYüklendi";
                            var okBytes = System.Text.Encoding.UTF8.GetBytes(okResp);
                            ns.Write(okBytes, 0, okBytes.Length);

                            Dispatcher.Invoke(() =>
                            {
                                QrStatus.Text = "Dosya alındı: " + Path.GetFileName(savedPath);
                                _onFileSaved?.Invoke(savedPath, originalFileName);
                                this.DialogResult = true;
                                this.Close();
                            });

                            return;
                        }
                    }
                }
            }
            catch { }
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return ip.ToString();
                }
            }
            catch { }
            return null;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            try { _tcpListener?.Stop(); } catch { }
            this.DialogResult = false;
            this.Close();
        }
    }
}

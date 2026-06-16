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
using System.Windows.Media.Imaging;

namespace TurizmOperasyonSistemi.Views
{
    public partial class QRUploadWindow : Window
    {
        private TcpListener _tcpListener;
        private int _port;
        private readonly Action<string> _onFileSaved;

        public QRUploadWindow(Action<string> onFileSaved)
        {
            InitializeComponent();
            _onFileSaved = onFileSaved;
            StartListener();
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
                            _ = Task.Run(() => HandleClient(client));
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
                                var html = "<html><body>\n<h3>Dosya yükle</h3>\n<form method='post' enctype='multipart/form-data'>\n<input type='file' name='file' />\n<input type='submit' value='Yükle' />\n</form>\n</body></html>";
                                var resp = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {System.Text.Encoding.UTF8.GetByteCount(html)}\r\n\r\n{html}";
                                var respBytes = System.Text.Encoding.UTF8.GetBytes(resp);
                                ns.Write(respBytes, 0, respBytes.Length);
                                return;
                            }

                            // POST handling: find Content-Length
                            var contentLength = 0;
                            var headersLines = headersText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var line in headersLines)
                            {
                                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                                {
                                    var parts = line.Split(':');
                                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var cl)) contentLength = cl;
                                }
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
                                    var parts = bodyStr.Split(new string[] { "--" + boundary }, StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var part in parts)
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

                                        var path = Path.Combine(tmpdir, filename);
                                        File.WriteAllBytes(path, contentBytes);
                                        savedPath = path;
                                        break;
                                    }
                                }
                            }
                            catch { savedPath = null; }

                            // Fallback: if we didn't save via multipart parsing, save raw body
                            if (savedPath == null)
                            {
                                var fileName = $"upload_{DateTime.Now:yyyyMMddHHmmss}.bin";
                                var path = Path.Combine(tmpdir, fileName);
                                using (var fs = File.Create(path))
                                {
                                    fs.Write(bodyBytes, 0, offset);
                                }
                                savedPath = path;
                            }

                            var okResp = "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: 7\r\n\r\nYüklendi";
                            var okBytes = System.Text.Encoding.UTF8.GetBytes(okResp);
                            ns.Write(okBytes, 0, okBytes.Length);

                            Dispatcher.Invoke(() =>
                            {
                                QrStatus.Text = "Dosya alındı: " + Path.GetFileName(savedPath);
                                _onFileSaved?.Invoke(savedPath);
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

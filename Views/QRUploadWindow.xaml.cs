using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TurizmOperasyonSistemi.Views
{
    public partial class QRUploadWindow : Window
    {
        private HttpListener _listener;
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
            var url = $"http://+:{_port}/upload/";
            _listener = new HttpListener();
            _listener.Prefixes.Add(url);
            try
            {
                _listener.Start();
            }
            catch (Exception ex)
            {
                QrStatus.Text = "QR dinleyici başlatılamadı: " + ex.Message;
                return;
            }

            // generate a QR image pointing to http://<machine-ip>:port/upload/
            var localIp = GetLocalIPAddress() ?? "localhost";
            var target = $"http://{localIp}:{_port}/upload/";
            var qrUrl = $"https://chart.googleapis.com/chart?cht=qr&chs=240x240&chl={Uri.EscapeDataString(target)}";
            var bmp = new BitmapImage(new Uri(qrUrl));
            QrImage.Source = bmp;

            Task.Run(async () =>
            {
                try
                {
                    while (_listener.IsListening)
                    {
                        var ctx = await _listener.GetContextAsync();
                        _ = Task.Run(() => HandleContext(ctx));
                    }
                }
                catch { }
            });
        }

        private void HandleContext(HttpListenerContext ctx)
        {
            try
            {
                if (ctx.Request.HttpMethod == "GET")
                {
                    // return a simple HTML upload form
                    var html = @"<html><body>
<h3>Dosya yükle</h3>
<form method='post' enctype='multipart/form-data'>
<input type='file' name='file' />
<input type='submit' value='Yükle' />
</form>
</body></html>";
                    var buf = System.Text.Encoding.UTF8.GetBytes(html);
                    ctx.Response.ContentType = "text/html";
                    ctx.Response.OutputStream.Write(buf, 0, buf.Length);
                    ctx.Response.OutputStream.Close();
                }
                else if (ctx.Request.HttpMethod == "POST")
                {
                    // read raw body and save to temp file - naive approach
                    var tmpdir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TurizmOperasyonSistemi", "Uploads");
                    Directory.CreateDirectory(tmpdir);

                    var fileName = $"upload_{DateTime.Now:yyyyMMddHHmmss}.bin";
                    var path = Path.Combine(tmpdir, fileName);
                    using (var fs = File.Create(path))
                    {
                        ctx.Request.InputStream.CopyTo(fs);
                    }

                    var resp = System.Text.Encoding.UTF8.GetBytes("Yüklendi");
                    ctx.Response.OutputStream.Write(resp, 0, resp.Length);
                    ctx.Response.OutputStream.Close();

                    // invoke callback on UI thread
                    Dispatcher.Invoke(() =>
                    {
                        QrStatus.Text = "Dosya alındı: " + Path.GetFileName(path);
                        _onFileSaved?.Invoke(path);
                        this.DialogResult = true;
                        this.Close();
                    });
                }
                else
                {
                    ctx.Response.StatusCode = 405;
                    ctx.Response.Close();
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
            try { _listener?.Stop(); } catch { }
            this.DialogResult = false;
            this.Close();
        }
    }
}

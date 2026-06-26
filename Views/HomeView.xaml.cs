using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Globalization;
using System.Net.Http;
using System.Xml.Linq;
using TurizmOperasyonSistemi.Data;
using TurizmOperasyonSistemi.Models;
using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;

namespace TurizmOperasyonSistemi.Views
{
    /// <summary>
    /// HomeView.xaml etkileşim mantığı
    /// </summary>
    public partial class HomeView : Page
    {
        private int _notificationCount = 0;
        private Storyboard _notificationsShakeStoryboard;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _ratesTimer;
        private static readonly HttpClient _httpClient = new HttpClient();
        // Döviz kurları: TRY per unit foreign currency (ForexBuying)
        private decimal _usdRate = 0m;
        private decimal _eurRate = 0m;
        private decimal _gbpRate = 0m;
        private decimal _sarRate = 0m;
        private Dictionary<string, (string Tour, string Period, string GroupType, string Days)> _tourKeyMap = new Dictionary<string, (string, string, string, string)>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (int Active, int Inactive, int Total)> _tourCountsMap = new Dictionary<string, (int, int, int)>(StringComparer.OrdinalIgnoreCase);

        public HomeView()
        {
            InitializeComponent();

            try
            {
                // Try to get storyboard from button resources first (namescope)
                var btn = this.FindName("NotificationsButton") as FrameworkElement;
                if (btn != null)
                {
                    try { _notificationsShakeStoryboard = btn.Resources["NotificationsShakeStoryboard"] as Storyboard; } catch { _notificationsShakeStoryboard = null; }
                }
                if (_notificationsShakeStoryboard == null)
                {
                    try { _notificationsShakeStoryboard = (Storyboard)FindResource("NotificationsShakeStoryboard"); } catch { _notificationsShakeStoryboard = null; }
                }
            }
            catch { _notificationsShakeStoryboard = null; }

            // Start clock timer to update digital time
            _clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1000)
            };
            _clockTimer.Tick += (s, e) => UpdateClock();
            _clockTimer.Start();
            UpdateClock();
            // Başlangıçta ve sonra düzenli aralıklarla TCMB kurlarını çek
            _ratesTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _ratesTimer.Tick += async (s, e) => await FetchExchangeRatesAsync();
            _ratesTimer.Start();

            // İlk yüklemede asenkron olarak kurları al
            _ = FetchExchangeRatesAsync();
            // Aktif turlar combobox'unu doldur
            LoadActiveTours();

            // Update visa summary for initial selection
            try { UpdateVisaSummaryForSelectedTour(ActiveToursCombo.SelectedItem as string); } catch { }
            // Update ticket summary for initial selection
            try { UpdateTicketSummaryForSelectedTour(ActiveToursCombo.SelectedItem as string); } catch { }
            // Update cargo summary for initial selection
            try { UpdateCargoSummaryForSelectedTour(ActiveToursCombo.SelectedItem as string); } catch { }

            // Abone ol: ödeme güncellemeleri geldiğinde toplamları yeniden hesapla
            TurizmOperasyonSistemi.Services.NotificationCenter.Subscribe(OnNotification);

            this.Unloaded += (s, e) => TurizmOperasyonSistemi.Services.NotificationCenter.Unsubscribe(OnNotification);
        }

        private void UpdateTicketSummaryForSelectedTour(string selected)
        {
            try
            {
                using var db = new AppDbContext();
                var regs = db.Registrations.AsNoTracking().Where(r => r.IsActive).ToList();

                if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                {
                    var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                    string tour = null, period = null, groupType = null, days = null;
                    if (parts.Length >= 4)
                    {
                        tour = parts[0]?.Trim();
                        period = parts[1]?.Trim();
                        groupType = parts[2]?.Trim();
                        days = parts[3]?.Replace(" gün", "").Trim();
                    }
                    else if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                    {
                        tour = key.Tour; period = key.Period; groupType = key.GroupType; days = key.Days;
                    }

                    var ntour = NormalizeForComparison(tour ?? string.Empty);
                    var nperiod = NormalizeForComparison(period ?? string.Empty);
                    var ngroup = NormalizeForComparison(groupType ?? string.Empty);
                    var ndays = NormalizeForComparison(days ?? string.Empty);

                    regs = regs.Where(r =>
                    {
                        try
                        {
                            bool ok = true;
                            if (!string.IsNullOrWhiteSpace(ntour))
                            {
                                if (ntour == "(belirtilmemiş)" || ntour == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.TourType);
                                else ok &= NormalizeForComparison(r.TourType ?? string.Empty) == ntour;
                            }
                            if (!string.IsNullOrWhiteSpace(nperiod))
                            {
                                if (nperiod == "(belirtilmemiş)" || nperiod == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.Period);
                                else ok &= NormalizeForComparison(r.Period ?? string.Empty) == nperiod;
                            }
                            if (!string.IsNullOrWhiteSpace(ngroup))
                            {
                                if (ngroup == "(belirtilmemiş)" || ngroup == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.GroupType);
                                else ok &= NormalizeForComparison(r.GroupType ?? string.Empty) == ngroup;
                            }
                            if (!string.IsNullOrWhiteSpace(ndays))
                            {
                                if (ndays == "-" || ndays == "(belirtilmemiş)" || ndays == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.DaysCount);
                                else ok &= NormalizeForComparison(r.DaysCount ?? string.Empty) == ndays;
                            }
                            return ok;
                        }
                        catch { return false; }
                    }).ToList();
                }

                var list = regs.Select(r => new { r.TicketRequest, r.TicketStatusChoice }).ToList();

                int willGet = 0, selfGet = 0, arrived = 0, notArrived = 0, inProgress = 0;
                int transferCount = 0;

                foreach (var item in list)
                {
                    var tr = NormalizeForComparison(item.TicketRequest ?? string.Empty);
                    var ts = NormalizeForComparison(item.TicketStatusChoice ?? string.Empty);

                    if (!string.IsNullOrWhiteSpace(tr))
                    {
                        if (tr.Contains("kendisi") || tr.Contains("kendi")) selfGet++;
                        else if (tr.Contains("alac") && tr.Contains("bilet")) willGet++;
                    }

                    if (!string.IsNullOrWhiteSpace(ts))
                    {
                        if (ts.Contains("gelmedi")) notArrived++;
                        else if (ts.Contains("geldi") || ts.Contains("bileti geldi")) arrived++;
                        else if (ts.Contains("alin") || ts.Contains("aliniyor")) inProgress++;
                    }
                }

                try
                {
                    transferCount = regs.Count(r =>
                    {
                        try
                        {
                            var ht = NormalizeForComparison(r.HasTransfer ?? string.Empty);
                            if (string.IsNullOrWhiteSpace(ht)) return false;
                            return ht.Contains("var") || ht.Contains("evet") || ht.Contains("yes") || ht == "1";
                        }
                        catch { return false; }
                    });
                }
                catch { }

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var a = this.FindName("TicketWillGetText") as TextBlock;
                        var b = this.FindName("TicketSelfGetText") as TextBlock;
                        var c = this.FindName("TicketArrivedText") as TextBlock;
                        var d = this.FindName("TicketNotArrivedText") as TextBlock;
                        var e = this.FindName("TicketInProgressText") as TextBlock;
                        if (a != null) a.Text = willGet.ToString();
                        if (b != null) b.Text = selfGet.ToString();
                        if (c != null) c.Text = arrived.ToString();
                        if (d != null) d.Text = notArrived.ToString();
                        if (e != null) e.Text = inProgress.ToString();
                        try { var ttc = this.FindName("TicketTransferCountText") as TextBlock; if (ttc != null) ttc.Text = transferCount.ToString(); } catch { }
                        try { var hint = this.FindName("TicketHintText") as TextBlock; if (hint != null) hint.Text = "Son güncelleme: " + DateTime.Now.ToString("g"); } catch { }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void NotificationsItem_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!(sender is Button btn)) return;
                var tag = btn.Tag;
                int id = 0;
                if (tag is int ti) id = ti;
                else if (tag is string ts && int.TryParse(ts, out var parsed)) id = parsed;

                if (id > 0)
                {
                    try
                    {
                        TurizmOperasyonSistemi.Services.NotificationCenter.MarkAsRead(id);
                    }
                    catch { }
                }

                // remove UI element
                var stack = this.FindName("NotificationsPopupStack") as StackPanel;
                if (stack != null)
                {
                    try { stack.Children.Remove(btn); } catch { }
                }

                // decrement badge
                try
                {
                    if (_notificationCount > 0) _notificationCount = Math.Max(0, _notificationCount - 1);
                    var badge = this.FindName("NotificationBadge") as Border;
                    var badgeText = this.FindName("NotificationBadgeText") as TextBlock;
                    if (_notificationCount <= 0)
                    {
                        if (badge != null) badge.Visibility = Visibility.Collapsed;
                        if (badgeText != null) badgeText.Text = "0";
                    }
                    else
                    {
                        if (badge != null) badge.Visibility = Visibility.Visible;
                        if (badgeText != null) badgeText.Text = _notificationCount.ToString();
                    }
                }
                catch { }
            }
            catch { }
        }

        private void UpdateCargoSummaryForSelectedTour(string selected)
        {
            try
            {
                using var db = new AppDbContext();
                var regs = db.Registrations.AsNoTracking().Where(r => r.IsActive).ToList();

                if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                {
                    var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                    string tour = null, period = null, groupType = null, days = null;
                    if (parts.Length >= 4)
                    {
                        tour = parts[0]?.Trim();
                        period = parts[1]?.Trim();
                        groupType = parts[2]?.Trim();
                        days = parts[3]?.Replace(" gün", "").Trim();
                    }
                    else if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                    {
                        tour = key.Tour; period = key.Period; groupType = key.GroupType; days = key.Days;
                    }

                    var ntour = NormalizeForComparison(tour ?? string.Empty);
                    var nperiod = NormalizeForComparison(period ?? string.Empty);
                    var ngroup = NormalizeForComparison(groupType ?? string.Empty);
                    var ndays = NormalizeForComparison(days ?? string.Empty);

                    regs = regs.Where(r =>
                    {
                        try
                        {
                            bool ok = true;
                            if (!string.IsNullOrWhiteSpace(ntour))
                            {
                                if (ntour == "(belirtilmemiş)" || ntour == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.TourType);
                                else ok &= NormalizeForComparison(r.TourType ?? string.Empty) == ntour;
                            }
                            if (!string.IsNullOrWhiteSpace(nperiod))
                            {
                                if (nperiod == "(belirtilmemiş)" || nperiod == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.Period);
                                else ok &= NormalizeForComparison(r.Period ?? string.Empty) == nperiod;
                            }
                            if (!string.IsNullOrWhiteSpace(ngroup))
                            {
                                if (ngroup == "(belirtilmemiş)" || ngroup == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.GroupType);
                                else ok &= NormalizeForComparison(r.GroupType ?? string.Empty) == ngroup;
                            }
                            if (!string.IsNullOrWhiteSpace(ndays))
                            {
                                if (ndays == "-" || ndays == "(belirtilmemiş)" || ndays == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.DaysCount);
                                else ok &= NormalizeForComparison(r.DaysCount ?? string.Empty) == ndays;
                            }
                            return ok;
                        }
                        catch { return false; }
                    }).ToList();
                }

                var list = regs.Select(r => new { r.CargoRequest }).ToList();

                int willSend = 0, sent = 0, noSend = 0, pickupWill = 0, picked = 0;

                foreach (var item in list)
                {
                    var cr = NormalizeForComparison(item.CargoRequest ?? string.Empty);
                    if (string.IsNullOrWhiteSpace(cr)) continue;

                    // prioritize negative/specific checks
                    if (cr.Contains("almayacak") || cr.Contains("almayacak"))
                    {
                        noSend++;
                        continue;
                    }

                    if (cr.Contains("gonderildi") || cr.Contains("gönderildi") || cr.Contains("gonderildi"))
                    {
                        sent++;
                        continue;
                    }

                    if (cr.Contains("gonder") || cr.Contains("gonderilecek") || cr.Contains("gönderilecek") )
                    {
                        willSend++;
                        continue;
                    }

                    if (cr.Contains("elden teslim aldi") || cr.Contains("elden teslim aldi") || cr.Contains("elden teslim aldi") || cr.Contains("elden teslim aldi") || cr.Contains("elden teslimaldi") || cr.Contains("elden teslim aldi"))
                    {
                        picked++;
                        continue;
                    }

                    if (cr.Contains("elden teslim") || cr.Contains("elden teslim alacak") || cr.Contains("elden teslim alac"))
                    {
                        pickupWill++;
                        continue;
                    }
                }

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var a = this.FindName("CargoWillSendText") as TextBlock;
                        var b = this.FindName("CargoSentText") as TextBlock;
                        var c = this.FindName("CargoNoSendText") as TextBlock;
                        var d = this.FindName("CargoPickupWillText") as TextBlock;
                        var e = this.FindName("CargoPickedText") as TextBlock;
                        if (a != null) a.Text = willSend.ToString();
                        if (b != null) b.Text = sent.ToString();
                        if (c != null) c.Text = noSend.ToString();
                        if (d != null) d.Text = pickupWill.ToString();
                        if (e != null) e.Text = picked.ToString();
                        try { var hint = this.FindName("CargoHintText") as TextBlock; if (hint != null) hint.Text = "Son güncelleme: " + DateTime.Now.ToString("g"); } catch { }
                    }
                    catch { }
                });
            }
            catch { }
        }

                // (removed stray duplicate UI update)

        private void OnNotification(string key, object payload)
        {
            try
            {
                // If any notification arrives, increase badge and start shake until user inspects
                _notificationCount++;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var badge = this.FindName("NotificationBadge") as Border;
                        var badgeText = this.FindName("NotificationBadgeText") as TextBlock;
                        if (badge != null && badgeText != null)
                        {
                            badgeText.Text = _notificationCount.ToString();
                            badge.Visibility = Visibility.Visible;
                        }
                        // Başka güncellemeler tetiklenebilir
                        var sel = ActiveToursCombo.SelectedItem as string;
                        try { UpdateTotalsForSelectedTour(sel); } catch { }
                        try { UpdateVisaSummaryForSelectedTour(sel); } catch { }
                        try { UpdateTicketSummaryForSelectedTour(sel); } catch { }
                        try { UpdateCargoSummaryForSelectedTour(sel); } catch { }
                        StartNotificationsShake();
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void StartNotificationsShake()
        {
            try
            {
                if (_notificationsShakeStoryboard != null)
                {
                    var btn = this.FindName("NotificationsButton") as FrameworkElement;
                    if (btn != null)
                    {
                        _notificationsShakeStoryboard.Begin(btn, true);
                    }
                    else
                    {
                        _notificationsShakeStoryboard.Begin(this, true);
                    }
                }
            }
            catch { }
        }

        private void StopNotificationsShake()
        {
            try
            {
                if (_notificationsShakeStoryboard != null)
                {
                    var btn = this.FindName("NotificationsButton") as FrameworkElement;
                    if (btn != null)
                    {
                        _notificationsShakeStoryboard.Stop(btn);
                    }
                    else
                    {
                        _notificationsShakeStoryboard.Stop(this);
                    }
                }
            }
            catch { }
        }

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Aç: popup'ı doldur ve göster
                var popup = this.FindName("NotificationsPopup") as System.Windows.Controls.Primitives.Popup;
                var stack = this.FindName("NotificationsPopupStack") as StackPanel;
                if (popup != null && stack != null)
                {
                    stack.Children.Clear();
                    try
                    {
                        var recs = TurizmOperasyonSistemi.Services.NotificationCenter.GetRecentNotifications();
                        if (recs != null && recs.Count > 0)
                        {
                            foreach (var r in recs.OrderByDescending(x => x.Timestamp))
                            {
                                int notifId = 0;
                                string messageText = r.Key ?? string.Empty; // primary message shown as title
                                string poolDescFromPayloadLocal = string.Empty; // readable pool description
                                string timestampText = string.Empty;
                                try
                                {
                                    var payload = r.Payload;
                                    if (payload == null)
                                    {
                                        messageText = r.Key ?? string.Empty;
                                    }
                                    else if (payload is TurizmOperasyonSistemi.Models.Notification dbn)
                                    {
                                        notifId = dbn.Id;
                                        // prefer Message field from DB row
                                        if (!string.IsNullOrWhiteSpace(dbn.Message)) messageText = dbn.Message;

                                        // try to parse PayloadJson for richer fields
                                        if (!string.IsNullOrWhiteSpace(dbn.PayloadJson))
                                        {
                                            try
                                            {
                                                using var doc = System.Text.Json.JsonDocument.Parse(dbn.PayloadJson);
                                                if (string.IsNullOrWhiteSpace(messageText) && doc.RootElement.TryGetProperty("Message", out var msgEl) && msgEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    messageText = msgEl.GetString() ?? messageText;
                                                }

                                                // build pool description if possible
                                                string hotel = null, tour = null, period = null, group = null, days = null, pool = null;
                                                if (doc.RootElement.TryGetProperty("Pool", out var poolEl) && poolEl.ValueKind == System.Text.Json.JsonValueKind.String) pool = poolEl.GetString();
                                                if (doc.RootElement.TryGetProperty("Hotel", out var hotelEl) && hotelEl.ValueKind == System.Text.Json.JsonValueKind.String) hotel = hotelEl.GetString();
                                                if (doc.RootElement.TryGetProperty("Tour", out var tourEl) && tourEl.ValueKind == System.Text.Json.JsonValueKind.String) tour = tourEl.GetString();
                                                if (doc.RootElement.TryGetProperty("Period", out var periodEl) && periodEl.ValueKind == System.Text.Json.JsonValueKind.String) period = periodEl.GetString();
                                                if (doc.RootElement.TryGetProperty("Group", out var groupEl) && groupEl.ValueKind == System.Text.Json.JsonValueKind.String) group = groupEl.GetString();
                                                if (doc.RootElement.TryGetProperty("Days", out var daysEl) && daysEl.ValueKind == System.Text.Json.JsonValueKind.String) days = daysEl.GetString();

                                                if (!string.IsNullOrWhiteSpace(pool)) poolDescFromPayloadLocal = pool;
                                                else
                                                {
                                                    var parts = new List<string>();
                                                    if (!string.IsNullOrWhiteSpace(tour)) parts.Add(tour);
                                                    if (!string.IsNullOrWhiteSpace(period)) parts.Add(period);
                                                    if (!string.IsNullOrWhiteSpace(group)) parts.Add(group);
                                                    if (!string.IsNullOrWhiteSpace(days)) parts.Add(days.EndsWith(" gün", StringComparison.OrdinalIgnoreCase) ? days : days + " gün");
                                                    if (!string.IsNullOrWhiteSpace(hotel)) parts.Add("Otel: " + hotel);
                                                    if (parts.Count > 0) poolDescFromPayloadLocal = string.Join(" + ", parts);
                                                }

                                                if (doc.RootElement.TryGetProperty("Timestamp", out var tsEl))
                                                {
                                                    try
                                                    {
                                                        var tss = tsEl.GetString();
                                                        if (!string.IsNullOrWhiteSpace(tss) && DateTime.TryParse(tss, out var dt)) timestampText = dt.ToLocalTime().ToString("g");
                                                    }
                                                    catch { }
                                                }
                                            }
                                            catch { }
                                        }
                                        // fallback timestamp from CreatedAt
                                        if (string.IsNullOrWhiteSpace(timestampText) && dbn.CreatedAt != default) timestampText = dbn.CreatedAt.ToLocalTime().ToString("g");
                                    }
                                    else if (payload is string s)
                                    {
                                        messageText = s;
                                    }
                                    else
                                    {
                                        var pt = payload.GetType();
                                        var msgProp = pt.GetProperty("Message");
                                        if (msgProp != null)
                                        {
                                            var v = msgProp.GetValue(payload);
                                            if (v != null) messageText = v.ToString();
                                        }
                                        else
                                        {
                                            var nameProp = pt.GetProperty("Name");
                                            if (nameProp != null)
                                            {
                                                var v = nameProp.GetValue(payload);
                                                if (v != null) messageText = v.ToString();
                                            }
                                            else
                                            {
                                                messageText = payload.ToString();
                                            }
                                        }
                                    }
                                }
                                catch { }

                                // determine read state (default: unread when unknown)
                                bool isRead = false;
                                try
                                {
                                    if (r.Payload is TurizmOperasyonSistemi.Models.Notification dbnForRead)
                                    {
                                        isRead = dbnForRead.IsRead;
                                    }
                                }
                                catch { }

                                // create clickable item with clearer layout: icon, title (bold for unread), subtitle (timestamp)
                                var outerBorder = new Border
                                {
                                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isRead ? "#07101A" : "#0F1B2A")),
                                    CornerRadius = new CornerRadius(6),
                                    Padding = new Thickness(6),
                                    Margin = new Thickness(0, 4, 0, 4),
                                    BorderThickness = new Thickness(isRead ? 0 : 1),
                                    BorderBrush = isRead ? null : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#172433"))
                                };

                                var grid = new Grid();
                                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                                // icon circle
                                var icon = new System.Windows.Shapes.Ellipse { Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                                var iconFill = (SolidColorBrush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B5CF6"));
                                try
                                {
                                    if (r.Key != null && r.Key.Equals("person.deleted", StringComparison.OrdinalIgnoreCase))
                                        iconFill = (SolidColorBrush)new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                                    // if read, make icon muted
                                    if (isRead)
                                    {
                                        iconFill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                                    }
                                }
                                catch { }
                                icon.Fill = iconFill;
                                Grid.SetColumn(icon, 0);
                                grid.Children.Add(icon);

                                // text stack
                                var textStack = new StackPanel { Orientation = Orientation.Vertical };
                                // Prepare vars for pool description and titles
                                string poolDescFromPayload = string.Empty;
                                // messageText and timestampText will be filled when parsing payload above
                                // Use messageText (parsed above) as the primary title
                                var titleTb = new TextBlock { Text = messageText, FontWeight = isRead ? FontWeights.Normal : FontWeights.SemiBold, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isRead ? "#94A3B8" : "#E6EEF8")), TextWrapping = TextWrapping.Wrap };
                                titleTb.Margin = new Thickness(2, 0, 0, 2);
                                textStack.Children.Add(titleTb);

                                // subtitle: try to show person name (if available), pool details and timestamp
                                string subtitle = string.Empty;
                                try
                                {
                                    if (r.Payload is TurizmOperasyonSistemi.Models.Notification dbn2)
                                    {
                                        // try parse PayloadJson for Name or Timestamp
                                        if (!string.IsNullOrWhiteSpace(dbn2.PayloadJson))
                                        {
                                            try
                                            {
                                                using var doc = System.Text.Json.JsonDocument.Parse(dbn2.PayloadJson);
                                                if (doc.RootElement.TryGetProperty("Name", out var nameEl) && nameEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    var nm = nameEl.GetString();
                                                    if (!string.IsNullOrWhiteSpace(nm)) subtitle = nm;
                                                }
                                                if (string.IsNullOrWhiteSpace(subtitle) && doc.RootElement.TryGetProperty("Message", out var msgEl) && msgEl.ValueKind == System.Text.Json.JsonValueKind.String)
                                                {
                                                    subtitle = msgEl.GetString();
                                                }
                                                // try to build pool description from payload fields
                                                try
                                                {
                                                    string hotel = null, tour = null, period = null, group = null, days = null, pool = null;
                                                    if (doc.RootElement.TryGetProperty("Pool", out var poolEl) && poolEl.ValueKind == System.Text.Json.JsonValueKind.String) pool = poolEl.GetString();
                                                    if (doc.RootElement.TryGetProperty("Hotel", out var hotelEl) && hotelEl.ValueKind == System.Text.Json.JsonValueKind.String) hotel = hotelEl.GetString();
                                                    if (doc.RootElement.TryGetProperty("Tour", out var tourEl) && tourEl.ValueKind == System.Text.Json.JsonValueKind.String) tour = tourEl.GetString();
                                                    if (doc.RootElement.TryGetProperty("Period", out var periodEl) && periodEl.ValueKind == System.Text.Json.JsonValueKind.String) period = periodEl.GetString();
                                                    if (doc.RootElement.TryGetProperty("Group", out var groupEl) && groupEl.ValueKind == System.Text.Json.JsonValueKind.String) group = groupEl.GetString();
                                                    if (doc.RootElement.TryGetProperty("Days", out var daysEl) && daysEl.ValueKind == System.Text.Json.JsonValueKind.String) days = daysEl.GetString();

                                                    if (!string.IsNullOrWhiteSpace(pool)) poolDescFromPayload = pool;
                                                    else
                                                    {
                                                        var parts = new List<string>();
                                                        if (!string.IsNullOrWhiteSpace(tour)) parts.Add(tour);
                                                        if (!string.IsNullOrWhiteSpace(period)) parts.Add(period);
                                                        if (!string.IsNullOrWhiteSpace(group)) parts.Add(group);
                                                        if (!string.IsNullOrWhiteSpace(days)) parts.Add((days.EndsWith(" gün", StringComparison.OrdinalIgnoreCase) ? days : days + " gün"));
                                                        if (!string.IsNullOrWhiteSpace(hotel)) parts.Add("Otel: " + hotel);
                                                        if (parts.Count > 0) poolDescFromPayload = string.Join(" + ", parts);
                                                    }
                                                }
                                                catch { }
                                                if (doc.RootElement.TryGetProperty("Timestamp", out var tsEl))
                                                {
                                                    try
                                                    {
                                                        var tss = tsEl.GetString();
                                                        if (!string.IsNullOrWhiteSpace(tss) && DateTime.TryParse(tss, out var dt))
                                                        {
                                                            subtitle += (string.IsNullOrWhiteSpace(subtitle) ? "" : " — ") + dt.ToLocalTime().ToString("g");
                                                        }
                                                    }
                                                    catch { }
                                                }
                                            }
                                            catch { }
                                        }

                                        if (string.IsNullOrWhiteSpace(subtitle) && dbn2.CreatedAt != default) subtitle = dbn2.CreatedAt.ToLocalTime().ToString("g");
                                    }
                                }
                                catch { }

                                // Build subtitle: if we have a pool description show it first (so user sees Tur/Dönem/Grup/Gün/Otel), then timestamp; otherwise fall back to parsed subtitle
                                string finalSubtitle;
                                if (!string.IsNullOrWhiteSpace(poolDescFromPayload))
                                {
                                    finalSubtitle = poolDescFromPayload;
                                    if (!string.IsNullOrWhiteSpace(timestampText)) finalSubtitle += " — " + timestampText;
                                }
                                else
                                {
                                    finalSubtitle = subtitle;
                                }

                                var subTb = new TextBlock { Text = finalSubtitle, FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isRead ? "#6B7280" : "#94A3B8")), TextWrapping = TextWrapping.Wrap };
                                subTb.Margin = new Thickness(2, 0, 0, 0);
                                textStack.Children.Add(subTb);

                                Grid.SetColumn(textStack, 1);
                                grid.Children.Add(textStack);

                                // time/right area: show short timestamp if not in subtitle
                                Grid.SetColumn(outerBorder, 0);

                                outerBorder.Child = grid;

                                var btn = new Button
                                {
                                    Content = outerBorder,
                                    Tag = notifId,
                                    Background = Brushes.Transparent,
                                    BorderThickness = new Thickness(0),
                                    Padding = new Thickness(0),
                                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                                };
                                btn.Click += NotificationsItem_Click;
                                // tooltip with raw payload
                                try
                                {
                                    if (r.Payload is TurizmOperasyonSistemi.Models.Notification dbn3)
                                    {
                                        btn.ToolTip = dbn3.Message ?? dbn3.PayloadJson;
                                    }
                                    else if (r.Payload != null)
                                    {
                                        btn.ToolTip = r.Payload.ToString();
                                    }
                                }
                                catch { }

                                stack.Children.Add(btn);
                            }
                        }
                        else
                        {
                            var tb = new TextBlock { Text = "Yeni bildirim yok.", Foreground = (Brush)FindResource("{x:Static SystemColors.ControlLightBrushKey}") };
                            tb.Margin = new Thickness(6);
                            stack.Children.Add(tb);
                        }
                    }
                    catch
                    {
                        stack.Children.Add(new TextBlock { Text = "Bildirimler yüklenemedi.", Margin = new Thickness(6) });
                    }

                    // show popup - open on dispatcher so placement/layout have settled (prevents initial off-screen placement)
                    try
                    {
                        // ensure placement target and reset offsets so placement recalculates
                        try
                        {
                            var btn = this.FindName("NotificationsButton") as UIElement;
                            if (btn != null) popup.PlacementTarget = btn;
                            popup.HorizontalOffset = 0;
                            popup.VerticalOffset = 0;
                        }
                        catch { }

                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try { popup.IsOpen = true; } catch { }
                        }), System.Windows.Threading.DispatcherPriority.Input);
                    }
                    catch { }
                    // stop shake when opened
                    StopNotificationsShake();
                    // Do not clear badge until user marks read; but if there were none, keep badge hidden
                }
                else
                {
                    // Fallback: eski davranışı sürdür
                    _notificationCount = 0;
                    var badge = this.FindName("NotificationBadge") as Border;
                    var badgeText = this.FindName("NotificationBadgeText") as TextBlock;
                    if (badge != null) badge.Visibility = Visibility.Collapsed;
                    if (badgeText != null) badgeText.Text = "0";
                    StopNotificationsShake();
                }
            }
            catch { }
        }

        private void NotificationsMarkAllReadButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear stored recent notifications and reset badge
                TurizmOperasyonSistemi.Services.NotificationCenter.ClearRecent();
                _notificationCount = 0;
                var badge = this.FindName("NotificationBadge") as Border;
                var badgeText = this.FindName("NotificationBadgeText") as TextBlock;
                if (badge != null) badge.Visibility = Visibility.Collapsed;
                if (badgeText != null) badgeText.Text = "0";
                // Mark DB notifications as read if table exists
                try
                {
                    using (var db = new TurizmOperasyonSistemi.Data.AppDbContext())
                    {
                        try { db.EnsureNotificationsTableExists(); } catch { }
                        var unread = db.Notifications.Where(n => n.IsRead == false).ToList();
                        foreach (var u in unread)
                        {
                            u.IsRead = true;
                        }
                        db.SaveChanges();
                    }
                }
                catch { }
                // close popup
                var popup = this.FindName("NotificationsPopup") as System.Windows.Controls.Primitives.Popup;
                if (popup != null) popup.IsOpen = false;
            }
            catch { }
        }

        // Helper to extract display text from NotificationRecord payload
        // Implemented as an extension-like local method by using a private static helper


        private decimal TryParseRate(string txt)
        {
            if (string.IsNullOrWhiteSpace(txt)) return 0m;
            // Try invariant (dot) then Turkish (comma)
            if (decimal.TryParse(txt, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return v;
            if (decimal.TryParse(txt, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            // remove non-digit chars
            var cleaned = new string(txt.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
            if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
            if (decimal.TryParse(cleaned, NumberStyles.Any, new CultureInfo("tr-TR"), out v)) return v;
            return 0m;
        }

        private decimal ConvertToUsd(decimal amount, string currency)
        {
            if (amount == 0m) return 0m;
            var cur = (currency ?? "").Trim().ToUpperInvariant();
            try
            {
                if (string.IsNullOrEmpty(cur) || cur == "TRY" || cur == "TL")
                {
                    // TRY to USD: amount / usdRate
                    if (_usdRate > 0) return amount / _usdRate;
                    return 0m;
                }
                if (cur == "USD" || cur == "US") return amount;
                if (cur == "EUR")
                {
                    if (_eurRate > 0 && _usdRate > 0) return amount * (_eurRate / _usdRate);
                }
                if (cur == "GBP")
                {
                    if (_gbpRate > 0 && _usdRate > 0) return amount * (_gbpRate / _usdRate);
                }
                if (cur == "SAR")
                {
                    if (_sarRate > 0 && _usdRate > 0) return amount * (_sarRate / _usdRate);
                }
                // Fallback: if we have usdRate assume amount is TRY
                if (_usdRate > 0) return amount / _usdRate;
            }
            catch { }
            return 0m;
        }

        private void UpdateTotalsForSelectedTour(string selected)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var query = db.Registrations.AsQueryable().Where(r => r.IsActive);

                    if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                    {
                        var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                        if (parts.Length >= 4)
                        {
                            var tour = parts[0]?.Trim();
                            var period = parts[1]?.Trim();
                            var groupType = parts[2]?.Trim();
                            var days = parts[3]?.Replace(" gün", "").Trim();

                            try
                            {
                                if (!string.IsNullOrEmpty(tour)) query = query.Where(r => (r.TourType ?? "") == tour);
                                if (!string.IsNullOrEmpty(period)) query = query.Where(r => (r.Period ?? "") == period);
                                if (!string.IsNullOrEmpty(groupType)) query = query.Where(r => (r.GroupType ?? "") == groupType);
                                if (!string.IsNullOrEmpty(days)) query = query.Where(r => (r.DaysCount ?? "") == days);
                            }
                            catch { }
                        }
                        else
                        {
                            if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(key.Tour)) query = query.Where(r => (r.TourType ?? "") == key.Tour);
                                    if (!string.IsNullOrEmpty(key.Period)) query = query.Where(r => (r.Period ?? "") == key.Period);
                                    if (!string.IsNullOrEmpty(key.GroupType)) query = query.Where(r => (r.GroupType ?? "") == key.GroupType);
                                    if (!string.IsNullOrEmpty(key.Days)) query = query.Where(r => (r.DaysCount ?? "") == key.Days);
                                }
                                catch { }
                            }
                        }
                    }

                    var regs = query.ToList();

                    var ledgerPerPerson = regs.GroupBy(r => r.PersonId)
                                              .Select(g => new
                                              {
                                                  PersonId = g.Key,
                                                  Total = g.Sum(x => (decimal?)x.TotalPrice) ?? 0m,
                                                  Paid = g.Sum(x => (decimal?)x.PaidAmount) ?? 0m,
                                                  Remaining = g.Sum(x => (decimal?)x.RemainingDebt) ?? 0m,
                                                  Currency = g.FirstOrDefault()?.Currency ?? string.Empty
                                              }).ToList();

                    var totalsByCurrency = ledgerPerPerson.GroupBy(p => string.IsNullOrWhiteSpace(p.Currency) ? "" : p.Currency)
                                                          .Select(g => new
                                                          {
                                                              Currency = g.Key,
                                                              Collected = g.Sum(x => x.Paid),
                                                              Remaining = g.Sum(x => x.Remaining)
                                                          }).ToList();

                    string FormatAmount(decimal amount, string currency)
                    {
                        if (string.IsNullOrWhiteSpace(currency))
                            return amount.ToString("N0");
                        var c = currency.Trim().ToUpperInvariant();
                        if (c == "TRY" || c == "TL" || c == "₺")
                            return $"₺{amount:N0}";
                        if (c == "USD" || c == "$")
                            return $"${amount:N0}";
                        if (c == "EUR" || c == "€")
                            return $"€{amount:N0}";
                        return $"{amount:N0} {currency}";
                    }

                    var collectedParts = totalsByCurrency.Select(t => FormatAmount(t.Collected, t.Currency)).ToList();
                    var remainingParts = totalsByCurrency.Select(t => FormatAmount(t.Remaining, t.Currency)).ToList();

                    var collectedDisplay = collectedParts.Any() ? string.Join(" — ", collectedParts) : FormatAmount(0, string.Empty);
                    var remainingDisplay = remainingParts.Any() ? string.Join(" — ", remainingParts) : FormatAmount(0, string.Empty);

                    var collectedTb = this.FindName("TotalCollectedText") as TextBlock;
                    var remainingTb = this.FindName("TotalRemainingText") as TextBlock;
                    var collectedPb = this.FindName("CollectedProgressBar") as ProgressBar;
                    var remainingPb = this.FindName("RemainingProgressBar") as ProgressBar;
                    var collectedPercent = this.FindName("CollectedPercentageText") as TextBlock;
                    var remainingPercent = this.FindName("RemainingPercentageText") as TextBlock;

                    if (collectedTb != null) collectedTb.Text = collectedDisplay;
                    if (remainingTb != null) remainingTb.Text = remainingDisplay;

                    // Progress Bar Güncelleme
                    try
                    {
                        decimal totalCollected = ledgerPerPerson.Sum(p => p.Paid);
                        decimal totalRemaining = ledgerPerPerson.Sum(p => p.Remaining);
                        decimal grandTotal = totalCollected + totalRemaining;

                        if (grandTotal > 0 && collectedPb != null && remainingPb != null)
                        {
                            double collectedPercent_Value = (double)((totalCollected / grandTotal) * 100);
                            double remainingPercent_Value = (double)((totalRemaining / grandTotal) * 100);

                            collectedPb.Value = collectedPercent_Value;
                            remainingPb.Value = remainingPercent_Value;

                            if (collectedPercent != null)
                                collectedPercent.Text = $"{Math.Round(collectedPercent_Value, 1)}%";
                            if (remainingPercent != null)
                                remainingPercent.Text = $"{Math.Round(remainingPercent_Value, 1)}%";
                        }
                        else if (collectedPb != null && remainingPb != null)
                        {
                            collectedPb.Value = 0;
                            remainingPb.Value = 0;
                            if (collectedPercent != null) collectedPercent.Text = "0%";
                            if (remainingPercent != null) remainingPercent.Text = "0%";
                        }
                    }
                    catch { }
                }
            }
            catch
            {
                try
                {
                    var collectedTb = this.FindName("TotalCollectedText") as TextBlock;
                    var remainingTb = this.FindName("TotalRemainingText") as TextBlock;
                    var collectedPb = this.FindName("CollectedProgressBar") as ProgressBar;
                    var remainingPb = this.FindName("RemainingProgressBar") as ProgressBar;
                    var collectedPercent = this.FindName("CollectedPercentageText") as TextBlock;
                    var remainingPercent = this.FindName("RemainingPercentageText") as TextBlock;

                    if (collectedTb != null) collectedTb.Text = "-";
                    if (remainingTb != null) remainingTb.Text = "-";
                    if (collectedPb != null) collectedPb.Value = 0;
                    if (remainingPb != null) remainingPb.Value = 0;
                    if (collectedPercent != null) collectedPercent.Text = "0%";
                    if (remainingPercent != null) remainingPercent.Text = "0%";
                }
                catch { }
            }
        }

        private async Task FetchExchangeRatesAsync()
        {
            try
            {
                const string url = "https://www.tcmb.gov.tr/kurlar/today.xml";
                string xml = await _httpClient.GetStringAsync(url).ConfigureAwait(false);

                // Parse XML
                var doc = XDocument.Parse(xml);

                string GetRate(string code, string elementName)
                {
                    try
                    {
                        var node = doc.Descendants("Currency").FirstOrDefault(x => (string)x.Attribute("CurrencyCode") == code);
                        if (node == null) return "-";
                        var el = node.Element(elementName);
                        if (el == null) return "-";
                        var val = el.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(val)) return "-";
                        return val;
                    }
                    catch
                    {
                        return "-";
                    }
                }

                var usdBuy = GetRate("USD", "ForexBuying");
                var usdSell = GetRate("USD", "ForexSelling");
                var eurBuy = GetRate("EUR", "ForexBuying");
                var eurSell = GetRate("EUR", "ForexSelling");
                var gbpBuy = GetRate("GBP", "ForexBuying");
                var gbpSell = GetRate("GBP", "ForexSelling");
                var sarBuy = GetRate("SAR", "ForexBuying");
                var sarSell = GetRate("SAR", "ForexSelling");

                // Parse and store numeric rates (TRY per unit foreign currency)
                _usdRate = TryParseRate(usdBuy);
                _eurRate = TryParseRate(eurBuy);
                _gbpRate = TryParseRate(gbpBuy);
                _sarRate = TryParseRate(sarBuy);

                // UI'yi güncelle (UI thread)
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var usdBuyTb = this.FindName("UsdBuyText") as TextBlock;
                        var usdSellTb = this.FindName("UsdSellText") as TextBlock;
                        var eurBuyTb = this.FindName("EurBuyText") as TextBlock;
                        var eurSellTb = this.FindName("EurSellText") as TextBlock;
                        var gbpBuyTb = this.FindName("GbpBuyText") as TextBlock;
                        var gbpSellTb = this.FindName("GbpSellText") as TextBlock;
                        var sarBuyTb = this.FindName("SarBuyText") as TextBlock;
                        var sarSellTb = this.FindName("SarSellText") as TextBlock;

                        if (usdBuyTb != null) usdBuyTb.Text = usdBuy;
                        if (usdSellTb != null) usdSellTb.Text = usdSell;
                        if (eurBuyTb != null) eurBuyTb.Text = eurBuy;
                        if (eurSellTb != null) eurSellTb.Text = eurSell;
                        if (gbpBuyTb != null) gbpBuyTb.Text = gbpBuy;
                        if (gbpSellTb != null) gbpSellTb.Text = gbpSell;
                        if (sarBuyTb != null) sarBuyTb.Text = sarBuy;
                        if (sarSellTb != null) sarSellTb.Text = sarSell;
                        // Güncellenen kurlar geldiyse seçili tura göre toplamları da yenile
                        var sel = ActiveToursCombo.SelectedItem as string;
                        try { UpdateTotalsForSelectedTour(sel); } catch { }
                    }
                    catch
                    {
                        // ignore UI update errors
                    }
                });
            }
            catch
            {
                // Eğer hata olursa rate TextBlock'ları '-' olarak bırak
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        foreach (var name in new[] { "UsdBuyText", "UsdSellText", "EurBuyText", "EurSellText", "GbpBuyText", "GbpSellText", "SarBuyText", "SarSellText" })
                        {
                            var tb = this.FindName(name) as TextBlock;
                            if (tb != null) tb.Text = "-";
                        }
                    }
                    catch { }
                });
            }
        }

        private void LoadActiveTours()
        {
            try
            {
                var list = new List<string>();
                // map to hold latest CreatedDate for each display group (used to pick most-recently-saved tour)
                var latestMap = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
                _tourKeyMap.Clear();

                using (var db = new AppDbContext())
                {
                    // Registrations tablosuna göre grupları al; aktivite sonu ise RoomAllocations'daki en ileri CheckOutDate'e göre belirlenecek
                    var regs = db.Registrations.AsNoTracking().ToList();
                    var allocations = db.RoomAllocations.AsNoTracking().ToList();

                    var grouped = regs
                        .GroupBy(r => new { r.TourType, r.Period, r.GroupType, r.DaysCount })
                        .ToList();

                    var culture = new CultureInfo("tr-TR");

                    foreach (var g in grouped)
                    {
                        // Ön şart: grupta en az bir aktif kayıt (Status == "Aktif" veya IsActive == true) olmalı
                        bool registrationActive = g.Any(r => string.Equals(r.Status, "Aktif", StringComparison.OrdinalIgnoreCase) || r.IsActive);
                        if (!registrationActive) continue;

                        // Aynı grup için odalama kayıtlarını bul
                        var allocsForGroup = allocations.Where(a =>
                            string.Equals(a.TourType ?? "", g.Key.TourType ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.Period ?? "", g.Key.Period ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.GroupType ?? "", g.Key.GroupType ?? "", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(a.DaysCount ?? "", g.Key.DaysCount ?? "", StringComparison.OrdinalIgnoreCase)
                        ).ToList();

                        bool allocationDeterminesActive = false; // true means allocations exist and determine active status
                        bool allocationActive = true; // default if no allocations

                        if (allocsForGroup.Any())
                        {
                            allocationDeterminesActive = true;

                            // Eğer herhangi bir CheckOutDate boşsa bunu aktif kabul et (henüz çıkış belirlenmemiş)
                            if (allocsForGroup.Any(a => string.IsNullOrWhiteSpace(a.CheckOutDate)))
                            {
                                allocationActive = true;
                            }
                            else
                            {
                                DateTime? maxCheckout = null;
                                foreach (var a in allocsForGroup)
                                {
                                    DateTime parsed;
                                    if (DateTime.TryParse(a.CheckOutDate, culture, DateTimeStyles.None, out parsed) || DateTime.TryParse(a.CheckOutDate, out parsed))
                                    {
                                        if (!maxCheckout.HasValue || parsed.Date > maxCheckout.Value.Date) maxCheckout = parsed.Date;
                                    }
                                }

                                if (maxCheckout.HasValue)
                                {
                                    // Grup, en ileri çıkış tarihi bugünden önce ise pasif sayılacak
                                    allocationActive = maxCheckout.Value.Date >= DateTime.Today;
                                }
                                else
                                {
                                    // Eğer hiçbir tarih parse edilemediyse temkinli davran: aktif say
                                    allocationActive = true;
                                }
                            }
                        }

                        // Nihai karar: kayıtlar aktifse ve (eğer odalama kayıtları varsa) odalama da aktif gösteriyorsa grubu ekle
                        if (!registrationActive) continue;
                        if (allocationDeterminesActive && !allocationActive) continue;

                        string tour = string.IsNullOrWhiteSpace(g.Key.TourType) ? "(Belirtilmemiş)" : g.Key.TourType;
                        string period = string.IsNullOrWhiteSpace(g.Key.Period) ? "(Belirtilmemiş)" : g.Key.Period;
                        string groupType = string.IsNullOrWhiteSpace(g.Key.GroupType) ? "(Belirtilmemiş)" : g.Key.GroupType;
                        string days = string.IsNullOrWhiteSpace(g.Key.DaysCount) ? "?" : g.Key.DaysCount;

                        var display = $"{tour} + {period} + {groupType} + {days} gün";

                        if (!_tourKeyMap.ContainsKey(display))
                        {
                            _tourKeyMap[display] = (tour, period, groupType, g.Key.DaysCount ?? "");
                        }

                        // store latest CreatedDate for this group (to later choose most recently saved tour)
                        try
                        {
                            var maxCreated = g.Max(r => r.CreatedDate);
                            latestMap[display] = maxCreated;
                        }
                        catch
                        {
                            latestMap[display] = DateTime.MinValue;
                        }

                        // hesapla aktiv/pasif kişi sayıları doğrudan registrations grubu üzerinden
                        var totalPeople = g.Count();
                        var activePeople = g.Count(r => r.IsActive || string.Equals(r.Status, "Aktif", StringComparison.OrdinalIgnoreCase));
                        var inactivePeople = Math.Max(0, totalPeople - activePeople);
                        _tourCountsMap[display] = (activePeople, inactivePeople, totalPeople);

                        list.Add(display);
                    }
                }

                var distinctList = list.Distinct().OrderBy(x => x).ToList();
                var items = new List<string> { "Tümü" };
                items.AddRange(distinctList);
                ActiveToursCombo.ItemsSource = items;

                // Varsayılan seçimi bu aya ait olan seçenek yap
                try
                {
                    var culture = new CultureInfo("tr-TR");
                    var monthName = DateTime.Now.ToString("MMMM", culture); // e.g. "haziran"
                    var normMonth = NormalizeForComparison(monthName ?? string.Empty);

                    // latestMap is populated earlier while enumerating groups

                    var monthCandidates = distinctList.Where(d => NormalizeForComparison(d ?? string.Empty).Contains(normMonth)).ToList();
                    if (monthCandidates.Any())
                    {
                        // if multiple, pick the one with latest created date
                        var chosen = monthCandidates
                            .OrderByDescending(d => latestMap.ContainsKey(d) ? latestMap[d] : DateTime.MinValue)
                            .FirstOrDefault();

                        if (!string.IsNullOrWhiteSpace(chosen))
                        {
                            ActiveToursCombo.SelectedItem = chosen;
                        }
                        else
                        {
                            ActiveToursCombo.SelectedIndex = 0;
                        }
                    }
                    else
                    {
                        ActiveToursCombo.SelectedIndex = 0;
                    }
                }
                catch
                {
                    ActiveToursCombo.SelectedIndex = 0;
                }

                // Aktif tur sayısını göster
                try
                {
                    int count = distinctList.Count;
                    var tb = this.FindName("ActiveToursCountText") as TextBlock;
                    if (tb != null)
                    {
                        tb.Text = $"Aktif: {count}";
                    }
                }
                catch
                {
                    // ignore
                }
            }
            catch
            {
                // Sessizce yoksay
            }
        }

        private void ActiveToursCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                var selected = ActiveToursCombo.SelectedItem as string;
                UpdateCountsForSelectedTour(selected);
                try { UpdateTotalsForSelectedTour(selected); } catch { }
                try { UpdateVisaSummaryForSelectedTour(selected); } catch { }
                try { UpdateTicketSummaryForSelectedTour(selected); } catch { }
                try { UpdateCargoSummaryForSelectedTour(selected); } catch { }
                try { UpdateRoomAllocationSummaryForSelectedTour(selected); } catch { }
            }
            catch
            {
                // ignore
            }
        }

        private void UpdateRoomAllocationSummaryForSelectedTour(string selected)
        {
            try
            {
                using var db = new AppDbContext();

                    var regsQuery = db.Registrations.AsNoTracking().Where(r => r.IsActive).AsQueryable();

                    if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                    {
                        var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                        if (parts.Length >= 4)
                        {
                            var tour = parts[0]?.Trim();
                            var period = parts[1]?.Trim();
                            var groupType = parts[2]?.Trim();
                            var days = parts[3]?.Replace(" gün", "").Trim();

                            try
                            {
                                if (!string.IsNullOrEmpty(tour)) regsQuery = regsQuery.Where(r => (r.TourType ?? "") == tour);
                                if (!string.IsNullOrEmpty(period)) regsQuery = regsQuery.Where(r => (r.Period ?? "") == period);
                                if (!string.IsNullOrEmpty(groupType)) regsQuery = regsQuery.Where(r => (r.GroupType ?? "") == groupType);
                                if (!string.IsNullOrEmpty(days)) regsQuery = regsQuery.Where(r => (r.DaysCount ?? "") == days);
                            }
                            catch { }
                        }
                        else
                        {
                            if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(key.Tour)) regsQuery = regsQuery.Where(r => (r.TourType ?? "") == key.Tour);
                                    if (!string.IsNullOrEmpty(key.Period)) regsQuery = regsQuery.Where(r => (r.Period ?? "") == key.Period);
                                    if (!string.IsNullOrEmpty(key.GroupType)) regsQuery = regsQuery.Where(r => (r.GroupType ?? "") == key.GroupType);
                                    if (!string.IsNullOrEmpty(key.Days)) regsQuery = regsQuery.Where(r => (r.DaysCount ?? "") == key.Days);
                                }
                                catch { }
                            }
                        }
                    }

                    var regs = regsQuery.ToList();
                    var totalRegs = regs.Count;

                    // All allocations matching the same criteria (any hotel)
                    var allocQuery = db.RoomAllocations.AsNoTracking().Include(a => a.Occupants).AsQueryable();
                    if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                    {
                        var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                        if (parts.Length >= 4)
                        {
                            var tour = parts[0]?.Trim();
                            var period = parts[1]?.Trim();
                            var groupType = parts[2]?.Trim();
                            var days = parts[3]?.Replace(" gün", "").Trim();
                            try
                            {
                                if (!string.IsNullOrEmpty(tour)) allocQuery = allocQuery.Where(a => (a.TourType ?? "") == tour);
                                if (!string.IsNullOrEmpty(period)) allocQuery = allocQuery.Where(a => (a.Period ?? "") == period);
                                if (!string.IsNullOrEmpty(groupType)) allocQuery = allocQuery.Where(a => (a.GroupType ?? "") == groupType);
                                if (!string.IsNullOrEmpty(days)) allocQuery = allocQuery.Where(a => (a.DaysCount ?? "") == days);
                            }
                            catch { }
                        }
                        else
                        {
                            if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                            {
                                try
                                {
                                    if (!string.IsNullOrEmpty(key.Tour)) allocQuery = allocQuery.Where(a => (a.TourType ?? "") == key.Tour);
                                    if (!string.IsNullOrEmpty(key.Period)) allocQuery = allocQuery.Where(a => (a.Period ?? "") == key.Period);
                                    if (!string.IsNullOrEmpty(key.GroupType)) allocQuery = allocQuery.Where(a => (a.GroupType ?? "") == key.GroupType);
                                    if (!string.IsNullOrEmpty(key.Days)) allocQuery = allocQuery.Where(a => (a.DaysCount ?? "") == key.Days);
                                }
                                catch { }
                            }
                        }
                    }

                    var allocs = allocQuery.ToList();
                    var allocatedPassengerIds = allocs.SelectMany(a => a.Occupants).Select(o => o.PassengerId).Distinct().ToList();

                    int allocatedCount = allocatedPassengerIds.Count;
                    int availableCount = Math.Max(0, totalRegs - allocatedCount);

                    // Per-hotel breakdown: allocated vs pool (unallocated relative to that hotel)
                    var hotels = db.Hotels.AsNoTracking().OrderBy(h => h.Name).ToList();
                    var perHotelLines = new List<string>();

                    if (allocs.Count == 0)
                    {
                        // No allocation records at all for these criteria: all registrations are "Odalama kaydı açılmamış"
                        if (totalRegs > 0)
                        {
                            perHotelLines.Add($"Odalama kaydı açılmamış: {totalRegs} kişi");
                        }
                    }
                    else
                    {
                        foreach (var h in hotels)
                        {
                            try
                            {
                                var allocsForHotel = allocQuery.Where(a => a.Hotel != null && a.Hotel.Name == h.Name).ToList();
                                if (!allocsForHotel.Any()) continue; // skip hotels without saved allocation rows for this criteria

                                var idsForHotel = allocsForHotel.SelectMany(a => a.Occupants).Select(o => o.PassengerId).Distinct().ToList();
                                var allocatedForHotel = idsForHotel.Count;
                                // poolForHotel: registrations matching criteria that are not placed into this hotel's allocations
                                var poolForHotel = regs.Count(r => !idsForHotel.Contains(r.PersonId));

                                // Only include if there is any allocated or pool count
                                if (allocatedForHotel > 0 || poolForHotel > 0)
                                {
                                    perHotelLines.Add($"{h.Name}: odalanmış {allocatedForHotel}, Odalanmamış {poolForHotel}");
                                }
                            }
                            catch { }
                        }
                    }

                    // Update UI
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var reservedTb = this.FindName("RoomAllocReservedText") as TextBlock;
                            var availTb = this.FindName("RoomAllocAvailableText") as TextBlock;
                            var perHotelPanel = this.FindName("RoomAllocPerHotelPanel") as StackPanel;
                            var hintTb = this.FindName("RoomAllocHintText") as TextBlock;
                            if (reservedTb != null) reservedTb.Text = allocatedCount.ToString();
                            if (availTb != null) availTb.Text = availableCount.ToString();

                            // Colors pool for hotels (deterministic selection)
                            var colors = new[] { "#8B5CF6", "#7EE787", "#F59E0B", "#FCA5A5", "#14B8A6", "#60A5FA", "#F97316", "#06B6D4" };

                            if (perHotelPanel != null)
                            {
                                perHotelPanel.Children.Clear();
                                if (perHotelLines.Count == 0)
                                {
                                    var tb = new TextBlock { Text = "Tüm kayıtlar odalandı.", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7EE787")), FontSize = 12 };
                                    perHotelPanel.Children.Add(tb);
                                }
                                else
                                {
                                    foreach (var line in perHotelLines)
                                    {
                                        try
                                        {
                                            var idx = line.IndexOf(':');
                                            string hotel = idx >= 0 ? line.Substring(0, idx) : line;
                                            string rest = idx >= 0 ? line.Substring(idx + 1).Trim() : string.Empty;

                                            // choose color based on hashcode
                                            int hash = Math.Abs((hotel ?? string.Empty).GetHashCode());
                                            var colorHex = colors[hash % colors.Length];
                                            var colorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));

                                            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap };
                                            tb.Inlines.Add(new Run(hotel + ": ") { Foreground = colorBrush, FontWeight = FontWeights.SemiBold });
                                            tb.Inlines.Add(new Run(rest) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CBD5E1")) });

                                            perHotelPanel.Children.Add(tb);
                                        }
                                        catch { }
                                    }
                                }
                            }

                            if (hintTb != null)
                            {
                                hintTb.Text = "Son güncelleme: " + DateTime.Now.ToString("g");
                            }
                        }
                        catch { }
                    });
            }
            catch { }
        }

        private string NormalizeForComparison(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            return input.Trim().ToLowerInvariant()
                .Replace('ı','i').Replace('ğ','g').Replace('ü','u').Replace('ş','s').Replace('ö','o').Replace('ç','c');
        }

        private void UpdateVisaSummaryForSelectedTour(string selected)
        {
            try
            {
                using var db = new AppDbContext();
                // Load active registrations into memory and apply the same string-normalized filtering
                var regs = db.Registrations.AsNoTracking().Where(r => r.IsActive).ToList();

                if (!string.IsNullOrWhiteSpace(selected) && selected != "Tümü")
                {
                    var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                    string tour = null, period = null, groupType = null, days = null;
                    if (parts.Length >= 4)
                    {
                        tour = parts[0]?.Trim();
                        period = parts[1]?.Trim();
                        groupType = parts[2]?.Trim();
                        days = parts[3]?.Replace(" gün", "").Trim();
                    }
                    else if (_tourKeyMap != null && _tourKeyMap.TryGetValue(selected, out var key))
                    {
                        tour = key.Tour; period = key.Period; groupType = key.GroupType; days = key.Days;
                    }

                    // Normalize selection for comparison
                    var ntour = NormalizeForComparison(tour ?? string.Empty);
                    var nperiod = NormalizeForComparison(period ?? string.Empty);
                    var ngroup = NormalizeForComparison(groupType ?? string.Empty);
                    var ndays = NormalizeForComparison(days ?? string.Empty);

                    regs = regs.Where(r =>
                    {
                        try
                        {
                            bool ok = true;
                            if (!string.IsNullOrWhiteSpace(ntour))
                            {
                                if (ntour == "(belirtilmemiş)" || ntour == "(belirtilmemis)")
                                {
                                    ok &= string.IsNullOrWhiteSpace(r.TourType);
                                }
                                else ok &= NormalizeForComparison(r.TourType ?? string.Empty) == ntour;
                            }
                            if (!string.IsNullOrWhiteSpace(nperiod))
                            {
                                if (nperiod == "(belirtilmemiş)" || nperiod == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.Period);
                                else ok &= NormalizeForComparison(r.Period ?? string.Empty) == nperiod;
                            }
                            if (!string.IsNullOrWhiteSpace(ngroup))
                            {
                                if (ngroup == "(belirtilmemiş)" || ngroup == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.GroupType);
                                else ok &= NormalizeForComparison(r.GroupType ?? string.Empty) == ngroup;
                            }
                            if (!string.IsNullOrWhiteSpace(ndays))
                            {
                                if (ndays == "-" || ndays == "(belirtilmemiş)" || ndays == "(belirtilmemis)") ok &= string.IsNullOrWhiteSpace(r.DaysCount);
                                else ok &= NormalizeForComparison(r.DaysCount ?? string.Empty) == ndays;
                            }
                            return ok;
                        }
                        catch { return false; }
                    }).ToList();
                }

                var list = regs.Select(r => new { r.VisaRequest, r.VisaStatusChoice }).ToList();

                int willGet = 0, selfGet = 0, issued = 0, notIssued = 0, inProgress = 0, passportPending = 0;

                int passportYes = 0, passportNo = 0;

                foreach (var item in list)
                {
                    var vr = NormalizeForComparison(item.VisaRequest ?? string.Empty);
                    var vs = NormalizeForComparison(item.VisaStatusChoice ?? string.Empty);

                    // Vize Talebi based
                    if (!string.IsNullOrWhiteSpace(vr))
                    {
                        if (vr.Contains("kendisi") || vr.Contains("kendi")) selfGet++;
                        else if (vr.Contains("alac") && vr.Contains("vize")) willGet++;
                    }

                    // Vize durumu based
                    if (!string.IsNullOrWhiteSpace(vs))
                    {
                        // Check negative forms ("çıkmadı") before positive ones to avoid substring collisions
                        if (vs.Contains("cikmad") || vs.Contains("cikmadi") || vs.Contains("cikm"))
                        {
                            notIssued++;
                        }
                        else if (vs.Contains("cikti") || vs.Contains("cikt") || vs.Contains("vizecikti") )
                        {
                            issued++;
                        }
                        else if (vs.Contains("alin") || vs.Contains("aliniyor") || vs.Contains("aliniyor"))
                        {
                            inProgress++;
                        }
                        else if (vs.Contains("pasaport") || vs.Contains("pasaportbeklen") || vs.Contains("beklen"))
                        {
                            passportPending++;
                        }
                    }

                // Additionally count PassportArrived yes/no from filtered registrations
                try
                {
                    // Load AppSettings for PassportArrived type to map Id -> Value similar to PersonsGeneralView
                    var appSettings = db.AppSettings.Where(a => a.Type == "Pasaportu Geldi mi ?" || a.Type == "Pasaport Geldi mi" || a.Type == "PassportArrived").AsNoTracking().ToList();
                    var settingsById = appSettings.ToDictionary(a => a.Id, a => (a.Value ?? string.Empty));

                    passportYes = 0; passportNo = 0;
                    foreach (var r in regs)
                    {
                        try
                        {
                            if (!r.PassportArrived.HasValue)
                                continue;

                            var val = r.PassportArrived.Value;
                            if (val < 0)
                                continue; // unspecified

                            string sl = string.Empty;
                            if (settingsById.TryGetValue(val, out var sv)) sl = sv ?? string.Empty;
                            else
                            {
                                // fallback: interpret numeric
                                sl = val != 0 ? "1" : "0";
                            }

                            var slNorm = NormalizeForComparison(sl ?? string.Empty);
                            // Treat explicit 'pasaport vermeyecek' or unspecified as neither Yes nor No
                            if (slNorm.Contains("verme") || slNorm.Contains("vermeyecek") || slNorm.Contains("belirtilme") || slNorm.Contains("belirtilmedi"))
                            {
                                // skip counting
                            }
                            else if (slNorm == "evet" || slNorm == "var" || slNorm == "yes" || slNorm == "1")
                            {
                                passportYes++;
                            }
                            else if (slNorm == "hayir" || slNorm == "hay" || slNorm == "0")
                            {
                                passportNo++;
                            }
                            else
                            {
                                // fallback: if value contains 'hay' treat as no, otherwise skip
                                if (slNorm.Contains("hay")) passportNo++;
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                }

                // Update UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var a = this.FindName("VisaWillGetText") as TextBlock;
                        var b = this.FindName("VisaSelfGetText") as TextBlock;
                        var c = this.FindName("VisaIssuedText") as TextBlock;
                        var d = this.FindName("VisaNotIssuedText") as TextBlock;
                        var e = this.FindName("VisaInProgressText") as TextBlock;
                        var f = this.FindName("PassportPendingText") as TextBlock;
                        if (a != null) a.Text = willGet.ToString();
                        if (b != null) b.Text = selfGet.ToString();
                        if (c != null) c.Text = issued.ToString();
                        if (d != null) d.Text = notIssued.ToString();
                        if (e != null) e.Text = inProgress.ToString();
                        if (f != null) f.Text = passportPending.ToString();
                        try { var py = this.FindName("PassportArrivedYesText") as TextBlock; if (py != null) py.Text = passportYes.ToString(); } catch { }
                        try { var pn = this.FindName("PassportArrivedNoText") as TextBlock; if (pn != null) pn.Text = passportNo.ToString(); } catch { }
                        try { var hint = this.FindName("VisaHintText") as TextBlock; if (hint != null) hint.Text = "Son güncelleme: " + DateTime.Now.ToString("g"); } catch { }
                    }
                    catch { }
                });
            }
            catch { }
        }

        private void UpdateCountsForSelectedTour(string selected)
        {
            try
            {
                var activeTb = this.FindName("ActivePeopleText") as TextBlock;
                var inactiveTb = this.FindName("InactivePeopleText") as TextBlock;

                if (activeTb == null || inactiveTb == null)
                    return;

                using (var db = new AppDbContext())
                {
                    if (string.IsNullOrWhiteSpace(selected) || selected == "Tümü")
                    {
                        // Eğer map'te toplamlar saklandıysa kullan, yoksa DB'den al
                        if (_tourCountsMap != null && _tourCountsMap.Count > 0)
                        {
                            var total = _tourCountsMap.Values.Sum(v => v.Total);
                            var active = _tourCountsMap.Values.Sum(v => v.Active);
                            var inactive = Math.Max(0, total - active);

                            activeTb.Text = $"Aktif Kişi: {active}";
                            inactiveTb.Text = $"Pasif Kişi: {inactive}";
                            return;
                        }

                        var totalDb = db.Registrations.AsNoTracking().Count();
                        var activeDb = db.Registrations.AsNoTracking().Count(r => r.IsActive || string.Equals(r.Status, "Aktif", StringComparison.OrdinalIgnoreCase));
                        var inactiveDb = Math.Max(0, totalDb - activeDb);

                        activeTb.Text = $"Aktif Kişi: {activeDb}";
                        inactiveTb.Text = $"Pasif Kişi: {inactiveDb}";
                        return;
                    }

                    // Öncelikle mapping'e bakarak güvenli değerleri al
                    if (_tourCountsMap != null && _tourCountsMap.TryGetValue(selected, out var counts))
                    {
                        activeTb.Text = $"Aktif Kişi: {counts.Active}";
                        inactiveTb.Text = $"Pasif Kişi: {counts.Inactive}";
                        return;
                    }

                    // Parse edilen format: "{tour} + {period} + {groupType} + {days} gün" - fallback
                    var parts = selected.Split(new[] { " + " }, StringSplitOptions.None);
                    if (parts.Length >= 4)
                    {
                        var tour = parts[0]?.Trim();
                        var period = parts[1]?.Trim();
                        var groupType = parts[2]?.Trim();
                        var days = parts[3]?.Replace(" gün", "").Trim();

                        var query = db.Registrations.AsNoTracking().Where(r =>
                            string.Equals((r.TourType ?? ""), (tour ?? ""), StringComparison.OrdinalIgnoreCase)
                            && string.Equals((r.Period ?? ""), (period ?? ""), StringComparison.OrdinalIgnoreCase)
                            && string.Equals((r.GroupType ?? ""), (groupType ?? ""), StringComparison.OrdinalIgnoreCase)
                            && string.Equals((r.DaysCount ?? ""), (days ?? ""), StringComparison.OrdinalIgnoreCase)
                        );

                        var total = query.Count();
                        var active = query.Count(r => r.IsActive || string.Equals(r.Status, "Aktif", StringComparison.OrdinalIgnoreCase));
                        var inactive = Math.Max(0, total - active);

                        activeTb.Text = $"Aktif Kişi: {active}";
                        inactiveTb.Text = $"Pasif Kişi: {inactive}";
                        return;
                    }

                    // Eğer parsing başarısızsa sıfırla
                    activeTb.Text = "Aktif Kişi: 0";
                    inactiveTb.Text = "Pasif Kişi: 0";
                }
            }
            catch
            {
                // ignore errors
            }
        }



        private void UpdateClock()
        {
            var now = DateTime.Now;

            // Dijital saat gösterimi (saat:dakika:saniye)
            try
            {
                var tb = this.FindName("DigitalTimeText") as TextBlock;
                if (tb != null)
                {
                    tb.Text = now.ToString("HH:mm:ss");
                }
            }
            catch { }

            // Tarih - Türkçe format
            try
            {
                DateText.Text = now.ToString("dddd, dd MMMM yyyy", new CultureInfo("tr-TR"));
            }
            catch { }
        }
    }
}

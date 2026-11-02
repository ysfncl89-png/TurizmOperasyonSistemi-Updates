# Görev İlerleme Detayları

## Değişiklikler
- `Velopack` 1.2.161 paketi `TurizmOperasyonSistemi.csproj` dosyasına eklendi.
- `Services/VelopackUpdateService.cs` oluşturuldu. MutfaGO desenindeki HTTPS GitHub `GithubSource`, `UpdateManager.CheckForUpdatesAsync`, kullanıcı onayı, indirme ve `ApplyUpdatesAndRestart` akışı uyarlandı.
- `Data/appsettings.json` içine `Updates` bölümü eklendi; repository URL, kontrol aralığı ve prerelease seçeneği tanımlandı.
- `Services/ConfigurationProvider.cs` içine Velopack ayar erişimleri eklendi; eski manifest tabanlı `Update` ayarları devre dışı bırakıldı.
- `App.xaml.cs` içinde Velopack başlangıç kontrolü eklendi ve eski `UpdateBackgroundService` kaydı kaldırıldı. Eski servis sınıfları henüz sonraki dokümantasyon/temizlik görevine kadar silinmedi.
- `Views/PersonsWizardView.xaml`, `Views/PersonsGeneralView.xaml` ve LocalAppData manifest mantığı değiştirilmedi.

## Doğrulama
- `TurizmOperasyonSistemi.csproj` derlemesi başarılı.
- Derleme çıktısında hata veya uyarı bildirilmedi.
- DEBUG derlemelerinde ağ güncelleme kontrolü yapılmıyor; release davranışı MutfaGO örneğindeki gibi GitHub Releases üzerinden çalışacak.

## Notlar
- Güncelleme repository URL'si örnek değer olarak mevcut releases deposunu kullanıyor; yayın pipeline görevi bu değeri TOS Lite'ın gerçek GitHub Updates deposuna göre tamamlayacak.
- Network/GitHub erişim hataları normal uygulama başlangıcını engellemiyor.

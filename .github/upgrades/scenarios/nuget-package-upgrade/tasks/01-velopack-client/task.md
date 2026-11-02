# 01-velopack-client: MutfaGO desenini TOS Lite istemcisine uyarlama

MutfaGO projesi incelendi. İstemci `Velopack.Sources.GithubSource` ile public GitHub updates deposuna bağlanıyor; `UpdateManager.CheckForUpdatesAsync()` ile release kontrolü yapıyor, `DownloadUpdatesAsync()` sonrasında `ApplyUpdatesAndRestart()` çağırıyor. Repository URL `Updates:RepositoryUrl` yapılandırmasından okunuyor; güncelleme kontrolü release notlarını ve kullanıcı onayını içeriyor. Çevrimdışı veya GitHub erişim hatasında normal uygulama açılışı korunuyor.

TOS Lite araştırma bulguları:
- Proje `TurizmOperasyonSistemi.csproj`, hedef `net8.0-windows`, WPF ve nullable enabled.
- Velopack mevcut değil; NuGet paketi eklenmesi gerekiyor.
- Güncel `UpdateBackgroundService`, `UpdateCheckerService`, `UpdateDownloadService` ve `UpdateInstallerService` URL/manifest/zip/INNO Setup mantığına dayanıyor.
- Güncelleme servisi `App.xaml.cs` içinde `BackgroundServiceManager` ile başlatılıyor.
- `Data/appsettings.json` içindeki mevcut `Update` bölümü varsayılan olarak etkin; yeni yapılandırma GitHub repository URL'sini ve Velopack opt-in davranışını açıkça taşımalı.
- `Services/ManifestService.cs`, `App.xaml.cs` ve LocalAppData verileri veritabanı manifesti için kullanılıyor; `.github/copilot-instructions.md` içindeki basit `MachineId`, `Version`, `LastModified`, `Hash` formatı değiştirilmeyecek.
- `Views/PersonsWizardView.xaml` ve `Views/PersonsGeneralView.xaml` bu görev kapsamında değiştirilmemeli; PersonsGeneralView neon violet `#FF8B5CF6` ComboBox davranışı korunmalı.
- INNO Setup artık fallback olarak korunmayacak; kullanıcı kararı gereği güncelleme istemcisi yalnızca Velopack olacak. Eski zip/manifest sınıfları migration/yardımcı kullanım için gerekli değilse kaldırılmalı veya yeni akıştan ayrıştırılmalı.

## Scope Inventory

- **Project**: `TurizmOperasyonSistemi.csproj`
- **Distinct concerns**: Velopack package/API integration, configuration, startup update orchestration, removal of legacy installer launch path.
- **Reference implementation**: `C:\Users\turiz\Desktop\MutfaGO\Services\UpdateCoordinator.cs`, `MutfaGO.csproj`, `appsettings.json`.
- **Safety boundaries**: no hardcoded tokens, HTTPS repository validation, offline startup fallback, database and LocalAppData manifest preservation.

**Done when**: TOS Lite HTTPS GitHub repository ayarından Velopack güncellemesi kontrol eder, kullanıcı onayı sonrası indirip yeniden başlatır, çevrimdışı durumda normal açılışı engellemez ve uygulama warning-free derlenir.

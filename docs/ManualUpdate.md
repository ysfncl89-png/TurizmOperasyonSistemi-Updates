# TOS Lite Velopack Güncelleme Akışı

TOS Lite güncellemeleri yalnızca Velopack ve GitHub Releases üzerinden dağıtılır. INNO Setup, zip üzerine elle kopyalama ve `latest.json` manifesti kullanılmaz.

## İlk kurulum

1. GitHub Releases içindeki Velopack tarafından üretilen `Setup.exe` dosyasını indirin.
2. `Setup.exe` dosyasını çalıştırın.
3. Uygulama dosyaları Velopack tarafından yönetilen kurulum dizinine yüklenir.
4. Kullanıcı veritabanı `%LOCALAPPDATA%\TurizmOperasyonSistemi\turizm.db` altında tutulur; kurulum paketi bu dosyayı içermez.

## Otomatik güncelleme

1. Uygulama başlangıçta `Updates:RepositoryUrl` adresindeki GitHub Releases deposunu kontrol eder.
2. Yeni bir release bulunursa sürüm notlarıyla birlikte kullanıcı onayı istenir.
3. Onaydan sonra Velopack güncellemeyi indirir ve `ApplyUpdatesAndRestart` ile uygulamayı yeniden başlatır.
4. GitHub'a erişilemiyorsa uygulama mevcut sürümle normal şekilde açılır.
5. Kontrol sıklığı `Updates:CheckIntervalMinutes` ile belirlenir; önsürüm kullanımı `Updates:AllowPrerelease` ile açılır.

## Yayınlama öncesi kontrol

- `TurizmOperasyonSistemi.csproj` içindeki `<Version>` değerini üç parçalı sürüm olarak artırın; örnek: `1.0.0.10` → `1.0.0.11`.
- GitHub Actions variable `UPDATE_REPOSITORY` değerini `owner/repository` biçiminde ayarlayın.
- GitHub Actions secret `UPDATE_REPO_TOKEN` değerini yalnızca gerekli release yazma yetkileriyle ekleyin.
- Önce Actions ekranından **workflow_dispatch** ile kuru paketleme çalıştırmasını doğrulayın.
- Daha sonra `v1.0.0.11` gibi proje sürümüyle aynı bir tag gönderin.

## Veritabanı güvenliği

- Güncelleme paketi kullanıcı veritabanını içermez.
- Uygulama başlangıç migration'larını mevcut akışla çalıştırır.
- LocalAppData manifesti basit JSON biçiminde kalır: `MachineId`, `Version`, `LastModified`, `Hash`.
- Sorun halinde uygulamayı kapatıp LocalAppData veritabanı yedeğini geri yükleyin.


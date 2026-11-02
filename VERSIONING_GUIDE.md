# Sürümleme Rehberi

TOS Lite sürümleri `TurizmOperasyonSistemi.csproj` içindeki `<Version>` alanından alınır. GitHub Actions bu değeri doğrular ve Velopack paket sürümü olarak kullanır.

## Kullanılacak format

- Proje sürümü: `1.0.10`
- GitHub Release etiketi: `v1.0.10`
- Velopack paket sürümü: `1.0.10`
- .NET assembly/file version: `1.0.10.0`

Tag ile proje sürümü aynı olmalıdır. Workflow, eşleşmiyorsa release yayınlamayı durdurur.

## Yeni sürüm yayınlama

1. `TurizmOperasyonSistemi.csproj` içindeki `<Version>` değerini artırın.
2. GitHub Actions variable `UPDATE_REPOSITORY` değerinin public updates deposunu gösterdiğini doğrulayın.
3. `UPDATE_REPO_TOKEN` secret'ının release yazma yetkisi olduğunu doğrulayın.
4. İsteğe bağlı olarak workflow'u `workflow_dispatch` ile çalıştırıp artifact paketini kontrol edin.
5. Proje sürümüyle aynı üç parçalı `vMAJOR.MINOR.PATCH` tag'ini gönderin; örneğin `v1.0.11`.
6. Workflow .NET 8 self-contained `win-x64` publish çıktısını Velopack ile paketler ve GitHub Releases'e yükler.

## İlk kurulum ve sonraki güncellemeler

İlk kurulum Velopack `Setup.exe` ile yapılır. Kurulu kullanıcılar yeni tag yayınlandığında uygulama başlangıcında GitHub Releases'i kontrol eder ve kullanıcı onayından sonra Velopack ile yeniden başlatılarak güncellenir.






# GitHub Release Hazırlık Notu

Bu proje, GitHub Release üzerine koyduğunuz bir `latest.json` manifestini okuyacak şekilde hazırlanmıştır.

## Release içinde bulunması önerilen dosyalar
- `latest.json` → update manifest
- `TurizmOperasyonSistemi-<version>.zip` veya `*.exe` / `*.msi` → dağıtım paketi

## Manifest alanları
- `version` → uygulama sürümü
- `releaseType` → `app`, `migration` veya `both`
- `minimumDatabaseVersion` → migration gerekiyorsa minimum DB versiyonu
- `minimumClientVersion` → minimum client sürümü
- `mandatory` → zorunlu güncelleme mi
- `channel` → `stable`, `beta` vb.
- `releaseNotes` → kullanıcıya gösterilecek notlar
- `downloadUrl` → indirilecek paket
- `checksum` → SHA-256 kontrol değeri

## Akış
1. GitHub Release oluştur.
2. Manifesti asset olarak yükle.
3. `Data/appsettings.json` içindeki `Update:ManifestUrl` alanını bu manifestin raw/indirilebilir URL'sine bağla.
4. Uygulama açıldığında update servisi manifesti okuyacak.
5. Uygulama sürümü, DB sürümü veya ikisi birden gerekiyorsa bildirim üretilecek.

## Not
- Mevcut UI `update.available` akışını kullanır.
- Backend artık ayrıca `update.migration.required` ve `update.both.required` anahtarlarını da üretir.
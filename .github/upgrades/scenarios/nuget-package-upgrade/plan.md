# TOS Lite Velopack Güncelleme Entegrasyonu Planı

## Overview

**Target**: TOS Lite .NET 8 WPF uygulamasını MutfaGO örneğindeki gibi yalnızca Velopack ve GitHub Releases üzerinden güncellenebilir hale getirmek.
**Scope**: Tek proje, uygulama başlangıcı ve güncelleme servisi, Velopack paketleme, GitHub Actions release hattı ve dağıtım dokümantasyonu. INNO Setup güncelleme yolu kaldırılacak.

## Upgrade Options

| Option | Selected | Why |
|--------|----------|-----|
| Güncelleme yolu | Yalnızca Velopack + GitHub Releases | Kullanıcının son kararı; INNO Setup artık kullanılmayacak |
| İstemci yaklaşımı | MutfaGO `GithubSource` + `UpdateManager` deseni | Kanıtlanmış örnek davranışı TOS Lite’a uyarlamak için |
| Paket sürümü | En güncel kararlı Velopack | .NET 8 hedefiyle uyumlu kararlı paket kullanılacak |
| CI kimlik bilgileri | GitHub Actions secrets/vars | Token ve depo bilgileri kaynak kodda saklanmayacak |
| Güncelleme onayı | Kullanıcı onayı ve güvenli başlangıç | Veritabanı/migration akışını riske atmadan kontrollü kurulum |

## Tasks

### 01-velopack-client: MutfaGO desenini TOS Lite istemcisine uyarlama

MutfaGO projesindeki `GithubSource`, `UpdateManager.CheckForUpdatesAsync`, indirme ve `ApplyUpdatesAndRestart` akışını inceleyip TOS Lite’ın başlangıç/güncelleme servislerine uyarla. Mevcut URL tabanlı zip/INNO Setup güncelleme kodunu kaldır veya Velopack istemcisiyle değiştir; LocalAppData veritabanı ve manifest şeması ile Persons görünümlerine dokunma.

**Done when**: TOS Lite HTTPS GitHub repository ayarından Velopack güncellemesi kontrol eder, kullanıcı onayı sonrası indirip yeniden başlatır, çevrimdışı durumda normal açılışı engellemez ve uygulama warning-free derlenir.

---

### 02-github-release-pipeline: Velopack paketleme ve GitHub Releases otomasyonu

MutfaGO workflow yaklaşımını TOS Lite’ın .NET 8 publish çıktısına uyarla. Windows self-contained/win-x64 yayınını Velopack ile paketleyen, release asset’lerini hazırlayan ve GitHub Releases’e gönderen workflow ekle. Kimlik bilgileri yalnızca GitHub Actions secrets/variables veya GitHub bağlamından alınmalı; yerel kullanıcı yolları ve sabit token kullanılmamalı.

**Done when**: Workflow publish/pack/release adımlarını içerir, secrets/vars ile çalışır, ilk kurulum için Velopack Setup.exe ve sonraki sürümler için release paketleri üretir.

---

### 03-validation-documentation: Yeni tekil güncelleme yolunu doğrulama ve belgeleme

INNO Setup referanslarını güncelleme prosedüründen çıkarıp Velopack/GitHub Releases kurulumunu, repository değişkenlerini, sürüm etiketi akışını, ilk kurulum ve geri dönüş notlarını belgele. Mevcut LocalAppData manifest formatını ve uygulama veritabanı güvenliğini koruyarak build/test doğrulamalarını tamamla.

**Done when**: Çözüm warning-free build olur, mevcut testler geçer, dokümantasyon yalnızca Velopack akışını anlatır ve eski Persons/manifest davranışında regresyon bulunmaz.

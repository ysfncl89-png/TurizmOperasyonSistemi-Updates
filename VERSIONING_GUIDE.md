# Database Versiyonlama Sistemi - Kuruluş ve Kullanım Kılavuzu

## Genel Bakış

Bu proje, Entity Framework Core ile database versiyonlarını otomatikman takip eder ve güncellemeler sırasında gerekli migration'ları çalıştırır. Kullanıcılar programı her açtıklarında, sistem otomatikman database'i mevcut uygulama versiyonuna güncelleyebilir.

## İş Akışı

```
Kullanıcı Uygulamayı Başlatıyor
		↓
App() Yapıcısı Çalışıyor
		↓
MigrationService Başlatıldı
		↓
1. Database Versiyon Okunuyor
		↓
2. Pending Migrations Kontrol Ediliyor
		↓
3. Eksik Migrations Çalıştırılıyor (Entity Framework)
		↓
4. DatabaseVersion Tablosuna Yeni Versiyon Yazılıyor
		↓
5. Custom Tablolar Ensure Ediliyor
		↓
Uygulama Başarıyla Açılıyor ✓
```

## Dosya Yapısı

```
Services/
├── MigrationService.cs           # Versiyonlama ve migration yönetimi
│
Data/
├── AppDbContext.cs               # Database context (DbSet'ler eklendi)
│
Models/
├── DatabaseVersion.cs            # Versiyon kaydı
├── MigrationLog.cs              # Migration geçmişi
│
Migrations/
├── 20260620000000_AddDatabaseVersioning.cs         # Yeni migration
├── 20260620000000_AddDatabaseVersioning.Designer.cs
│
App.xaml.cs                        # Başlatma ve versiyonlama mantığı
```

## MigrationService Sınıfı

### Temel Metodlar

#### `ApplyMigrations()`
- Pending migrations'ları tespit eder
- Entity Framework'u kullanarak migration'ları uygular
- Başarılı migration'ları DatabaseVersions tablosuna kaydeder
- Hataları detaylı şekilde debug window'a yazıyor

#### `GetDatabaseVersion()`
- Mevcut database versiyonunu döndürür
- DatabaseVersions tablosundan en son versiyonu okur

#### `GetApplicationVersion()`
- Assembly version'ını döndürür (AssemblyInfo'dan)
- Format: Major.Minor.Patch.Build (ör: 1.2.0.0)

#### `EnsureAllCustomTablesExists()`
- RoomAllocationHistories
- Payments
- Notifications
- Users
- TableExist olayını kontrol eder ve CREATE TABLE IF NOT EXISTS çalıştırır

### Debug Çıktısı

Konsol/Debug window'da şu gibi log çıktıları görürsünüz:

```
=== Database Migration Process Started ===
Current Application Version: 1.0.0.0
Database ensured created.
Found 2 pending migrations:
  - 20260620000000_AddDatabaseVersioning
  - (varsa diğer migrations)
Migrations applied successfully.
Database version recorded: 1.0.0.0
All custom tables ensured.
=== Database Migration Process Completed Successfully ===
```

## Yeni Migration Ekleme

Eğer yeni bir migration eklemek istiyorsanız:

### Seçenek 1: Entity Framework CLI (EF Tools Yüklü İse)
```powershell
cd C:\Users\gülistan\Desktop\TurizmOperasyonSistemi
dotnet ef migrations add YeniMigrationAdı
```

### Seçenek 2: Manual (Önerilir - Kontrollü)
1. `Migrations` klasöründe yeni dosya oluşturun: `YYYYMMDDHHMMSS_MigrationName.cs`
2. Template'i (bkz: `20260620000000_AddDatabaseVersioning.cs`) kullanarak `Up()` ve `Down()` metodlarını yazın
3. Tüm yapısal değişiklikler için `migrationBuilder.Sql()` kullanın
4. Hata durumunda rollback için `Down()` parasını yazın
5. Build'i test edin: `dotnet build`

### Örnek Migration Şablonu

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

namespace TurizmOperasyonSistemi.Migrations
{
	public partial class AddYeniTablo : Migration
	{
		protected override void Up(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.Sql(@"
				CREATE TABLE IF NOT EXISTS YeniTablo (
					Id INTEGER PRIMARY KEY AUTOINCREMENT,
					Ad TEXT NOT NULL,
					CreatedAt TEXT NOT NULL
				);
			");
		}

		protected override void Down(MigrationBuilder migrationBuilder)
		{
			migrationBuilder.DropTable(name: "YeniTablo");
		}
	}
}
```

## Deployment / Kullanıcılara Dağıtım

### Kullanıcı Tarafında Yapılacak İşler:

1. **Yeni Versiyon İndir**: Güncellenmiş `.exe` dosyasını indir
2. **Eski Veritabanı Korunur**: `C:\TurizmDB\turizm.db` dosyası korunur (veri kaybı yok)
3. **Uygulamayı Çalıştır**: Programı açarken otomatikman migration'lar çalışır
4. **Message Gözlemle**: Hata durumunda MessageBox gösterilir

### Admin Tarafında Yapılacak İşler:

```
1. Yeni Feature Ekle
   ↓
2. AppDbContext'e DbSet Ekle (opsiyonel)
   ↓
3. Yeni Migration Oluştur
   ↓
4. AssemblyVersion Artır (Properties\AssemblyInfo.cs)
   ↓
5. Uygulamayı Build Et
   ↓
6. Release Oluştur
   ↓
7. Kullanıcılara Dağıt
```

## Hata Giderme

### "Database initialization error"
- **Sebep**: Migration başarısız, tablo oluşturulamadı
- **Çözüm**: Debug window'da tam error message'ı okuyun

### "Pending migrations found but not applied"
- **Sebep**: Migration file'ı var ama çalışmadı
- **Çözüm**: SQL syntax'ını kontrol edin, PRAGMA kullanın

### "Table already exists"
- **Sebep**: Migration'lardan önce runtime'da tablo oluşturulmuş
- **Çözüm**: `CREATE TABLE IF NOT EXISTS` kullanın

### "Foreign Key Constraint Failed"
- **Sebep**: Migration sınıfta ilişkiler hatalı
- **Çözüm**: PRAGMA foreign_keys ile kontrol edin

## Best Practices

✅ **YAPIN:**
- `CREATE TABLE IF NOT EXISTS` kullanın
- Her migration'ın `Down()` metodunu yazın
- Debugging için `System.Diagnostics.Debug.WriteLine()` kullanın
- Schema değişkelikleri için `migrationBuilder.Sql()` kullanın
- Foreign key'ler için `ON DELETE CASCADE` belirtin

❌ **YAPMAYIN:**
- Database'i doğrudan değiştirmeyin (migration olmadan)
- Migration'ları silmeyin (history bozulur)
- `EnsureCreated()` ve `Migrate()` aynı anda kullanmayın
- SQL injection riski için parameterize query kullanın

## Technical Details

- **Database**: SQLite (`C:\TurizmDB\turizm.db`)
- **Framework**: Entity Framework Core 8.0
- **Target**: .NET 8
- **Language**: C# 12

## İletişim ve Destek

Versiyonlama sistemi ile ilgili sorularınız varsa:
- Debug window'daki log'ları kontrol edin
- MigrationService.cs source'u inceleyin
- App.xaml.cs startup logic'ini review edin

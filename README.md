# SupaBlox

Multi-instance Roblox + account manager untuk Windows (C# .NET 8 + Avalonia), hasil reverse engineering mekanisme Astro Account Manager.

## Fitur (v0.3.0)
- **Akun**: tambah lewat login browser (Edge/Chrome, profil sementara), tempel cookie, atau impor massal. Cookie `.ROBLOSECURITY` disimpan terenkripsi DPAPI (hanya akun Windows kamu yang bisa membuka). Grup, alias, catatan, favorit, game default, status presence.
- **Kontrol Cepat**: pilih game dengan **mengetik nama** (atau tempel Place ID / link), opsional Job ID atau link server privat, lalu launch banyak akun sekaligus dengan jeda dan batas instance. Semua isian tersimpan otomatis dan tidak reset saat pindah tab atau restart.
- **Game & server**: cari game, lihat server publik (ping, slot, **region**), filter per region, salin Job ID, pakai Job ID di Kontrol Cepat, Join ke server tertentu atau server acak dengan akun yang dicentang.
  Region dicek lewat `gamejoin.roblox.com` (alamat IP server, tanpa membuka Roblox) memakai cookie akun pertama yang dicentang, lalu IP digeolokasi lewat ip-api.com.
- **Instance**: Multi Roblox ON/OFF (status mutex/event), daftar RobloxPlayerBeta (PID, akun, mulai + lama jalan, RAM, judul), Fokus, Tutup (graceful). Matikan Multi saat >1 Roblox jalan: pilih satu yang dipertahankan, sisanya ditutup normal.
- Anti-AFK dan atur jendela sengaja tidak dibuat (pakai Multi Roblox Tab Manager). SupaBlox tidak mengganti judul jendela Roblox.
- **Follow user** (Kontrol Cepat): ketik username, akun yang dicentang join ke server orang itu (presence -> Job ID). Target harus terlihat dari akunmu (berteman / privasi join terbuka).
- **Pantau + auto-relaunch** per akun (⋯ Edit): kabari kalau Roblox crash/tertutup, launch ulang otomatis dengan jeda, maks percobaan, opsi relaunch walau ditutup normal, dan rejoin server yang sama. Hanya memantau proses (handle query terbatas seperti Task Manager), tidak membaca/menulis memori. Tutup lewat SupaBlox tidak memicu relaunch.
- **Search global Ctrl+K**: akun, game terakhir/favorit, cari game, Job ID/link -> Kontrol Cepat, Follow @username, aksi cepat.
- **Profil Roblox** (⋯ Edit): refresh profil publik (nama, display name, tanggal dibuat) dan ubah deskripsi profil (`users.roblox.com/v1/description`).
- **Grup**: chip grup di atas daftar akun, `+ Grup baru`, seret kartu akun ke chip untuk pindah grup (kartu yang dicentang ikut pindah semua), klik kanan chip untuk hapus grup.
- **Pindah PC**: ekspor/impor akun ke file `.sbx` terenkripsi password (PBKDF2-SHA256 200k + AES-256-GCM), karena cookie DPAPI hanya bisa dibuka di akun Windows asal.
- Title bar custom, logo baru.
- Tray, mulai bersama Windows, nyalakan Multi Roblox otomatis.
- Font **Builder Sans** dimuat dari instalasi Roblox Player di PC kamu (`%LocalAppData%\Roblox\Versions\*\content\fonts`). Font ini tidak dibundel karena lisensinya; kalau tidak ada, UI memakai Inter.

## Cara kerja Multi Roblox
Roblox menolak jalan dobel lewat dua objek kernel bernama: `ROBLOX_singletonMutex` dan `ROBLOX_singletonEvent`.
SupaBlox membuat dan memegang keduanya di satu thread khusus selama Multi Roblox ON.

Mode **mutex-only**: SupaBlox tidak menyentuh memori proses Roblox (tanpa DuplicateHandle, WriteProcessMemory, injeksi, DLL, atau FFlags). Launch akun memakai alur resmi web Roblox: auth ticket (`auth.roblox.com`) lalu URI `roblox-player:`.

## Pakai
1. Tutup semua Roblox, buka `SupaBlox.exe` (Multi Roblox menyala otomatis).
2. Tambah akun, centang beberapa, pilih game di Kontrol Cepat, klik **Launch akun terpilih**.

Data lokal: `%LocalAppData%\SupaBlox` (accounts.json, settings.json, error.log).

## Build
```powershell
dotnet build src\SupaBlox -c Release
dotnet publish src\SupaBlox -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

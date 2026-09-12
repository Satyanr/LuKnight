# Phase 8D.5 — Local Desktop Command Engine

Phase 9 / 9A tetap ditahan; perubahan ini hanya memperluas command desktop lokal.

| Bagian | Implementasi |
| --- | --- |
| 8D.5A Natural Command Parser | Variasi buka/bukain/bukakan/jalankan, fokus/pindah/balik, awalan sopan, dan akhiran dong/ya. |
| 8D.5B Installed App Discovery | Start Menu pengguna dan bersama; App Paths HKCU/HKLM, registry 32/64-bit; alias built-in dipertahankan. |
| 8D.5C Fuzzy App Resolver | Alias tanpa vendor/tahun, Levenshtein, deduplikasi registrasi, respons ambigu, pilihan versi dengan nama lengkap. |
| 8D.5D Explorer Navigation | Home, Desktop, Documents, Downloads, Pictures, Music, Videos. Downloads mengikuti Windows Known Folder yang dapat dipindahkan. |
| 8D.5E Explorer Search | Query diserahkan ke `search-ms:` dengan encoding parameter; tidak melakukan crawling file. |
| 8D.5F Local Responses / 0 Token | Action, konfirmasi, pembatalan, aplikasi tidak ditemukan, perintah ditolak, dan ambigu selalu lokal. Chat/analisis tetap Gemini. |
| 8D.5G Restricted Apps Security | Shell, script host dan utility admin diblokir sebelum fuzzy matching, saat discovery dan sebelum launch. |

Aktifkan **Settings → AI & Chat → Desktop actions**. Setiap eksekusi tetap melalui
proposal konfirmasi satu kali dengan batas waktu yang sudah ada.

Contoh:

```text
eh tolong bukain photoshop dong
coba buka vscode
fokuskan ke chrome
tolong buka folder dokumen
bukain desktop dong
carikan logo di pictures
cari Laporan Q3-2026.pdf di documents
buka explorer dan cari LuKnight
```

Nama app yang ambigu menghasilkan daftar alternatif. Ulangi dengan nama lengkap,
misalnya `buka Adobe Photoshop 2026`. Katalog cache diperbarui setelah 10 menit;
miss dapat memicu refresh dengan jeda minimal 30 detik. API `Refresh()` tersedia
untuk refresh eksplisit. Satu sesi discovery memakai satu objek COM pembaca shortcut.
Registrasi langsung dan updater Squirrel (misalnya Discord) disatukan dengan
identitas aplikasi yang sama; launcher updater diprioritaskan agar perubahan
direktori versi tidak memunculkan pilihan aplikasi ganda. Nama dan alias App Paths
mencakup nama registrasi dan executable target, termasuk activation helper packaged app.

Shortcut hanya dibaca, tidak dieksekusi selama discovery. Shortcut rusak, target
non-EXE/remote, serta shortcut yang menyamarkan executable terlarang ditolak.
Target dan argumen diikat ke proposal melalui fingerprint dan diperiksa ulang
sebelum launch. Eksekusi memakai executable/argumen yang diperiksa, bukan membuka
ulang shortcut yang dapat berubah setelah pemeriksaan. Argumen berasal dari
registrasi shortcut, tidak pernah dari teks perintah pengguna.

"Aplikasi terdaftar" bukan seluruh EXE di disk. Shortcut Store/UWP yang tidak
dapat diuraikan menjadi executable lokal tidak diindeks. Built-in yang tidak
terpasang dapat menghasilkan kegagalan launch lokal. Pencarian tidak merangkum
hasil; Explorer menampilkan hasilnya. Folder scope yang hilang tidak diganti
diam-diam dengan pencarian seluruh komputer.

## Pengujian

```powershell
dotnet clean
dotnet build -c Release
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --assistant
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release
```

Suite assistant memakai fixture katalog, executor tiruan, HTTP counter dan kontrol
positif: seluruh operasi desktop harus menghasilkan nol request, sementara satu
permintaan reasoning harus menghasilkan satu request. Tes shortcut membuat dan
membaca shortcut sementara milik tes, termasuk penggantian target ke CMD; shortcut
tersebut tidak diluncurkan.

Smoke test opt-in berikut **membuka aplikasi nyata dan Explorer**, menggunakan enam
perintah contoh yang diminta, dengan konfirmasi lewat controller dan HTTP counter
yang harus tetap nol. Tidak dijalankan oleh suite default:

```powershell
dotnet run --project tests/LuKnight.RenderChecks/LuKnight.RenderChecks.csproj -c Release -- --desktop-commands-live
```

Smoke test memeriksa respons launch native dan request count. Hasil visual atau
kelengkapan hasil pencarian Windows perlu diperiksa pada jendela Explorer.

Hasil pada 12 September 2026: build Release tanpa warning/error, 271 assistant
checks dan 858 pemeriksaan suite lengkap lulus. Smoke test di luar sandbox menemukan 112 target dalam sekitar 0,7
detik. Windows menerima launch Photoshop 2026, helper Spotify (`spotify_cli`),
Discord, Downloads, serta dua pencarian Explorer. Keenam perintah menggunakan
**0 request Gemini**. Sandbox membatasi sebagian discovery dan shell activation;
hasil native di atas diperoleh melalui eksekusi smoke test tanpa batasan tersebut.
